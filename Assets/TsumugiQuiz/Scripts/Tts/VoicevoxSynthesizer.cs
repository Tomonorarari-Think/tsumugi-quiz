using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using TsumugiQuiz.Tts.Native;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// voicevox_core の初期化から合成までを担う IDisposable ラッパ（docs/tts-native-api.md §1.1）。
    ///
    /// 呼び出し順:
    ///   onnxruntime_load_once → open_jtalk_rc_new → synthesizer_new → open_jtalk_rc_delete
    ///   → voice_model_file_open / load_voice_model / voice_model_file_delete → tts
    ///
    /// 注意:
    /// - 合成（<see cref="Tts"/> / <see cref="Synthesis"/>）は数百 ms〜数秒かかる。メインスレッドで呼ばない。
    /// - 1 プロセス 1 インスタンスを想定する（ONNX Runtime はプロセス内シングルトン）。
    /// - 破棄後にハンドルを使うのは未定義動作なので、Dispose 後の呼び出しは例外にする。
    /// - <b>ネイティブ呼び出しはインスタンス単位のロックで直列化する</b>。
    ///   ヘッダの `voicevox_synthesizer_delete` の仕様が「破棄対象への他スレッドでのアクセスが
    ///   存在する場合、それらがすべて終わるのを待ってから破棄する」ため、
    ///   <see cref="Dispose"/> は実行中の合成が終わるまでブロックする。
    /// - <b>ファイナライザは持たない</b>。GC スレッドから
    ///   `voicevox_synthesizer_delete` を呼ぶと、合成中の他スレッドと競合しうるうえ、
    ///   終了時に DLL のアンロード順と競合する危険があるため。
    ///   <b>必ず <see cref="Dispose"/> を呼ぶこと</b>（常駐サービスなら `Application.quitting` などで）。
    /// </summary>
    public sealed class VoicevoxSynthesizer : IDisposable
    {
        private static readonly object OnnxruntimeGate = new object();
        private static IntPtr _onnxruntime;

        /// <summary>ネイティブ呼び出しと破棄を直列化するロック。</summary>
        private readonly object _gate = new object();

        private readonly string[] _loadedModelFiles;
        private IntPtr _synthesizer;
        private bool _disposed;

        private VoicevoxSynthesizer(IntPtr synthesizer, string[] loadedModelFiles)
        {
            _synthesizer = synthesizer;
            _loadedModelFiles = loadedModelFiles;
        }

        /// <summary>ONNX Runtime のロードに実際に効いた手段（docs/tts-native-api.md §3.3）。診断ログ用。</summary>
        public static NativeLoadStrategy OnnxruntimeLoadStrategy { get; private set; } = NativeLoadStrategy.None;

        /// <summary>読み込んだ .vvm のパス一覧。</summary>
        public IReadOnlyList<string> LoadedModelFiles => _loadedModelFiles;

        /// <summary>voicevox_core のバージョン文字列。</summary>
        public string CoreVersion => Utf8.FromPtr(VoicevoxNative.voicevox_get_version());

        /// <summary>GPU モードで動作しているか。</summary>
        public bool IsGpuMode
        {
            get
            {
                lock (_gate)
                {
                    ThrowIfDisposed();
                    return VoicevoxNative.voicevox_synthesizer_is_gpu_mode(_synthesizer);
                }
            }
        }

        /// <summary>
        /// 配置を解決して初期化する。
        /// 呼び出し側は例外を捕捉して読み上げを無効化し、ゲームは続行すること（docs/tts.md §9）。
        /// 成功したら <see cref="Dispose"/> を呼ぶ責任は呼び出し側にある。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="location"/> が null</exception>
        /// <exception cref="TtsSetupException">
        /// 配置が足りない（DLL / 辞書 / .vvm が無い）、ONNX Runtime をロードできない、
        /// voicevox_core.dll が見つからない（<see cref="DllNotFoundException"/> を包む）とき
        /// </exception>
        /// <exception cref="VoicevoxException">
        /// 辞書の読み込み・シンセサイザ生成・音声モデルの登録がネイティブ側で失敗したとき
        /// </exception>
        public static VoicevoxSynthesizer Create(VoicevoxLocation location)
            => Create(location, VoicevoxModelSelection.All, prepareNativeSearchPath: true);

        /// <summary>
        /// 読み込む .vvm を <paramref name="selection"/> で絞り込んで初期化する（#22 統括メモ (2)）。
        /// 該当する .vvm が判定できなかった場合は全件読み込みにフォールバックする。
        /// </summary>
        /// <inheritdoc cref="Create(VoicevoxLocation)"/>
        public static VoicevoxSynthesizer Create(VoicevoxLocation location, VoicevoxModelSelection selection)
            => Create(location, selection, prepareNativeSearchPath: true);

        /// <summary>
        /// <paramref name="prepareNativeSearchPath"/> に false を渡すと、SetDllDirectory と
        /// 事前ロード（docs/tts-native-api.md §3.3 の対策 2・3）を行わず、
        /// 絶対パス指定（§3.1）だけで解決できるかを試す。DLL 検索パスの実測テスト用。
        /// </summary>
        internal static VoicevoxSynthesizer Create(VoicevoxLocation location, bool prepareNativeSearchPath)
            => Create(location, VoicevoxModelSelection.All, prepareNativeSearchPath);

        /// <inheritdoc cref="Create(VoicevoxLocation, bool)"/>
        internal static VoicevoxSynthesizer Create(
            VoicevoxLocation location, VoicevoxModelSelection selection, bool prepareNativeSearchPath)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            ThrowIfNotReady(location);

            if (prepareNativeSearchPath) NativeLibraryLoader.Prepare(location.NativeDir);

            IntPtr openJtalk = IntPtr.Zero;
            IntPtr synthesizer = IntPtr.Zero;
            try
            {
                var onnxruntime = EnsureOnnxruntime(location.NativeDir);

                Vv.Check(
                    VoicevoxNative.voicevox_open_jtalk_rc_new(Utf8.ToNullTerminated(location.DictionaryDir), out openJtalk),
                    $"Open JTalk 辞書の読み込み（{location.DictionaryDir}）");

                var initOptions = VoicevoxNative.voicevox_make_default_initialize_options();
                Vv.Check(
                    VoicevoxNative.voicevox_synthesizer_new(onnxruntime, openJtalk, initOptions, out synthesizer),
                    "音声シンセサイザの生成");

                // 辞書を差し替えないので、公式サンプルどおり synthesizer_new の直後に破棄する。
                VoicevoxNative.voicevox_open_jtalk_rc_delete(openJtalk);
                openJtalk = IntPtr.Zero;

                var modelFiles = selection.IsAll
                    ? location.VoiceModelFiles
                    : VoicevoxModelSelector.SelectWithNative(
                        location.VoiceModelFiles, selection.SpeakerName, selection.StyleName);

                var loaded = LoadVoiceModels(synthesizer, modelFiles);
                var instance = new VoicevoxSynthesizer(synthesizer, loaded);
                synthesizer = IntPtr.Zero;
                return instance;
            }
            catch (DllNotFoundException e)
            {
                // 最初の P/Invoke で発生する（voicevox_core.dll 自体が解決できない）ため MissingCoreDll 扱い。
                throw new TtsSetupException(
                    $"音声合成ライブラリが見つかりません（{location.NativeDir}）。External の配置手順（External/README.md）を確認してください。",
                    TtsUnavailableReason.MissingCoreDll, e);
            }
            finally
            {
                if (openJtalk != IntPtr.Zero) VoicevoxNative.voicevox_open_jtalk_rc_delete(openJtalk);
                if (synthesizer != IntPtr.Zero) VoicevoxNative.voicevox_synthesizer_delete(synthesizer);
            }
        }

        /// <summary>読み込み済みの話者メタ情報（JSON）を取得する。</summary>
        /// <exception cref="ObjectDisposedException">破棄済みのとき</exception>
        public string CreateMetasJson()
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                var ptr = VoicevoxNative.voicevox_synthesizer_create_metas_json(_synthesizer);
                try
                {
                    return Utf8.FromPtr(ptr);
                }
                finally
                {
                    if (ptr != IntPtr.Zero) VoicevoxNative.voicevox_json_free(ptr);
                }
            }
        }

        /// <summary>話者名・スタイル名からスタイル ID を解決する（ID はハードコードしない）。</summary>
        public VoicevoxStyleResolution ResolveStyle(
            string speakerName = VoicevoxStyleResolver.DefaultSpeakerName,
            string styleName = VoicevoxStyleResolver.DefaultStyleName)
            => VoicevoxStyleResolver.Resolve(CreateMetasJson(), speakerName, styleName);

        /// <summary>テキストを一括で合成して wav バイト列を返す（速度変更が不要なとき）。</summary>
        /// <exception cref="VoicevoxException">ネイティブ側が失敗したとき</exception>
        /// <exception cref="ObjectDisposedException">破棄済みのとき</exception>
        public byte[] Tts(string text, uint styleId, bool enableInterrogativeUpspeak = true)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            lock (_gate)
            {
                ThrowIfDisposed();

                var options = VoicevoxNative.voicevox_make_default_tts_options();
                options.EnableInterrogativeUpspeak = enableInterrogativeUpspeak;

                Vv.Check(
                    VoicevoxNative.voicevox_synthesizer_tts(
                        _synthesizer, Utf8.ToNullTerminated(text), styleId, options, out var wavLength, out var wavPtr),
                    "音声合成");

                return CopyAndFreeWav(wavLength, wavPtr);
            }
        }

        /// <summary>AudioQuery の JSON を生成する（読み上げ速度を変えるときの 1 段目）。</summary>
        /// <exception cref="VoicevoxException">ネイティブ側が失敗したとき</exception>
        /// <exception cref="ObjectDisposedException">破棄済みのとき</exception>
        public string CreateAudioQuery(string text, uint styleId)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            lock (_gate)
            {
                ThrowIfDisposed();

                Vv.Check(
                    VoicevoxNative.voicevox_synthesizer_create_audio_query(
                        _synthesizer, Utf8.ToNullTerminated(text), styleId, out var jsonPtr),
                    "AudioQuery の生成");

                try
                {
                    return Utf8.FromPtr(jsonPtr);
                }
                finally
                {
                    if (jsonPtr != IntPtr.Zero) VoicevoxNative.voicevox_json_free(jsonPtr);
                }
            }
        }

        /// <summary>AudioQuery の JSON から合成する（読み上げ速度を変えるときの 2 段目）。</summary>
        /// <exception cref="VoicevoxException">ネイティブ側が失敗したとき</exception>
        /// <exception cref="ObjectDisposedException">破棄済みのとき</exception>
        public byte[] Synthesis(string audioQueryJson, uint styleId, bool enableInterrogativeUpspeak = true)
        {
            if (audioQueryJson == null) throw new ArgumentNullException(nameof(audioQueryJson));

            lock (_gate)
            {
                ThrowIfDisposed();

                var options = VoicevoxNative.voicevox_make_default_synthesis_options();
                options.EnableInterrogativeUpspeak = enableInterrogativeUpspeak;

                Vv.Check(
                    VoicevoxNative.voicevox_synthesizer_synthesis(
                        _synthesizer, Utf8.ToNullTerminated(audioQueryJson), styleId, options,
                        out var wavLength, out var wavPtr),
                    "音声合成（AudioQuery）");

                return CopyAndFreeWav(wavLength, wavPtr);
            }
        }

        /// <summary>
        /// シンセサイザを破棄する。<b>実行中の合成が終わるまでブロックする</b>
        /// （ヘッダの voicevox_synthesizer_delete の仕様に合わせるため）。
        /// ファイナライザは無いので、必ず呼ぶこと。2 回目以降の呼び出しは何もしない。
        /// </summary>
        public void Dispose() => ReleaseSynthesizer();

        private static void ThrowIfNotReady(VoicevoxLocation location)
        {
            switch (location.Readiness)
            {
                case TtsReadiness.Ready:
                    return;
                case TtsReadiness.MissingNative:
                    // 両方無い／core だけ無い場合は MissingCoreDll、core はあって onnxruntime だけ無い場合は
                    // MissingOnnxRuntime とする（core の欠落のほうが根本的なので優先する）。
                    var reason = VoicevoxPaths.CoreDllExists(location.NativeDir)
                        ? TtsUnavailableReason.MissingOnnxRuntime
                        : TtsUnavailableReason.MissingCoreDll;
                    throw new TtsSetupException(
                        $"音声合成ライブラリが見つかりません（{location.NativeDir}）。scripts/setup-external.ps1 を実行してください。",
                        reason);
                case TtsReadiness.MissingDictionary:
                    throw new TtsSetupException(
                        "読み上げ用の辞書が見つかりません。scripts/setup-external.ps1 を実行してください。",
                        TtsUnavailableReason.MissingDictionary);
                case TtsReadiness.MissingModels:
                    throw new TtsSetupException(
                        "音声モデル（.vvm）が見つかりません。scripts/setup-external.ps1 を実行してください。",
                        TtsUnavailableReason.MissingModel);
                default:
                    throw new TtsSetupException(
                        $"音声合成を初期化できません（{location.Readiness}）。", TtsUnavailableReason.InitializationFailed);
            }
        }

        /// <summary>
        /// ONNX Runtime をロードする。まず絶対パス指定（§3.1）、失敗したら既定のファイル名指定
        /// （検索パスに頼る、§3.3）の順に試す。どちらも失敗したら
        /// 要求バージョンを添えた <see cref="TtsSetupException"/> を投げる（docs/tts.md §9）。
        /// </summary>
        private static IntPtr EnsureOnnxruntime(string nativeDir)
        {
            lock (OnnxruntimeGate)
            {
                if (_onnxruntime != IntPtr.Zero) return _onnxruntime;

                // 既にロード済み（別インスタンスやエディタの前回の Play）なら借用ポインタを取り直す。
                var alreadyLoaded = VoicevoxNative.voicevox_onnxruntime_get();
                if (alreadyLoaded != IntPtr.Zero)
                {
                    _onnxruntime = alreadyLoaded;
                    return alreadyLoaded;
                }

                var ortPath = Path.Combine(nativeDir, VoicevoxNative.OnnxruntimeDllFileName);
                var absolutePathCode = VoicevoxResultCode.Ok;

                if (File.Exists(ortPath))
                {
                    absolutePathCode = LoadOnnxruntimeFromPath(ortPath, out var handle);
                    if (absolutePathCode == VoicevoxResultCode.Ok)
                    {
                        OnnxruntimeLoadStrategy = NativeLoadStrategy.PreloadAbsolutePath;
                        _onnxruntime = handle;
                        return handle;
                    }
                }

                var defaultOptions = VoicevoxNative.voicevox_make_default_load_onnxruntime_options();
                var code = VoicevoxNative.voicevox_onnxruntime_load_once(defaultOptions, out var fallbackHandle);
                if (code != VoicevoxResultCode.Ok)
                {
                    // 絶対パス指定のほうが情報量が多いので、そちらのエラーを優先して報告する。
                    throw CreateOnnxruntimeSetupException(
                        absolutePathCode != VoicevoxResultCode.Ok ? absolutePathCode : code, ortPath);
                }

                OnnxruntimeLoadStrategy = NativeLibraryLoader.LastStrategy == NativeLoadStrategy.SetDllDirectory
                    ? NativeLoadStrategy.SetDllDirectory
                    : NativeLoadStrategy.UnityPluginResolution;
                _onnxruntime = fallbackHandle;
                return fallbackHandle;
            }
        }

        /// <summary>
        /// ONNX Runtime のロード失敗を <see cref="TtsSetupException"/> にする（#25 H-4）。
        ///
        /// <b>MissingOnnxRuntime はここでは判定しない</b>。このメソッドに到達する時点で
        /// <see cref="ThrowIfNotReady"/> の <see cref="TtsReadiness"/> 判定を通過済み、つまり
        /// <c>voicevox_onnxruntime.dll</c> はファイルとして存在している（未配置は Readiness 段階で
        /// <see cref="TtsUnavailableReason.MissingOnnxRuntime"/> として弾かれる）。
        ///
        /// ここで判定できるのは「ファイルはあるのにロードできない」ケースのみで、
        /// <b>ResultCode が <see cref="VoicevoxResultCode.InitInferenceRuntime"/> で、かつ
        /// 対応バージョン範囲（min/max）を取得できた場合だけ</b> バージョン不一致と断定する。
        /// それ以外は原因を断定できないため <see cref="TtsUnavailableReason.InitializationFailed"/> に
        /// 留め、ユーザー向け文言でも「対応していません」と言い切らない。
        /// </summary>
        private static TtsSetupException CreateOnnxruntimeSetupException(VoicevoxResultCode code, string ortPath)
        {
            var nativeMessage = Vv.DescribeResultCode(code);
            uint? min = null;
            uint? max = null;
            string recommended = null;

            try
            {
                min = VoicevoxNative.voicevox_get_onnxruntime_lib_min_required_minor_version();
                max = VoicevoxNative.voicevox_get_onnxruntime_lib_max_supported_minor_version();
                recommended = Utf8.FromPtr(VoicevoxNative.voicevox_get_onnxruntime_lib_recommended_versioned_filename());
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // バージョン情報自体が取れない場合は判定不能（InitializationFailed）にフォールバックする。
            }

            var logMessage =
                $"ONNX Runtime のロードに失敗しました（{ortPath}）。詳細: [{(int)code} {code}] {nativeMessage}" +
                (min.HasValue && max.HasValue
                    ? $" 対応バージョンは 1.{min} 以上 1.{max} 以下、推奨ファイル名は {recommended} です。"
                    : string.Empty);

            if (code == VoicevoxResultCode.InitInferenceRuntime && min.HasValue && max.HasValue)
            {
                return new TtsSetupException(
                    logMessage, TtsUnavailableReason.OnnxRuntimeVersionMismatch, (int)min.Value, (int)max.Value);
            }

            return new TtsSetupException(logMessage, TtsUnavailableReason.InitializationFailed);
        }

        private static VoicevoxResultCode LoadOnnxruntimeFromPath(string ortPath, out IntPtr handle)
        {
            var pathUtf8 = Utf8.ToNullTerminated(ortPath);
            var pinned = GCHandle.Alloc(pathUtf8, GCHandleType.Pinned);
            try
            {
                var options = new VoicevoxLoadOnnxruntimeOptions { Filename = pinned.AddrOfPinnedObject() };
                return VoicevoxNative.voicevox_onnxruntime_load_once(options, out handle);
            }
            finally
            {
                pinned.Free();
            }
        }

        private static string[] LoadVoiceModels(IntPtr synthesizer, IReadOnlyList<string> modelFiles)
        {
            var loaded = new List<string>(modelFiles.Count);
            var loadOptions = VoicevoxNative.voicevox_make_default_load_voice_model_options();

            foreach (var modelFile in modelFiles)
            {
                Vv.Check(
                    VoicevoxNative.voicevox_voice_model_file_open(Utf8.ToNullTerminated(modelFile), out var model),
                    $"音声モデルの読み込み（{Path.GetFileName(modelFile)}）");

                try
                {
                    Vv.Check(
                        VoicevoxNative.voicevox_synthesizer_load_voice_model(synthesizer, model, loadOptions),
                        $"音声モデルの登録（{Path.GetFileName(modelFile)}）");
                    loaded.Add(modelFile);
                }
                finally
                {
                    // ファイルディスクリプタを閉じるだけで、ファイル自体は削除されない。
                    VoicevoxNative.voicevox_voice_model_file_delete(model);
                }
            }

            return loaded.ToArray();
        }

        private static byte[] CopyAndFreeWav(UIntPtr wavLength, IntPtr wavPtr)
        {
            try
            {
                var length = checked((int)wavLength.ToUInt64());
                var wav = new byte[length];
                if (length > 0) Marshal.Copy(wavPtr, wav, 0, length);
                return wav;
            }
            finally
            {
                if (wavPtr != IntPtr.Zero) VoicevoxNative.voicevox_wav_free(wavPtr);
            }
        }

        /// <summary>ロックを取ってから破棄するので、実行中の合成が終わるまで待つ。</summary>
        private void ReleaseSynthesizer()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;

                if (_synthesizer == IntPtr.Zero) return;
                var handle = _synthesizer;
                _synthesizer = IntPtr.Zero;
                VoicevoxNative.voicevox_synthesizer_delete(handle);
            }
        }

        /// <summary>必ず <see cref="_gate"/> を取った状態で呼ぶ。</summary>
        private void ThrowIfDisposed()
        {
            if (_disposed || _synthesizer == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(VoicevoxSynthesizer));
            }
        }
    }
}
