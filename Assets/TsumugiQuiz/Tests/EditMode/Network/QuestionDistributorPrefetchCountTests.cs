using NUnit.Framework;
using TsumugiQuiz.Network;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.Network
{
    /// <summary>
    /// <see cref="QuestionDistributor.PrefetchCount"/> のクランプ（<c>0</c>〜
    /// <see cref="QuestionDistributor.MaxPrefetchCount"/>）を確かめる（issue #27、docs/room-settings.md §2）。
    /// </summary>
    /// <remarks>
    /// <c>question.prefetchCount</c> は<b>アプリ設定</b>なのでクライアントへ同期しない。
    /// 値の出所は <c>app-settings.json</c>（#28 で配線）で、範囲外の値がそのまま入ると
    /// クライアント側の DTO キャッシュ上限（<c>MaxPrefetchCount + 1</c>）を超えて取りこぼすため、
    /// setter で丸めていることを保証する。
    /// </remarks>
    public sealed class QuestionDistributorPrefetchCountTests
    {
        private GameObject _gameObject;
        private QuestionDistributor _distributor;

        [SetUp]
        public void SetUp()
        {
            // 非アクティブのまま組み立てる（Awake / OnDestroy を走らせず、NetworkManager に触らせない）。
            _gameObject = new GameObject(nameof(QuestionDistributorPrefetchCountTests));
            _gameObject.SetActive(false);
            _gameObject.AddComponent<NetworkObject>();
            _distributor = _gameObject.AddComponent<QuestionDistributor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
            }

            _gameObject = null;
            _distributor = null;
        }

        [Test]
        public void PrefetchCount_Default_IsDefaultPrefetchCount()
        {
            Assert.AreEqual(QuestionDistributor.DefaultPrefetchCount, _distributor.PrefetchCount);
        }

        [Test]
        public void PrefetchCount_OutOfRange_IsClampedToZeroAndMax()
        {
            _distributor.PrefetchCount = -5;
            Assert.AreEqual(0, _distributor.PrefetchCount, "負の値は 0 に丸めるはず。");

            _distributor.PrefetchCount = QuestionDistributor.MaxPrefetchCount + 1;
            Assert.AreEqual(
                QuestionDistributor.MaxPrefetchCount,
                _distributor.PrefetchCount,
                "上限を超える値は MaxPrefetchCount に丸めるはず。");

            _distributor.PrefetchCount = int.MaxValue;
            Assert.AreEqual(QuestionDistributor.MaxPrefetchCount, _distributor.PrefetchCount);
        }

        [Test]
        public void PrefetchCount_InRange_IsKept()
        {
            for (var value = 0; value <= QuestionDistributor.MaxPrefetchCount; value++)
            {
                _distributor.PrefetchCount = value;
                Assert.AreEqual(value, _distributor.PrefetchCount, $"範囲内の {value} はそのまま保持するはず。");
            }
        }
    }
}
