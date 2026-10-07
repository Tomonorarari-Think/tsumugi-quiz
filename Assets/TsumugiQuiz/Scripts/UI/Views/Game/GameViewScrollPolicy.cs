using TsumugiQuiz.Core;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// Game 画面の縦スクロール領域（<c>game-scroll-view</c>、#187）を、フェーズが変わったときに
    /// どこへ合わせるかの対象。
    /// </summary>
    public enum GameScrollTarget
    {
        /// <summary>スクロール位置を変えない。</summary>
        None,

        /// <summary>先頭（問題パネル）へ戻す。新しい問題の提示時。</summary>
        Top,

        /// <summary>早押しボタン（<c>buzz-section</c>）を表示範囲に入れる。</summary>
        Buzz,

        /// <summary>回答欄（<c>answer-section</c>）を表示範囲に入れる。</summary>
        Answer,

        /// <summary>選択肢（<c>choice-section</c>）を表示範囲に入れる。</summary>
        Choice,

        /// <summary>判定結果（<c>result-section</c>）を表示範囲に入れる。</summary>
        Result,
    }

    /// <summary>
    /// フェーズの変化に応じて Game 画面のスクロール位置をどこへ合わせるかを決める純 C# ロジック（#187）。
    /// </summary>
    /// <remarks>
    /// Game 画面の本体は作業領域（1600x900 基準で論理 836px、21:9 では約 707px。docs/architecture.md §10.2）に
    /// 収まるよう詰めてあるが、3 行以上の問題文・5 択以上・低い論理縦幅では溢れうる。そのとき
    /// 「操作中のフェーズでは主要操作（早押し・回答欄・選択肢）が必ず表示範囲に入る」ことを優先し、
    /// 操作が始まるフェーズでその要素までスクロールさせる。収まっている間はスクロールは起きない
    /// （<c>ScrollView.ScrollTo</c> は既に見えている要素に対しては位置を変えない）。
    /// </remarks>
    public static class GameViewScrollPolicy
    {
        /// <summary>
        /// 新しいフェーズに入ったときのスクロール先。
        /// </summary>
        /// <param name="phase">新しいフェーズ。</param>
        /// <param name="isBuzzSectionVisible">早押しセクションを表示しているか（選択式・司会では false）。</param>
        /// <param name="isAnswerSectionVisible">回答欄を表示しているか（自分が回答権を持つときだけ true）。</param>
        /// <param name="isChoiceSectionVisible">選択肢を表示しているか（選択式かつ司会でないとき）。</param>
        public static GameScrollTarget TargetFor(
            QuizPhase phase, bool isBuzzSectionVisible, bool isAnswerSectionVisible, bool isChoiceSectionVisible)
        {
            switch (phase)
            {
                case QuizPhase.Reading:
                    return GameScrollTarget.Top;
                case QuizPhase.BuzzOpen:
                    return isBuzzSectionVisible ? GameScrollTarget.Buzz : GameScrollTarget.None;
                case QuizPhase.Answering:
                    return isAnswerSectionVisible ? GameScrollTarget.Answer : GameScrollTarget.None;
                case QuizPhase.ChoiceAnswering:
                    return isChoiceSectionVisible ? GameScrollTarget.Choice : GameScrollTarget.None;
                case QuizPhase.Result:
                    return GameScrollTarget.Result;
                default:
                    return GameScrollTarget.None;
            }
        }
    }
}
