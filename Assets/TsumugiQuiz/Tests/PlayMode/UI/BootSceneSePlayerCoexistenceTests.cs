using System.Collections;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// Boot シーンに <see cref="SePlayer"/>（#35）と <see cref="NetworkBootstrap"/> / <see cref="NetworkManager"/>
    /// （#2）の両方が常駐配置されていることを確認する。develop への merge で Boot.unity を
    /// develop 側（NetworkManager 入り）に統一した後、SePlayerBootstrap で SePlayer を再配置しているため、
    /// 両者が共存し続けることを回帰的に確認する。
    /// </summary>
    public class BootSceneSePlayerCoexistenceTests
    {
        private const string BootSceneName = "Boot";

        [TearDown]
        public void TearDown()
        {
            var sePlayer = SePlayer.Instance;
            if (sePlayer != null)
            {
                Object.DestroyImmediate(sePlayer.gameObject);
            }

            var bootstrap = NetworkBootstrap.Instance;
            if (bootstrap != null)
            {
                Object.DestroyImmediate(bootstrap.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator BootScene_HasBothSePlayerAndNetworkManager()
        {
            yield return SceneManager.LoadSceneAsync(BootSceneName, LoadSceneMode.Single);

            Assert.IsNotNull(SePlayer.Instance, "Boot シーンに SePlayer が配置されているはず。");
            Assert.IsNotNull(NetworkManager.Singleton, "Boot シーンに NetworkManager が配置されているはず。");
            Assert.IsNotNull(NetworkBootstrap.Instance, "Boot シーンに NetworkBootstrap が配置されているはず。");

            // SePlayer.Awake が DontDestroyOnLoad(gameObject) を呼ぶため、Boot シーンの
            // ロードが完了した時点（Awake は scene load 完了までに同期的に実行される）で
            // 既に特殊シーン "DontDestroyOnLoad" へ移されている。
            Assert.AreEqual("DontDestroyOnLoad", SePlayer.Instance.gameObject.scene.name,
                "SePlayer は DontDestroyOnLoad で常駐しているはず。");

            // SeAssetPaths（SeKind の全値）すべてが SePlayer 側のクリップ解決に成功することを確認する。
            foreach (var kind in SeAssetPaths.AllKinds)
            {
                Assert.IsTrue(
                    SePlayer.Instance.TryResolveClip(kind, out var clip) && clip != null,
                    $"SeKind.{kind} に対応するクリップが Boot シーンの SePlayer で解決できません。");
            }

            // 実際に SE が解決・再生できることも合わせて確認する。
            Assert.DoesNotThrow(() => SePlayer.Instance.Play(SeKind.Buzz));
        }
    }
}
