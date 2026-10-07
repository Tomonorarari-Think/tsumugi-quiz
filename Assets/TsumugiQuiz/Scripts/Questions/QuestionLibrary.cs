using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TsumugiQuiz.Questions
{
    /// <summary>
    /// 問題フォルダ（既定: <see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/>）の
    /// 起動時読み込み・「再読込」・<see cref="FileSystemWatcher"/> によるフォルダ変更検知をまとめて扱う
    /// （issue #29「問題フォルダ監視/再読込」。UI 結線は #5 に引き継ぎ）。
    ///
    /// - フォルダが存在しない場合は初回起動時に自動作成し、Resources に同梱したサンプル問題データ
    ///   （docs/samples/sample-questions.json 相当）を書き出す（既にファイルがあれば書き出さない）。
    /// - フォルダ作成・監視開始のいずれかに失敗しても、コンストラクタは例外を投げない。
    ///   その場合は監視なし（手動「再読込」のみ）に degrade し、原因は
    ///   <see cref="QuestionLoadReport.FolderErrors"/> に載せて UI から確認できるようにする（H1）。
    /// - <see cref="FileSystemWatcher"/> の変更通知は短時間に連続して発生しうるため
    ///   <see cref="ReloadDebouncer"/>（既定 500ms）でまとめ、生成時に取得した
    ///   <see cref="SynchronizationContext"/>（Unity のメインスレッド）へ Post して
    ///   メインスレッド上で結果を反映する。
    ///   このとき実際のフォルダ読み込み・検証はワーカースレッド（<see cref="Task.Run(Action)"/>）で行い、
    ///   メインスレッドでは結果を <see cref="CurrentReport"/> に反映して <see cref="Changed"/> を
    ///   発火するだけに留める（M10）。手動で呼び出す <see cref="Reload"/> は従来通り同期版として残す。
    /// </summary>
    public sealed class QuestionLibrary : IDisposable
    {
        private const string SampleJsonFileName = "sample-questions.json";
        private const string SampleImageRelativePath = "images/sample.png";
        private const string SampleJsonResourcePath = "Questions/sample-questions";
        private const string SampleImageResourcePath = "Questions/sample-image";

        private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);

        // M8: 1階層読み込み対象の *.json に加え、images/ 配下の画像変更も再読込のトリガーにする
        // （imagePath 検証結果が変わりうるため）。FileSystemWatcher.Filter は1パターンしか
        // 指定できないため、フィルタなしで監視して OnFileSystemEvent 側で拡張子により間引く。
        private static readonly HashSet<string> RelevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".png", ".jpg", ".jpeg",
        };

        private const int WatcherInternalBufferSize = 64 * 1024; // H2: 既定8KBだと変更多発時に取りこぼす恐れがある

        private readonly string _folderPath;
        private readonly QuestionRepository _repository;
        private readonly SynchronizationContext _mainThreadContext;
        private readonly bool _fileWatcherRequested;
        private readonly TimeSpan _debounceDelay;
        private readonly IDebounceScheduler _debounceScheduler;
        private readonly object _watcherGate = new object();
        private readonly object _reportGate = new object();

        private ReloadDebouncer _debouncer;
        private FileSystemWatcher _watcher;
        private string _folderSetupError;
        private string _watcherError;
        private bool _disposed;

        /// <summary>再読込が完了するたびに（メインスレッドで）発火する。</summary>
        public event Action<QuestionLoadReport> Changed;

        /// <summary>直近の読み込み結果。</summary>
        public QuestionLoadReport CurrentReport
        {
            get
            {
                lock (_reportGate)
                {
                    return _currentReport;
                }
            }
            private set
            {
                lock (_reportGate)
                {
                    _currentReport = value;
                }
            }
        }

        private QuestionLoadReport _currentReport;

        /// <summary>読み込み元フォルダの絶対パス。</summary>
        public string FolderPath => _folderPath;

        /// <summary>
        /// <see cref="QuestionLibrary"/> を生成し、初回読み込みを行う。
        /// フォルダ作成・監視開始のいずれに失敗しても例外を投げない（H1）。
        /// </summary>
        /// <param name="folderPath">
        /// 問題フォルダの絶対パス。null / 空文字の場合は
        /// <see cref="QuestionRepository.GetDefaultQuestionsFolderPath"/> を使う。
        /// </param>
        /// <param name="enableFileWatcher">
        /// <see cref="FileSystemWatcher"/> による監視を有効にするか。テストで無効化できる。
        /// </param>
        /// <param name="debounceDelay">デバウンス時間。省略時は 500ms。</param>
        /// <param name="debounceScheduler">
        /// デバウンスに使うスケジューラ。省略時は <see cref="TimerDebounceScheduler"/>。テストで差し替える。
        /// </param>
        public QuestionLibrary(
            string folderPath = null,
            bool enableFileWatcher = true,
            TimeSpan? debounceDelay = null,
            IDebounceScheduler debounceScheduler = null)
        {
            _folderPath = string.IsNullOrEmpty(folderPath)
                ? QuestionRepository.GetDefaultQuestionsFolderPath()
                : folderPath;
            _repository = new QuestionRepository(_folderPath);
            _mainThreadContext = SynchronizationContext.Current;
            _fileWatcherRequested = enableFileWatcher;
            _debounceDelay = debounceDelay ?? DefaultDebounceDelay;
            _debounceScheduler = debounceScheduler;

            EnsureFolderAndSample();

            if (enableFileWatcher)
            {
                if (_mainThreadContext == null)
                {
                    // L15: メインスレッドの同期コンテキストが無いと、監視イベントを安全に
                    // メインスレッドへ marshalling できないため、監視自体を無効化する。
                    const string message = "フォルダ監視を初期化できませんでした（メインスレッドの同期コンテキストが取得できません）。「再読込」ボタンで手動更新してください。";
                    Debug.LogError($"[QuestionLibrary] {message}");
                    _watcherError = message;
                }
                else
                {
                    _debouncer = new ReloadDebouncer(OnDebouncedChange, _debounceDelay, _debounceScheduler);
                    TryCreateAndArmWatcher();
                }
            }

            _currentReport = BuildReport();
        }

        /// <summary>
        /// 問題フォルダを同期的に再読込し、結果を返す。<see cref="Changed"/> も発火する。
        /// 監視が無効化されている場合（フォルダ不在等）は、このタイミングで再アームを試みる（H2）。
        /// </summary>
        public QuestionLoadReport Reload()
        {
            if (_fileWatcherRequested && _mainThreadContext != null)
            {
                lock (_watcherGate)
                {
                    if (_watcher == null && !_disposed)
                    {
                        TryCreateAndArmWatcher();
                    }
                }
            }

            var report = BuildReport();
            CurrentReport = report;
            Changed?.Invoke(report);
            return report;
        }

        private QuestionLoadReport BuildReport()
        {
            var result = _repository.LoadAll();
            return QuestionLoadReport.FromRepositoryResult(DateTimeOffset.Now, result, CollectExtraFolderErrors());
        }

        private IEnumerable<string> CollectExtraFolderErrors()
        {
            if (_folderSetupError != null)
            {
                if (Directory.Exists(_folderPath))
                {
                    // フォルダが後から用意された（利用者が作成した等）ので解消したとみなす。
                    _folderSetupError = null;
                }
                else
                {
                    yield return _folderSetupError;
                }
            }

            string watcherError;
            lock (_watcherGate)
            {
                watcherError = _watcherError;
            }

            if (watcherError != null)
            {
                yield return watcherError;
            }
        }

        private void EnsureFolderAndSample()
        {
            // L2/M5: フォルダが無い場合のみ自動作成し、そのタイミングでサンプルを書き出す
            // （＝「初回起動」相当。フォルダが既に存在する場合はユーザーが削除しない限り触らない）。
            if (Directory.Exists(_folderPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_folderPath);
            }
            catch (Exception ex)
            {
                var message = $"問題フォルダを作成できませんでした: {_folderPath}: {ex.Message}";
                Debug.LogError($"[QuestionLibrary] {message}");
                _folderSetupError = message;
                return;
            }

            WriteSampleIfMissing();
        }

        private void WriteSampleIfMissing()
        {
            var jsonPath = Path.Combine(_folderPath, SampleJsonFileName);
            if (!File.Exists(jsonPath))
            {
                WriteResourceText(SampleJsonResourcePath, jsonPath);
            }

            var imagePath = Path.Combine(_folderPath, SampleImageRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(imagePath))
            {
                WriteResourceBytes(SampleImageResourcePath, imagePath);
            }
        }

        private static void WriteResourceText(string resourcePath, string destinationPath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[QuestionLibrary] サンプル問題データが見つかりません（Resources: {resourcePath}）。書き出しをスキップします。");
                return;
            }

            try
            {
                File.WriteAllText(destinationPath, asset.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestionLibrary] サンプル問題データの書き出しに失敗しました: {destinationPath}: {ex.Message}");
            }
        }

        private static void WriteResourceBytes(string resourcePath, string destinationPath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[QuestionLibrary] サンプル画像が見つかりません（Resources: {resourcePath}）。書き出しをスキップします。");
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(destinationPath, asset.bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestionLibrary] サンプル画像の書き出しに失敗しました: {destinationPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// H1/H2: 既存の監視を破棄したうえで新しい <see cref="FileSystemWatcher"/> の生成・購読・
        /// 有効化までを試み、いずれかの段階で失敗しても例外を外へ伝播させない。
        /// フォルダが存在しない場合は監視を開始できないため <see cref="_watcherError"/> を設定して
        /// 諦め、次回 <see cref="Reload"/> 時に再度呼び出されるのを待つ（H2「フォルダ不在時は
        /// 次回 Reload() で再arm」）。呼び出し元で <see cref="_watcherGate"/> を保持していない場合も
        /// 安全なように、このメソッド自身でロックを取る。
        /// </summary>
        private void TryCreateAndArmWatcher()
        {
            lock (_watcherGate)
            {
                DisposeWatcherLocked();

                if (!Directory.Exists(_folderPath))
                {
                    _watcherError = "問題フォルダが存在しないため監視を開始できません。次回の再読込時に自動的に監視を再開します。";
                    return;
                }

                try
                {
                    var watcher = new FileSystemWatcher(_folderPath)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite
                            | NotifyFilters.FileName
                            | NotifyFilters.DirectoryName
                            | NotifyFilters.Size, // L22
                        InternalBufferSize = WatcherInternalBufferSize,
                    };

                    watcher.Changed += OnFileSystemEvent;
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemEvent;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;

                    _watcher = watcher;
                    _watcherError = null;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[QuestionLibrary] フォルダ監視の開始に失敗しました: {_folderPath}: {ex.Message}");
                    _watcherError = $"フォルダ監視を開始できませんでした（{ex.Message}）。「再読込」ボタンで手動更新してください。";
                    _watcher = null;
                }
            }
        }

        /// <summary>呼び出し元で <see cref="_watcherGate"/> を保持していることが前提。</summary>
        private void DisposeWatcherLocked()
        {
            if (_watcher == null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Deleted -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileSystemEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (!IsRelevantPath(e.FullPath))
            {
                return;
            }

            _debouncer?.Trigger();
        }

        private static bool IsRelevantPath(string path)
        {
            var extension = Path.GetExtension(path);
            return !string.IsNullOrEmpty(extension) && RelevantExtensions.Contains(extension);
        }

        /// <summary>
        /// H2: watcher 自体がエラー（内部バッファのオーバーフロー等）を報告してきた場合、
        /// LogError で明確に記録したうえで、取りこぼした変更を回収するために即座に再読込を予約し、
        /// 監視そのものも破棄して作り直す（フォルダが無くなっていた場合は次回 Reload() まで待つ）。
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Debug.LogError($"[QuestionLibrary] 問題フォルダの監視でエラーが発生しました。再読込と監視の再構築を試みます: {e.GetException()?.Message}");

            _debouncer?.Trigger();

            if (!_disposed)
            {
                TryCreateAndArmWatcher();
            }
        }

        private void OnDebouncedChange()
        {
            // M10: I/O・検証はワーカースレッドで行い、メインスレッドでは結果の反映のみを行う。
            Task.Run(() =>
            {
                if (_disposed)
                {
                    return;
                }

                QuestionLoadReport report;
                try
                {
                    report = BuildReport();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[QuestionLibrary] 再読込中にエラーが発生しました: {ex.Message}");
                    return;
                }

                if (_mainThreadContext != null)
                {
                    _mainThreadContext.Post(_ => ApplyReportIfNotDisposed(report), null);
                }
                else
                {
                    ApplyReportIfNotDisposed(report);
                }
            });
        }

        private void ApplyReportIfNotDisposed(QuestionLoadReport report)
        {
            if (_disposed)
            {
                return;
            }

            CurrentReport = report;
            Changed?.Invoke(report);
        }

        /// <summary>
        /// テスト専用: 実際の <see cref="FileSystemWatcher"/> イベント（OS 依存でタイミングが
        /// 不確定）を経由せずに、デバウンサへのトリガーだけを直接発火させる。
        /// EditMode テストで「Dispose 後は再読込が反映されない」ことを実タイマー・実ファイル
        /// システムイベントに依存せず決定的に検証するために用意している（H3）。
        /// </summary>
        internal void TriggerDebouncedReloadForTesting()
        {
            _debouncer?.Trigger();
        }

        /// <summary>
        /// フォルダ監視（<see cref="FileSystemWatcher"/>）を停止し、保留中のデバウンス予約を
        /// キャンセルする。Dispose 後は、Dispose 前に発火した（かもしれない）自動再読込の結果も
        /// <see cref="CurrentReport"/> へは反映されない（<see cref="ApplyReportIfNotDisposed"/> が
        /// ガードする）。ただし、呼び出し元が明示的に <see cref="Reload"/> を呼んだ場合は
        /// Dispose 後でも同期的に動作する（意図的な仕様。L20）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_watcherGate)
            {
                DisposeWatcherLocked();
            }

            _debouncer?.Dispose();
        }
    }
}
