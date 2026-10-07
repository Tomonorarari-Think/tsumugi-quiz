using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// <see cref="CharacterView"/> の PlayMode テスト（issue #24）。
    /// GameView（#14）が持つプロセス単位のテクスチャキャッシュ（レビュー M6）とは切り離し、
    /// <c>textureResolver</c> にスタブを注入して配線だけを検証する。
    /// リフレクションは使わず、<see cref="TtsSyncPlayer"/> / <see cref="CharacterView"/> の
    /// テスト専用 internal フックを使う（レビュー L5）。
    /// </summary>
    public sealed class CharacterViewTests
    {
        private const string TemplatePath = "Assets/TsumugiQuiz/UI/Templates/character-view.uxml";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";

        /// <summary>トークンを読む theme.uss（Application.dataPath からの相対）。</summary>
        private const string ThemeUssRelativePath = "TsumugiQuiz/UI/Styles/theme.uss";

        /// <summary>
        /// 色の近似比較の許容誤差（#196 レビュー L）。tintColor は USS の 8bit/ch の値を float に直したもの
        /// なので、1/255 の丸め差を許して比べる（Color の == は成分ごとの完全一致に近く、壊れやすい）。
        /// </summary>
        private const float ColorTolerance = 1.5f / 255f;

        private GameObject _documentObject;
        private UIDocument _document;

        [SetUp]
        public void CreateDocument()
        {
#if UNITY_EDITOR
            var template = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TemplatePath);
            Assert.IsNotNull(template, $"{TemplatePath} が見つかりません。");

            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(panelSettings, $"{PanelSettingsPath} が見つかりません。");

            _documentObject = new GameObject(nameof(CharacterViewTests));
            _document = _documentObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
            Assert.IsNotNull(_document.rootVisualElement, "UIDocument.rootVisualElement が null です。");
            _document.rootVisualElement.Add(template.Instantiate());
#else
            Assert.Ignore("PlayMode テストは Editor 上でのみ実行する（AssetDatabase を使うため）。");
#endif
        }

        [TearDown]
        public void DestroyDocument()
        {
            if (_documentObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_documentObject);
            }

            _documentObject = null;
            _document = null;
        }

        private VisualElement GetCharacterRoot()
        {
            var root = _document.rootVisualElement.Q<VisualElement>("character-root");
            Assert.IsNotNull(root, "character-root がロードした UXML から見つかりません。");
            return root;
        }

        [UnityTest]
        public IEnumerator Constructor_ConsentedButNoTexture_StaysHidden()
        {
            var view = new CharacterView(GetCharacterRoot(), _ => null, () => true);

            yield return null;

            Assert.That(view.State, Is.EqualTo(CharacterState.Idle));
            Assert.IsFalse(view.IsVisible, "画像が解決できない間は character-root ごと非表示のはず（レビュー H1）。");

            view.Dispose();
        }

        [UnityTest]
        public IEnumerator Constructor_ConsentedWithTexture_BecomesVisible()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var root = GetCharacterRoot();
                var view = new CharacterView(root, _ => texture, () => true);

                yield return null;

                Assert.IsTrue(view.IsVisible);
                var image = root.Q<Image>("character-image");
                Assert.AreSame(texture, image.image);

                view.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator NotConsented_StaysHiddenEvenWithTexture_AndDoesNotResolveTexture()
        {
            var resolveCount = 0;
            var view = new CharacterView(
                GetCharacterRoot(),
                _ => { resolveCount++; return new Texture2D(4, 4); },
                () => false);

            yield return null;

            Assert.IsFalse(view.IsVisible);
            Assert.AreEqual(0, resolveCount, "未同意の間はテクスチャを解決しない（レビュー M6）。");

            view.Dispose();
        }

        [UnityTest]
        public IEnumerator Consent_GrantedLater_ResolvesTextureExactlyOnce()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var resolveCount = 0;
                var view = new CharacterView(
                    GetCharacterRoot(),
                    _ => { resolveCount++; return texture; },
                    () => false);

                yield return null;
                Assert.IsFalse(view.IsVisible);
                Assert.AreEqual(0, resolveCount);

                view.SetConsentCheckForTesting(() => true);
                yield return null;

                Assert.IsTrue(view.IsVisible);
                Assert.AreEqual(1, resolveCount);

                // 一度解決したら、同意状況を再確認しても再解決しない（実際のキャッシュは GameView 側、
                // レビュー M6）。CharacterView 自身も無駄な再解決をしないことを確認する。
                view.RefreshVisibility();
                Assert.AreEqual(1, resolveCount);

                view.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// issue #139: パネルから外れている間に同意が撤回され、再びパネルへ接続されたら
        /// （＝ 撤回操作の画面から戻ってきたら）その時点で同意を評価し直して非表示にする。
        /// </summary>
        [UnityTest]
        public IEnumerator ReattachToPanel_AfterRevocation_HidesCharacter()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            var consented = true;
            var root = GetCharacterRoot();
            var parent = root.parent;
            var view = new CharacterView(root, _ => texture, () => consented);

            try
            {
                yield return null;
                Assert.IsTrue(view.IsVisible, "同意済み・画像ありなので表示されるはず。");

                root.RemoveFromHierarchy();
                yield return null;

                consented = false;
                Assert.IsTrue(view.IsVisible, "パネルから外れている間は再評価しない（契機は出題とパネル接続）。");

                parent.Add(root);
                yield return null;

                Assert.IsFalse(view.IsVisible, "パネルへ戻った時点で撤回を反映して非表示にするはず。");
                Assert.IsNull(root.Q<Image>("character-image").image, "テクスチャの参照も手放すこと（PR #135 レビュー L4）。");
            }
            finally
            {
                view.Dispose();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator ConsentCheck_Throws_TreatedAsNotConsented()
        {
            var view = new CharacterView(
                GetCharacterRoot(), _ => null, () => throw new InvalidOperationException("boom"));

            yield return null;

            Assert.IsFalse(view.IsVisible);

            view.Dispose();
        }

        [UnityTest]
        public IEnumerator QuestionResolved_Correct_ShowsCorrectMark_ThenReturnsToIdleAfterHold()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var root = GetCharacterRoot();
                var view = new CharacterView(root, _ => texture, () => true);

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;

                Assert.That(view.State, Is.EqualTo(CharacterState.Correct));
                var markCorrect = root.Q<VisualElement>("character-mark-correct");
                Assert.That(markCorrect.style.display.value, Is.EqualTo(DisplayStyle.Flex));

                // #192: 枠全体を覆う矩形オーバーレイではなく、character-image 自身に
                // tintColor（テクスチャの画素にだけ乗算）で色を付ける。
                var image = root.Q<Image>("character-image");
                Assert.IsTrue(image.ClassListContains("character-image--tint-correct"));
                Assert.IsFalse(image.ClassListContains("character-image--tint-wrong"));
                AssertColorApproximately(
                    ReadThemeColor("--color-character-tint-correct"), image.tintColor,
                    "正解時は --color-character-tint-correct を乗算するはず。");

                var elapsed = 0f;
                while (view.State != CharacterState.Idle && elapsed < 5f)
                {
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }

                Assert.That(view.State, Is.EqualTo(CharacterState.Idle),
                    $"{CharacterStateMachine.ResultHoldSeconds}秒後には Idle に戻るはず。");
                Assert.That(markCorrect.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.IsFalse(image.ClassListContains("character-image--tint-correct"),
                    "Idle に戻ったらティントのクラスも外れるはず。");
                AssertColorApproximately(
                    ReadThemeColor("--color-character-tint-none"), image.tintColor, "Idle では無着色（白）に戻るはず。");

                view.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator QuestionResolved_Wrong_ShowsWrongMarks()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var root = GetCharacterRoot();
                var view = new CharacterView(root, _ => texture, () => true);

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Wrong);
                yield return null;

                Assert.That(view.State, Is.EqualTo(CharacterState.Wrong));
                Assert.That(root.Q<VisualElement>("character-mark-wrong-a").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<VisualElement>("character-mark-wrong-b").style.display.value, Is.EqualTo(DisplayStyle.Flex));

                // #192: character-image に不正解用のティントクラスが付き、tintColor が白から変化する。
                var image = root.Q<Image>("character-image");
                Assert.IsTrue(image.ClassListContains("character-image--tint-wrong"));
                Assert.IsFalse(image.ClassListContains("character-image--tint-correct"));
                AssertColorApproximately(
                    ReadThemeColor("--color-character-tint-wrong"), image.tintColor,
                    "不正解時は --color-character-tint-wrong を乗算するはず。");

                view.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// issue #192: 枠全体を覆う矩形オーバーレイ（character-tint-overlay）は廃止した。
        /// テンプレートに残っていないことを確認する（回帰防止）。
        /// </summary>
        [UnityTest]
        public IEnumerator Template_DoesNotContainSquareTintOverlayElement()
        {
            var root = GetCharacterRoot();
            var view = new CharacterView(root, _ => null, () => true);

            yield return null;

            Assert.IsNull(root.Q<VisualElement>("character-tint-overlay"),
                "立ち絵の枠全体を覆う矩形オーバーレイは廃止済みのはず（#192）。");

            view.Dispose();
        }

        /// <summary>
        /// issue #191: カード（character-image-frame）の縦横比を表示中の画像に合わせ、画像が無くなったら
        /// USS の既定（バストアップ 0.771）に戻す。
        /// </summary>
        [UnityTest]
        public IEnumerator FrameAspectRatio_FollowsTexture_AndResetsWhenCleared()
        {
            var texture = new Texture2D(40, 80, TextureFormat.RGBA32, mipChain: false);
            var consented = true;
            var root = GetCharacterRoot();
            var view = new CharacterView(root, _ => texture, () => consented);
            try
            {
                yield return null;

                var frame = root.Q<VisualElement>("character-image-frame");
                Assert.IsNotNull(frame, "character-image-frame が見つかりません。");
                Assert.AreEqual(StyleKeyword.Undefined, frame.style.aspectRatio.keyword, "画像があるときはインラインで指定するはず。");
                Assert.That(frame.style.aspectRatio.value.value, Is.EqualTo(0.5f).Within(0.0001f), "40x80 の画像なら 0.5 のはず。");
                Assert.That(frame.resolvedStyle.aspectRatio.value, Is.EqualTo(0.5f).Within(0.0001f));

                // 撤回 → 再評価で画像を手放すと、インライン指定を外して USS の既定に戻る。
                consented = false;
                view.RefreshVisibility();
                yield return null;

                Assert.AreEqual(StyleKeyword.Null, frame.style.aspectRatio.keyword, "画像が無いときはインライン指定を外すはず。");
                Assert.That(
                    frame.resolvedStyle.aspectRatio.value, Is.EqualTo(0.771f).Within(0.0001f),
                    "USS の既定（バストアップの縦横比）に戻るはず。");
            }
            finally
            {
                view.Dispose();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator ReadingStartedAndCompleted_ThroughRealTtsSyncPlayer_DrivesState()
        {
            var playerObject = new GameObject(nameof(ReadingStartedAndCompleted_ThroughRealTtsSyncPlayer_DrivesState));
            var player = playerObject.AddComponent<TtsSyncPlayer>();

            var view = new CharacterView(GetCharacterRoot(), _ => null, () => true);
            view.Bind(player, gameSession: null);

            player.RaiseReadingStartedForTesting(0);
            yield return null;
            Assert.That(view.State, Is.EqualTo(CharacterState.Reading), "TtsSyncPlayer.ReadingStarted を購読して Reading になるはず。");

            player.RaiseReadingCompletedForTesting(0);
            yield return null;
            Assert.That(view.State, Is.EqualTo(CharacterState.Idle));

            // Unbind 後はイベントを購読していないこと。
            view.Unbind();
            player.RaiseReadingStartedForTesting(1);
            yield return null;
            Assert.That(view.State, Is.EqualTo(CharacterState.Idle), "Unbind 後は ReadingStarted を反映しないはず。");

            view.Dispose();
            UnityEngine.Object.DestroyImmediate(playerObject);
        }

        [UnityTest]
        public IEnumerator Unbind_ResetsStateMachineToIdle()
        {
            var view = new CharacterView(GetCharacterRoot(), _ => null, () => true);

            view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
            Assert.That(view.State, Is.EqualTo(CharacterState.Correct));

            view.Unbind();

            Assert.That(view.State, Is.EqualTo(CharacterState.Idle), "レビュー M4: Unbind で状態を待機に戻す。");

            yield return null;
            view.Dispose();
        }

        /// <summary>theme.uss の :root から色トークン（#rrggbb）を読む（期待値を USS と二重管理しないため）。</summary>
        private static Color ReadThemeColor(string token)
        {
            var path = Path.Combine(Application.dataPath, ThemeUssRelativePath);
            var text = File.ReadAllText(path);
            var match = Regex.Match(text, Regex.Escape(token) + @"\s*:\s*(#[0-9a-fA-F]{6})\s*;");
            Assert.IsTrue(match.Success, $"{path} に {token}（#rrggbb）が見つかりません。");
            Assert.IsTrue(ColorUtility.TryParseHtmlString(match.Groups[1].Value, out var color), $"{token} を色として解釈できません。");
            return color;
        }

        private static void AssertColorApproximately(Color expected, Color actual, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(ColorTolerance), $"{message}（r、期待 {expected} / 実際 {actual}）");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(ColorTolerance), $"{message}（g、期待 {expected} / 実際 {actual}）");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(ColorTolerance), $"{message}（b、期待 {expected} / 実際 {actual}）");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(ColorTolerance), $"{message}（a、期待 {expected} / 実際 {actual}）");
        }

        [UnityTest]
        public IEnumerator Bind_WithRealGameSession_DoesNotThrow_AndUnbindStopsSubscription()
        {
            var sessionObject = new GameObject(nameof(Bind_WithRealGameSession_DoesNotThrow_AndUnbindStopsSubscription));
            sessionObject.SetActive(false);
            var session = sessionObject.AddComponent<GameSession>();

            var view = new CharacterView(GetCharacterRoot(), _ => null, () => true);

            Assert.DoesNotThrow(() => view.Bind(ttsSyncPlayer: null, gameSession: session));
            Assert.DoesNotThrow(view.Unbind);

            yield return null;

            view.Dispose();
            UnityEngine.Object.DestroyImmediate(sessionObject);
        }
    }
}
