using System;
using System.Runtime.InteropServices;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>
    /// voicevox_core 0.17.0 の C API に対する P/Invoke 宣言。
    /// 署名は External/voicevox_core/c_api/include/voicevox_core.h を実際に読んで確定した
    /// （docs/tts-native-api.md §2）。
    ///
    /// 文字列はすべて UTF-8 のヌル終端バイト列（<see cref="Utf8.ToNullTerminated"/>）で渡す。
    /// 既定マーシャリング（ANSI）では日本語が化けるため string を直接渡してはいけない。
    /// </summary>
    internal static class VoicevoxNative
    {
        /// <summary>
        /// Unity は Assets/Plugins/voicevox_core/x86_64/voicevox_core.dll を
        /// "voicevox_core" という名前で解決する（拡張子・ディレクトリは付けない）。
        /// </summary>
        internal const string Dll = "voicevox_core";

        /// <summary>ネイティブ本体のファイル名（Unity の Plugin 配置・事前ロードで使う）。</summary>
        internal const string CoreDllFileName = Dll + ".dll";

        /// <summary>
        /// ONNX Runtime のファイル名。実測（2026-09-13、voicevox_onnxruntime 1.17.3）では
        /// バージョン接尾辞が付かないため固定名で扱う（docs/tts-native-api.md §3.4）。
        /// </summary>
        internal const string OnnxruntimeDllFileName = "voicevox_onnxruntime.dll";

        // ---- ONNX Runtime ----------------------------------------------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint voicevox_get_onnxruntime_lib_min_required_minor_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint voicevox_get_onnxruntime_lib_max_supported_minor_version();

        /// <summary>
        /// 推奨されるバージョン付きファイル名。返り値は静的文字列で解放不要。
        /// Windows では voicevox_get_onnxruntime_lib_recommended_unversioned_filename と同じ値を返す
        /// （ヘッダのコメント）ので、非バージョン付きの宣言は持たない。
        /// </summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_get_onnxruntime_lib_recommended_versioned_filename();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxLoadOnnxruntimeOptions voicevox_make_default_load_onnxruntime_options();

        /// <param name="outOnnxruntime">const VoicevoxOnnxruntime** — 借用ポインタ。解放しない</param>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_onnxruntime_load_once(
            VoicevoxLoadOnnxruntimeOptions options, out IntPtr outOnnxruntime);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_onnxruntime_get();

        // ---- Open JTalk ------------------------------------------------------

        /// <param name="openJtalkDicDir">UTF-8 のヌル終端文字列</param>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_open_jtalk_rc_new(
            byte[] openJtalkDicDir, out IntPtr outOpenJtalk);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void voicevox_open_jtalk_rc_delete(IntPtr openJtalk);

        // ---- Synthesizer -----------------------------------------------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxInitializeOptions voicevox_make_default_initialize_options();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_synthesizer_new(
            IntPtr onnxruntime, IntPtr openJtalk,
            VoicevoxInitializeOptions options, out IntPtr outSynthesizer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void voicevox_synthesizer_delete(IntPtr synthesizer);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool voicevox_synthesizer_is_gpu_mode(IntPtr synthesizer);

        /// <summary>返り値は char* — 使用後に voicevox_json_free で解放すること</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_synthesizer_create_metas_json(IntPtr synthesizer);

        // ---- Voice model -----------------------------------------------------

        /// <param name="path">UTF-8 のヌル終端文字列（.vvm へのパス）</param>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_voice_model_file_open(
            byte[] path, out IntPtr outModel);

        /// <summary>
        /// 開いた 1 つの .vvm に含まれる話者メタ情報（返り値は char* で voicevox_json_free で解放）。
        ///
        /// スタイル<b>解決</b>には使わない（読み込み済みの全モデルを対象にしたいので
        /// voicevox_synthesizer_create_metas_json を使う。docs/tts.md §4.1）。
        /// こちらは<b>読み込む .vvm を絞り込む</b>ためだけに使う
        /// （<see cref="TsumugiQuiz.Tts.VoicevoxModelSelector"/>、#22 統括メモ (2)）。
        /// </summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_voice_model_file_create_metas_json(IntPtr model);

        /// <summary>ファイルディスクリプタを閉じてオブジェクトを破棄する（ファイル自体は削除されない）。</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void voicevox_voice_model_file_delete(IntPtr model);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxLoadVoiceModelOptions voicevox_make_default_load_voice_model_options();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_synthesizer_load_voice_model(
            IntPtr synthesizer, IntPtr model, VoicevoxLoadVoiceModelOptions options);

        // ---- 合成 ------------------------------------------------------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxTtsOptions voicevox_make_default_tts_options();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxSynthesisOptions voicevox_make_default_synthesis_options();

        /// <param name="text">UTF-8 ヌル終端</param>
        /// <param name="styleId">VoicevoxStyleId = uint32_t</param>
        /// <param name="outputWavLength">uintptr_t* → UIntPtr</param>
        /// <param name="outputWav">uint8_t** → IntPtr。voicevox_wav_free で解放</param>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_synthesizer_tts(
            IntPtr synthesizer, byte[] text, uint styleId, VoicevoxTtsOptions options,
            out UIntPtr outputWavLength, out IntPtr outputWav);

        /// <summary>返り値の JSON は voicevox_json_free で解放</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_synthesizer_create_audio_query(
            IntPtr synthesizer, byte[] text, uint styleId, out IntPtr outputAudioQueryJson);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern VoicevoxResultCode voicevox_synthesizer_synthesis(
            IntPtr synthesizer, byte[] audioQueryJson, uint styleId,
            VoicevoxSynthesisOptions options,
            out UIntPtr outputWavLength, out IntPtr outputWav);

        // ---- 解放・エラー ----------------------------------------------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void voicevox_json_free(IntPtr json);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void voicevox_wav_free(IntPtr wav);

        /// <summary>返り値は静的な UTF-8 文字列。解放不要</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_error_result_to_message(VoicevoxResultCode resultCode);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_get_version();
    }
}
