using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.UI;
using TsumugiQuiz.UI.Views.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 場面ごとの表情（issue #212）を、実際に <see cref="AppPaths.DataRoot"/> 配下へ置いた PNG を読ませて確認する
    /// PlayMode テスト。回答権・誤答の瞬間・時間切れ・回答できる人がいない・選択式と、
    /// 専用の差分が無いときのフォールバックとティント、<c>GameView</c> のまとめ読み込みを見る。
    /// </summary>
    /// <remarks>
    /// 立ち絵素材そのものは二次配布禁止（docs/licenses.md §3）でリポジトリに置けないため、
    /// <see cref="CharacterExpressionSwitchTests"/> と同じく、表情ごとに違う幅の小さな PNG を置いて、
    /// 読み込まれたテクスチャの幅で「どのファイルが選ばれたか」を判定する。
    /// </remarks>
    public sealed class CharacterSceneExpressionTests
    {
        private const string TemplatePath = "Assets/TsumugiQuiz/UI/Templates/character-view.uxml";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";

        private const int IdleWidth = 11;
        private const int ReadingWidth = 12;
        private const int CorrectWidth = 13;
        private const int WrongWidth = 14;
        private const int BuzzSelfWidth = 21;
        private const int BuzzOtherWidth = 22;
        private const int WrongMomentWidth = 23;
        private const int TimedOutWidth = 24;
        private const int NoEligibleBuzzersWidth = 25;

        /// <summary>自分のクライアント ID（回答権の自分 / 他人の判定に使う）。</summary>
        private const ulong LocalClientId = 1;

        /// <summary>他の参加者のクライアント ID。</summary>
        private const ulong OtherClientId = 2;

        /// <summary>実際の生成物と同じ大きさ（scripts/generate_tsumugi_expressions.py の bustup・高さ 1280）。</summary>
        private const int GeneratedWidth = 987;
        private const int GeneratedHeight = 1280;

        private const string TintCorrectClassName = "character-image--tint-correct";
        private const string TintWrongClassName = "character-image--tint-wrong";

        private GameObject _documentObject;
        private UIDocument _document;
        private CharacterImageFileScope _fileScope;

        /// <summary>テスト中に読み込んだテクスチャ（所有権はテストにあるので TearDown で破棄する）。</summary>
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

            _documentObject = new GameObject(nameof(CharacterSceneExpressionTests));
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
        /// 専用の表情差分かどうかも返す解決経路（<see cref="CharacterImageLoader.LoadExpression"/>）で作る。
        /// <c>GameView</c> と同じく、フォールバックしたときだけティントが掛かる。
        /// </summary>
        private CharacterView CreateView() =>
            new CharacterView(GetCharacterRoot(), ResolveAndTrack, consentCheck: () => true);

        private CharacterTextureResolution ResolveAndTrack(CharacterState state)
        {
            var resolution = CharacterImageLoader.LoadExpression(state);
            if (resolution.Texture != null)
            {
                _createdTextures.Add(resolution.Texture);
            }

            return resolution;
        }

        private VisualElement GetCharacterRoot()
        {
            var root = _document.rootVisualElement.Q<VisualElement>("character-root");
            Assert.IsNotNull(root, "character-root がロードした UXML から見つかりません。");
            return root;
        }

        private Image GetCharacterImage() => GetCharacterRoot().Q<Image>("character-image");

        private static void WriteLegacyFourExpressions()
        {
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.IdleFileName, IdleWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.ReadingFileName, ReadingWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.CorrectFileName, CorrectWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.WrongFileName, WrongWidth);
        }

        private static void WriteSceneExpressions()
        {
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.BuzzSelfFileName, BuzzSelfWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.BuzzOtherFileName, BuzzOtherWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.WrongMomentFileName, WrongMomentWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.TimedOutFileName, TimedOutWidth);
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.NoEligibleBuzzersFileName, NoEligibleBuzzersWidth);
        }

        private static int WidthOf(Image image)
        {
            Assert.IsNotNull(image.image, "立ち絵テクスチャが適用されていません。");
            return image.image.width;
        }

        private bool IsMarkShown(string name) =>
            GetCharacterRoot().Q<VisualElement>(name).style.display.value == DisplayStyle.Flex;

        private void AssertMarks(bool correct, bool wrong, string scene)
        {
            Assert.AreEqual(correct, IsMarkShown("character-mark-correct"), $"{scene}: ○");
            Assert.AreEqual(wrong, IsMarkShown("character-mark-wrong-a"), $"{scene}: ×（a）");
            Assert.AreEqual(wrong, IsMarkShown("character-mark-wrong-b"), $"{scene}: ×（b）");
        }

        private void AssertTint(bool correct, bool wrong, string scene)
        {
            var image = GetCharacterImage();
            Assert.AreEqual(correct, image.ClassListContains(TintCorrectClassName), $"{scene}: 正解のティント");
            Assert.AreEqual(wrong, image.ClassListContains(TintWrongClassName), $"{scene}: 不正解のティント");
        }

        /// <summary>
        /// 9 枚すべてを生成した環境。場面ごとに専用の表情差分へ切り替わり、専用の差分なのでティントは掛からない。
        /// マークは正解 ○、不正解・時間切れ・回答できる人がいない ×、誤答の瞬間は無し。
        /// </summary>
        [UnityTest]
        public IEnumerator AllExpressionsPlaced_EachSceneShowsItsOwnExpression_WithoutTint()
        {
            WriteLegacyFourExpressions();
            WriteSceneExpressions();

            var view = CreateView();
            view.Bind(ttsSyncPlayer: null, gameSession: null, localClientIdProvider: () => LocalClientId);
            var image = GetCharacterImage();

            try
            {
                yield return null;
                Assert.AreEqual(IdleWidth, WidthOf(image));

                view.SimulateBuzzLockedForTesting(LocalClientId);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.BuzzSelf));
                Assert.AreEqual(BuzzSelfWidth, WidthOf(image), "自分が回答権を得たら tsumugi_buzz_self.png。");
                AssertMarks(correct: false, wrong: false, "回答権・自分");

                view.SimulateBuzzReopenedForTesting(LocalClientId);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.WrongMoment));
                Assert.AreEqual(WrongMomentWidth, WidthOf(image), "誤答の瞬間は tsumugi_wrong_moment.png。");
                AssertMarks(correct: false, wrong: false, "誤答の瞬間（結果は未確定なのでマークなし）");
                AssertTint(correct: false, wrong: false, "誤答の瞬間");

                view.SimulateBuzzLockedForTesting(OtherClientId);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.BuzzOther));
                Assert.AreEqual(BuzzOtherWidth, WidthOf(image), "他の人が回答権を得たら tsumugi_buzz_other.png。");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Wrong);
                yield return null;
                Assert.AreEqual(WrongWidth, WidthOf(image));
                AssertMarks(correct: false, wrong: true, "不正解の確定");
                AssertTint(correct: false, wrong: false, "不正解の確定（専用の差分）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.TimedOut);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.TimedOut));
                Assert.AreEqual(TimedOutWidth, WidthOf(image), "時間切れは tsumugi_timeout.png。");
                AssertMarks(correct: false, wrong: true, "時間切れ");
                AssertTint(correct: false, wrong: false, "時間切れ（専用の差分）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.NoEligibleBuzzers);
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.NoEligibleBuzzers));
                Assert.AreEqual(NoEligibleBuzzersWidth, WidthOf(image), "回答できる人がいないは tsumugi_no_eligible.png。");
                AssertMarks(correct: false, wrong: true, "回答できる人がいない");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.AreEqual(CorrectWidth, WidthOf(image));
                AssertMarks(correct: true, wrong: false, "正解");
                AssertTint(correct: false, wrong: false, "正解（専用の差分）");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// #86 の 4 枚だけを生成した利用者。増やした場面は意味の近い既存の表情へフォールバックし、
        /// フォールバックした結果の場面にだけティントを掛ける。
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyLegacyFourExpressionsPlaced_NewScenesFallBack_AndTintOnlyWhenFallingBack()
        {
            WriteLegacyFourExpressions();

            var view = CreateView();
            view.Bind(ttsSyncPlayer: null, gameSession: null, localClientIdProvider: () => LocalClientId);
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateBuzzLockedForTesting(LocalClientId);
                yield return null;
                Assert.AreEqual(IdleWidth, WidthOf(image), "回答権の差分が無ければ待機の差分。");
                AssertTint(correct: false, wrong: false, "回答権（フォールバックしても結果ではないので無着色）");

                view.SimulateBuzzReopenedForTesting(LocalClientId);
                yield return null;
                Assert.AreEqual(WrongWidth, WidthOf(image), "誤答の瞬間の差分が無ければ不正解の差分。");
                AssertMarks(correct: false, wrong: false, "誤答の瞬間");
                AssertTint(correct: false, wrong: false, "誤答の瞬間（結果ではないので無着色）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.TimedOut);
                yield return null;
                Assert.AreEqual(WrongWidth, WidthOf(image), "時間切れの差分が無ければ不正解の差分。");
                AssertMarks(correct: false, wrong: true, "時間切れ");
                AssertTint(correct: false, wrong: true, "時間切れ（フォールバックしたのでティント）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.NoEligibleBuzzers);
                yield return null;
                Assert.AreEqual(WrongWidth, WidthOf(image), "回答できる人がいないの差分も時間切れも無ければ不正解の差分。");
                AssertTint(correct: false, wrong: true, "回答できる人がいない（フォールバック）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Wrong);
                yield return null;
                Assert.AreEqual(WrongWidth, WidthOf(image));
                AssertTint(correct: false, wrong: false, "不正解の確定（専用の差分なので無着色）");

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.AreEqual(CorrectWidth, WidthOf(image));
                AssertTint(correct: false, wrong: false, "正解（専用の差分なので無着色）");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>待機の差分だけの環境では、正解も待機の顔になるので正解のティントで補う。</summary>
        [UnityTest]
        public IEnumerator OnlyIdlePlaced_CorrectFallsBack_AndGetsCorrectTint()
        {
            CharacterImageTestFiles.WriteExpression(CharacterImagePaths.IdleFileName, IdleWidth);

            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateQuestionResolvedForTesting(QuizJudgement.Correct);
                yield return null;
                Assert.AreEqual(IdleWidth, WidthOf(image));
                AssertTint(correct: true, wrong: false, "正解（待機へフォールバック）");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>選択式の一斉判定でも表情が変わる（自分が答えていれば自分の正誤を優先する）。</summary>
        [UnityTest]
        public IEnumerator ChoiceResolved_UsesOwnResult()
        {
            WriteLegacyFourExpressions();

            var view = CreateView();
            view.Bind(ttsSyncPlayer: null, gameSession: null, localClientIdProvider: () => LocalClientId);
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateChoiceResolvedForTesting(new[]
                {
                    new ChoiceAnswerEntry(OtherClientId, 0, isCorrect: true, scoreDelta: 1, totalScore: 1),
                    new ChoiceAnswerEntry(LocalClientId, 1, isCorrect: false, scoreDelta: 0, totalScore: 0),
                });
                yield return null;

                Assert.That(view.State, Is.EqualTo(CharacterState.Wrong));
                Assert.AreEqual(WrongWidth, WidthOf(image));
                AssertMarks(correct: false, wrong: true, "選択式・自分は不正解");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// 選択式で、回答できる立場なのに選ばなかったプレイヤーは時間切れ（どんよりと ×）になる
        /// （ほかの人が正解していても。ユーザー決定 2026-10-03）。
        /// </summary>
        [UnityTest]
        public IEnumerator ChoiceResolved_AnswererWhoDidNotChoose_ShowsTimedOut()
        {
            WriteLegacyFourExpressions();
            WriteSceneExpressions();

            var view = CreateView();
            view.Bind(
                ttsSyncPlayer: null, gameSession: null,
                localClientIdProvider: () => LocalClientId, isLocalChoiceAnswererProvider: () => true);
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateChoiceResolvedForTesting(new[]
                {
                    new ChoiceAnswerEntry(OtherClientId, 0, isCorrect: true, scoreDelta: 1, totalScore: 1),
                });
                yield return null;

                Assert.That(view.State, Is.EqualTo(CharacterState.TimedOut));
                Assert.AreEqual(TimedOutWidth, WidthOf(image), "選ばなかったプレイヤーは tsumugi_timeout.png。");
                AssertMarks(correct: false, wrong: true, "選択式・選ばなかった");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// 選択式で、回答できない立場（司会専任のホスト・次問休み）は全体の結果になる（誰かが正解なら正解）。
        /// </summary>
        [UnityTest]
        public IEnumerator ChoiceResolved_NonAnswerer_ShowsOverallResult()
        {
            WriteLegacyFourExpressions();
            WriteSceneExpressions();

            var view = CreateView();
            view.Bind(
                ttsSyncPlayer: null, gameSession: null,
                localClientIdProvider: () => LocalClientId, isLocalChoiceAnswererProvider: () => false);
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateChoiceResolvedForTesting(new[]
                {
                    new ChoiceAnswerEntry(OtherClientId, 0, isCorrect: true, scoreDelta: 1, totalScore: 1),
                });
                yield return null;

                Assert.That(view.State, Is.EqualTo(CharacterState.Correct));
                Assert.AreEqual(CorrectWidth, WidthOf(image));
                AssertMarks(correct: true, wrong: false, "選択式・回答できない立場で誰かが正解");
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>出題で、前の問題の回答権の表情が残っていれば待機へ戻す。自分の ID が分からない間は他人として扱う。</summary>
        [UnityTest]
        public IEnumerator QuestionShown_ClearsLeftoverBuzzExpression()
        {
            WriteLegacyFourExpressions();
            WriteSceneExpressions();

            var view = CreateView();
            var image = GetCharacterImage();

            try
            {
                yield return null;

                view.SimulateBuzzLockedForTesting(LocalClientId);
                yield return null;
                Assert.AreEqual(BuzzOtherWidth, WidthOf(image), "ID が分からない（未 Bind）間は他人として扱う。");

                view.SimulateQuestionShownForTesting();
                yield return null;
                Assert.That(view.State, Is.EqualTo(CharacterState.Idle));
                Assert.AreEqual(IdleWidth, WidthOf(image));
            }
            finally
            {
                view.Dispose();
            }
        }

        /// <summary>
        /// <c>GameView</c> のキャッシュは、どれか 1 つの状態が初めて必要になったときに全状態をまとめて読み込む
        /// （初めての回答権・誤答の瞬間にデコードが重ならないように）。実際の生成物と同じ 987x1280 の PNG を
        /// 9 枚置いて、まとめて読み込む時間を測り、ログに残す。
        /// </summary>
        /// <remarks>
        /// テスト画像はノイズなので PNG の圧縮が効かず（1 枚約 5MB）、実際の立ち絵（約 0.94MB）より
        /// デコードは重い。上限の目安として測る。時間のしきい値は環境差が大きいので設けない。
        /// </remarks>
        [Test]
        public void GameViewCache_FirstResolve_PreloadsEveryState()
        {
            var states = (CharacterState[])Enum.GetValues(typeof(CharacterState));
            var pngBytes = TestImageFactory.CreatePng(GeneratedWidth, GeneratedHeight, seed: 212);
            foreach (var state in states)
            {
                var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.GetFileName(state));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pngBytes);
            }

            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var idle = GameView.ResolveCharacterTexture(CharacterState.Idle);
                stopwatch.Stop();

                Assert.IsNotNull(idle.Texture);
                Debug.Log($"[CharacterPreloadMeasurement] {states.Length} 状態"
                    + $"（{GeneratedWidth}x{GeneratedHeight}、PNG 1 枚 {pngBytes.Length:N0} バイト）を"
                    + $" {stopwatch.Elapsed.TotalMilliseconds:F1} ms で読み込みました。");

                // ファイルを消しても引けること ＝ 最初の 1 回で全状態を読み込み済みであること。
                foreach (var state in states)
                {
                    File.Delete(AppPaths.Combine(CharacterImagePaths.DirectoryName, CharacterImagePaths.GetFileName(state)));
                }

                var textures = new HashSet<Texture2D>();
                foreach (var state in states)
                {
                    var resolution = GameView.ResolveCharacterTexture(state);
                    Assert.IsNotNull(resolution.Texture, $"{state} も最初の解決で読み込まれているはず。");
                    Assert.IsTrue(resolution.IsDedicated, $"{state} は専用の表情差分を置いたので IsDedicated のはず。");
                    Assert.AreEqual(GeneratedWidth, resolution.Texture.width);
                    textures.Add(resolution.Texture);
                }

                Assert.AreEqual(states.Length, textures.Count, "状態ごとに別のファイルを読んだはず。");
            }
            finally
            {
                // Reset がキャッシュ内のテクスチャを破棄する。
                GameView.ResetCharacterTextureCacheForTesting();
            }
        }

        /// <summary>
        /// #86 の 4 枚だけの利用者: 増やした場面はフォールバック先と同じテクスチャを共有し、IsDedicated は false。
        /// </summary>
        [Test]
        public void GameViewCache_LegacyFourPlaced_NewScenesShareFallbackTexture()
        {
            WriteLegacyFourExpressions();

            GameView.ResetCharacterTextureCacheForTesting();
            try
            {
                var wrong = GameView.ResolveCharacterTexture(CharacterState.Wrong);
                Assert.IsTrue(wrong.IsDedicated);

                var timedOut = GameView.ResolveCharacterTexture(CharacterState.TimedOut);
                Assert.IsFalse(timedOut.IsDedicated, "時間切れは不正解の差分へフォールバックしている。");
                Assert.IsTrue(ReferenceEquals(wrong.Texture, timedOut.Texture), "同じ PNG を二重に読まない。");

                var buzzSelf = GameView.ResolveCharacterTexture(CharacterState.BuzzSelf);
                var idle = GameView.ResolveCharacterTexture(CharacterState.Idle);
                Assert.IsFalse(buzzSelf.IsDedicated);
                Assert.IsTrue(idle.IsDedicated);
                Assert.IsTrue(ReferenceEquals(idle.Texture, buzzSelf.Texture));
            }
            finally
            {
                GameView.ResetCharacterTextureCacheForTesting();
            }
        }
    }
}
