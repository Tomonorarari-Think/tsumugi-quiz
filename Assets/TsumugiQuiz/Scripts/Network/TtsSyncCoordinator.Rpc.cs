using System;
using System.Threading;
using TsumugiQuiz.Core.Audio;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="TtsSyncCoordinator"/> のうち、RPC 定義（docs/network.md §1.5 / §7.3、docs/tts.md §6.2）と
    /// クライアント側の合成・報告をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <see cref="TtsReadyRpc"/> はクライアント → サーバーで、送信元は
    /// <c>rpcParams.Receive.SenderClientId</c> から取る（引数で受け取ると詐称できるため）。
    /// <see cref="PlayAtRpc"/> はサーバー → 全員で、<c>InvokePermission = RpcInvokePermission.Server</c> と
    /// <c>private</c> によりクライアントからは送れない（偽の再生時刻を配れない）。
    /// </remarks>
    public sealed partial class TtsSyncCoordinator
    {
        /// <summary>RPC の頻度制限（docs/network.md §9、#52）。初回アクセス時に生成する。</summary>
        private RpcRateGuard _rpcRateGuard;

        /// <summary>このコンポーネント専用のレート制限ガード（<see cref="TtsReadyRpc"/> で使う）。</summary>
        private RpcRateGuard RpcRateGuard => _rpcRateGuard ??= new RpcRateGuard(NetworkManager, nameof(TtsSyncCoordinator));

        /// <summary>
        /// 棄却ログの間引き（クライアント単位で 1 秒 1 回、docs/network.md §9）。
        /// 実体は <see cref="RpcRejectLogger"/>（#72、3 系統に分裂していた間引き実装の統合先）。
        /// 初回アクセス時に生成する（<see cref="RpcRateGuard"/> と同じ遅延生成の方針）。
        /// </summary>
        private RpcRejectLogger _rejectLogger;

        /// <summary>このコンポーネント専用の棄却ログロガー。</summary>
        private RpcRejectLogger RejectLogger => _rejectLogger ??= new RpcRejectLogger(NetworkManager);

        /// <summary>
        /// 読み上げの準備完了通知（クライアント → サーバー、docs/tts.md §6.2）。
        /// 読み上げを行わないクライアントも長さ 0 で即座に報告し、全員の進行を止めない。
        /// </summary>
        /// <param name="questionIndex">準備できた問題インデックス。</param>
        /// <param name="durationSec">合成した音声の長さ（秒）。読み上げない場合は 0。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。送信元 ID はここから取る。</param>
        [Rpc(SendTo.Server)]
        internal void TtsReadyRpc(int questionIndex, double durationSec, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (!_readyTracker.IsAwaiting)
            {
                // Ready 待ちを始めていない（読み上げ無効・読み上げ不可、またはタイムアウトで打ち切った後）。
                // クライアントは「サーバーが待っているか」を知らずに報告するため、これは正常な経路なので
                // ログには残さない（タイムアウトした場合の未報告クライアントは打ち切り時に記録済み）。
                return;
            }

            if (!_readyTracker.TryReportReady(senderId, questionIndex, durationSec, out var rejectReason))
            {
                // 棄却理由はサーバーのログにだけ残す（クライアントには返さない、docs/network.md §9）。
                LogRejected(
                    senderId,
                    $"[TtsSyncCoordinator] 読み上げ準備の通知を棄却しました"
                    + $"（送信元 {senderId}、問題 {questionIndex}、理由: {rejectReason}）。");
                return;
            }

            if (_readyTracker.IsComplete)
            {
                CompleteReadyRound(timedOut: false);
            }
        }

        /// <summary>
        /// 読み上げの再生開始時刻の配信（サーバー → 全員、docs/tts.md §6.1）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="playAtServerTime">再生開始のサーバー時刻（秒）。</param>
        /// <param name="durationSec">読み上げ時間（秒）。<b>ホストの合成結果</b>（docs/tts.md §6.2）。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void PlayAtRpc(int questionIndex, double playAtServerTime, double durationSec)
        {
            // 送信元がサーバーであっても受信データは検証する（docs/network.md §9）。
            if (questionIndex < 0
                || double.IsNaN(playAtServerTime) || double.IsInfinity(playAtServerTime)
                || double.IsNaN(durationSec) || double.IsInfinity(durationSec)
                || durationSec < 0d || durationSec > TtsReadyTracker.MaxReportedDurationSec)
            {
                Debug.LogWarning(
                    "[TtsSyncCoordinator] 再生開始の配信が不正だったため読み上げません"
                    + $"（問題 {questionIndex}、playAt {playAtServerTime}、長さ {durationSec}）。");
                return;
            }

            if (questionIndex == _resyncedQuestionIndex)
            {
                // 途中参加・再接続で合流した問題（#109）。合成していないので鳴らせない。
                // 想定内なので警告ではなく情報として残す（docs/tts.md §6.7）。
                // ただし文字送りは読み上げの時間軸に合わせられるので、記録と通知は行う
                // （捨てる理由は音声の再生予約だけ。#144 再レビュー 2 回目の統括判断）。
                Debug.Log(
                    $"[TtsSyncCoordinator] 途中参加・再接続で合流した問題 {questionIndex} の読み上げは行いません"
                    + "（文字送りの時間軸としてだけ使います）。");
                RecordReading(questionIndex, playAtServerTime, durationSec);
                ReadingScheduled?.Invoke(questionIndex, playAtServerTime, durationSec);
                return;
            }

            if (questionIndex != _shownQuestionIndex)
            {
                // 提示されていない問題（データが届いていない／既に次の問題へ進んでいる）。
                // 古い読み上げを鳴らさないよう捨てる。
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 現在の問題（{_shownQuestionIndex}）と一致しない読み上げの配信"
                    + $"（問題 {questionIndex}）を無視しました。");
                return;
            }

            if (questionIndex < LastReadingQuestionIndex)
            {
                // 再送・順序入れ替わりで届いた古い配信。
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 古い読み上げの配信（問題 {questionIndex}、"
                    + $"直近は {LastReadingQuestionIndex}）を無視しました。");
                return;
            }

            RecordReading(questionIndex, playAtServerTime, durationSec);

            // 自分の時計（LocalTime）で playAtServerTime まで何秒あるかを渡す
            // （docs/tts.md §6.1 / docs/network.md §7.3 の leadSec の式）。
            ResolvePlayback()?.Schedule(
                questionIndex, playAtServerTime, NetworkManager.LocalTime.Time, durationSec);

            ReadingScheduled?.Invoke(questionIndex, playAtServerTime, durationSec);
        }

        /// <summary>
        /// 読み上げの初期化を始める（クライアント側、docs/tts.md §6.5）。
        /// 失敗しても例外は投げず、読み上げなしで進行できる状態にする。
        /// </summary>
        private async void InitializeReadingAsync()
        {
            var playback = ResolvePlayback();
            if (playback == null)
            {
                return;
            }

            var token = _lifetime?.Token ?? CancellationToken.None;
            try
            {
                await playback.InitializeAsync(token);
            }
            catch (OperationCanceledException)
            {
                // セッションを抜けた。初期化は打ち切ってよい（読み上げなしで進行できる）。
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 読み上げの初期化に失敗しました（{e.GetType().Name}: {e.Message}）。"
                    + "読み上げなしで進行します。");
            }
        }

        /// <summary>
        /// 読み上げ音声を用意し、準備完了をサーバーへ報告する（クライアント側）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="readingText">読み上げるテキスト。</param>
        private async void PrepareReadingAsync(int questionIndex, string readingText)
        {
            CancelPrepare();

            var playback = ResolvePlayback();
            if (playback == null || !playback.IsReadingPossible || string.IsNullOrWhiteSpace(readingText))
            {
                // 読み上げを行わないクライアント（未同意・配置不足・読み上げ不可）は即座に Ready を返す。
                // 長さは 0 で報告し、全員に配る値はホストのものを使う（docs/tts.md §6.2）。
                // 配信の前に撤回・未同意だったクライアントはここ以外にログが出ないため、理由付きで 1 行残す
                // （issue #164。配信の後の撤回は TtsSyncPlayer 側のログ（FR-75）で追える）。
                Debug.Log(
                    $"[TtsSyncCoordinator] 問題 {questionIndex} は読み上げません"
                    + $"（{DescribeSkipReason(playback, readingText)}）。");
                ReportReady(questionIndex, 0d);
                return;
            }

            var prepare = CancellationTokenSource.CreateLinkedTokenSource(_lifetime?.Token ?? CancellationToken.None);
            _prepare = prepare;

            var durationSec = 0d;
            try
            {
                durationSec = await playback.PrepareAsync(questionIndex, readingText, Speed, prepare.Token);
            }
            catch (OperationCanceledException)
            {
                // 次の問題へ移った / セッションを抜けた。古い合成の結果は捨てる。
                DisposePrepare(prepare);
                return;
            }
            catch (Exception e)
            {
                // 合成の失敗で進行を止めない（docs/tts.md §9）。読み上げなしとして Ready を返す。
                Debug.LogWarning(
                    $"[TtsSyncCoordinator] 問題 {questionIndex} の読み上げを用意できませんでした"
                    + $"（{e.GetType().Name}: {e.Message}）。読み上げなしで続行します。");
            }

            var isCurrent = !prepare.IsCancellationRequested && ReferenceEquals(_prepare, prepare);
            DisposePrepare(prepare);

            if (!isCurrent)
            {
                // 次の問題の用意が始まっている。古い結果は報告しない。
                return;
            }

            ReportReady(questionIndex, durationSec);
        }

        /// <summary>
        /// <see cref="PrepareReadingAsync"/> が読み上げを省いた理由を、ログ用の日本語 1 文で返す（issue #164）。
        /// </summary>
        /// <remarks>
        /// <c>Network</c> 層は <c>Tts</c> 層を参照できない（docs/architecture.md §3）ため、
        /// <see cref="IReadingPlayback.IsReadingPossible"/> が false になった具体的な理由
        /// （未同意・読み上げ無効・初期化未完了）はここでは区別できない。区別するには
        /// <c>Core</c> 層のインターフェースを広げる必要があり、ログ 1 行を出すだけの本 issue の
        /// 範囲としては見合わないため、まとめて 1 つの文言にする（統括判断）。
        /// </remarks>
        /// <param name="playback">読み上げの窓口（<see langword="null"/> なら未搭載）。</param>
        /// <param name="readingText">読み上げるテキスト。</param>
        /// <returns>理由を表す短い日本語の文。</returns>
        private static string DescribeSkipReason(IReadingPlayback playback, string readingText)
        {
            if (playback == null)
            {
                return "読み上げの窓口がありません";
            }

            if (string.IsNullOrWhiteSpace(readingText))
            {
                return "読み上げるテキストが空です";
            }

            // playback.IsReadingPossible が false。未同意・読み上げ無効・初期化未完了のいずれか
            // （区別しない理由は上の remarks を参照）。
            return "未同意・読み上げ無効・準備未完了のいずれか";
        }

        /// <summary>使い終わった取り消しトークンを破棄する（現在のものなら参照も外す）。</summary>
        /// <param name="prepare">破棄する取り消しトークン。</param>
        private void DisposePrepare(CancellationTokenSource prepare)
        {
            if (ReferenceEquals(_prepare, prepare))
            {
                _prepare = null;
            }

            prepare.Dispose();
        }

        /// <summary>準備完了をサーバーへ報告する（スポーン中のクライアントのみ）。</summary>
        /// <param name="questionIndex">問題インデックス。</param>
        /// <param name="durationSec">音声の長さ（秒）。</param>
        private void ReportReady(int questionIndex, double durationSec)
        {
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsClient)
            {
                return;
            }

            TtsReadyRpc(questionIndex, durationSec);
        }

        /// <summary>
        /// 棄却をサーバーのログにだけ残す（docs/network.md §9）。
        /// 同じクライアントからの連続した棄却は <see cref="RpcRejectLogger"/> により 1 秒 1 回へ間引く（#72）。
        /// サーバー時刻の取得は <see cref="RpcRejectLogger"/> 側に集約した（#83）。
        /// </summary>
        /// <param name="clientId">棄却したクライアント ID。</param>
        /// <param name="message">ログに残す内容。</param>
        private void LogRejected(ulong clientId, string message) => RejectLogger.LogRejected(clientId, message);
    }
}
