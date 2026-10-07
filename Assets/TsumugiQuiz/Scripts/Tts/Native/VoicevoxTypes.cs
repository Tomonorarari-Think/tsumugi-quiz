using System;
using System.Runtime.InteropServices;

namespace TsumugiQuiz.Tts.Native
{
    /// <summary>
    /// voicevox_core の処理結果コード。
    /// 値は External/voicevox_core/c_api/include/voicevox_core.h (0.17.0) の
    /// enum VoicevoxResultCode を実際に読んで転記したもの。
    /// </summary>
    public enum VoicevoxResultCode
    {
        Ok = 0,
        NotLoadedOpenjtalkDict = 1,
        GetSupportedDevices = 3,
        GpuSupport = 4,
        StyleNotFound = 6,
        ModelNotFound = 7,
        RunModel = 8,
        AnalyzeText = 11,
        InvalidUtf8Input = 12,
        ParseKana = 13,
        InvalidAudioQuery = 14,
        InvalidAccentPhrase = 15,
        OpenZipFile = 16,
        ReadZipEntry = 17,
        ModelAlreadyLoaded = 18,
        LoadUserDict = 20,
        SaveUserDict = 21,
        UserDictWordNotFound = 22,
        UseUserDict = 23,
        InvalidUserDictWord = 24,
        InvalidUuid = 25,
        StyleAlreadyLoaded = 26,
        InvalidModelData = 27,
        InvalidModelFormat = 28,
        InitInferenceRuntime = 29,
        InvalidMora = 30,
        InvalidScore = 31,
        InvalidNote = 32,
        InvalidFrameAudioQuery = 33,
        InvalidFramePhoneme = 34,
        IncompatibleQueries = 35,
    }

    /// <summary>ハードウェアアクセラレーションモード（voicevox_core.h: VoicevoxAccelerationMode）。</summary>
    internal enum VoicevoxAccelerationMode
    {
        Auto = 0,
        Cpu = 1,
        Gpu = 2,
    }

    /// <summary>同じ ID の音声モデルが既に読み込まれていたときのふるまい。</summary>
    internal enum VoicevoxOnExistingVoiceModelId
    {
        Error = 0,
        Reload = 1,
        Skip = 2,
    }

    /// <summary>voicevox_onnxruntime_load_once のオプション。filename は UTF-8 ヌル終端文字列へのポインタ。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxLoadOnnxruntimeOptions
    {
        public IntPtr Filename;
    }

    /// <summary>voicevox_synthesizer_new のオプション。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxInitializeOptions
    {
        public VoicevoxAccelerationMode AccelerationMode; // int32_t
        public ushort CpuNumThreads;                      // uint16_t（0 = 環境に合わせる）
    }

    /// <summary>voicevox_synthesizer_load_voice_model のオプション。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxLoadVoiceModelOptions
    {
        public VoicevoxOnExistingVoiceModelId OnExisting; // int32_t
    }

    /// <summary>voicevox_synthesizer_tts のオプション。C の bool は 1 バイト。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxTtsOptions
    {
        [MarshalAs(UnmanagedType.U1)] public bool EnableInterrogativeUpspeak;
    }

    /// <summary>voicevox_synthesizer_synthesis のオプション。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct VoicevoxSynthesisOptions
    {
        [MarshalAs(UnmanagedType.U1)] public bool EnableInterrogativeUpspeak;
    }
}
