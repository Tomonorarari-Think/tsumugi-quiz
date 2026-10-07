using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、現在のフェーズの締め切り（残り時間表示の元）を求める部分（issue #154）。
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 現在のフェーズの締め切り（サーバー時刻軸の秒）。締め切りが無いフェーズでは <c>double.NaN</c>。
        /// クライアントはこの値と自分の <c>ServerTime.Time</c> の差で残り時間を表示する。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 時刻アンカー（<see cref="PhaseStartServerTime"/> / <see cref="BuzzOpenServerTime"/>）は
        /// <c>NetworkVariable</c> で全ピアに届く。制限時間は <see cref="DeadlineTimeLimits"/> を使う
        /// （サーバーは進行に使っている値、クライアントは確定したルーム設定の値）。
        /// フェーズと制限時間の対応は <see cref="QuizDeadlines.DeadlineServerTime"/>（Core）に一本化してある。
        /// </para>
        /// <para>
        /// 以前はクライアントの <c>_limits</c> が常に <see cref="QuizTimeLimits.Default"/> のままで、
        /// ルーム設定で制限時間を変えるとクライアントの表示だけがずれていた（#154）。
        /// </para>
        /// </remarks>
        public double CurrentDeadlineServerTime =>
            QuizDeadlines.DeadlineServerTime(
                _phase.Value, _phaseStartServerTime.Value, _buzzOpenServerTime.Value, DeadlineTimeLimits);

        /// <summary>
        /// 締め切りの計算に使う制限時間（issue #154）。
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        ///   <item><description>
        ///     サーバー（ホスト）・未スポーン: <see cref="Configure"/> / <see cref="StartSession"/> が確定させた
        ///     進行用の値（<c>_limits</c>）。これが締め切りの唯一の権威。
        ///   </description></item>
        ///   <item><description>
        ///     クライアント: 同じ <c>NetworkObject</c> 上の <see cref="RoomSettingsSync.Current"/> の制限時間。
        ///     サーバーはゲーム開始操作の時点で「進行に使う値」をルーム設定へ書き戻してからロックする
        ///     （<see cref="CommitRoomSettingsForStart"/>）ので、範囲内の値ならサーバーと一致する。
        ///     ルーム設定は出題（Reading）より前に届くため、BuzzOpen 以降の表示には間に合う。
        ///   </description></item>
        ///   <item><description>
        ///     クライアントで <see cref="RoomSettingsSync"/> が無い・未スポーンの構成では従来どおり
        ///     <c>_limits</c>（＝ <see cref="QuizTimeLimits.Default"/>）。この構成でサーバーが既定値以外を使うと
        ///     表示がずれるため、サーバー側が <see cref="WarnIfLimitsNotSynced"/> で警告する。
        ///   </description></item>
        /// </list>
        /// </remarks>
        internal QuizTimeLimits DeadlineTimeLimits
        {
            get
            {
                if (!IsSpawned || IsServer)
                {
                    return _limits;
                }

                var sync = SettingsSync;
                RoomSettings settings = sync != null && sync.IsSpawned ? sync.Current : null;
                return settings?.TimeLimits ?? _limits;
            }
        }
    }
}
