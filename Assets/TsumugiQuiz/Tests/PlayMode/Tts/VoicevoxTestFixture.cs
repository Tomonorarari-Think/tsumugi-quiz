using TsumugiQuiz.Tts;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 実 DLL を使うテストの前提チェック（docs/tts.md §11.2）。
    /// External を配置していない環境ではテストをスキップするために使う。
    /// </summary>
    internal static class VoicevoxTestFixture
    {
        /// <summary>現在の環境で解決した配置。</summary>
        public static VoicevoxLocation Location => VoicevoxPaths.Resolve();

        /// <summary>ネイティブ DLL・辞書・音声モデルがすべてそろっているか。</summary>
        public static bool IsAvailable => Location.Readiness == TtsReadiness.Ready;

        /// <summary>スキップ時に表示する説明。</summary>
        public static string SkipReason =>
            "voicevox_core / 辞書 / .vvm が配置されていないためスキップします " +
            $"(scripts/setup-external.ps1 を実行してください: {Location.Describe()})";
    }
}
