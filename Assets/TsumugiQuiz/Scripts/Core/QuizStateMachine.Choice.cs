using System.Collections.Generic;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizStateMachine"/> のうち、選択式（<c>choice</c>）の出題・回答・一斉判定をまとめた部分
    /// （<b>仮決め: #17</b>、docs/question-data.md §6、docs/room-settings.md
    /// <c>answer.choiceTimeLimitSec</c>「早押しなしで全員が回答する形式」）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// freeText（早押し → 勝者だけが回答）とは異なり、選択式は早押しを介さず
    /// <see cref="QuizPhase.ChoiceAnswering"/> の間、全クライアントがそれぞれ 1 回だけ選択できる。
    /// 判定は個々の送信時ではなく、制限時間切れ（<c>QuizStateMachine.Tick.cs</c> の
    /// <c>TickChoiceAnswering</c>）で一斉に行う。
    /// </para>
    /// <para>
    /// 正解の選択肢テキストは本クラスが持たない（<c>choices</c> 配列自体は Questions/Network 層にある）。
    /// <see cref="CorrectChoiceIndex"/>（元インデックス）だけを保持し、テキストへの変換は呼び出し側
    /// （<c>GameSession</c>）が問題データから引く。
    /// </para>
    /// </remarks>
    public sealed partial class QuizStateMachine
    {
        /// <summary>選択肢の最小件数（docs/question-data.md §1 の <c>choices</c> と同じ値）。</summary>
        public const int MinChoiceCount = 2;

        /// <summary>選択肢の最大件数。</summary>
        public const int MaxChoiceCount = 8;

        private static readonly IReadOnlyList<ChoiceAnswerResult> EmptyChoiceResults = System.Array.Empty<ChoiceAnswerResult>();

        /// <summary>選択済みのクライアントごとの選択インデックス（元 <c>choices</c> インデックス）。</summary>
        private readonly Dictionary<ulong, int> _choiceSelections = new Dictionary<ulong, int>();

        private bool _isChoiceQuestion;
        private int _correctChoiceIndex = -1;
        private int _choiceCount;
        private IReadOnlyList<ChoiceAnswerResult> _lastChoiceResults = EmptyChoiceResults;

        /// <summary>現在の問題が選択式（<c>choice</c>）か。</summary>
        public bool IsChoiceQuestion => _isChoiceQuestion;

        /// <summary>現在の問題の正解インデックス（元 <c>choices</c> インデックス）。freeText では -1。</summary>
        public int CorrectChoiceIndex => _correctChoiceIndex;

        /// <summary>現在の問題の選択肢件数。freeText では 0。</summary>
        public int ChoiceCount => _choiceCount;

        /// <summary>
        /// 直近の一斉判定の結果（選択した全クライアント分。選択しなかったクライアントは含まない）。
        /// 次の出題（<c>StartQuestion</c>、freeText / choice いずれのオーバーロードでも）まで保持する。
        /// </summary>
        public IReadOnlyList<ChoiceAnswerResult> LastChoiceResults => _lastChoiceResults;

        /// <summary>
        /// 選択式の問題として出題を開始する（Lobby / Result → Reading）。
        /// </summary>
        /// <param name="questionIndex">問題インデックス（0 以上）。</param>
        /// <param name="correctChoiceIndex">正解の <c>choices</c> インデックス（0 以上、<paramref name="choiceCount"/> 未満）。</param>
        /// <param name="choiceCount">選択肢件数（<see cref="MinChoiceCount"/>〜<see cref="MaxChoiceCount"/>）。</param>
        /// <param name="serverNow">現在のサーバー時刻（秒）。</param>
        /// <param name="reason">拒否理由。成功時は <see cref="QuizReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool StartQuestion(
            int questionIndex, int correctChoiceIndex, int choiceCount, double serverNow, out QuizReject reason)
        {
            if (!ValidateStartQuestion(questionIndex, serverNow, out reason))
            {
                return false;
            }

            if (choiceCount < MinChoiceCount || choiceCount > MaxChoiceCount
                || correctChoiceIndex < 0 || correctChoiceIndex >= choiceCount)
            {
                reason = QuizReject.InvalidChoice;
                return false;
            }

            _answers.Clear();
            _isChoiceQuestion = true;
            _correctChoiceIndex = correctChoiceIndex;
            _choiceCount = choiceCount;

            CommitQuestionStart(questionIndex, serverNow);
            reason = QuizReject.None;
            return true;
        }

        /// <summary>
        /// 選択を受理する（<see cref="QuizPhase.ChoiceAnswering"/> 中、クライアントごとに 1 回まで）。
        /// freeText の <see cref="SubmitAnswer"/> と異なり、判定はここでは行わず選択を集めるだけ。
        /// 実際の判定は制限時間切れ（<c>TickChoiceAnswering</c>）で一斉に行う。
        /// </summary>
        /// <param name="clientId">送信元クライアント ID（RPC の SenderClientId から取ること）。</param>
        /// <param name="choiceIndex">選択した元 <c>choices</c> インデックス。</param>
        /// <param name="serverNow">サーバーが受信した時刻（秒）。</param>
        /// <param name="reason">棄却理由。受理時は <see cref="AnswerReject.None"/>。</param>
        /// <returns>受理したら true。</returns>
        public bool SubmitChoice(ulong clientId, int choiceIndex, double serverNow, out AnswerReject reason)
        {
            if (IsPaused)
            {
                // 司会が一時停止中は選択を受け付けない（#20。freeText の SubmitAnswer と同じ扱い）。
                reason = AnswerReject.Paused;
                return false;
            }

            if (Phase != QuizPhase.ChoiceAnswering)
            {
                reason = AnswerReject.NotAnswering;
                return false;
            }

            if (!_isChoiceQuestion)
            {
                // 到達不能防御: ChoiceAnswering には choice の出題からしか入らない。
                reason = AnswerReject.WrongQuestionKind;
                return false;
            }

            // お手つきペナルティ（score.penaltyType = "skipNext"）で今問は休みのクライアント（仮決め: #17、M3）。
            if (Penalties.IsSuspended(QuestionIndex, clientId))
            {
                reason = AnswerReject.Penalized;
                return false;
            }

            if (choiceIndex < 0 || choiceIndex >= _choiceCount)
            {
                reason = AnswerReject.InvalidChoiceIndex;
                return false;
            }

            if (_choiceSelections.ContainsKey(clientId))
            {
                reason = AnswerReject.AlreadyAttempted;
                return false;
            }

            if (!double.IsFinite(serverNow))
            {
                reason = AnswerReject.NonFiniteTime;
                return false;
            }

            // 締切後に届いた送信を弾く（レビュー M1）。フェーズはネットワーク tick が
            // TickChoiceAnswering を処理するまで ChoiceAnswering のままなので、Phase 判定だけでは
            // 締切を過ぎた送信を見分けられない。理由は NotAnswering ではなく Expired にして、
            // 「そもそもフェーズ外」と「締切を過ぎた」をログで区別できるようにする（レビュー R-2）。
            if (serverNow - BuzzOpenServerTime >= _limits.ChoiceTimeLimitSec)
            {
                reason = AnswerReject.Expired;
                return false;
            }

            _choiceSelections[clientId] = choiceIndex;
            reason = AnswerReject.None;
            return true;
        }

        /// <summary>
        /// 選択式の一斉判定（<c>TickChoiceAnswering</c> が制限時間切れを検出したときに呼ぶ）。
        /// 選択した全クライアントの正誤を判定し、得点表へ反映して <see cref="LastChoiceResults"/> に積む。
        /// </summary>
        private void ApplyChoiceScores()
        {
            if (_choiceSelections.Count == 0)
            {
                _lastChoiceResults = EmptyChoiceResults;
                return;
            }

            // クライアント ID 昇順にしておく（レビュー L4）。Dictionary の列挙順は保証されないため、
            // GameSession が配る配列やテストの期待値が実行のたびにばらつかないようにする。
            var clientIds = new List<ulong>(_choiceSelections.Keys);
            clientIds.Sort();

            var results = new List<ChoiceAnswerResult>(clientIds.Count);
            foreach (var clientId in clientIds)
            {
                var choiceIndex = _choiceSelections[clientId];
                var judgement = choiceIndex == _correctChoiceIndex ? QuizJudgement.Correct : QuizJudgement.Wrong;
                var delta = judgement == QuizJudgement.Correct ? _rules.Score.CorrectDelta : _rules.Score.WrongDelta;

                if (judgement == QuizJudgement.Correct)
                {
                    Scores = Scores.WithCorrect(clientId);
                }
                else
                {
                    Scores = Scores.WithWrong(clientId);

                    // 誤答者は次問休み（score.penaltyType = "skipNext"）の対象に積む（仮決め: #17、M3）。
                    // freeText の ApplyScore と同じ規則を選択式にも適用する（統括判断）。
                    if (_rules.Score.AppliesSkipNext)
                    {
                        Penalties = Penalties.WithSkipNext(clientId);
                    }
                }

                results.Add(new ChoiceAnswerResult(clientId, choiceIndex, judgement, delta, Scores.GetScore(clientId)));
            }

            _lastChoiceResults = results.AsReadOnly();
        }
    }
}
