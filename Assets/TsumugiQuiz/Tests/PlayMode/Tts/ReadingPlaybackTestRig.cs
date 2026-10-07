using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tts;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.Tts
{
    /// <summary>
    /// 同期再生（#23）の PlayMode テストで、<see cref="TtsService"/> と
    /// <see cref="TtsSyncPlayer"/> を組み立てる補助（docs/tts.md §11.2）。
    /// </summary>
    /// <remarks>
    /// <see cref="TtsService"/> は<b>非アクティブな GameObject</b> に載せて生成する。
    /// <c>Awake</c> を走らせないことで <c>TtsService.Instance</c> と <c>DontDestroyOnLoad</c> に触らず、
    /// ホスト用・クライアント用の 2 つを同じプロセスに共存させられる。
    /// キャッシュ先はテストごとの一時ディレクトリに差し替える。
    /// </remarks>
    internal sealed class ReadingPlaybackTestRig : IDisposable
    {
        private readonly List<GameObject> _serviceObjects = new List<GameObject>();
        private readonly List<string> _cacheRoots = new List<string>();

        /// <summary>
        /// テスト用の <see cref="TtsService"/> を作り、初期化を始める。
        /// </summary>
        /// <param name="name">GameObject 名（ログで見分けるため）。</param>
        /// <param name="engineFactory">合成エンジンの生成関数。null なら本番実装（実 DLL）。</param>
        /// <param name="consentCheck">
        /// 同意確認（#127 レビュー M-2）。本番では UI 層がアプリ起動時に
        /// <see cref="TtsService.ConfigureDefaults"/> で登録するもの。null なら制限しない（従来どおり）。
        /// <b>ここで渡さないと <see cref="TtsService"/> 側の同意ゲートは素通りになる</b>ので、
        /// 同意まわりを検証するテストは必ず渡すこと。
        /// </param>
        /// <returns>初期化を始めた <see cref="TtsService"/>。</returns>
        public TtsService CreateService(
            string name, TtsSynthesisEngineFactory engineFactory = null, Func<bool> consentCheck = null)
        {
            var cacheRoot = Path.Combine(
                Path.GetTempPath(), "TsumugiQuizTtsSyncTests", Guid.NewGuid().ToString("N"));
            _cacheRoots.Add(cacheRoot);

            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            _serviceObjects.Add(gameObject);

            var service = gameObject.AddComponent<TtsService>();
            service.Initialize(
                cacheRootOverride: cacheRoot, consentCheck: consentCheck, engineFactory: engineFactory);
            return service;
        }

        /// <summary>
        /// <see cref="GameSession"/> に載っている <see cref="TtsSyncPlayer"/> に
        /// 使う <see cref="TtsService"/> を差し込む。
        /// </summary>
        /// <param name="session">対象のセッション（ホスト側 / クライアント側）。</param>
        /// <param name="service">使う読み上げサービス。</param>
        /// <returns>差し込んだ <see cref="TtsSyncPlayer"/>。</returns>
        public static TtsSyncPlayer AttachPlayer(GameSession session, TtsService service)
        {
            Assert.IsNotNull(session, "セッションが必要です。");

            var player = session.GetComponent<TtsSyncPlayer>();
            Assert.IsNotNull(player, "GameSession プレハブに TtsSyncPlayer が載っているはず（#23）。");

            player.SetService(service);
            return player;
        }

        /// <summary>同じ <c>NetworkObject</c> に載っている同期再生の司令塔を取り出す。</summary>
        /// <param name="session">対象のセッション。</param>
        /// <returns>司令塔。</returns>
        public static TtsSyncCoordinator GetCoordinator(GameSession session)
        {
            Assert.IsNotNull(session, "セッションが必要です。");

            var coordinator = session.GetComponent<TtsSyncCoordinator>();
            Assert.IsNotNull(coordinator, "GameSession プレハブに TtsSyncCoordinator が載っているはず（#23）。");
            return coordinator;
        }

        /// <summary>生成した <see cref="TtsService"/> と一時ディレクトリを片付ける。</summary>
        public void Dispose()
        {
            foreach (var gameObject in _serviceObjects)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _serviceObjects.Clear();

            foreach (var cacheRoot in _cacheRoots)
            {
                try
                {
                    if (Directory.Exists(cacheRoot))
                    {
                        Directory.Delete(cacheRoot, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // 一時ディレクトリなので、まだ書き込み中でも放置してよい。
                }
                catch (UnauthorizedAccessException)
                {
                    // 同上（ロック中・権限なし）。テスト結果には影響しないので握って進む。
                }
            }

            _cacheRoots.Clear();
        }
    }
}
