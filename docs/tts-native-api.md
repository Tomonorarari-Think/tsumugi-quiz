# voicevox_core 0.17.0 ネイティブ API 連携

## 目的

voicevox_core 0.17.0 の C API を Unity(.NET Standard 2.1) から P/Invoke で呼ぶための、
関数の呼び出し順・C# 署名・ONNX Runtime の動的ロードと DLL 配置を定める。

本書の関数名・引数の順序・構造体の中身は、
`External/voicevox_core/voicevox_core-windows-x64-0.17.0.zip` 内の `include/voicevox_core.h` を
実際に展開して読んだ結果である。呼び出し順は公式サンプル
<https://github.com/VOICEVOX/voicevox_core/blob/0.17.0/example/cpp/windows/simple_tts/simple_tts.cpp>
に合わせた。

## 関連ドキュメント

- [docs/tts.md](tts.md) — TTS 設計の全体（スタイル解決・キャッシュ・再生同期・配布方式）
- [External/README.md](../External/README.md) — External/ の構成とダウンローダー実行手順

---

## 1. C API の呼び出し順

### 1.1 初期化から合成まで

公式サンプル `example/cpp/windows/simple_tts/simple_tts.cpp`（タグ 0.17.0）の流れをそのまま採用する。

```
 1. voicevox_make_default_load_onnxruntime_options()   → VoicevoxLoadOnnxruntimeOptions
 2. voicevox_onnxruntime_load_once(opts, &onnxruntime) → const VoicevoxOnnxruntime*
 3. voicevox_open_jtalk_rc_new(dictDir, &openJtalk)    → OpenJtalkRc*
 4. voicevox_make_default_initialize_options()         → VoicevoxInitializeOptions
 5. voicevox_synthesizer_new(onnxruntime, openJtalk, initOpts, &synthesizer)
 6. voicevox_open_jtalk_rc_delete(openJtalk)           ← ここで破棄してよい（synthesizer が保持する）
 7. 各 .vvm について:
      voicevox_voice_model_file_open(path, &model)
      voicevox_voice_model_file_create_metas_json(model)   → スタイル ID 解決に使う（§4）
      voicevox_synthesizer_load_voice_model(synthesizer, model, voicevox_make_default_load_voice_model_options())
      voicevox_voice_model_file_delete(model)
 8. voicevox_make_default_tts_options()                → VoicevoxTtsOptions
 9. voicevox_synthesizer_tts(synthesizer, utf8Text, styleId, ttsOpts, &wavLen, &wavPtr)
10. wav をコピーして使う
11. voicevox_wav_free(wavPtr)
12. アプリ終了時: voicevox_synthesizer_delete(synthesizer)
```

ポイント:

- **`voicevox_onnxruntime_load_once` は一度成功したら以後は引数を無視して同じ参照を返す**（ヘッダのコメント: 「一度成功したら、以後は引数を無視して同じ参照を返す」）。プロセス内でシングルトンとして扱ってよい。
- **`voicevox_open_jtalk_rc_delete` は `voicevox_synthesizer_new` の直後に呼んでよい**（公式サンプルがそうしている）。ただし辞書を後から差し替える予定があるなら保持する。本プロジェクトでは差し替えないので、サンプルどおり即破棄する。
- **`voicevox_voice_model_file_delete` は「ファイルディスクリプタを閉じてオブジェクトを破棄する」**だけで、ファイルの削除ではない（ヘッダのコメントに明記）。`load_voice_model` 後は破棄してよい。
- **文字列は UTF-8 のヌル終端**。UTF-16 を渡してはいけない。
- **`voicevox_wav_free` / `voicevox_json_free` 以外で解放してはいけない**。ヘッダの Safety 節に「このライブラリで生成したオブジェクトの解放は、このライブラリが提供する API で行わなくてはならない（`free` や `HeapFree` で行ってはならない）」と明記されている。

### 1.2 テキスト → 音声の 2 系統

| 系統 | 関数 | 用途 |
|---|---|---|
| 一括 | `voicevox_synthesizer_tts(synth, text, styleId, opts, &len, &wav)` | 既定。速度調整が不要なとき |
| 2 段 | `voicevox_synthesizer_create_audio_query(synth, text, styleId, &json)` → JSON の `speedScale` などを書き換え → `voicevox_synthesizer_synthesis(synth, json, styleId, opts, &len, &wav)` | **読み上げ速度を変えるとき（tts.md §6.4）** |

`VoicevoxTtsOptions` は `bool enable_interrogative_upspeak`（疑問文の語尾上げ）**1 つだけ**で、速度のパラメータを持たない（ヘッダで確認）。
したがって「読み上げ速度を設定で変えられるようにする」には **必ず 2 段系統**を使う。本プロジェクトは 2 段系統を既定実装とし、`speed == 1.0` のときだけ一括系統にショートカットする。

---

## 2. C# P/Invoke 署名案

配置先: `Assets/TsumugiQuiz/Scripts/Tts/Native/VoicevoxNative.cs`（asmdef `TsumugiQuiz.Tts`、仮決め K5）

`allowUnsafeCode` が無効（`ProjectSettings.asset` で実測）なので、**ポインタは `IntPtr` で受ける**設計にする。

```csharp
using System;
using System.Runtime.InteropServices;

namespace TsumugiQuiz.Tts.Native
{
    // voicevox_core.h: typedef int32_t VoicevoxResultCode;
    internal enum VoicevoxResultCode : int
    {
        Ok                          = 0,
        NotLoadedOpenjtalkDict      = 1,
        GetSupportedDevices         = 3,
        GpuSupport                  = 4,
        StyleNotFound               = 6,
        ModelNotFound               = 7,
        RunModel                    = 8,
        AnalyzeText                 = 11,
        InvalidUtf8Input            = 12,
        ParseKana                   = 13,
        InvalidAudioQuery           = 14,
        InvalidAccentPhrase         = 15,
        OpenZipFile                 = 16,
        ReadZipEntry                = 17,
        ModelAlreadyLoaded          = 18,
        LoadUserDict                = 20,
        SaveUserDict                = 21,
        UserDictWordNotFound        = 22,
        UseUserDict                 = 23,
        InvalidUserDictWord         = 24,
        InvalidUuid                 = 25,
        StyleAlreadyLoaded          = 26,
        InvalidModelData            = 27,
        InvalidModelFormat          = 28,
        InitInferenceRuntime        = 29,
        InvalidMora                 = 30,
        InvalidScore                = 31,
        InvalidNote                 = 32,
        InvalidFrameAudioQuery      = 33,
        InvalidFramePhoneme         = 34,
        IncompatibleQueries         = 35,
    }

    internal enum VoicevoxAccelerationMode : int { Auto = 0, Cpu = 1, Gpu = 2 }

    internal enum VoicevoxOnExistingVoiceModelId : int { Error = 0, Reload = 1, Skip = 2 }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxLoadOnnxruntimeOptions
    {
        // const char *filename;  — NULL なら推奨ファイル名が使われる
        public IntPtr Filename;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxInitializeOptions
    {
        public VoicevoxAccelerationMode AccelerationMode;  // int32_t
        public ushort CpuNumThreads;                       // uint16_t（0 = 環境に合わせる）
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxLoadVoiceModelOptions
    {
        public VoicevoxOnExistingVoiceModelId OnExisting;  // int32_t
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxTtsOptions
    {
        // C の bool は 1 バイト
        [MarshalAs(UnmanagedType.U1)] public bool EnableInterrogativeUpspeak;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxSynthesisOptions
    {
        [MarshalAs(UnmanagedType.U1)] public bool EnableInterrogativeUpspeak;
    }

    internal static class VoicevoxNative
    {
        // Unity は Assets/Plugins/voicevox_core/x86_64/voicevox_core.dll を
        // "voicevox_core" という名前で解決する（拡張子・ディレクトリは付けない）。
        private const string Dll = "voicevox_core";

        // ---- ONNX Runtime ----------------------------------------------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint voicevox_get_onnxruntime_lib_min_required_minor_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint voicevox_get_onnxruntime_lib_max_supported_minor_version();

        /// <summary>推奨されるバージョン付きファイル名。返り値は静的文字列で解放不要。</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_get_onnxruntime_lib_recommended_versioned_filename();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_get_onnxruntime_lib_recommended_unversioned_filename();

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

        /// <summary>返り値は char* — voicevox_json_free で解放</summary>
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr voicevox_voice_model_file_create_metas_json(IntPtr model);

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
```

### 2.1 UTF-8 文字列のヘルパ

`DllImport` の既定マーシャリングは Unity（Mono/IL2CPP）では ANSI（`CharSet.Ansi`）になり、日本語が化ける。
**必ず `byte[]`（UTF-8 + ヌル終端）で渡す**。

```csharp
internal static class Utf8
{
    /// <summary>UTF-8 のヌル終端バイト列に変換する（P/Invoke の in 文字列用）</summary>
    public static byte[] ToNullTerminated(string s)
    {
        var len   = System.Text.Encoding.UTF8.GetByteCount(s);
        var bytes = new byte[len + 1];
        System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        bytes[len] = 0;
        return bytes;
    }

    /// <summary>ネイティブの const char*（UTF-8 ヌル終端）を string にする</summary>
    public static string FromPtr(IntPtr p)
    {
        if (p == IntPtr.Zero) return string.Empty;
        var len = 0;
        while (Marshal.ReadByte(p, len) != 0) len++;
        var buf = new byte[len];
        Marshal.Copy(p, buf, 0, len);
        return System.Text.Encoding.UTF8.GetString(buf);
    }
}
```

> .NET Standard 2.1 には `Marshal.PtrToStringUTF8` があるが、Unity の IL2CPP での挙動が環境によって異なる報告があるため、
> 上記の自前実装を使う（実装後に PlayMode テストで両者を比較し、問題なければ標準 API に寄せる）。

### 2.2 エラー処理のラッパ

```csharp
internal static class Vv
{
    public static void Check(VoicevoxResultCode code, string what)
    {
        if (code == VoicevoxResultCode.Ok) return;
        var msg = Utf8.FromPtr(VoicevoxNative.voicevox_error_result_to_message(code));
        throw new VoicevoxException(code, $"{what} に失敗しました: [{(int)code}] {msg}");
    }
}

public sealed class VoicevoxException : Exception
{
    public int ResultCode { get; }
    internal VoicevoxException(VoicevoxResultCode code, string message) : base(message)
        => ResultCode = (int)code;
}
```

### 2.3 wav のコピーと解放

```csharp
public byte[] Tts(string text, uint styleId)
{
    var textUtf8 = Utf8.ToNullTerminated(text);
    var opts     = VoicevoxNative.voicevox_make_default_tts_options();

    var code = VoicevoxNative.voicevox_synthesizer_tts(
        _synthesizer, textUtf8, styleId, opts, out var lenPtr, out var wavPtr);
    Vv.Check(code, "音声合成");

    try
    {
        var len = checked((int)lenPtr.ToUInt64());
        var wav = new byte[len];
        Marshal.Copy(wavPtr, wav, 0, len);   // ネイティブ → マネージドへコピー
        return wav;
    }
    finally
    {
        VoicevoxNative.voicevox_wav_free(wavPtr);   // 例外が出ても必ず解放
    }
}
```

### 2.4 スレッドとライフサイクル

- `voicevox_synthesizer_tts` は数百 ms〜数秒かかる。**メインスレッドで呼んではいけない**。`Task.Run` またはワーカースレッドで実行し、結果だけをメインスレッドに戻す。
- ヘッダの `voicevox_synthesizer_delete` のコメントに「破棄対象への他スレッドでのアクセスが存在する場合、それらがすべて終わるのを待ってから破棄する」とあるので、破棄自体はスレッドセーフ。ただし破棄後にハンドルを使うのは未定義動作。
- **`Synthesizer` は 1 プロセス 1 個**のシングルトンとし、`DontDestroyOnLoad` の常駐サービス（`Boot.unity`、仮決め K4）が保持する。
- エディタの Play 停止では DLL はアンロードされない（Unity は一度ロードした P/Invoke DLL をエディタ再起動まで保持する）。ハンドルを `static` に持つと、Play 停止後に無効なハンドルが残る。**`[RuntimeInitializeOnLoadMethod]` で必ず初期化し直す**か、`Application.quitting` で破棄してフラグを落とす。

---

## 3. ONNX Runtime の扱い

### 3.1 動的ロードであること

`voicevox_core.h` の冒頭に次のマクロ定義がある（実測）。

```c
//#define VOICEVOX_LINK_ONNXRUNTIME
#define VOICEVOX_LOAD_ONNXRUNTIME
```

ヘッダの Availability 節によれば、リリース版ライブラリでは **iOS のみ `VOICEVOX_LINK_ONNXRUNTIME`、他プラットフォームは `VOICEVOX_LOAD_ONNXRUNTIME`**。
Windows 版は後者なので、`voicevox_onnxruntime.dll` は **実行時に `voicevox_onnxruntime_load_once` で動的ロード**される。

`VoicevoxLoadOnnxruntimeOptions.filename` のコメント（実測）:

> ONNX Runtime のファイル名（モジュール名）もしくはファイルパスを指定する。
> `dlopen`/[`LoadLibraryExW`](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw) の引数に使われる。
> デフォルトは `voicevox_get_onnxruntime_lib_recommended_versioned_filename` と同じ。

つまり **`filename` に絶対パスを渡せば、DLL 検索パスに依存せず確実にロードできる**。これを採用する。

```csharp
var ortPath = Path.Combine(NativeDir, "voicevox_onnxruntime.dll");   // 絶対パス
var pathUtf8 = Utf8.ToNullTerminated(ortPath);
var handle   = GCHandle.Alloc(pathUtf8, GCHandleType.Pinned);
try
{
    var opts = new VoicevoxLoadOnnxruntimeOptions { Filename = handle.AddrOfPinnedObject() };
    Vv.Check(VoicevoxNative.voicevox_onnxruntime_load_once(opts, out _onnxruntime),
             "ONNX Runtime のロード");
}
finally { handle.Free(); }
```

> `voicevox_get_onnxruntime_lib_recommended_versioned_filename()` は Windows では
> `voicevox_get_onnxruntime_lib_recommended_unversioned_filename()` と同じ値を返す（ヘッダのコメントに明記）。
> 既定に任せる場合はファイル名だけで検索されるため、§3.3 の検索パス問題が起きる。**絶対パス指定を既定とする。**

### 3.2 Unity での配置（仮決め K24）

```
Assets/Plugins/voicevox_core/x86_64/
    voicevox_core.dll             ← External から setup-external.ps1 でコピー（git 管理外）
    voicevox_onnxruntime.dll      ← 同上（git 管理外）
```

Plugin Inspector の設定:

| 項目 | 値 |
|---|---|
| Platforms | Standalone のみ |
| Standalone > Windows | x86_64 のみ |
| Editor | 有効（エディタでも動作確認したいため）、CPU = x86_64、OS = Windows |
| Load on startup | 無効（`voicevox_onnxruntime.dll` は `voicevox_core` が自分でロードする） |

`.meta` は Unity が生成するが、**DLL 本体が git 管理外なので `.meta` も `.gitignore` に含める**（`Assets/Plugins/voicevox_core/` ごと除外）。
これにより、External を配置していない環境では Plugin 設定が失われるが、そもそも DLL がないので影響はない。

**実装（2026-09-13、issue #21）**: `.gitignore` に否定パターンを足して `.meta` だけコミットする方法は採らず、
**`Assets/TsumugiQuiz/Scripts/Editor/Tts/VoicevoxPluginPostprocessor.cs`（`AssetPostprocessor.OnPreprocessAsset`）で
`PluginImporter` をコードから設定する**方法を採った。理由:

- `.meta` は Unity が DLL をインポートした後にしか生成されない。DLL が無い環境（External 未配置）に `.meta` だけ存在すると、
  参照先を失った孤児 `.meta` になる。上の §3.2 の方針（`.meta` も除外）とも矛盾する
- 設定を C# のコードとしてコミットすれば、**誰の環境でも `setup-external.ps1` で DLL を置いた瞬間に同じ設定が適用される**
  （GUID の衝突・.meta の手動編集が不要）
- 設定内容（Windows x86_64 のみ、Editor 有効、`isPreloaded = false`）がレビュー可能な差分として残る

### 3.3 ビルド後の配置と DLL 検索パス

Unity の Windows Standalone ビルドでは、`Assets/Plugins/<arch>/` の DLL は次に配置される。

```
Builds/Windows/
    TsumugiQuiz.exe
    TsumugiQuiz_Data/
        Plugins/
            x86_64/
                voicevox_core.dll
                voicevox_onnxruntime.dll
        StreamingAssets/
            voicevox_core/
                open_jtalk_dic_utf_8-1.11/
                models/
                    vvms/
                        0.vvm
        ...
    UnityPlayer.dll
```

**リスク**: `voicevox_core.dll` は Unity が `LoadLibrary` でロードするので見つかるが、そこから `voicevox_onnxruntime.dll` を `LoadLibraryExW` でロードするとき、Windows の DLL 検索順は「`voicevox_core.dll` があるディレクトリ」を**自動では含まない**（`LOAD_WITH_ALTERED_SEARCH_PATH` が指定されていない限り、検索基準はプロセスの実行ファイル = `TsumugiQuiz.exe` のあるディレクトリになる）。

対策は 3 段構え。

1. **`filename` に絶対パスを渡す**（§3.1）。これだけで通常は解決する。絶対パスが渡されたとき `LoadLibraryExW` は検索を行わない。
2. それでも失敗する場合に備え、**ロード前に `SetDllDirectory` でネイティブディレクトリを検索パスに追加**する。

   ```csharp
   [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
   [return: MarshalAs(UnmanagedType.Bool)]
   private static extern bool SetDllDirectoryW(string lpPathName);
   ```
3. 最後の手段として、**ビルド後処理で `voicevox_onnxruntime.dll` を exe の隣にもコピーする**（`IPostprocessBuildWithReport`）。

`NativeDir` の決定:

```csharp
static string NativeDir =>
    Application.isEditor
        ? Path.Combine(Application.dataPath, "Plugins", "voicevox_core", "x86_64")
        // Application.dataPath はビルド時 "…/TsumugiQuiz_Data"
        : Path.Combine(Application.dataPath, "Plugins", "x86_64");
```

#### 実測結果（2026-09-13、issue #21、Unity 6000.6.0f1 / Windows 11 / batchmode PlayMode テスト）

上記 3 段のうち **実際に必要だったのは 1.（`filename` に絶対パス）だけ**だった。

| 確認項目 | 結果 |
|---|---|
| エディタ（batchmode）で `DllImport("voicevox_core")` が `Assets/Plugins/voicevox_core/x86_64/` から解決されるか | **解決された**。`SetDllDirectory` も `LoadLibraryExW` による事前ロードも**行わない状態**で成功（PlayMode テスト `VoicevoxDllSearchPathTests` が `NativeLibraryLoader.LastStrategy == None` を検証） |
| `voicevox_onnxruntime.dll` が絶対パス指定でロードできるか | **できた**。`VoicevoxLoadOnnxruntimeOptions.filename` に `Assets/Plugins/voicevox_core/x86_64/voicevox_onnxruntime.dll` の絶対パスを渡して `voicevox_onnxruntime_load_once` が `Ok` |
| 2.（`SetDllDirectoryW`）・3.（exe 隣へのコピー） | **不要だった**。実装は保険として残すが、`SetDllDirectoryW` は**既定で OFF**（`NativeLibraryLoader.Prepare(nativeDir, useSetDllDirectory: true)` を明示したときだけ使い、プロセス全体の検索パスを汚さないよう `finally` で `SetDllDirectoryW(null)` に戻す）。既定で行うのは `LoadLibraryExW(LOAD_WITH_ALTERED_SEARCH_PATH)` による絶対パス事前ロードのみ |
| 合成の実測値 | 初期化（onnxruntime ロード + 辞書 + synthesizer + `0.vvm` 読み込み）= **541〜597ms**、「こんにちは」の合成 = **217〜244ms**、wav = **45,100 バイト / 24,000Hz**、`voicevox_get_version()` = **0.17.0**、GPU モード = false |
| スタイル解決 | `春日部つむぎ` / `ノーマル` が `Exact` で解決し、実測のスタイル ID は **8**（コードにはハードコードしていない） |

> ビルド後の exe での DLL 解決（`TsumugiQuiz_Data/Plugins/x86_64`）は、配置されることまでを確認した。
> exe を実行しての合成確認は後続 issue（`docs/tts.md` §11.3）。

### 3.4 バージョンの整合

```csharp
var min = VoicevoxNative.voicevox_get_onnxruntime_lib_min_required_minor_version();
var max = VoicevoxNative.voicevox_get_onnxruntime_lib_max_supported_minor_version();
Debug.Log($"voicevox_core が要求する ONNX Runtime 1.x のマイナーバージョン: {min} 以上 {max} 以下（推奨）");
```

ヘッダのコメントによれば、`voicevox_onnxruntime_load_once` は **min 未満なら失敗、max 超過なら警告**。
ダウンローダーが取得する `voicevox_onnxruntime` は 0.17.0 の downloader では `>=1.17.3,<1.24` の範囲から選ばれる（downloader の `SUPPORTED_ONNXRUNTIME_VERSIONS` を実測）。現時点の最新リリースは `voicevox_onnxruntime-1.17.3`（アセット `voicevox_onnxruntime-win-x64-1.17.3.tgz`）。

**実測（2026-09-13、公式ダウンローダー実行結果）**: 実際に選ばれたバージョンは **1.17.3**（`External/voicevox_core/onnxruntime/VERSION_NUMBER` を読んで確認）。展開後の DLL 名は `onnxruntime/lib/voicevox_onnxruntime.dll` で、**バージョン接尾辞は付かなかった**（`voicevox_get_onnxruntime_lib_recommended_versioned_filename` が返す名前をそのまま採用していると推測されるが、実際のファイル名としては非バージョン付きの `voicevox_onnxruntime.dll` になった）。ワイルドカードで検証・コピーする実装（`setup-external.ps1`）はバージョン付きファイル名のケースにも耐える設計のままでよいが、少なくとも本バージョンでは単純に `voicevox_onnxruntime.dll` 固定でも動く。
