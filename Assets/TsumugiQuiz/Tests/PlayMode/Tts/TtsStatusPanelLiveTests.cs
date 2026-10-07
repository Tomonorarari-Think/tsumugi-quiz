using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI;
using UnityEngine;
using UnityEngine.TestTools;
using TsumugiQuiz.Tests.PlayMode.UI;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// <see cref="TtsService.StatusChanged"/> が <c>Update</c> のポーリングを経てメインスレッドで発火し、
    /// それを購読した表示（Title 隅のコーナー表示相当）が実際に更新されることの検証（#25 M-3）。
    /// EditMode の <c>[Test]</c> はフレームを進めないため、この検証は PlayMode でのみ行える。
    /// </summary>
    public sealed class TtsStatusPanelLiveTests
    {
        private GameObject _gameObject;
        private TtsService _service;
        private string _cacheRoot;
        private MainSceneTestHelpers.ConsentFileScope _consentScope;

        [SetUp]
        public void SetUp()
        {
            // TtsStatusPanel.DescribeCornerStatus は未同意を最優先で「利用不可」にする（FR-74/75）ため、
            // Status 由来の遷移（未確認→Ready 等）を見るにはあらかじめ同意済みにしておく必要がある。
            _consentScope = MainSceneTestHelpers.ConsentFileScope.Backup();
            var store = ConsentGate.CreateDefaultStore();
            store.RecordConsent(TermsCatalog.LoadRequiredTerms(), Application.version, DateTime.UtcNow);

            // Boot シーンを経由する他のテストが DontDestroyOnLoad の TtsService を残していることがある。
            if (TtsService.Instance != null)
            {
                UnityEngine.Object.DestroyImmediate(TtsService.Instance.gameObject);
            }

            _cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsStatusPanelLiveTests", Guid.NewGuid().ToString("N"));

            _gameObject = new GameObject(nameof(TtsStatusPanelLiveTests));
            _service = _gameObject.AddComponent<TtsService>();

            // Initialize はまだ呼ばない（StatusChanged の初回遷移から観測するため、SetUp では起動しない）。
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
            _gameObject = null;
            _service = null;

            if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true);

            _consentScope?.Restore();
        }

        [UnityTest]
        public IEnumerator StatusChangedはUpdateポーリングを経て通知される()
        {
            var received = new List<TtsServiceState>();
            _service.StatusChanged += status => received.Add(status.State);

            Assert.That(_service.Status.State, Is.EqualTo(TtsServiceState.NotInitialized));

            var task = _service.EnsureInitializedAsync(new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot);

            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("初期化が終わりませんでした。");
                yield return null;
            }

            // Update() のポーリングが完了後の状態を検知するまで、もう数フレーム待つ。
            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(received, Is.Not.Empty, "StatusChanged が最低 1 回は発火すること");
            Assert.That(received[^1], Is.EqualTo(_service.Status.State),
                "最後に通知された状態が現在の Status と一致すること");
            Assert.That(received.Contains(TtsServiceState.NotInitialized), Is.False,
                "NotInitialized からの遷移後に通知されるので、通知一覧に NotInitialized 自体は含まれないこと");
        }

        [UnityTest]
        public IEnumerator コーナー表示はStatusChanged経由で更新される()
        {
            var initialCorner = TtsStatusPanel.DescribeCornerStatus(_service);
            Assert.That(initialCorner, Is.EqualTo("読み上げ: 未確認"), "初期化前は未確認と表示されること");

            var latestCorner = initialCorner;
            _service.StatusChanged += _ => latestCorner = TtsStatusPanel.DescribeCornerStatus(_service);

            var task = _service.EnsureInitializedAsync(new FixedTtsSettingsProvider(TtsSettings.Default), _cacheRoot);

            var deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("初期化が終わりませんでした。");
                yield return null;
            }

            for (var i = 0; i < 5; i++) yield return null;

            Assert.That(latestCorner, Is.Not.EqualTo(initialCorner),
                "StatusChanged 経由でコーナー表示が更新されること");
            Assert.That(latestCorner, Is.EqualTo(TtsStatusPanel.DescribeCornerStatus(_service)),
                "購読側が見ている値が現在の実際の表示と一致すること");
        }
    }
}
