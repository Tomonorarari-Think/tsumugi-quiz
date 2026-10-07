using System;
using System.Threading;
using System.Threading.Tasks;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 合成エンジンの差し替え口。本番実装は <c>TtsSynthesisEngine</c>（voicevox_core）。
    ///
    /// <see cref="TtsService.Initialize"/> に別の生成関数を渡すことで、
    /// ネイティブ DLL を使わないフェイクに差し替えられる（EditMode テスト用）。
    /// ここに出てくる値はすべて<b>キャッシュキーの構成要素</b>（docs/tts.md §7.1）なので、
    /// フェイクでも安定した値を返すこと。
    ///
    /// <see cref="SynthesizeAsync"/> は<b>メインスレッド外</b>で重い処理を行う契約で、
    /// 実装側で同時実行数を 1 に絞る（docs/tts.md §7.4）。
    /// </summary>
    public interface ITtsSynthesisEngine : IDisposable
    {
        /// <summary>解決されたスタイル（docs/tts.md §4.1 のフォールバック順を適用済み）。</summary>
        VoicevoxStyleResolution Style { get; }

        /// <summary>解決された話者名（キャッシュキーに入る）。</summary>
        string SpeakerName { get; }

        /// <summary>解決されたスタイル名（キャッシュキーに入る）。</summary>
        string StyleName { get; }

        /// <summary>voicevox_core のバージョン（キャッシュキーに入る）。</summary>
        string CoreVersion { get; }

        /// <summary>音声モデルのバージョン（キャッシュキーに入る）。</summary>
        string ModelsVersion { get; }

        /// <summary>テキストを合成して wav バイト列（RIFF / 16bit PCM）を返す。</summary>
        /// <exception cref="OperationCanceledException">キャンセルされたとき</exception>
        Task<byte[]> SynthesizeAsync(string text, float speed, CancellationToken cancellationToken);

        /// <summary>ログ用の説明文。</summary>
        string Describe();
    }

    /// <summary>
    /// 合成エンジンの生成関数。<b>メインスレッド外</b>で呼ばれ、数秒かかってよい。
    /// 配置不足なら <see cref="TtsSetupException"/>（UI に出してよい文言）を投げる。
    /// </summary>
    public delegate ITtsSynthesisEngine TtsSynthesisEngineFactory(VoicevoxLocation location, TtsSettings settings);
}
