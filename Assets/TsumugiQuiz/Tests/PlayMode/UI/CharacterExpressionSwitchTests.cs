using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 表情差分（issue #86）の切り替えを、実際に <see cref="AppPaths.DataRoot"/> 配下へ置いた
    /// PNG を読ませて確認する PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CharacterViewTests"/> は <c>textureResolver</c> にスタブを挿して配線だけを見るのに対し、
    /// 本テストは既定の解決経路（<see cref="CharacterImageLoader.Load(CharacterState)"/> →
    /// <see cref="CharacterImagePaths"/> → <see cref="AppPaths"/>）をそのまま通す。
    /// </para>
    /// <para>
    /// 立ち絵素材そのものは二次配布禁止（docs/licenses.md §3）でリポジトリに置けないため、
    /// テスト用の小さな PNG（<see cref="TestImageFactory.CreatePng"/>）を表情ごとに違う幅で作り、
    /// 読み込まれたテクスチャの幅で「どのファイルが選ばれたか」を判定する。
    /// 書き込み先はデータルート（<c>scripts/verify.ps1</c> が worktree ごとに分離する）だが、
    /// Editor から直接実行した場合に実ユーザーのデータを壊さないよう、既存ファイルは退避して復元する。
    /// </para>
    /// </remarks>
    public sealed class CharacterExpressionSwitchTests
    {
        private const string TemplatePath = "Assets/TsumugiQuiz/UI/Templates/character-view.uxml";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";

        /// <summary>表情ごとのテスト画像の幅。どの候補が読まれたかを幅で見分ける。</summary>
        private const int IdleWidth = 11;
        private const int ReadingWidth = 12;
        private const int CorrectWidth = 13;
        private const int WrongWidth = 14;
        private const int LegacyWidth = 15;

        private GameObject _documentObject;
        private UIDocument _document;
        private CharacterImageFileScope _fileScope;

        /// <summary>
        /// テスト中に <see cref="CharacterImageLoader"/> が作ったテクスチャ。
        /// 所有権は呼び出し側（本番では <c>GameView</c> のキャッシュ）にあるので、テストが後始末する。
        /// </summary>
        private readonly List<Texture2D> _createdTextures = new();

        [SetUp]
        public void CreateDocument()
        {
#if UNITY_EDITOR
            var template = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TemplatePath);
            Assert.IsNotNull(template, $"{TemplatePath} が見つかりません。");

            var panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            Assert.IsNotNull(panelSettings, $"{PanelSettingsPath} が見つかりません。");

            CharacterImageTestFiles.AssertIsolatedDataRoot();
            _fileScope = CharacterImageFileScope.Backup();

            _documentObject = new GameObject(nameof(CharacterExpressionSwitchTests));
            _document = _documentObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
            Assert.IsNotNull(_document.rootVisualElement, "UIDocument.rootVisualElement が null です。");
            _document.rootVisualElement.Add(template.Instantiate());
#else
            Assert.Ignore("PlayMode テストは Editor 上でのみ実行する（AssetDatabase を使うため）。");
#endif
        }

        [TearDown]
        public void Cleanup()
        {
            if (_documentObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_documentObject);
            }

            _documentObject = null;
            _document = null;

            foreach (var texture in _createdTextures)
            {
                CharacterImageLoader.DestroyTexture(texture);
            }

            _createdTextures.Clear();

            _fileScope?.Restore();
            _fileScope = null;
        }

        /// <summary>
        /// 既定の解決経路（<see cref="CharacterImageLoader.Load(CharacterState)"/>）をそのまま使いつつ、
        /// 生成されたテクスチャを TearDown で破棄できるように記録する。
        /// </summary>
        private Texture2D ResolveAndTrack(CharacterState state)
        {
            var texture = CharacterImageLoader.Load(state).Texture;
            if (texture != null)
            {
                _createdTextures.Add(texture);
            }

            return texture;
        }

        private CharacterView CreateView() =>
            new CharacterView(GetCharacterRoot(), ResolveAndTrack, consentCheck: () => true);

        private VisualElement GetCharacterRoot()
        {
            var root = _document.rootVisualElement.Q<VisualElement>("character-root");
            Assert.IsNotNull(root, "character-root がロードした UXML から見つかりません。");
            return root;
        }

        private Image GetCharacterImage() => GetCharacterRoot().Q<Image>("character-image");

        private static void WriteExpression(string fileName, int width) =>
            CharacterImageTestFiles.WriteExpression(fileName, width);

        private static void WriteAllExpressions()
        {
            WriteExpression(CharacterImagePaths.IdleFileName, IdleWidth);
            WriteExpression(CharacterImagePaths.ReadingFileName, ReadingWidth);
            WriteExpression(CharacterImagePaths.CorrectFileName, CorrectWidth);
            WriteExpression(CharacterImagePaths.WrongFileName, WrongWidth);
        }

        private static int WidthOf(Image image)
        {
            Assert.IsNotNull(image.image, "立ち絵テクスチャが適用されていません。");
            return image.image.width;
        }

        [UnityTest]
        public IEnumerator Reading_SwitchesToReadingExpression_AndBackToIdle()
        {
            WriteAllExpressions();

            var playerObject = new GameObject(nameof(Reading_SwitchesToReadingExpression_AndBackToIdle));
            var player = playerObject.AddComponent<TtsSyncPlayer>();
            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;
                Assert.IsTrue(view.IsVisible, "待機の表情差分を配置したので表示されるはず。");
                Assert.AreEqual(IdleWidth, WidthOf(image));

                view.Bind(player, gameSession: null);

                player.RaiseReadingStartedForTesting(0);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Reading));
                Assert.AreEqual(ReadingWidth, WidthOf(image), "読み上げ中は tsumugi_reading.png に切り替わるはず。");

                player.RaiseReadingCompletedForTesting(0);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Idle));
                Assert.AreEqual(IdleWidth, WidthOf(image), "読み上げ完了で待機の表情に戻るはず。");
            }
            finally
            {
                view.Dispose();
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [UnityTest]
        public IEnumerator QuestionResolved_SwitchesToCorrectAndWrongExpressions_ThenReturnsToIdle()
        {
            WriteAllExpressions();

            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;
                Assert.AreEqual(IdleWidth, WidthOf(image));

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Correct));
                Assert.AreEqual(CorrectWidth, WidthOf(image));

                // ResultHoldSeconds 経過で待機に戻る。
                var elapsed = 0f;
                while (view.State != CharacterState.Idle && elapsed < 5f)
                {
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }

                Assert.That(view.State, Is.EqualTo(CharacterState.Idle));
                Assert.AreEqual(IdleWidth, WidthOf(image));

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Wrong);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Wrong));
                Assert.AreEqual(WrongWidth, WidthOf(image));
            }
            finally
            {
                view.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator OnlyLegacyWholeBodyPngPlaced_AllStatesUseIt_AndStayVisible()
        {
            // #24 までの環境（setup-external.ps1 が置いた tsumugi_v2.png だけ）。
            WriteExpression(CharacterImagePaths.FileName, LegacyWidth);

            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;
                Assert.IsTrue(view.IsVisible);
                Assert.AreEqual(LegacyWidth, WidthOf(image));

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Correct));
                Assert.IsTrue(view.IsVisible, "表情差分が無くても立ち絵は出したまま（#24 の演出で表現する）。");
                Assert.AreEqual(LegacyWidth, WidthOf(image), "未生成の表情は従来の全身 PNG にフォールバックするはず。");
            }
            finally
            {
                view.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator OnlyIdleExpressionPlaced_OtherStatesFallBackToIdle()
        {
            WriteExpression(CharacterImagePaths.IdleFileName, IdleWidth);

            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;
                Assert.AreEqual(IdleWidth, WidthOf(image));

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Wrong);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Wrong));
                Assert.AreEqual(IdleWidth, WidthOf(image), "不正解の差分が無ければ待機の差分を使うはず。");
            }
            finally
            {
                view.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator NothingPlaced_StaysHiddenThroughStateChanges()
        {
            // 何も配置していない状態（未配置の案内は警告ログのみ、UI には出さない）。
            var view = CreateView();

            try
            {
                yield return null;
                Assert.IsFalse(view.IsVisible);

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.IsFalse(view.IsVisible, "1 枚も無ければ状態が変わっても非表示のままにする（レビュー H1）。");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// <c>textureResolver</c> 未指定（既定）の <see cref="CharacterView"/> が
        /// <see cref="CharacterImageLoader"/> 経由でデータルートの表情差分を読むことを確認する。
        /// </summary>
        [UnityTest]
        public IEnumerator DefaultTextureResolver_ReadsExpressionFromDataRoot()
        {
            WriteExpression(CharacterImagePaths.IdleFileName, IdleWidth);

            var view = new CharacterView(GetCharacterRoot(), consentCheck: () => true);
            var image = GetCharacterImage();

            try
            {
                yield return null;

                Assert.IsTrue(view.IsVisible);
                Assert.AreEqual(IdleWidth, WidthOf(image));
            }
            finally
            {
                // 既定の解決関数が作ったテクスチャは誰も所有していないので、ここで破棄する。
                _createdTextures.Add(image.image as Texture2D);
                view.Dispose();
            }
        }

        /// <summary>
        /// <c>GameView</c> のキャッシュ（PR #135 レビュー M4）: 表情差分が未生成で 4 状態すべてが
        /// 同じ <c>tsumugi_v2.png</c> へフォールバックする場合、同じテクスチャ参照を返して
        /// PNG のデコードを 1 回で済ませることを確認する。
        /// </summary>
        [Test]
        public void GameViewCache_OnlyLegacyPngPlaced_AllStatesShareOneTexture()
        {
            WriteExpression(CharacterImagePaths.FileName, LegacyWidth);

            // 他のテストが残したプロセス単位キャッシュを一旦捨ててから測る（レビュー L1）。
            GameView.ResetCharacterTextureCacheForTesting();

            // 再レビュー L3: ReferenceEquals の検証が失敗した場合（＝別々のテクスチャが返った場合）でも
            // 取りこぼさないよう、受け取ったテクスチャはすべて控えて finally でまとめて破棄する。
            var resolved = new List<Texture2D>();
            try
            {
                var first = GameView.ResolveCharacterTexture(CharacterState.Idle).Texture;
                resolved.Add(first);
                Assert.IsNotNull(first, "tsumugi_v2.png を配置したので読み込めるはず。");
                Assert.AreEqual(LegacyWidth, first.width);

                foreach (CharacterState state in Enum.GetValues(typeof(CharacterState)))
                {
                    var texture = GameView.ResolveCharacterTexture(state).Texture;
                    resolved.Add(texture);
                    Assert.IsTrue(
                        ReferenceEquals(first, texture),
                        $"{state} も同じファイルへフォールバックするので、同じテクスチャを使い回すはず。");
                }
            }
            finally
            {
                // レビュー L1 / 再レビュー L2: Reset がキャッシュ内のテクスチャを破棄する。
                // ここでの破棄は、キャッシュに載らなかった分の保険（DestroyTexture は冪等）。
                GameView.ResetCharacterTextureCacheForTesting();
                foreach (var texture in resolved)
                {
                    CharacterImageLoader.DestroyTexture(texture);
                }
            }
        }
    }
}
