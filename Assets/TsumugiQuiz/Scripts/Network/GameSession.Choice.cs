using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、選択式（<c>choice</c>）の回答送信・一斉判定の配信をまとめた部分
    /// （<b>仮決め: #17</b>、docs/question-data.md §6、docs/room-settings.md
    /// <c>answer.choiceTimeLimitSec</c>「早押しなしで全員が回答する形式」）。
    /// </summary>
    /// <remarks>
    /// freeText の <c>SubmitAnswerRpc</c> / <c>QuestionResultRpc</c>（GameSession.Rpc.cs）と対になる、
    /// 選択式専用の RPC 2 本（<see cref="SubmitChoiceRpc"/> / <see cref="ChoiceResultRpc"/>）を持つ。
    /// 選択式は早押しの勝者 1 人ではなく全クライアントが対象になるため、既存の RPC を流用せず分けている。
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>
        /// 一斉判定の結果として配る件数の上限（<see cref="MaxFinalScoreCount"/> と同じ考え方）。
        /// 受信側はこれを超える配列を不正として捨てる（docs/network.md §9）。
        /// </summary>
        public const int MaxChoiceResultCount = 32;

        /// <summary>
        /// 選択式の一斉判定が終わったとき（正解インデックス・選択した全クライアント分の結果）。
        /// 誰も選択していなくても、正解インデックスを知らせるために発火する。
        /// </summary>
        public event Action<int, IReadOnlyList<ChoiceAnswerEntry>> ChoiceResolved;

        /// <summary>
        /// 選択をサーバーへ送る（クライアント側から呼ぶ）。
        /// </summary>
        /// <param name="choiceIndex">選択した元 <c>choices</c> インデックス（0〜<c>QuizStateMachine.MaxChoiceCount - 1</c>）。</param>
        /// <returns>送信したら true。フェーズ外・範囲外・未接続なら false。</returns>
        public bool RequestChoice(int choiceIndex)
        {
            if (!IsSpawned || NetworkManager == null || !NetworkManager.IsClient)
            {
                return false;
            }

            // ローカルの事前判定は体感のためだけで、正しさはサーバーが決める（docs/network.md §9）。
            if (_phase.Value != QuizPhase.ChoiceAnswering)
            {
                return false;
            }

            if (choiceIndex < 0 || choiceIndex > byte.MaxValue)
            {
                return false;
            }

            SubmitChoiceRpc((byte)choiceIndex);
            return true;
        }

        /// <summary>
        /// 選択（クライアント → サーバー）。送信元は <c>rpcParams.Receive.SenderClientId</c> から取る
        /// （docs/network.md §1.3 / §1.5）。判定はここでは行わず、集計だけする
        /// （一斉判定は制限時間切れの tick、<see cref="HandleServerTick"/> の <c>QuizEvent.ChoiceJudged</c>）。
        /// </summary>
        /// <param name="choiceIndex">選択した元 <c>choices</c> インデックス。</param>
        /// <param name="rpcParams">NGO が埋める受信情報。</param>
        [Rpc(SendTo.Server)]
        public void SubmitChoiceRpc(byte choiceIndex, RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!RpcRateGuard.Allow(senderId, NetworkManager.ServerTime.Time)) { return; }

            if (IsModeratorHostSender(senderId))
            {
                LogRejected(senderId, "[GameSession] 司会専用モードのホストは選択できません（選択は司会操作パネルの対象外）。");
                return;
            }

            if (_machine == null)
            {
                LogRejected(senderId, $"[GameSession] 選択を受け取りましたが進行が始まっていません（送信元 {senderId}）。");
                return;
            }

            if (!_machine.SubmitChoice(senderId, choiceIndex, NetworkManager.ServerTime.Time, out var reason))
            {
                LogRejected(senderId, $"[GameSession] 選択を受け付けませんでした（送信元 {senderId}、理由: {reason}、フェーズ: {_machine.Phase}）。");
                return;
            }

            // #194: 「回答済み」を参加者パネルへ配る（選んだ番号・正誤は判定まで載せない）。
            // 選択の受理はフェーズ遷移を伴わないので、ここで明示的に反映する。
            PublishQuestionProgress();
        }

        /// <summary>
        /// 選択式の一斉判定の結果を全員へ配る（サーバーのみ、<see cref="HandleServerTick"/> から呼ぶ）。
        /// 得点表（<c>NetworkList</c>）自体は <see cref="PublishState"/> が既に反映済みで、
        /// freeText の <see cref="NotifyScoreChanged"/>（<see cref="ScoreChangedRpc"/>）とは異なり、
        /// 選択式は本 RPC 1 本で選択・得点をまとめて配るため <see cref="ScoreChangedRpc"/> は使わない。
        /// </summary>
        private void NotifyChoiceResults()
        {
            if (_machine == null)
            {
                return;
            }

            var results = _machine.LastChoiceResults;
            var count = Math.Min(results.Count, MaxChoiceResultCount);
            if (count < results.Count)
            {
                Debug.LogWarning($"[GameSession] 選択式の回答が {results.Count} 件あるため先頭 {count} 件だけを配信します。");
            }

            var clientIds = new ulong[count];
            var choiceIndices = new byte[count];
            var scoreDeltas = new int[count];
            var totalScores = new int[count];
            for (var i = 0; i < count; i++)
            {
                var result = results[i];
                clientIds[i] = result.ClientId;
                choiceIndices[i] = (byte)result.ChoiceIndex;
                scoreDeltas[i] = result.ScoreDelta;
                totalScores[i] = result.TotalScore;
            }

            ChoiceResultRpc(_machine.CorrectChoiceIndex, clientIds, choiceIndices, scoreDeltas, totalScores);
        }

        /// <summary>
        /// 選択式の一斉判定（サーバー → 全員）。正解インデックスと、選択した全クライアント分の
        /// 選択・得点をまとめて配る（<b>仮決め: #17</b>）。誰も選択していなければ配列は空になる。
        /// </summary>
        /// <param name="correctChoiceIndex">正解の元 <c>choices</c> インデックス。</param>
        /// <param name="clientIds">選択したクライアント ID（他の 3 配列と同じ並び）。</param>
        /// <param name="choiceIndices">各クライアントが選択した元 <c>choices</c> インデックス。</param>
        /// <param name="scoreDeltas">各クライアントのこの問題での得点増減。</param>
        /// <param name="totalScores">各クライアントの増減後の累計得点。</param>
        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void ChoiceResultRpc(
            int correctChoiceIndex, ulong[] clientIds, byte[] choiceIndices, int[] scoreDeltas, int[] totalScores)
        {
            // サーバー発でも受信データは検証する（docs/network.md §9）。
            if (clientIds == null || choiceIndices == null || scoreDeltas == null || totalScores == null
                || clientIds.Length != choiceIndices.Length
                || clientIds.Length != scoreDeltas.Length
                || clientIds.Length != totalScores.Length)
            {
                Debug.LogWarning("[GameSession] 選択式の判定結果が壊れていたため破棄しました。");
                return;
            }

            if (clientIds.Length > MaxChoiceResultCount)
            {
                Debug.LogWarning($"[GameSession] 選択式の判定結果の件数が上限を超えていたため破棄しました（{clientIds.Length} 件）。");
                return;
            }

            // レビュー M7: correctChoiceIndex・各 choiceIndices とも選択肢の取りうる範囲
            // （0〜MaxChoiceCount-1）に収まっているか検証する（docs/network.md §9）。
            if (correctChoiceIndex < 0 || correctChoiceIndex >= QuizStateMachine.MaxChoiceCount)
            {
                Debug.LogWarning($"[GameSession] 選択式の正解インデックスが範囲外のため破棄しました（{correctChoiceIndex}）。");
                return;
            }

            for (var i = 0; i < choiceIndices.Length; i++)
            {
                if (choiceIndices[i] >= QuizStateMachine.MaxChoiceCount)
                {
                    Debug.LogWarning($"[GameSession] 選択式の選択インデックスが範囲外のため破棄しました（{choiceIndices[i]}）。");
                    return;
                }
            }

            var entries = new List<ChoiceAnswerEntry>(clientIds.Length);
            for (var i = 0; i < clientIds.Length; i++)
            {
                entries.Add(new ChoiceAnswerEntry(
                    clientIds[i], choiceIndices[i], choiceIndices[i] == correctChoiceIndex, scoreDeltas[i], totalScores[i]));
            }

            ChoiceResolved?.Invoke(correctChoiceIndex, entries);
        }
    }
}
