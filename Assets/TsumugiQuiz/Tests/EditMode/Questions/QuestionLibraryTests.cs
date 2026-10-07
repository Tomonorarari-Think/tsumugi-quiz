using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using TsumugiQuiz.Questions;
using UnityEngine;
using UnityEngine.TestTools;

namespace TsumugiQuiz.Tests.EditMode.Questions
{
    /// <summary>
    /// <see cref="QuestionLibrary"/> の検証（issue #29）。
    /// 一時フォルダを使い、初回起動時のフォルダ自動作成・サンプル書き出し・再読込・
    /// 混在フォルダの結果を確認する。FileSystemWatcher 経由の実イベントは1件だけ
    /// UnityTest として確認し（デバウンスロジック自体は ReloadDebouncerTests で決定的に検証済み）、
    /// それ以外は enableFileWatcher: false でスレッド・タイマーに依存せず検証する。
    /// </summary>
    public class QuestionLibraryTests
    {
        [Test]
        public void Constructor_FolderMissing_CreatesFolderAndWritesWorkingSample()
        {
            var tempDir = CreateTempFolderPath();
            Assert.IsFalse(Directory.Exists(tempDir));

            QuestionLibrary library = null;
            try
            {
                library = new QuestionLibrary(tempDir, enableFileWatcher: false);

                Assert.IsTrue(Directory.Exists(tempDir), "フォルダが自動作成されること");

                var jsonPath = Path.Combine(tempDir, "sample-questions.json");
                var imagePath = Path.Combine(tempDir, "images", "sample.png");
                Assert.IsTrue(File.Exists(jsonPath), "サンプル問題データが書き出されること");
                Assert.IsTrue(File.Exists(imagePath), "サンプル画像も書き出されること（画像参照の問題を含むため）");

                // サンプルは docs/samples/sample-questions.json 相当（3問、画像込みで検証を通過すること）。
                Assert.AreEqual(0, library.CurrentReport.SkippedSets.Count,
                    "書き出したサンプルはそのまま検証を通過すること（画像も同梱済み）");
                Assert.AreEqual(1, library.CurrentReport.Sets.Count);
                Assert.AreEqual("sample-set-01", library.CurrentReport.Sets[0].SetId);
                Assert.AreEqual(3, library.CurrentReport.Sets[0].Questions.Count);
                Assert.AreEqual(0, library.CurrentReport.FolderErrors.Count, "正常系ではフォルダ単位のエラーが無いこと");
            }
            finally
            {
                library?.Dispose();
                DeleteIfExists(tempDir);
            }
        }

        [Test]
        public void Constructor_FolderAlreadyExists_DoesNotWriteSample()
        {
            var tempDir = CreateTempFolderPath();
            Directory.CreateDirectory(tempDir);

            QuestionLibrary library = null;
            try
            {
                library = new QuestionLibrary(tempDir, enableFileWatcher: false);

                var jsonPath = Path.Combine(tempDir, "sample-questions.json");
                Assert.IsFalse(File.Exists(jsonPath),
                    "フォルダが既に存在する場合（＝初回起動ではない）はサンプルを書き出さないこと");
                Assert.AreEqual(0, library.CurrentReport.Sets.Count);
                Assert.AreEqual(0, library.CurrentReport.SkippedSets.Count);
            }
            finally
            {
                library?.Dispose();
                DeleteIfExists(tempDir);
            }
        }

        [Test]
        public void Constructor_FolderCreationFails_ReportsFolderErrorWithoutThrowing()
        {
            // H1: フォルダ作成に失敗しても例外を投げず、監視は行わない（手動再読込のみ）に degrade し、
            // 理由を QuestionLoadReport.FolderErrors に積む。
            // 既存の「ファイル」と同名のパスをフォルダとして渡すことで CreateDirectory を失敗させる。
            var tempFile = Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(tempFile, "occupied");

            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionLibrary\].*作成できませんでした"));
                // フォルダ作成自体の失敗（QuestionLibrary）に加え、その後 LoadAll() が呼ばれた際に
                // QuestionRepository 自身も「フォルダが見つからない」ことを別途 Warning でログに残す
                // （フォルダが実在しない以上、これも正しい状態なので抑制しない）。
                LogAssert.Expect(LogType.Warning, new Regex(@"\[QuestionRepository\].*見つかりません"));

                QuestionLibrary library = null;
                Assert.DoesNotThrow(() =>
                {
                    library = new QuestionLibrary(tempFile, enableFileWatcher: false);
                });

                Assert.AreEqual(0, library.CurrentReport.Sets.Count);
                // QuestionLibrary（作成失敗）と QuestionRepository（フォルダ不在）の両方が、
                // それぞれの視点でフォルダ単位のエラーを積むため2件になる（重複の抑制はしない）。
                Assert.AreEqual(2, library.CurrentReport.FolderErrors.Count);
                Assert.IsTrue(
                    library.CurrentReport.FolderErrors.Any(m => m.Contains("作成できませんでした")),
                    "フォルダ作成失敗の理由が含まれること");

                library.Dispose();
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public void OnWatcherError_TriggersDebounceForCatchUpAndAttemptsRearm()
        {
            // H2: watcher 自体がエラーを報告した場合、LogError で記録したうえで
            // デバウンサへ Trigger して取りこぼし分を回収し、監視の再構築を試みる。
            // 実際に OS レベルで watcher エラーを起こすのはタイミング依存で不安定なため、
            // リフレクションで OnWatcherError を直接呼び出して決定的に検証する。
            // SynchronizationContext.Current の有無はテスト実行環境（NUnit アダプタ）依存のため、
            // ここで明示的に設定し、_mainThreadContext が null になって監視が無効化される
            // （L15）ケースに巻き込まれないようにする。
            using (new SynchronizationContextScope())
            {
                var tempDir = CreateTempFolderPath();
                Directory.CreateDirectory(tempDir);

                var scheduler = new ManualDebounceScheduler();
                var library = new QuestionLibrary(tempDir, enableFileWatcher: true, debounceScheduler: scheduler);
                try
                {
                    Assert.AreEqual(0, scheduler.ScheduleCount, "コンストラクタの時点ではまだ何もトリガーされていないこと");

                    LogAssert.Expect(LogType.Error, new Regex(@"\[QuestionLibrary\].*監視でエラーが発生"));

                    var method = typeof(QuestionLibrary).GetMethod("OnWatcherError", BindingFlags.NonPublic | BindingFlags.Instance);
                    Assert.IsNotNull(method, "OnWatcherError が見つかること");
                    // メッセージに英単語 "error" を含めると verify.ps1 のログ走査（\berror\b）に
                    // 誤検知されるため、あえて含めない文言にする。
                    method.Invoke(library, new object[] { null, new ErrorEventArgs(new IOException("simulated watcher failure for testing")) });

                    Assert.AreEqual(1, scheduler.ScheduleCount, "取りこぼし回収のため Trigger が呼ばれること");
                }
                finally
                {
                    library.Dispose();
                    DeleteIfExists(tempDir);
                }
            }
        }

        [Test]
        public void Reload_MixedValidAndInvalidFiles_ReportsBothIndependently()
        {
            var tempDir = CreateTempFolderPath();
            Directory.CreateDirectory(tempDir);

            QuestionLibrary library = null;
            try
            {
                library = new QuestionLibrary(tempDir, enableFileWatcher: false);
                Assert.AreEqual(0, library.CurrentReport.Sets.Count, "初回はフォルダが空であること");

                // M4: docs/samples/sample-questions.json を単一ソースとして利用する
                // （テストデータとして別ファイルへ手で複製しない。テスト側で docs をコピーして読む）。
                CopyDocsSampleWithImage(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "invalid-set.json"), "{ invalid json ");

                var report = library.Reload();

                Assert.AreEqual(1, report.Sets.Count, "正常なセットだけが読み込まれること");
                Assert.AreEqual("sample-set-01", report.Sets[0].SetId);
                Assert.AreEqual(1, report.SkippedSets.Count, "不正なセットだけがスキップされること");
                StringAssert.Contains("invalid-set.json", report.SkippedSets[0].FilePath);
                Assert.AreSame(report, library.CurrentReport, "Reload の戻り値と CurrentReport は同じ内容であること");
            }
            finally
            {
                library?.Dispose();
                DeleteIfExists(tempDir);
            }
        }

        [Test]
        public void Reload_RaisesChangedEventWithTheSameReport()
        {
            var tempDir = CreateTempFolderPath();
            Directory.CreateDirectory(tempDir);

            QuestionLibrary library = null;
            try
            {
                library = new QuestionLibrary(tempDir, enableFileWatcher: false);

                QuestionLoadReport eventReport = null;
                library.Changed += r => eventReport = r;

                var reloadReport = library.Reload();

                Assert.IsNotNull(eventReport, "Reload 時に Changed イベントが発火すること");
                Assert.AreSame(reloadReport, eventReport);
            }
            finally
            {
                library?.Dispose();
                DeleteIfExists(tempDir);
            }
        }

        [Test]
        public void Dispose_ThenDebouncedReloadFires_CurrentReportUnchanged()
        {
            // H3: 実際の FileSystemWatcher イベント・実タイマーに依存すると EditMode テストとして
            // 不安定になるため、IDebounceScheduler（ManualDebounceScheduler）を注入し、
            // 実時間の待機なしに「Dispose 後は自動再読込が反映されない」ことを決定的に検証する。
            using (new SynchronizationContextScope())
            {
                var tempDir = CreateTempFolderPath();
                Directory.CreateDirectory(tempDir);

                var scheduler = new ManualDebounceScheduler();
                var library = new QuestionLibrary(tempDir, enableFileWatcher: true, debounceScheduler: scheduler);
                try
                {
                    // Dispose 前に変更検知があった想定でトリガーしておく（スケジュールが1件積まれる）。
                    library.TriggerDebouncedReloadForTesting();
                    Assert.AreEqual(1, scheduler.ScheduleCount);

                    var before = library.CurrentReport;

                    library.Dispose();

                    // Dispose 後にスケジュールが発火しても、CurrentReport は変化しないこと
                    // （ReloadDebouncer.Dispose によるキャンセル、および QuestionLibrary 側の
                    // _disposed ガードの二段構え。詳細は ReloadDebouncerTests も参照）。
                    scheduler.FireLatest();

                    Assert.AreSame(before, library.CurrentReport);
                }
                finally
                {
                    DeleteIfExists(tempDir);
                }
            }
        }

        [UnityTest]
        [Category("Slow")]
        public IEnumerator Watcher_FileAddedToFolder_ReloadsAfterDebounce()
        {
            // NOTE: このテストは UnityTest（コルーチン）として Unity のメインスレッドの
            // PlayerLoop 上で実行されるため、Unity が用意する実際の SynchronizationContext を
            // そのまま利用する（ここを差し替えると Changed イベントがスレッドプールスレッドから
            // 発火するようになり、下記 changedCount のポーリングに新たなスレッド安全性の懸念を
            // 持ち込んでしまうため、あえて SynchronizationContextScope は使わない）。
            var tempDir = CreateTempFolderPath();
            Directory.CreateDirectory(tempDir);

            QuestionLibrary library = null;
            try
            {
                library = new QuestionLibrary(tempDir, enableFileWatcher: true, debounceDelay: TimeSpan.FromMilliseconds(100));

                var changedCount = 0;
                library.Changed += _ => changedCount++;

                File.WriteAllText(Path.Combine(tempDir, "watcher-set.json"), ValidSetJson("watcher-test"));

                var stopwatch = Stopwatch.StartNew();
                while (changedCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
                {
                    yield return null;
                }

                Assert.Greater(changedCount, 0,
                    "FileSystemWatcher の変更検知からデバウンス経由で Reload が実行され、Changed が発火すること");
                Assert.AreEqual(1, library.CurrentReport.Sets.Count);
                Assert.AreEqual("watcher-test", library.CurrentReport.Sets[0].SetId);
            }
            finally
            {
                library?.Dispose();
                DeleteIfExists(tempDir);
            }
        }

        private static string CreateTempFolderPath()
        {
            return Path.Combine(Path.GetTempPath(), "TsumugiQuizTests_" + Guid.NewGuid().ToString("N"));
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        /// <summary>
        /// M4: docs/samples/sample-questions.json を単一ソースとして、テスト用フォルダへコピーする
        /// （画像参照 images/sample.png も検証を通すため、Resources に同梱済みのプレースホルダー画像を
        /// 併せてコピーする）。
        /// </summary>
        private static void CopyDocsSampleWithImage(string destinationFolder)
        {
            var docsSamplePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "samples", "sample-questions.json"));
            Assert.IsTrue(File.Exists(docsSamplePath), $"docs/samples/sample-questions.json が見つかりません: {docsSamplePath}");

            File.Copy(docsSamplePath, Path.Combine(destinationFolder, "sample-questions.json"), overwrite: true);

            var imagesDir = Path.Combine(destinationFolder, "images");
            Directory.CreateDirectory(imagesDir);

            var placeholderImage = Resources.Load<TextAsset>("Questions/sample-image");
            Assert.IsNotNull(placeholderImage, "Resources/Questions/sample-image.bytes が見つかりません");
            File.WriteAllBytes(Path.Combine(imagesDir, "sample.png"), placeholderImage.bytes);
        }

        private static string ValidSetJson(string setId)
        {
            return "{"
                + "\"schemaVersion\": 1,"
                + $"\"setId\": \"{setId}\","
                + "\"title\": \"テストセット\","
                + "\"questions\": ["
                + "{ \"id\": \"q1\", \"type\": \"freeText\", \"text\": \"テスト問題\", \"answers\": [\"ok\"] }"
                + "]}";
        }

        /// <summary>
        /// プレーンな NUnit <c>[Test]</c> 実行時に <see cref="SynchronizationContext.Current"/> が
        /// 存在するかどうかはテストランナー実装依存のため、<see cref="QuestionLibrary"/> の
        /// L15（同期コンテキストが無い場合は監視を無効化する）分岐に意図せず巻き込まれないよう、
        /// テスト中だけ明示的に（ダミーの）同期コンテキストを設定し、終了時に元へ戻す。
        /// </summary>
        private sealed class SynchronizationContextScope : IDisposable
        {
            private readonly SynchronizationContext _previous;

            public SynchronizationContextScope()
            {
                _previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            }

            public void Dispose()
            {
                SynchronizationContext.SetSynchronizationContext(_previous);
            }
        }
    }
}
