using TsumugiQuiz.Core;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、本体の縦スクロール領域（<c>game-scroll-view</c>、#187）の
    /// スクロール位置を扱う部分。
    /// </summary>
    /// <remarks>
    /// どこへ合わせるかは <see cref="GameViewScrollPolicy"/> が決める。表示の切り替え直後はまだレイアウトが
    /// 解決していないため、実際のスクロールは次のレイアウト後（<c>schedule.Execute</c>）に行う。
    /// </remarks>
    public sealed partial class GameView
    {
        private ScrollView _scrollView;

        /// <summary>スクロール領域を取得する。見つからなくても Game 画面の表示は続ける（スクロールしないだけ）。</summary>
        private void InitializeScroll(VisualElement root)
        {
            _scrollView = root.Q<ScrollView>("game-scroll-view");
            if (_scrollView != null)
            {
                _scrollView.scrollOffset = UnityEngine.Vector2.zero;
            }
        }

        private void TeardownScroll()
        {
            _scrollView = null;
        }

        /// <summary>
        /// フェーズの変化に合わせてスクロール位置を合わせる。各セクションの表示更新を済ませてから呼ぶこと
        /// （<see cref="HandlePhaseChanged"/> の末尾）。
        /// </summary>
        private void ScrollForPhase(QuizPhase phase)
        {
            if (_scrollView == null)
            {
                return;
            }

            var target = GameViewScrollPolicy.TargetFor(
                phase,
                IsDisplayed(_buzzSection),
                IsDisplayed(_answerSection),
                IsDisplayed(_choiceSection));

            ScrollTo(target);
        }

        /// <summary>
        /// 指定した対象へスクロールする。表示を切り替えた直後はレイアウトが未解決（要素の位置・高さが古い）なので、
        /// 次の更新で合わせる。表示中かどうかも実行時に見直す（判定結果の文言はフェーズ変更の後に届くことがある）。
        /// </summary>
        private void ScrollTo(GameScrollTarget target)
        {
            if (_scrollView == null || target == GameScrollTarget.None)
            {
                return;
            }

            var element = ElementFor(target);
            if (target != GameScrollTarget.Top && element == null)
            {
                return;
            }

            var scrollView = _scrollView;
            scrollView.schedule.Execute(() =>
            {
                if (target == GameScrollTarget.Top)
                {
                    scrollView.scrollOffset = UnityEngine.Vector2.zero;
                }
                else if (element.panel != null && IsDisplayed(element))
                {
                    scrollView.ScrollTo(element);
                }
            });
        }

        /// <summary>スクロール位置を先頭（問題パネル）へ戻す（新しい問題の提示時、#187 レビュー L2）。</summary>
        private void ScrollToTop()
        {
            ScrollTo(GameScrollTarget.Top);
        }

        /// <summary>
        /// 判定結果を表示範囲へ入れる（判定の RPC が届いて結果セクションが表示されたとき、#187 レビュー M1）。
        /// 判定後（<see cref="QuizPhase.Result"/>）以外や、結果セクションが隠れている間は何もしない。
        /// </summary>
        private void ScrollToResultSection()
        {
            if (_session == null || _session.Phase.Value != QuizPhase.Result || !IsDisplayed(_resultSection))
            {
                return;
            }

            ScrollTo(GameScrollTarget.Result);
        }

        private VisualElement ElementFor(GameScrollTarget target)
        {
            switch (target)
            {
                case GameScrollTarget.Buzz:
                    return _buzzSection;
                case GameScrollTarget.Answer:
                    return _answerSection;
                case GameScrollTarget.Choice:
                    return _choiceSection;
                case GameScrollTarget.Result:
                    return _resultSection;
                default:
                    return null;
            }
        }

        private static bool IsDisplayed(VisualElement element)
            => element != null && element.style.display.value != DisplayStyle.None;
    }
}
