using System;
using System.Collections.Generic;
using System.IO;
using TsumugiQuiz.Core.Audio;

namespace TsumugiQuiz.Tts
{
    /// <summary>
    /// 合成済み wav のディスクキャッシュ（docs/tts.md §7）。
    ///
    /// 配置:
    /// <code>
    /// &lt;root&gt;/index.json              ← LRU 管理用のメタ情報（TtsCacheIndex）
    /// &lt;root&gt;/&lt;key[0:2]&gt;/&lt;key&gt;.wav   ← 先頭 2 文字でサブディレクトリ分割
    /// </code>
    ///
    /// 方針:
    /// <list type="bullet">
    ///   <item><description>キャッシュの読み書き失敗は<b>致命的でない</b>。ログを出して合成にフォールバックする</description></item>
    ///   <item><description>上限超過時は <c>lastUsedUtc</c> の古い順に、上限の 90% を下回るまで削除する</description></item>
    ///   <item><description><c>index.json</c> が壊れていたらディレクトリ走査で再構築する</description></item>
    ///   <item><description>wav の書き込みは <c>.wav.tmp</c> 経由で置き換える（<see cref="ITtsCacheFileSystem"/>）</description></item>
    ///   <item><description>ヘッダが壊れている wav は索引に載せず削除する（容量を握ったまま追い出せなくなるため）</description></item>
    /// </list>
    ///
    /// Unity API に依存しないので EditMode テストで検証できる。インスタンスはスレッドセーフ。
    /// </summary>
    public sealed class TtsCache
    {
        /// <summary>LRU 管理用インデックスのファイル名。</summary>
        public const string IndexFileName = "index.json";

        /// <summary>キャッシュされる wav の拡張子。</summary>
        public const string WavExtension = ".wav";

        /// <summary>走査で wav を拾うときの検索パターン。</summary>
        public const string WavSearchPattern = "*" + WavExtension;

        /// <summary>走査で一時ファイルの残骸を拾うときの検索パターン。</summary>
        public const string TempSearchPattern = "*" + TtsCacheFileSystem.TempSuffix;

        /// <summary>最終アクセス時刻の更新をまとめて書き出すまでの回数。</summary>
        private const int TouchFlushThreshold = 16;

        private readonly object _gate = new object();
        private readonly ITtsCacheFileSystem _fileSystem;
        private readonly Action<string> _logWarning;
        private readonly Func<DateTime> _utcNow;

        private TtsCacheIndex _index;
        private bool _loaded;
        private bool _indexDirty;
        private int _pendingTouches;

        /// <param name="rootDirectory"><c>AppPaths.DataRoot/TtsCache</c> 相当（#71）</param>
        /// <param name="limits">上限（<see cref="TtsCacheLimits.FromSettings"/>）</param>
        /// <param name="fileSystem">I/O 実装。null なら <see cref="TtsCacheFileSystem"/></param>
        /// <param name="logWarning">警告の出力先。null なら何もしない（呼び出し側が Debug.LogWarning を渡す）</param>
        /// <param name="utcNow">現在時刻（UTC）の取得。テストで LRU を決定的にするために差し替えられる</param>
        public TtsCache(
            string rootDirectory,
            TtsCacheLimits limits,
            ITtsCacheFileSystem fileSystem = null,
            Action<string> logWarning = null,
            Func<DateTime> utcNow = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("キャッシュディレクトリが空です。", nameof(rootDirectory));
            }

            RootDirectory = rootDirectory;
            Limits = limits;
            _fileSystem = fileSystem ?? new TtsCacheFileSystem();
            _logWarning = logWarning;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _index = new TtsCacheIndex();
        }

        /// <summary>キャッシュのルートディレクトリ。</summary>
        public string RootDirectory { get; }

        /// <summary>上限。</summary>
        public TtsCacheLimits Limits { get; }

        /// <summary><c>index.json</c> のパス。</summary>
        public string IndexPath => Path.Combine(RootDirectory, IndexFileName);

        /// <summary>直近の読み込みでディレクトリ走査による再構築が起きたか（診断用）。</summary>
        public bool IndexWasRebuilt { get; private set; }

        /// <summary>上限が 0 で、実質キャッシュが無効になっているか。</summary>
        public bool IsDisabled => Limits.MaxBytes <= 0L || Limits.MaxEntries <= 0;

        /// <summary>現在のエントリ数。</summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    EnsureLoaded();
                    return _index.Count;
                }
            }
        }

        /// <summary>現在の合計バイト数。</summary>
        public long TotalBytes
        {
            get
            {
                lock (_gate)
                {
                    EnsureLoaded();
                    return _index.TotalBytes;
                }
            }
        }

        /// <summary>キーに対応する wav のパス。</summary>
        /// <exception cref="ArgumentException">キーの形式が不正なとき</exception>
        public string GetWavPath(string key)
            => Path.Combine(RootDirectory, TtsCacheKey.GetShard(key), key + WavExtension);

        /// <summary>
        /// キャッシュを引く。命中したら wav バイト列と再生時間を返し、最終アクセス時刻を更新する。
        /// ファイルが消えている・読めない場合はミス扱いにしてインデックスから外す。
        /// </summary>
        public bool TryGet(string key, out byte[] wav, out double durationSec)
        {
            wav = null;
            durationSec = 0d;
            if (!TtsCacheKey.IsValid(key)) return false;

            lock (_gate)
            {
                EnsureLoaded();

                if (!_index.TryGet(key, out var entry)) return false;

                var path = GetWavPath(key);
                try
                {
                    if (!_fileSystem.FileExists(path))
                    {
                        // index.json にはあるがファイルが無い（手動削除など）。索引から外す。
                        _index.Remove(key);
                        _indexDirty = true;
                        return false;
                    }

                    wav = _fileSystem.ReadAllBytes(path);
                }
                catch (Exception e) when (IsIoFailure(e))
                {
                    Warn($"キャッシュの読み込みに失敗しました（{path}）。合成にフォールバックします: {e.Message}");
                    return false;
                }

                durationSec = entry.DurationSec;
                _index.Touch(key, _utcNow());
                _indexDirty = true;

                if (++_pendingTouches >= TouchFlushThreshold)
                {
                    FlushCore();
                }

                return true;
            }
        }

        /// <summary>
        /// キャッシュに存在するか（wav 本体は読まない）。存在すれば最終アクセス時刻を更新する。
        /// 事前合成（docs/tts.md §7.4）で「もう合成済みか」を判定するために使う。
        /// </summary>
        public bool Contains(string key)
        {
            if (!TtsCacheKey.IsValid(key)) return false;

            lock (_gate)
            {
                EnsureLoaded();

                if (!_index.TryGet(key, out _)) return false;

                var path = GetWavPath(key);
                try
                {
                    if (!_fileSystem.FileExists(path))
                    {
                        _index.Remove(key);
                        _indexDirty = true;
                        return false;
                    }
                }
                catch (Exception e) when (IsIoFailure(e))
                {
                    Warn($"キャッシュファイルの確認に失敗しました（{path}）: {e.Message}");
                    return false;
                }

                _index.Touch(key, _utcNow());
                _indexDirty = true;

                if (++_pendingTouches >= TouchFlushThreshold)
                {
                    FlushCore();
                }

                return true;
            }
        }

        /// <summary>
        /// 合成結果を保存する。保存に失敗しても例外は投げない（読み上げは既に成立しているため）。
        /// 保存後に上限を超えていたら古い順に削除する。
        /// </summary>
        public void Put(string key, byte[] wav, double durationSec, string speakerName, string styleName)
        {
            if (!TtsCacheKey.IsValid(key))
            {
                Warn($"キャッシュキーの形式が不正なため保存しません: {key}");
                return;
            }
            if (wav == null || wav.Length == 0)
            {
                Warn("空の wav はキャッシュしません。");
                return;
            }
            if (IsDisabled)
            {
                // 上限 0 = キャッシュ無効。書いた直後に必ず追い出されるので、最初から書かない。
                return;
            }

            lock (_gate)
            {
                EnsureLoaded();

                var path = GetWavPath(key);
                try
                {
                    _fileSystem.WriteAllBytesAtomic(path, wav);
                }
                catch (Exception e) when (IsIoFailure(e))
                {
                    Warn($"キャッシュの書き込みに失敗しました（{path}）: {e.Message}");
                    return;
                }

                try
                {
                    _index.Put(new TtsCacheEntry(key, wav.Length, durationSec, _utcNow(), speakerName, styleName));
                }
                catch (ArgumentException e)
                {
                    // durationSec が NaN など、索引に載せられない値だったとき。
                    // Put は「失敗しても例外を投げない」契約なので、警告にとどめて書いた wav を消す。
                    Warn($"キャッシュエントリを作成できませんでした（key={key}）: {e.Message}");
                    DeleteWav(key);
                    return;
                }

                _indexDirty = true;

                Evict();
                FlushCore();
            }
        }

        /// <summary>指定キーのキャッシュを捨てる（読み込んだ wav が壊れていたときに使う）。</summary>
        public void Invalidate(string key)
        {
            if (!TtsCacheKey.IsValid(key)) return;

            lock (_gate)
            {
                EnsureLoaded();
                if (!_index.Remove(key)) return;

                DeleteWav(key);
                _indexDirty = true;
                FlushCore();
            }
        }

        /// <summary>キャッシュを全消しする（設定画面の「キャッシュをクリア」、メモリ不足時のリトライ）。</summary>
        public void Clear()
        {
            lock (_gate)
            {
                EnsureLoaded();

                foreach (var entry in _index.EntriesOldestFirst)
                {
                    DeleteWav(entry.Key);
                }
                _index.Clear();
                _indexDirty = true;
                FlushCore();
            }
        }

        /// <summary>最終アクセス時刻の更新を <c>index.json</c> に書き出す。終了時に必ず呼ぶ。</summary>
        public void Flush()
        {
            lock (_gate)
            {
                if (!_loaded) return;
                FlushCore();
            }
        }

        /// <summary>ログ用の説明文。</summary>
        public string Describe()
        {
            lock (_gate)
            {
                EnsureLoaded();
                return $"root={RootDirectory} entries={_index.Count} bytes={_index.TotalBytes} " +
                       $"{Limits} rebuilt={IndexWasRebuilt}";
            }
        }

        /// <summary>必ず <see cref="_gate"/> を取った状態で呼ぶ。</summary>
        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _index = LoadIndex();
            Evict();

            // 起動時の追い出し結果と、走査による再構築結果を書き出す。
            if (_indexDirty) FlushCore();
        }

        private TtsCacheIndex LoadIndex()
        {
            try
            {
                _fileSystem.EnsureDirectory(RootDirectory);

                if (_fileSystem.FileExists(IndexPath))
                {
                    var json = _fileSystem.ReadAllText(IndexPath);
                    if (TtsCacheIndex.TryParse(json, out var parsed, out var error))
                    {
                        return DropMissingFiles(parsed);
                    }

                    Warn($"{IndexFileName} が読めないためディレクトリ走査で再構築します: {error}");
                }
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Warn($"{IndexFileName} の読み込みに失敗したためディレクトリ走査で再構築します: {e.Message}");
            }

            return RebuildIndex();
        }

        /// <summary><c>index.json</c> にあるがファイルが消えているエントリを落とす。</summary>
        private TtsCacheIndex DropMissingFiles(TtsCacheIndex index)
        {
            foreach (var entry in index.EntriesOldestFirst)
            {
                var path = GetWavPath(entry.Key);
                try
                {
                    if (_fileSystem.FileExists(path)) continue;
                }
                catch (Exception e) when (IsIoFailure(e))
                {
                    // 存在確認そのものが失敗したときは「無い」と決めつけない。
                    // 索引から落とすと、実在するファイルが追い出し対象から外れて残り続ける。
                    Warn($"キャッシュファイルの確認に失敗しました（{path}）。エントリは残します: {e.Message}");
                    continue;
                }

                index.Remove(entry.Key);
                _indexDirty = true;
            }

            return index;
        }

        /// <summary>
        /// ディレクトリを走査して <c>index.json</c> を作り直す（docs/tts.md §7.3）。
        /// <c>durationSec</c> は wav ヘッダから算出し、ヘッダが壊れているファイルは削除する。
        /// </summary>
        private TtsCacheIndex RebuildIndex()
        {
            IndexWasRebuilt = true;
            _indexDirty = true;

            var rebuilt = new TtsCacheIndex();
            IReadOnlyList<string> files;
            try
            {
                files = _fileSystem.EnumerateFiles(RootDirectory, WavSearchPattern);
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Warn($"キャッシュディレクトリを走査できませんでした（{RootDirectory}）: {e.Message}");
                return rebuilt;
            }

            // 走査で拾ったファイルには最終アクセス時刻の情報が無い。
            // 同じ時刻を入れると LRU の順序が付かないので、走査順（キー順）でわずかにずらす。
            var baseTime = _utcNow();
            var skipped = 0;

            for (var i = 0; i < files.Count; i++)
            {
                var path = files[i];

                // パスの正規化・ファイル名の取得も失敗しうる（不正な文字、長すぎるパス）ので try の中で行う。
                try
                {
                    var key = Path.GetFileNameWithoutExtension(path);
                    if (!TtsCacheKey.IsValid(key))
                    {
                        // キー形式でないファイルは本キャッシュのものではない。触らない。
                        skipped++;
                        continue;
                    }
                    if (!string.Equals(
                            Path.GetFullPath(path), Path.GetFullPath(GetWavPath(key)),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // 規約どおりのサブディレクトリに無いファイルは索引に載せない
                        // （載せても GetWavPath が別の場所を指してしまう）。削除もしない。
                        skipped++;
                        continue;
                    }

                    var size = _fileSystem.GetFileSize(path);
                    if (size <= 0)
                    {
                        skipped++;
                        _fileSystem.DeleteFile(path);
                        continue;
                    }

                    var header = _fileSystem.ReadPrefix(path, WavParser.HeaderProbeSize);
                    if (!WavParser.TryGetDurationSec(header, size, out var durationSec, out var probe))
                    {
                        skipped++;

                        // 「壊れている」と確定したものだけ削除する。バッファ不足（Incomplete）は
                        // 先頭 1KB に収まらない変則的なヘッダというだけで、ファイル自体は正しいかもしれない。
                        if (probe == WavProbeResult.Invalid) _fileSystem.DeleteFile(path);
                        continue;
                    }

                    rebuilt.Put(new TtsCacheEntry(
                        key, size, durationSec, baseTime.AddTicks(i), speakerName: null, styleName: null));
                }
                catch (Exception e) when (IsIoFailure(e) || e is ArgumentException)
                {
                    skipped++;
                    Warn($"キャッシュファイルを読めませんでした（{path}）: {e.Message}");
                }
            }

            skipped += DeleteStaleTempFiles();

            // 初回起動（0 件）で警告を出すのは騒がしいので、実際に何か拾ったときだけ残す。
            if (rebuilt.Count > 0 || skipped > 0)
            {
                Warn($"{IndexFileName} を再構築しました（有効 {rebuilt.Count} 件 / スキップ {skipped} 件、{RootDirectory}）。");
            }

            return rebuilt;
        }

        /// <summary>
        /// 前回の異常終了などで残った <c>*.wav.tmp</c> を消す（docs/tts.md §7.3）。
        /// 別プロセスが書き込み中の一時ファイルは消さない
        /// （<see cref="TtsCacheFileSystem"/> が一時ファイル名にプロセス ID を入れている）。
        /// </summary>
        /// <returns>削除した件数。</returns>
        private int DeleteStaleTempFiles()
        {
            IReadOnlyList<string> temps;
            try
            {
                temps = _fileSystem.EnumerateFiles(RootDirectory, TempSearchPattern);
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Warn($"一時ファイルを走査できませんでした（{RootDirectory}）: {e.Message}");
                return 0;
            }

            var deleted = 0;
            foreach (var path in temps)
            {
                if (TtsCacheFileSystem.IsTempFileOfOtherLiveProcess(path)) continue;

                try
                {
                    _fileSystem.DeleteFile(path);
                    deleted++;
                }
                catch (Exception e) when (IsIoFailure(e))
                {
                    Warn($"一時ファイルを削除できませんでした（{path}）: {e.Message}");
                }
            }

            return deleted;
        }

        /// <summary>必ず <see cref="_gate"/> を取った状態で呼ぶ。</summary>
        private void Evict()
        {
            var evictions = _index.SelectEvictions(Limits.MaxBytes, Limits.MaxEntries);
            if (evictions.Count == 0) return;

            foreach (var entry in evictions)
            {
                DeleteWav(entry.Key);
                _index.Remove(entry.Key);
            }

            _indexDirty = true;
        }

        private void DeleteWav(string key)
        {
            var path = GetWavPath(key);
            try
            {
                _fileSystem.DeleteFile(path);
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Warn($"キャッシュファイルを削除できませんでした（{path}）: {e.Message}");
            }
        }

        /// <summary>必ず <see cref="_gate"/> を取った状態で呼ぶ。</summary>
        private void FlushCore()
        {
            _pendingTouches = 0;
            if (!_indexDirty) return;

            try
            {
                _fileSystem.WriteAllTextAtomic(IndexPath, _index.ToJson());
                _indexDirty = false;
            }
            catch (Exception e) when (IsIoFailure(e))
            {
                Warn($"{IndexFileName} を書き込めませんでした（{IndexPath}）: {e.Message}");
            }
        }

        /// <summary>
        /// キャッシュの I/O 失敗として扱う例外。致命的でないのでログに落として続行する。
        /// プログラミングエラー（<see cref="ArgumentException"/> など）は捕捉しない。
        /// </summary>
        private static bool IsIoFailure(Exception e)
            => e is IOException
               || e is UnauthorizedAccessException
               || e is NotSupportedException
               || e is System.Security.SecurityException;

        private void Warn(string message) => _logWarning?.Invoke($"[TtsCache] {message}");
    }
}
