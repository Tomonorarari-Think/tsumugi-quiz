using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Questions;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> の表示状態（フェーズ文言・残り時間・ボタン活性・結果文言）を組み立てる
    /// 純 C# ロジック（issue #14）。<see cref="HostSetup.HostSetupPresenter"/> と同じ方針で、
    /// Unity API（<c>UnityEngine.*</c>）に依存させず EditMode で直接テストできるようにする。
    /// 状態そのもの（フェーズ・締め切り・ロック保持者）は <c>GameSession</c>（Network 層）の
    /// <c>NetworkVariable</c> / RPC から届く値をそのまま渡す。
    /// </summary>
    /// <remarks>
    /// 自分のクライアント ID は <c>ulong?</c> で受け取る（レビュー L-14）。
    /// セッション・<c>NetworkManager</c> が無くて自分の ID が分からない場合は <c>null</c> を渡すこと。
    /// <c>ulong.MaxValue</c> 等の「番兵値」で表すと、万一その値が実際のクライアント ID と衝突した場合に
    /// 誤判定になりうるため（<c>NetworkManager.ServerClientId == 0</c> のように意味のある値もあるため
    /// 0 も番兵には使えない）、Nullable にして「わからない」を型で表現する。
    /// </remarks>
    public static class GameViewPresenter
    {
        /// <summary>残り時間バーの更新間隔（ミリ秒、レビュー L-15）。</summary>
        public const long TimeDisplayIntervalMs = 100;

        /// <summary>回答の最大文字数。<see cref="QuizStateMachine.MaxAnswerLength"/> と同じ値（レビュー H-3）。</summary>
        public const int MaxAnswerLength = QuizStateMachine.MaxAnswerLength;

        /// <summary>フェーズごとの表示文言（docs/architecture.md §4、docs/network.md §6.6）。</summary>
        public static string PhaseLabel(QuizPhase phase)
        {
            switch (phase)
            {
                case QuizPhase.Lobby:
                    return "待機中";
                case QuizPhase.Reading:
                    return "問題文表示中";
                case QuizPhase.BuzzOpen:
                    return "早押し受付中";
                case QuizPhase.Locked:
                    return "回答権確定";
                case QuizPhase.Answering:
                    return "回答入力中";
                case QuizPhase.ChoiceAnswering:
                    return "選択中";
                case QuizPhase.Judging:
                    return "判定中";
                case QuizPhase.Result:
                    return "結果発表";
                case QuizPhase.Finished:
                    return "終了";
                default:
                    return phase.ToString();
            }
        }

        /// <summary>
        /// 締め切り（サーバー時刻軸）までの残り秒数。負にはならない（0 未満は 0 に丸める）。
        /// </summary>
        /// <remarks>
        /// 計算そのものは Core の <see cref="QuizDeadlines.RemainingSeconds(double, double)"/> に委譲する（issue #178）。
        /// Core は「締め切り無し・現在時刻が壊れている」を <c>double.NaN</c>（不明）で表すが、
        /// この残り時間バー・ラベル向けの API では従来どおり 0.0 に丸めて返す
        /// （ラベルを空にするかどうかは呼び出し側が締め切りの有無で別途判定する）。
        /// </remarks>
        /// <param name="deadlineServerTime">締め切り。締め切りが無いフェーズでは <c>double.NaN</c>。</param>
        /// <param name="currentServerTime">現在のサーバー時刻（<c>NetworkManager.ServerTime.Time</c>）。</param>
        public static double RemainingSeconds(double deadlineServerTime, double currentServerTime)
        {
            var remaining = QuizDeadlines.RemainingSeconds(deadlineServerTime, currentServerTime);
            return double.IsNaN(remaining) ? 0.0 : remaining;
        }

        /// <summary>
        /// 残り時間バーの充填率（0.0〜1.0）。
        /// </summary>
        /// <param name="phaseStartServerTime">
        /// 現在の計測区間の開始時刻（<see cref="QuizDeadlines.IntervalStartServerTime"/>）。
        /// </param>
        /// <param name="deadlineServerTime">締め切り。締め切りが無いフェーズでは <c>double.NaN</c>（0.0 を返す）。</param>
        /// <param name="currentServerTime">現在のサーバー時刻。</param>
        public static double ProgressFraction(double phaseStartServerTime, double deadlineServerTime, double currentServerTime)
        {
            if (double.IsNaN(deadlineServerTime) || double.IsNaN(phaseStartServerTime))
            {
                return 0.0;
            }

            var total = deadlineServerTime - phaseStartServerTime;
            if (total <= 0.0)
            {
                return 0.0;
            }

            var fraction = RemainingSeconds(deadlineServerTime, currentServerTime) / total;
            if (double.IsNaN(fraction))
            {
                // レビュー L-13: total・currentServerTime が想定外の組み合わせでも NaN を UI に渡さない。
                return 0.0;
            }

            if (fraction < 0.0)
            {
                return 0.0;
            }

            return fraction > 1.0 ? 1.0 : fraction;
        }

        /// <summary>早押しボタン（Space キーも同じ判定）を押せる状態か。</summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="hasBuzzedLocally">この受付で既に押下を送ったか（ローカルの連打防止錠）。</param>
        /// <param name="isExcludedFromBuzzing">
        /// 自分がこの問題で早押しの対象外か（誤答後の受付再開放 #18 のローカルの印（レビュー H-5）、
        /// または同期された進行状態の誤答済み・次問休み #194。GameView.BuzzEligibility.cs）。
        /// </param>
        public static bool IsBuzzButtonEnabled(QuizPhase phase, bool hasBuzzedLocally, bool isExcludedFromBuzzing)
            => phase == QuizPhase.BuzzOpen && !hasBuzzedLocally && !isExcludedFromBuzzing;

        /// <summary>回答入力欄を表示する状態か（ロック保持者本人の回答中のみ）。</summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="lockedClientId">早押しロック保持者。</param>
        /// <param name="localClientId">自分のクライアント ID。分からなければ null。</param>
        public static bool IsAnswerFieldVisible(QuizPhase phase, ulong lockedClientId, ulong? localClientId)
            => phase == QuizPhase.Answering && localClientId.HasValue && lockedClientId == localClientId.Value;

        /// <summary>
        /// 早押しボタンを表示する状態か（#187）。自分が回答している間（回答欄を表示している間）は押しても
        /// 受け付けないので隠し、回答欄の分の縦幅を空ける。回答者でないクライアントは <see cref="QuizPhase.Answering"/>
        /// 中も表示したまま（押せるかどうかは <see cref="IsBuzzButtonEnabled"/> が決め、この間は無効）。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="lockedClientId">早押しロック保持者。</param>
        /// <param name="localClientId">自分のクライアント ID。分からなければ null。</param>
        public static bool IsBuzzButtonVisible(QuizPhase phase, ulong lockedClientId, ulong? localClientId)
            => !IsAnswerFieldVisible(phase, lockedClientId, localClientId);

        /// <summary>
        /// 選択肢セクションを表示する状態か（issue #17）。選択式（<c>choice</c>）の問題が
        /// 提示されてから（Reading）結果表示（Result）まで表示し続ける。freeText の問題や、
        /// まだ問題が提示されていない（<c>questionType</c> が null）ときは表示しない。
        /// </summary>
        /// <param name="questionType">現在の問題の出題形式。未提示なら null。</param>
        /// <param name="phase">現在のフェーズ。</param>
        public static bool IsChoiceSectionVisible(QuestionType? questionType, QuizPhase phase) =>
            questionType == QuestionType.Choice
            && phase != QuizPhase.Lobby
            && phase != QuizPhase.Finished;

        /// <summary>
        /// 早押しセクション（<c>buzz-section</c>）を表示する状態か（#132 レビュー H-1）。
        /// 選択式（<c>choice</c>）の問題は「早押しなし・時間切れで一斉判定」が確定仕様
        /// （docs/requirements.md / setup-brief.md K18）なので、早押しボタンを出す必要がない。
        /// 出しっぱなしにすると 16:9 フルスクリーンの論理縦幅で
        /// header + buzz + choice + result + actions が同時に並んで画面から溢れる。
        /// </summary>
        /// <param name="questionType">現在の問題の出題形式。未提示なら null（= 早押し側を表示）。</param>
        public static bool IsBuzzSectionVisible(QuestionType? questionType)
            => questionType != QuestionType.Choice;

        /// <summary>
        /// 判定結果セクション（<c>result-section</c>）を表示する状態か（#132 レビュー H-1）。
        /// 判定が出たあと（<see cref="QuizPhase.Result"/>）と全問終了
        /// （<see cref="QuizPhase.Finished"/>、Result View へ遷移するまでの一瞬）だけ表示する。
        /// 得点表示は <c>game-header-row</c> へ移したため、ここを隠しても常時見える。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        public static bool IsResultSectionVisible(QuizPhase phase)
            => phase == QuizPhase.Result || phase == QuizPhase.Finished;

        /// <summary>
        /// 選択肢セクションを詰めて表示する状態か（#187）。判定後（<see cref="QuizPhase.Result"/> /
        /// <see cref="QuizPhase.Finished"/>）はボタンを押せず正誤の表示だけになるので、
        /// ボタンを低くし、判定結果と「次へ」の分の縦幅を空ける（docs/architecture.md §10.2）。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        public static bool IsChoiceSectionCompact(QuizPhase phase)
            => phase == QuizPhase.Result || phase == QuizPhase.Finished;

        /// <summary>
        /// 残り時間ラベルに締め切りが無いとき（読み上げ中・判定後など）に入れる文字列（#187）。
        /// 空文字にするとラベルの高さが 1 行分（約 23px）縮み、フェーズが変わるたびに下の選択肢・早押しボタンが
        /// 上下にずれるため、見えない 1 文字（ノーブレークスペース）で行の高さを保つ。
        /// </summary>
        public const string EmptyTimeRemainingText = "\u00A0";

        /// <summary>
        /// 残り時間ラベルの文言（#187 で <c>GameView.UiBinding</c> から移した）。締め切りが無い（NaN）ときは
        /// <see cref="EmptyTimeRemainingText"/> を返す。
        /// </summary>
        /// <param name="deadlineServerTime">締め切り（サーバー時刻軸）。無ければ NaN。</param>
        /// <param name="remainingSeconds"><see cref="RemainingSeconds"/> の結果。</param>
        public static string FormatTimeRemaining(double deadlineServerTime, double remainingSeconds)
            => double.IsNaN(deadlineServerTime) ? EmptyTimeRemainingText : $"残り {remainingSeconds:F1} 秒";

        /// <summary>
        /// 選択肢ボタンを押せる状態か（<see cref="QuizPhase.ChoiceAnswering"/> 中、まだ選択していない間だけ）。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="hasSelectedLocally">この問題で既に選択を送ったか（<c>answer.singleAttemptOnly</c> 相当）。</param>
        public static bool IsChoiceButtonInteractable(QuizPhase phase, bool hasSelectedLocally) =>
            phase == QuizPhase.ChoiceAnswering && !hasSelectedLocally;

        /// <summary>
        /// ホストから届く問題データ（問題文・選択肢・正解）を表示する形にする（issue #209、docs/question-data.md §1.1）。
        /// 見えない文字と、基底文字 1 つあたり 5 個目以降の結合記号を、名前と同じ規則（<see cref="TextRules.RemoveHiddenCharacters(string)"/>）で除く。
        /// 改行・連続した空白は書かれたとおりに残し、切り詰めもしない（長さは読み込み時・受信時に検証済み）。
        /// 読み上げ（TTS）に渡す文字列には使わない。
        /// </summary>
        /// <param name="text">問題データの文字列。null 可。</param>
        /// <returns>表示する文字列。null なら空文字。</returns>
        public static string ToQuestionDisplayText(string text) => TextRules.RemoveHiddenCharacters(text);

        /// <summary>
        /// 選択式の一斉判定の結果メッセージ（自分が選択していた場合は正誤・得点も含める）。
        /// </summary>
        /// <param name="correctChoiceText">正解の選択肢テキスト。</param>
        /// <param name="localIsCorrect">自分が選択していた場合、その正誤。選択していなければ null。</param>
        /// <param name="scoreDelta">自分の得点増減（<paramref name="localIsCorrect"/> が null なら無視）。</param>
        /// <param name="totalScore">自分の累計得点（<paramref name="localIsCorrect"/> が null なら無視）。</param>
        public static string FormatChoiceResult(
            string correctChoiceText, bool? localIsCorrect, int scoreDelta, int totalScore)
        {
            var message = $"正解: {ToQuestionDisplayText(correctChoiceText)}";
            if (!localIsCorrect.HasValue)
            {
                return message + "（あなたは選択しませんでした）";
            }

            var deltaText = scoreDelta > 0 ? $"+{scoreDelta}" : scoreDelta.ToString();
            return message
                + (localIsCorrect.Value ? " / あなたは正解でした" : " / あなたは不正解でした")
                + $"（{deltaText}点 / 合計{totalScore}点）";
        }

        /// <summary>
        /// ホスト操作（「次へ」）を表示する状態か。Result フェーズかつホスト自身のときのみ。
        /// </summary>
        /// <param name="phase">現在のフェーズ。</param>
        /// <param name="isHost">自分がホストか（<c>NetworkManager.IsHost</c>）。</param>
        public static bool IsHostControlsVisible(QuizPhase phase, bool isHost)
            => isHost && phase == QuizPhase.Result;

        /// <summary>
        /// 回答入力の検証エラー文言。妥当なら空文字を返す（<c>GameSession.RequestAnswer</c> が
        /// false を返した理由をローカルでも判定し、送信前に案内できるようにする。レビュー H-3）。
        /// </summary>
        /// <param name="text">入力中の回答文字列。</param>
        public static string AnswerInputError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "回答を入力してください。";
            }

            if (text.Length > MaxAnswerLength)
            {
                return $"回答は{MaxAnswerLength}文字以内で入力してください（現在 {text.Length} 文字）。";
            }

            return string.Empty;
        }

        /// <summary>
        /// 早押しの勝者確定メッセージ。
        /// 自分なら常に「あなた」、他人は <paramref name="resolveName"/> で解決した表示名を使う。
        /// </summary>
        /// <param name="winnerClientId">勝者のクライアント ID。</param>
        /// <param name="localClientId">自分のクライアント ID。分からなければ null。</param>
        /// <param name="wasTie">同着抽選だったか（docs/network.md §6.3）。</param>
        /// <param name="resolveName">
        /// クライアント ID からプレイヤー名を解決する関数（#7 のロビーの名簿と接続する口）。
        /// null、または空文字を返した場合は <see cref="FormatPlayerFallbackName"/> にフォールバックする。
        /// </param>
        public static string FormatBuzzWinner(
            ulong winnerClientId, ulong? localClientId, bool wasTie, Func<ulong, string> resolveName = null)
        {
            var who = IsLocal(winnerClientId, localClientId) ? "あなた" : ResolveDisplayName(winnerClientId, resolveName);
            return wasTie
                ? $"{who} が同着抽選の末、回答権を獲得しました。"
                : $"{who} が回答権を獲得しました。";
        }

        /// <summary>
        /// 誤答後の受付再開放メッセージ（#18、<c>buzz.reopenAfterWrongAnswer</c>）。
        /// </summary>
        /// <param name="penalizedClientId">お手つきになったクライアント ID。</param>
        /// <param name="localClientId">自分のクライアント ID。分からなければ null。</param>
        /// <param name="resolveName">
        /// クライアント ID からプレイヤー名を解決する関数。<see cref="FormatBuzzWinner"/> と同じ口。
        /// </param>
        public static string FormatBuzzReopened(
            ulong penalizedClientId, ulong? localClientId, Func<ulong, string> resolveName = null)
        {
            var who = IsLocal(penalizedClientId, localClientId) ? "あなた" : ResolveDisplayName(penalizedClientId, resolveName);
            return $"{who} はお手つきです。他のプレイヤーの早押しを再開放します。";
        }

        /// <summary>
        /// プレイヤー名の解決（#7 のロビー名簿と接続する口）。<paramref name="resolveName"/> が
        /// null、または空文字を返した場合はクライアント ID による既定表示にフォールバックする。
        /// </summary>
        /// <param name="clientId">表示対象のクライアント ID。</param>
        /// <param name="resolveName">クライアント ID からプレイヤー名を解決する関数。null 可。</param>
        private static string ResolveDisplayName(ulong clientId, Func<ulong, string> resolveName)
        {
            var resolved = resolveName?.Invoke(clientId);
            return string.IsNullOrEmpty(resolved) ? FormatPlayerFallbackName(clientId) : resolved;
        }

        /// <summary>プレイヤー名が解決できないときの既定表示（<c>プレイヤー{clientId}</c>）。</summary>
        /// <param name="clientId">クライアント ID。</param>
        public static string FormatPlayerFallbackName(ulong clientId) => PlayerDisplayNameSanitizer.FallbackName(clientId);

        /// <summary><paramref name="clientId"/> が自分自身か（<paramref name="localClientId"/> が分かっている場合のみ true になりうる）。</summary>
        private static bool IsLocal(ulong clientId, ulong? localClientId) => localClientId.HasValue && clientId == localClientId.Value;

        /// <summary>
        /// 「退出」の確認パネルに出す文言（統括判断: HostSetupView と同じインライン確認）。
        /// ホストが退出するとゲームが終了して参加者全員が切断されるため、文言を変える。
        /// </summary>
        /// <param name="isHost">自分がホストか。</param>
        public static string ExitConfirmMessage(bool isHost) => isHost
            ? "ゲームを終了して退出しますか？参加者は全員切断されます。"
            : "ゲームを退出しますか？";

        /// <summary>
        /// 1 問の結果メッセージ（正誤・正解・回答者・得点差分・累計得点）。
        /// </summary>
        /// <param name="judgement">判定結果。</param>
        /// <param name="answererClientId">
        /// 回答者のクライアント ID。誰も押さなかった場合（<see cref="QuizJudgement.TimedOut"/> /
        /// <see cref="QuizJudgement.NoEligibleBuzzers"/>）は
        /// <c>GameSession.NoClientId</c>（本メソッドでは参照しない）。
        /// </param>
        /// <param name="localClientId">自分のクライアント ID。分からなければ null。</param>
        /// <param name="correctAnswer">正解（代表の 1 件）。</param>
        /// <param name="scoreDelta">この問題での得点増減。</param>
        /// <param name="totalScore">回答者の累計得点。</param>
        /// <param name="resolveName">クライアント ID からプレイヤー名を解決する関数。<see cref="FormatBuzzWinner"/> と同じ口。</param>
        /// <remarks>
        /// レビュー M-7: 得点の増減・累計は回答者本人の視点でのみ表示する。他人が回答した結果を見ている
        /// クライアントには、あたかも自分の得点であるかのように誤解されないよう得点部分を表示しない。
        /// </remarks>
        public static string FormatJudgement(
            QuizJudgement judgement,
            ulong answererClientId,
            ulong? localClientId,
            string correctAnswer,
            int scoreDelta,
            int totalScore,
            Func<ulong, string> resolveName = null)
        {
            switch (judgement)
            {
                case QuizJudgement.Correct:
                    return $"{FormatAnswererName(answererClientId, localClientId, resolveName)}が正解！ 正解: {ToQuestionDisplayText(correctAnswer)}"
                        + FormatScoreSuffix(answererClientId, localClientId, scoreDelta, totalScore);
                case QuizJudgement.Wrong:
                    return $"{FormatAnswererName(answererClientId, localClientId, resolveName)}は不正解… 正解: {ToQuestionDisplayText(correctAnswer)}"
                        + FormatScoreSuffix(answererClientId, localClientId, scoreDelta, totalScore);
                case QuizJudgement.TimedOut:
                    return $"時間切れ… 正解: {ToQuestionDisplayText(correctAnswer)}";
                case QuizJudgement.NoEligibleBuzzers:
                    // #200: 押せる人が居なくなって時間切れを待たずに締めた。時間切れとは文言で区別する。
                    return $"回答できる人がいないため締め切りました。正解: {ToQuestionDisplayText(correctAnswer)}";
                default:
                    // #132 レビュー L4-3: 空文字を返すと result-section は表示されない
                    // （M3-3 で「Result フェーズ かつ 判定文言がある」ときだけ開く仕様にしたため）。
                    // 判定が付いたのに文言を作れないのは想定外なので、気づけるよう警告を残す。
                    Debug.LogWarning(
                        $"[GameViewPresenter] 判定 '{judgement}' に対応する文言がありません。"
                        + "結果パネルは表示されません（#132 M3-3）。");
                    return string.Empty;
            }
        }

        private static string FormatAnswererName(ulong answererClientId, ulong? localClientId, Func<ulong, string> resolveName)
            => IsLocal(answererClientId, localClientId) ? "あなた" : ResolveDisplayName(answererClientId, resolveName);

        private static string FormatScoreSuffix(ulong answererClientId, ulong? localClientId, int scoreDelta, int totalScore)
        {
            if (!IsLocal(answererClientId, localClientId))
            {
                // 他人の累計得点は自分の得点であるかのように見えてしまうため表示しない（レビュー M-7）。
                return string.Empty;
            }

            var deltaText = scoreDelta > 0 ? $"+{scoreDelta}" : scoreDelta.ToString();
            return $"（{deltaText}点 / 合計{totalScore}点）";
        }
    }
}
