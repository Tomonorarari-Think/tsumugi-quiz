using System;
using TsumugiQuiz.Core;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、途中参加・再接続したクライアントへの再同期をまとめた部分
    /// （#19、<c>network.allowLateJoin</c>）。セッション進行そのものは GameSession.Session.cs にある。
    /// </summary>
    /// <remarks>
    /// フェーズ・問題インデックス・出題列の長さ・得点表は <c>NetworkVariable</c> / <c>NetworkList</c> で
    /// スポーン時に同期されるが、問題データ（DTO）と提示の合図は RPC なので後から参加した
    /// クライアントには届かない。ここではその 2 つを指定クライアントだけに送り直す。
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 再同期で受け取る出題列の長さの上限（受信検証用、docs/network.md §9）。
        /// 問題セットは 1 ファイル数百問の想定なので、これを超える値は壊れた配信とみなす。
        /// </summary>
        public const int MaxSessionQuestions = 10000;

        /// <summary>再同期の応答に読み上げの時間軸を載せないことを表す問題インデックス（#144）。</summary>
        private const int NoReadingQuestionIndex = -1;

        /// <summary>
        /// 途中参加・再接続で現在の進行状態を受け取ったとき。
        /// <see cref="ResyncClient"/> を受けたクライアントでのみ発火する。
        /// </summary>
        /// <remarks>
        /// <b>本番の購読者は <c>LobbyView</c> ただ 1 つ</b>（#117、PR #123 レビュー L-1）。
        /// 合図を根拠に Result View / Game View へ移るのはロビーの責務で、他の画面は購読しない。
        /// 購読者を増やす場合は、合図が <see cref="TryConsumePendingResync"/> で
        /// <b>1 回しか取り出せない</b>（先に取った側が消費する）ことに注意すること。
        /// public のままにしてあるのは、<c>TsumugiQuiz.UI</c> が別アセンブリだからで、
        /// 診断・テスト以外の用途を想定しているわけではない。
        /// </remarks>
        public event Action<SessionStateSnapshot> SessionResynced;

        /// <summary>
        /// 受信済みでまだ誰も消費していない再同期の状態（#117）。未受信・消費済みなら null。
        /// </summary>
        /// <remarks>
        /// <see cref="SessionResynced"/> は「届いた瞬間」にしか発火しないが、再同期の RPC は
        /// 接続完了直後（クライアントがまだ Join 画面に居る間）に届きうるため、
        /// 画面側が購読を張る前に取りこぼす。合流時点のフェーズを画面遷移の根拠に使う
        /// <c>LobbyView</c> のために、直近の 1 件だけ保持しておく。
        /// </remarks>
        private SessionStateSnapshot? _pendingResync;

        /// <summary>
        /// 受信済みの再同期の状態を 1 件だけ取り出す（取り出したら消える、#117）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 合流したクライアントの画面遷移（<c>LobbyView</c>）は、<see cref="Phase"/> の現在値ではなく
        /// <b>この値</b>を根拠にする。<see cref="Phase"/> は「ロビーへ戻る」直後にも
        /// <c>ReturnToLobbyRpc</c> より先に古い値（<see cref="QuizPhase.Result"/> /
        /// <see cref="QuizPhase.Finished"/>）として観測されうるため、それを遷移条件にすると
        /// ロビーへ戻った直後のクライアントが結果画面へ跳ね返る（PR #104 レビュー H-A）。
        /// 本メソッドが返すのは<b>サーバーが「あなたはいま合流した」と名指しで送った</b>
        /// <c>SessionStateRpc</c> の中身だけなので、その競合は起きない。
        /// </para>
        /// <para>
        /// 一度しか返さないのは、消費されずに残った値で後から（例えば「ロビーへ戻る」→ 再入場で）
        /// 二度目の遷移が起きないようにするため。<c>ReturnToLobbyRpc</c> の受信時にも捨てる
        /// （<see cref="ClearPendingResync"/>）。
        /// </para>
        /// <para>
        /// <b>本番の呼び出し元は <c>LobbyView</c> ただ 1 つ</b>（PR #123 レビュー L-1）。
        /// 複数の画面から呼ぶと、先に呼んだ側が合図を消費して後続が遷移できなくなる。
        /// </para>
        /// </remarks>
        /// <param name="snapshot">取り出した状態。無ければ既定値。</param>
        /// <returns>未消費の状態があれば true。</returns>
        public bool TryConsumePendingResync(out SessionStateSnapshot snapshot)
        {
            if (_pendingResync == null)
            {
                snapshot = default;
                return false;
            }

            snapshot = _pendingResync.Value;
            _pendingResync = null;
            return true;
        }

        /// <summary>
        /// 未消費の再同期の状態を捨てる（#117）。<c>ReturnToLobbyRpc</c> の受信時とデスポーン時に呼ぶ。
        /// </summary>
        internal void ClearPendingResync() => _pendingResync = null;

        /// <summary>
        /// 途中参加・再接続したクライアントへ、現在の進行状態と現在問のデータを送り直す（サーバーのみ）。
        /// </summary>
        /// <remarks>
        /// <b>呼び出し元はロビー（#7 の後続）</b>（統括判断 2026-09-13）。
        /// 接続そのものの可否（<c>network.allowLateJoin</c>）はロビーが判断し、
        /// ゲーム中の参加を承認したあとにロビーが本メソッドを呼ぶ。
        /// <see cref="GameSession"/> は接続イベントを購読せず、自動では再同期しない。
        /// #109 で実際の配線（<c>LobbyState.Server.cs</c> の <c>ResyncIfSessionInProgress</c>、
        /// サーバー側の接続完了時）を入れた。
        /// </remarks>
        /// <param name="clientId">再同期するクライアント ID。</param>
        /// <returns>送信できたら true。</returns>
        public bool ResyncClient(ulong clientId)
        {
            if (!IsSpawned || !IsServer || _machine == null)
            {
                Debug.LogWarning("[GameSession] 再同期はサーバーでのみ行えます。");
                return false;
            }

            if (clientId == NetworkManager.LocalClientId)
            {
                // ホスト自身は同じプロセスで状態を持っているので送る必要がない。
                return false;
            }

            if (!IsClientConnected(clientId))
            {
                Debug.LogWarning($"[GameSession] 接続していないクライアント {clientId} には再同期できません。");
                return false;
            }

            var questionIndex = _machine.QuestionIndex;
            if (questionIndex >= 0
                && _distributor != null
                && !_distributor.TryResendTo(clientId, questionIndex, out var error))
            {
                // 問題データを送り直せなくても、状態だけは送って UI を進める（進行はサーバー権威）。
                Debug.LogWarning($"[GameSession] 問題 {questionIndex} を再送できませんでした: {error}");
            }

            // #144: 現在の問題の読み上げの時間軸（再生開始時刻・長さ）も載せる。合流した PC は読み上げを
            // 鳴らさない（docs/tts.md §6.7）が、文字送りは同じ時間軸で途中から揃えられるようにする。
            var readingQuestionIndex = NoReadingQuestionIndex;
            var readingPlayAtServerTime = 0d;
            var readingDurationSec = 0d;
            var coordinator = GetComponent<TtsSyncCoordinator>();
            if (coordinator != null
                && coordinator.TryGetReading(questionIndex, out readingPlayAtServerTime, out readingDurationSec))
            {
                readingQuestionIndex = questionIndex;
            }

            SessionStateRpc(
                _machine.Phase,
                questionIndex,
                _machine.TotalQuestions,
                readingQuestionIndex,
                readingPlayAtServerTime,
                readingDurationSec,
                RpcTarget.Single(clientId, RpcTargetUse.Temp));
            return true;
        }

        /// <summary>
        /// 途中参加・再接続したクライアントへの現在状態（サーバー → 指定クライアント）。
        /// 問題データ（DTO）は直前に <see cref="QuestionDistributor.TryResendTo"/> が送っている。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="questionIndex">現在の問題インデックス（出題列上の位置）。未出題なら -1。</param>
        /// <param name="totalQuestions">出題列の長さ。</param>
        /// <param name="readingQuestionIndex">
        /// 読み上げの時間軸を載せた問題インデックス（#144）。サーバーが現在の問題の再生開始時刻を記録していなければ
        /// <see cref="NoReadingQuestionIndex"/>。
        /// </param>
        /// <param name="readingPlayAtServerTime">その問題の再生開始時刻（サーバー時刻軸の秒）。</param>
        /// <param name="readingDurationSec">その問題の読み上げ時間（秒、ホストの値）。</param>
        /// <param name="rpcParams">送信先（NGO が埋める受信情報）。</param>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void SessionStateRpc(
            QuizPhase phase,
            int questionIndex,
            int totalQuestions,
            int readingQuestionIndex,
            double readingPlayAtServerTime,
            double readingDurationSec,
            RpcParams rpcParams = default)
        {
            // サーバー発でも受信データは検証する（docs/network.md §9）。
            if (!IsDefinedPhase(phase))
            {
                Debug.LogWarning($"[GameSession] 未定義のフェーズを含む再同期を破棄しました（{(int)phase}）。");
                return;
            }

            if (totalQuestions < 0 || totalQuestions > MaxSessionQuestions)
            {
                Debug.LogWarning($"[GameSession] 総問題数が不正な再同期を破棄しました（{totalQuestions}）。");
                return;
            }

            // 出題列の長さが確定している（StartSession 済み）ときだけ範囲を照合する。
            // 単問モード（Configure + StartQuestion のみ）では totalQuestions が 0 のまま
            // 問題インデックス 0 が正当なため（QuizStateMachine.StartQuestion と同じ規則、#109）。
            // その場合も上限（MaxSessionQuestions）だけは必ず見る。総問題数の検証をすり抜けた
            // 巨大なインデックスを通さないため（PR #114 レビュー L-2）。
            if (questionIndex < -1
                || questionIndex >= MaxSessionQuestions
                || (totalQuestions > 0 && questionIndex >= totalQuestions))
            {
                Debug.LogWarning(
                    $"[GameSession] 問題インデックスが不正な再同期を破棄しました（{questionIndex} / {totalQuestions}）。");
                return;
            }

            var snapshot = new SessionStateSnapshot(phase, questionIndex, totalQuestions);

            // 画面遷移の根拠として使えるよう、購読者が居なくても 1 件だけ保持しておく（#117）。
            // 接続完了直後に届くため、ロビー画面が購読を張る前に来ることがある。
            _pendingResync = snapshot;

            SessionResynced?.Invoke(snapshot);

            if (questionIndex < 0 || _distributor == null)
            {
                return;
            }

            if (readingQuestionIndex == questionIndex)
            {
                // 提示（下の RaiseQuestionShown）より先に記録しておき、文字送りがこの時間軸を読めるようにする。
                // 値の検証は TtsSyncCoordinator 側で行う（不正なら記録しない）。別の問題の値は使わない。
                var readingCoordinator = GetComponent<TtsSyncCoordinator>();
                if (readingCoordinator != null)
                {
                    readingCoordinator.ApplyResyncReading(
                        readingQuestionIndex, readingPlayAtServerTime, readingDurationSec);
                }
            }

            if (_distributor.TryGetQuestion(questionIndex, out var question))
            {
                // 提示の合図（QuestionShownRpc）も後から参加したクライアントには届いていないので、
                // 再同期で受け取った DTO をそのまま UI へ渡す。経路を Resync として渡すことで、
                // 読み上げ（#23）の合成やゲーム開始ジングルは動かさない（#109 レビュー M-2 / L-4）。
                RaiseQuestionShown(questionIndex, question, QuestionShownSource.Resync);
            }
        }

        /// <summary><see cref="QuizPhase"/> として定義済みの値か（受信検証用）。</summary>
        /// <remarks>
        /// #117: 以前は「<see cref="QuizPhase.Lobby"/> 以上 <see cref="QuizPhase.Finished"/> 以下」という
        /// 範囲判定だったため、その外側に定義されている <see cref="QuizPhase.ChoiceAnswering"/>（= 8）が
        /// 未定義扱いになり、選択式の出題中に合流したクライアントの再同期がまるごと捨てられていた。
        /// 判定は列挙の定義そのものを見る <see cref="QuizPhases.IsDefined"/>（Core、EditMode でテスト）へ寄せた。
        /// </remarks>
        private static bool IsDefinedPhase(QuizPhase phase) => QuizPhases.IsDefined(phase);
    }
}
