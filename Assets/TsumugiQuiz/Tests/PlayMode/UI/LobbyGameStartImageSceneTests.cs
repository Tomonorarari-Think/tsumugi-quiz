using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Editing;
using TsumugiQuiz.Tests.PlayMode.UI.HostSetup;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using static TsumugiQuiz.Tests.PlayMode.UI.MainSceneTestHelpers;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// issue #185: ロビーの「ゲーム開始」（本番の出題開始経路）から、画像付きの問題の画像が
    /// 配信・表示されることを UI 操作だけで確かめる PlayMode テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// #185 の不具合のうちサーバー側（<see cref="GameSession.ConfigureImages"/> が本番コードから
    /// 一度も呼ばれていなかった）は、既存の画像配信テストがテストコードから
    /// <see cref="GameSession.ConfigureImages"/> を直接呼んでいたために検出できなかった
    /// （#95 の <see cref="GameSession.Configure"/> と同じ構図）。本テストは
    /// <b>テストコードから Configure / ConfigureImages / StartSession を一切呼ばず</b>、ロビーのボタンを
    /// 押すだけで画像が Game View に出ることを確認する（配線漏れの回帰テスト）。
    /// </para>
    /// <para>
    /// 問題データと画像は <see cref="LobbyGameStartSceneTests"/> と同じ方針で、問題フォルダ
    /// （<see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/>。<c>PlayModeTestAssemblySetUp</c> が
    /// 一時フォルダへ隔離済み）に GUID 付きのファイルとして書き、TearDown で必ず削除する。
    /// ホストもクライアントとして自分自身に画像を配信する（docs/network.md §8.6）ので、ホストの画面で確認できる。
    /// </para>
    /// </remarks>
    public sealed class LobbyGameStartImageSceneTests
    {
        private const string ImageQuestionText = "i185 この画像に写っているものは？";
        private const int ImageWidth = 200;
        private const int ImageHeight = 100;

        private ConsentFileScope _consentScope;
        private HostSetupPreferencesScope _preferencesScope;
        private string _testSetFilePath;
        private string _testImagePath;

        /// <summary>
        /// 他のテスト（サンプル問題セットの自動配置を含む）が問題フォルダに残した問題セットの退避先。
        /// 出題順はシャッフルされるため、他のセットが混ざると 1 問目が画像なしの問題になりうる。
        /// </summary>
        private string _stashFolder;

        [SetUp]
        public void SeedConsentedStateAndImageQuestionSet()
        {
            _consentScope = ConsentFileScope.Backup();

            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            _preferencesScope = HostSetupPreferencesScope.Backup();

            var folder = QuestionRepository.GetDefaultQuestionsFolderPath();
            Directory.CreateDirectory(folder);
            var uniqueSuffix = Guid.NewGuid().ToString("N");
            StashOtherQuestionSets(folder, uniqueSuffix);

            var imageRelativePath = "images/i185-playmode-test-" + uniqueSuffix + ".png";
            Directory.CreateDirectory(Path.Combine(folder, "images"));
            _testImagePath = Path.Combine(folder, imageRelativePath);
            File.WriteAllBytes(_testImagePath, TestImageFactory.CreatePng(ImageWidth, ImageHeight, seed: 185));

            var fileName = "i185-playmode-test-" + uniqueSuffix;
            _testSetFilePath = Path.Combine(folder, fileName + ".json");

            var question = new Question(
                "q1",
                QuestionType.FreeText,
                ImageQuestionText,
                answers: new[] { "こたえ" },
                imagePath: imageRelativePath);
            var set = new QuestionSet(1, fileName, "i185 PlayModeテストセット", string.Empty, new[] { question });
            Assert.IsTrue(QuestionSetWriter.TryWriteNew(_testSetFilePath, set, out var error), error);
        }

        [TearDown]
        public void DeleteTestFilesAndRestorePreferences()
        {
            DeleteIfExists(_testSetFilePath);
            DeleteIfExists(_testImagePath);
            RestoreStashedQuestionSets();

            _preferencesScope?.Restore();
            _consentScope?.Restore();
        }

        [UnityTearDown]
        public IEnumerator TearDownSceneAndNetwork()
        {
            var bootService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            bootService?.Stop();

            yield return WaitUntil(
                () => bootService == null
                      || !(bootService.IsListening || bootService.IsClient || bootService.IsShutdownInProgress),
                DefaultTimeoutSeconds,
                "ホストの停止が完了しませんでした。");
            yield return null;

            yield return MainSceneTestHelpers.UnloadMainSceneRoutine();

            TearDownMainSceneAndBootstrapSingletons();
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HostStartGame_FromLobby_DeliversAndShowsQuestionImage()
        {
            VisualElement panelRoot = null;
            yield return LobbyViewSceneTests.LoadMainThroughBootAndInstallFakes(root => panelRoot = root);

            Button hostButton = null;
            yield return WaitForElement<Button>(panelRoot, "host-button", found => hostButton = found);
            yield return SimulateClickRoutine(hostButton);

            yield return LobbyViewSceneTests.StartHostingWithAutoPort(panelRoot);

            Button lobbyButton = null;
            yield return WaitForElement<Button>(panelRoot, "lobby-button", found => lobbyButton = found);
            yield return WaitUntil(() => lobbyButton.enabledSelf, "到達性の解決完了後は lobby-button が有効になるはず。");
            yield return SimulateClickRoutine(lobbyButton);

            Button startGameButton = null;
            yield return WaitForElement<Button>(panelRoot, "start-game-button", found => startGameButton = found);
            yield return WaitUntil(
                () => startGameButton.enabledSelf,
                "ロビーの同期が済めば「ゲーム開始」が有効になるはず。");

            var session = NetworkBootstrap.Instance.Service.ActiveGameSession;
            Assert.IsNotNull(session, "ホスト開始時に GameSession がスポーンされるはず。");

            // --- 「ゲーム開始」（ここから先はテストコードからセッションを設定しない） ---
            yield return SimulateClickRoutine(startGameButton);

            VisualElement container = null;
            Image image = null;
            yield return WaitForElement<VisualElement>(panelRoot, "question-image-container", found => container = found);
            yield return WaitForElement<Image>(panelRoot, "question-image", found => image = found);

            yield return WaitUntil(
                () => container.resolvedStyle.display == DisplayStyle.Flex && image.image != null,
                DefaultTimeoutSeconds,
                () => "ロビーから開始した画像付きの問題で、画像が Game View に表示されませんでした"
                      + $"（display: {container.resolvedStyle.display}、配信器の保持: "
                      + $"{(session.Distributor != null && session.Distributor.TryGetImage(0, out _))}）。",
                panelRoot);

            Assert.IsTrue(session.Distributor.TryGetImage(0, out var texture), "ホスト自身の配信器が画像を保持しているはず。");
            Assert.AreSame(texture, image.image, "配信器が保持しているテクスチャを貼るはず。");
            Assert.AreEqual(ImageWidth, texture.width, "配信した画像が復号されているはず。");
            Assert.AreEqual(ImageHeight, texture.height);
        }

        /// <summary>
        /// 問題フォルダ直下の既存の問題セット（*.json）を退避する。問題フォルダは
        /// <c>PlayModeTestAssemblySetUp</c> がテスト専用の一時フォルダへ隔離しているので、実ユーザーのファイルには触れない。
        /// </summary>
        private void StashOtherQuestionSets(string folder, string uniqueSuffix)
        {
            var existing = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly);
            if (existing.Length == 0)
            {
                return;
            }

            _stashFolder = Path.Combine(Path.GetTempPath(), "tq-i185-stash-" + uniqueSuffix);
            Directory.CreateDirectory(_stashFolder);
            foreach (var path in existing)
            {
                File.Move(path, Path.Combine(_stashFolder, Path.GetFileName(path)));
            }
        }

        private void RestoreStashedQuestionSets()
        {
            if (_stashFolder == null || !Directory.Exists(_stashFolder))
            {
                return;
            }

            var folder = QuestionRepository.GetDefaultQuestionsFolderPath();
            foreach (var path in Directory.GetFiles(_stashFolder))
            {
                var destination = Path.Combine(folder, Path.GetFileName(path));
                if (!File.Exists(destination))
                {
                    File.Move(path, destination);
                }
            }

            Directory.Delete(_stashFolder, recursive: true);
            _stashFolder = null;
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
