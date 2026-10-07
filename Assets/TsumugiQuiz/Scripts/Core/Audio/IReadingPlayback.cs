using System.Threading;
using System.Threading.Tasks;

namespace TsumugiQuiz.Core.Audio
{
    /// <summary>
    /// 読み上げ音声の用意（合成）と、サーバー時刻に合わせた再生予約の窓口（docs/tts.md §6、#23）。
    /// 実装は <c>TsumugiQuiz.Tts.TtsSyncPlayer</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// この抽象を <c>Core</c>（純 C#）に置いているのは asmdef の依存方向のため
    /// （docs/architecture.md §3。<c>Core ← Questions/Room ← Network/Tts ← UI</c> の一方向で、
    /// <c>Network</c> と <c>Tts</c> は同じ層なので互いに参照しない）。
    /// 同期の司令塔である <c>TsumugiQuiz.Network.TtsSyncCoordinator</c> は
    /// このインターフェース越しに読み上げを扱い、実装は同じ <c>GameObject</c> 上の
    /// コンポーネントを <c>GetComponent&lt;IReadingPlayback&gt;()</c> で見つける。
    /// </para>
    /// <para>
    /// 読み上げが使えない場合（<c>tts.enabled=false</c>・未同意・配置不足・合成失敗）でも
    /// <b>例外を投げず</b>、読み上げなしで進行できるようにする（docs/tts.md §9）。
    /// </para>
    /// </remarks>
    public interface IReadingPlayback
    {
        /// <summary>
        /// いま読み上げを頼める見込みがあるか（初期化中は true）。
        /// false のとき、司令塔は Ready 待ちを行わず読み上げ自体を省く。
        /// </summary>
        bool IsReadingPossible { get; }

        /// <summary>
        /// 読み上げの初期化（<c>TtsService.EnsureInitializedAsync</c>）を行う。
        /// 読み上げを使う画面の起動シーケンスから 1 度呼ぶ（docs/tts.md §6.5）。
        /// 多重に呼んでも初期化は 1 回だけで、失敗しても例外は投げない。
        /// </summary>
        /// <param name="cancellationToken">取り消し（セッションを抜けたときなど）。</param>
        /// <returns>初期化の完了を表す <see cref="Task"/>。</returns>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 1 問分の読み上げ音声を用意する（キャッシュ照会 → 合成 → <c>AudioClip</c> 化）。
        /// 前問のクリップはここで解放する（docs/tts.md §6.3）。<b>メインスレッドから呼ぶこと。</b>
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="readingText">読み上げるテキスト（空なら問題文を渡すのは呼び出し側の責務）。</param>
        /// <param name="speed">読み上げ速度（<c>tts.speed</c>）。</param>
        /// <param name="cancellationToken">取り消し。</param>
        /// <returns>用意できた音声の長さ（秒）。読み上げない場合は 0。</returns>
        Task<double> PrepareAsync(int questionIndex, string readingText, float speed, CancellationToken cancellationToken);

        /// <summary>
        /// 再生開始時刻を予約する（docs/tts.md §6.1）。
        /// まだ合成が終わっていなければ、終わり次第そのタイミングで再生する。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="playAtServerTime">サーバーが指定した再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="referenceServerTimeNow">
        /// <paramref name="playAtServerTime"/> と同じ時刻軸での「いま」（NGO の <c>LocalTime.Time</c>）。
        /// 呼び出し側が渡すのは、<c>Tts</c> 層から NGO を参照しないため。
        /// </param>
        /// <param name="hostDurationSec">ホストが合成した音声の長さ（秒）。ホストの値を正とする（docs/tts.md §6.2）。</param>
        void Schedule(int questionIndex, double playAtServerTime, double referenceServerTimeNow, double hostDurationSec);

        /// <summary>
        /// 予約と再生を取り消し、保持している音声を解放する。
        /// 実装（<c>TtsSyncPlayer</c>）は、何かが進行中だった場合に限り、中断されたことを
        /// 購読者へ伝える（issue #24 レビュー M4。何も進行していなければ通知しない）。
        /// </summary>
        void CancelReading();
    }
}
