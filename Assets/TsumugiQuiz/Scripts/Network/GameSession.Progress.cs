using System;
using TsumugiQuiz.Core.Participants;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、参加者パネル（#194）へ配る「現在の問題の進行状態」
    /// （押下順・回答順・回答権・選択式の回答済み）の同期をまとめた部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 権威はサーバーの <c>QuizStateMachine</c>（<c>BuildProgress</c>）で、本ファイルはそれを
    /// <c>NetworkVariable&lt;QuestionProgressPayload&gt;</c> へ写すだけ。クライアントは読むだけ。
    /// RPC は使わない（途中参加・再接続でもスポーン時の同期で現在値が届くため、再同期に載せ直す必要がない）。
    /// </para>
    /// <para>
    /// 押下順は勝者が確定した時点（<c>QuizEvent.BuzzResolved</c> の <see cref="PublishState"/>）でまとめて書く。
    /// 集計窓の途中の押下は配らない（届いた順と押下時刻の順が食い違いうるため、統括判断 #194）。
    /// </para>
    /// <para>
    /// 得点は既存の得点表（<c>NetworkList&lt;ScoreEntry&gt;</c>、GameSession.Score.cs）をそのまま使い、ここには載せない。
    /// </para>
    /// </remarks>
    public sealed partial class GameSession
    {
        private readonly NetworkVariable<QuestionProgressPayload> _questionProgress =
            new NetworkVariable<QuestionProgressPayload>(QuestionProgressPayload.Empty);

        /// <summary>
        /// 現在の問題の進行状態が変わったとき（全ピア。ホストでは書き込んだ時点で発火する）。
        /// スポーン時の初期同期では発火しないので、画面を出した時点の値は <see cref="GetQuestionProgress"/> で読むこと。
        /// </summary>
        public event Action QuestionProgressChanged;

        /// <summary>
        /// 現在の問題の進行状態（受信値を検証して組み立て直したもの）。スポーン前は
        /// <see cref="QuestionProgress.Empty"/>。
        /// </summary>
        /// <returns>進行状態。</returns>
        public QuestionProgress GetQuestionProgress() =>
            IsSpawned ? _questionProgress.Value.ToQuestionProgress() : QuestionProgress.Empty;

        /// <summary>進行状態の同期値（書き込み権限の確認・診断用）。</summary>
        public NetworkVariableBase QuestionProgressVariable => _questionProgress;

        /// <summary>状態機械の進行状態を同期値へ反映する（サーバーのみ）。</summary>
        private void PublishQuestionProgress()
        {
            if (_machine == null || !IsSpawned || !IsServer)
            {
                return;
            }

            var payload = QuestionProgressPayload.FromProgress(_machine.BuildProgress(), out var dropped);
            if (dropped > 0)
            {
                Debug.LogWarning(
                    $"[GameSession] 進行状態の行が上限（{QuestionProgressPayload.MaxEntries}）を超えたため {dropped} 行を配信しませんでした。");
            }

            // NetworkVariable は Equals が true なら書き込まない（差分が出ない）ので、毎回呼んでよい。
            _questionProgress.Value = payload;
        }

        /// <summary>
        /// 進行状態を空に戻す（デスポーン時）。クライアントで呼ぶと NGO が権限エラーを出すので、
        /// 判定は <see cref="ClearScores"/> と同じ条件にする。
        /// </summary>
        private void ClearQuestionProgress()
        {
            if (NetworkManager != null && !IsServer)
            {
                return;
            }

            if (!_questionProgress.Value.Equals(QuestionProgressPayload.Empty))
            {
                _questionProgress.Value = QuestionProgressPayload.Empty;
            }
        }

        private void HandleQuestionProgressChanged(QuestionProgressPayload previous, QuestionProgressPayload current) =>
            QuestionProgressChanged?.Invoke();
    }
}
