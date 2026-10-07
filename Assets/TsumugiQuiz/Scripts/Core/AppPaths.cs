using System;
using System.IO;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// データ保存先ルート（<c>Application.persistentDataPath</c> 相当）の単一入口（#71）。
    ///
    /// 背景: PlayMode テストが <c>Application.persistentDataPath</c> を直接使うと、
    /// (1) メインツリーで開いている Unity Editor と、(2) 並列に走る複数 worktree の
    /// <c>verify.ps1</c> が同じファイル（consent.json・TtsCache 等）を奪い合い、
    /// <c>Sharing violation</c> / <c>IOException</c> で散発的に失敗する（#7 #52 #25）。
    /// 本クラスを唯一の入口にすることで、実行単位（プロセス）ごとにデータルートを切り替えられるようにする。
    ///
    /// <see cref="DataRoot"/> を決める優先順位:
    /// <list type="number">
    ///   <item><description><see cref="Configure"/> による明示設定（テスト、または <c>-tq-data-root</c> 起動引数。
    ///     <c>TsumugiQuiz.Network.AppPathsBootstrap</c> が Boot で設定する）</description></item>
    ///   <item><description>環境変数 <see cref="DataRootEnvironmentVariable"/>（<c>scripts/verify.ps1</c> が
    ///     Unity バッチ起動時にプロセス単位で設定する）</description></item>
    ///   <item><description><see cref="ConfigureDefault"/> で登録された既定値（Boot が渡す
    ///     <c>Application.persistentDataPath</c>。PlayMode テストでは
    ///     <c>PlayModeTestAssemblySetUp</c> がアセンブリ全体の一度きりのセットアップとして登録する）</description></item>
    /// </list>
    ///
    /// <c>Core</c> 層は Unity API に依存しない（<c>noEngineReferences: true</c>）ため、
    /// <c>Application.persistentDataPath</c> の取得自体は呼び出し側の責務とする。
    ///
    /// 静的な状態を持つため、テストは必ず <see cref="Configure"/> / <see cref="ConfigureDefault"/> で
    /// 自分が使う値を明示的に設定し、終了時に <see cref="Reset"/> で後始末すること
    /// （他のテストの実行順に依存しないようにするため）。
    /// </summary>
    public static class AppPaths
    {
        // PR #118 レビュー M-1: Configure / ConfigureDefault / 環境変数のいずれも
        // RootPathValidator で検証し、正規化済みの絶対パスを保持する。

        /// <summary>データルートを指定する環境変数名。<c>scripts/verify.ps1</c> が worktree ごとに設定する。</summary>
        public const string DataRootEnvironmentVariable = "TSUMUGI_DATA_ROOT";

        private static string _explicitRoot;
        private static string _defaultRoot;

        /// <summary>
        /// データルートを明示的に設定する（テスト、または起動引数 <c>-tq-data-root</c>）。
        /// 環境変数・既定値のどちらよりも優先される。
        /// </summary>
        /// <param name="root">絶対パス。</param>
        /// <exception cref="ArgumentException"><paramref name="root"/> が空、または絶対パスでないとき。</exception>
        public static void Configure(string root)
        {
            _explicitRoot = Validate(root, "AppPaths.Configure の root");
        }

        /// <summary>
        /// 既定値を登録する（Boot が <c>Application.persistentDataPath</c> を渡す）。
        /// <see cref="Configure"/> による明示設定・環境変数のどちらも無いときだけ使われる。
        /// </summary>
        /// <param name="root">絶対パス。</param>
        /// <exception cref="ArgumentException"><paramref name="root"/> が空、または絶対パスでないとき。</exception>
        public static void ConfigureDefault(string root)
        {
            _defaultRoot = Validate(root, "AppPaths.ConfigureDefault の root");
        }

        /// <summary>
        /// 明示設定・既定値をすべて解除する（テストの後始末用）。
        /// 環境変数は解除しない（プロセスの環境設定は本クラスが管理する対象ではないため）。
        /// </summary>
        public static void Reset()
        {
            _explicitRoot = null;
            _defaultRoot = null;
        }

        /// <summary>
        /// 現在有効なデータルート。<see cref="Configure"/> ・環境変数・<see cref="ConfigureDefault"/> の
        /// いずれも設定されていない場合は例外を投げる。
        /// </summary>
        /// <exception cref="InvalidOperationException">データルートが未設定のとき。</exception>
        public static string DataRoot
        {
            get
            {
                if (_explicitRoot != null)
                {
                    return _explicitRoot;
                }

                var envValue = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(envValue))
                {
                    return Validate(envValue, $"環境変数 {DataRootEnvironmentVariable}");
                }

                if (_defaultRoot != null)
                {
                    return _defaultRoot;
                }

                throw new InvalidOperationException(
                    "AppPaths のデータルートが未設定です。AppPaths.Configure(...) / ConfigureDefault(...) を呼ぶか、" +
                    $"環境変数 {DataRootEnvironmentVariable} を設定してください。");
            }
        }

        /// <summary>
        /// <see cref="DataRoot"/> 配下のパスを組み立てる（<c>System.IO.Path.Combine</c> の薄いラッパー）。
        /// </summary>
        /// <param name="parts">結合する要素（0 件なら <see cref="DataRoot"/> をそのまま返す）。</param>
        /// <exception cref="ArgumentException"><paramref name="parts"/> に空要素が含まれるとき。</exception>
        public static string Combine(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return DataRoot;
            }

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    throw new ArgumentException("空のパス要素は指定できません。", nameof(parts));
                }
            }

            var combined = new string[parts.Length + 1];
            combined[0] = DataRoot;
            Array.Copy(parts, 0, combined, 1, parts.Length);
            return Path.Combine(combined);
        }

        // #112: 入力検証は DocumentsPaths と共通（RootPathValidator が唯一の出所）。
        private static string Validate(string root, string label)
            => RootPathValidator.Validate(root, label, nameof(root));
    }
}
