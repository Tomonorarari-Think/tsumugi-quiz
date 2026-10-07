using System;
using TsumugiQuiz.Core;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// Boot シーンに置く起動スクリプト（#71）。<see cref="AppPaths"/> にデータルートを設定する。
    /// <see cref="NetworkBootstrap"/> と同じ「Boot に常駐させ、他の画面から参照される前に設定を終える」作法を踏む
    /// （こちらは NetworkManager を必要としないので、別 GameObject の単独コンポーネントとして置く）。
    ///
    /// <see cref="AppPaths.DataRoot"/> を決める優先順位（詳しくは <see cref="AppPaths"/> のコメントを参照）:
    /// <list type="number">
    ///   <item><description>起動引数 <see cref="LaunchArguments.DataRoot"/>（<c>-tq-data-root</c>）。
    ///     本クラスが読み取り、<see cref="AppPaths.Configure"/> で明示設定する</description></item>
    ///   <item><description>環境変数 <see cref="AppPaths.DataRootEnvironmentVariable"/>
    ///     （<c>scripts/verify.ps1</c> が Unity バッチ起動時に設定。<see cref="AppPaths.DataRoot"/> 自身が読む）</description></item>
    ///   <item><description>本クラスが <see cref="AppPaths.ConfigureDefault"/> で登録する
    ///     <c>Application.persistentDataPath</c>（常に登録する。上記 2 つが無いときの最後の拠り所）</description></item>
    /// </list>
    ///
    /// <c>-tq-data-root</c> のキー名は <see cref="LaunchArguments.DataRoot"/>（issue #8）に統一してあり、
    /// 本クラスはローカルに定数を持たない。
    ///
    /// <para>
    /// #112: 同じ場所で <see cref="DocumentsPaths"/>（問題フォルダ・プリセットフォルダの親）の
    /// 起動オプション <see cref="LaunchArguments.DocumentsRoot"/>（<c>-tq-documents-root</c>）も配線する。
    /// こちらは「明示設定（本クラス） &gt; 環境変数 <see cref="DocumentsPaths.RootEnvironmentVariable"/> &gt;
    /// テストが登録する既定値 &gt; 実ユーザーの Documents」の順で解決される
    /// （既定値の登録は本クラスの責務ではない。通常起動時は実ユーザーの Documents が使われる）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppPathsBootstrap : MonoBehaviour
    {
        /// <summary>常駐している唯一のインスタンス。Boot シーンを通っていない場合は null。</summary>
        public static AppPathsBootstrap Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Boot シーンが 2 度読み込まれた場合の保険。
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // PR #118 レビュー M-1: 実行時の書き込み先に Assets/ 配下を指定させない
            // （scripts/setup-external.ps1 の Assert-PathOutsideAssets と同じ方針）。
            var forbiddenPrefix = Application.dataPath;

            // PR #118 レビュー M-4: 環境変数が不正なままだと AppPaths.DataRoot /
            // DocumentsPaths.Root が参照のたびに例外を投げ続け、呼び出し側（HostSetup の
            // QuestionLibrary 等）が巻き添えで落ちる。起動時に検証し、不正なら無視する。
            TryDisableInvalidEnvironmentRoot(
                AppPaths.DataRootEnvironmentVariable, "データルート", forbiddenPrefix);
            TryDisableInvalidEnvironmentRoot(
                DocumentsPaths.RootEnvironmentVariable, "Documents ルート", forbiddenPrefix);

            // 既定値（優先順位の最下位）は常に登録しておく。
            // 環境変数・起動引数のどちらも無い場合の最後の拠り所（本番の通常起動時はこれが使われる）。
            AppPaths.ConfigureDefault(Application.persistentDataPath);

            var options = CommandLineOptions.Parse(Environment.GetCommandLineArgs());
            if (options.TryGetString(out var explicitRoot, LaunchArguments.DataRoot))
            {
                TryConfigureDataRoot(explicitRoot, forbiddenPrefix);
            }

            if (options.TryGetString(out var explicitDocumentsRoot, LaunchArguments.DocumentsRoot))
            {
                TryConfigureDocumentsRoot(explicitDocumentsRoot, forbiddenPrefix);
            }

            Debug.Log($"[AppPathsBootstrap] データルート: {AppPaths.DataRoot}");
            Debug.Log($"[AppPathsBootstrap] Documents ルート: {DescribeDocumentsRoot()}");
        }

        /// <summary>
        /// ログ用に現在の Documents ルートを文字列化する。<see cref="DocumentsPaths.Root"/> は
        /// 実行環境によっては例外を投げうる（ユーザープロファイル破損・不正な環境変数）ため、
        /// ログ出力のためだけに Boot の初期化を止めてしまわないよう、ここで理由に置き換える。
        /// </summary>
        private static string DescribeDocumentsRoot()
        {
            try
            {
                return DocumentsPaths.Root;
            }
            catch (Exception ex)
            {
                return $"(解決できません: {ex.GetType().Name}: {ex.Message})";
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// <c>-tq-data-root</c> の値で <see cref="AppPaths.Configure"/> を試みる。
        /// レビュー M-6: 空文字・相対パス等の不正な値は <see cref="AppPaths.Configure"/> が
        /// <see cref="ArgumentException"/> を投げる。ここで捕まえずに <see cref="Awake"/> から
        /// 伝播させると Boot シーンの初期化自体が失敗してしまうため、既定値（直前に登録済みの
        /// <see cref="AppPaths.ConfigureDefault"/>）へフォールバックし、理由をログするだけに留める
        /// （起動を止めない）。<c>internal</c>: MonoBehaviour のライフサイクルを介さずに
        /// EditMode テストから直接検証できるようにするため（<c>Awake</c> 自体は Unity が
        /// 呼び出すため直接テストしにくい）。
        /// </summary>
        /// <param name="explicitRoot"><c>-tq-data-root</c> に渡された値。</param>
        /// <param name="forbiddenPrefix">
        /// 配下を拒否するフォルダ（<c>Awake</c> は <c>Application.dataPath</c> を渡す）。
        /// 省略時は場所の制約なし（テスト用）。
        /// </param>
        /// <returns>設定に成功したら true、不正な値でフォールバックしたら false。</returns>
        internal static bool TryConfigureDataRoot(string explicitRoot, string forbiddenPrefix = null)
        {
            try
            {
                var validated = RootPathValidator.Validate(
                    explicitRoot, $"{LaunchArguments.DataRoot} の値", nameof(explicitRoot), forbiddenPrefix);
                AppPaths.Configure(validated);
                Debug.Log($"[AppPathsBootstrap] {LaunchArguments.DataRoot} 引数でデータルートを上書きしました: {validated}");
                return true;
            }
            catch (ArgumentException ex)
            {
                Debug.LogError(
                    $"[AppPathsBootstrap] {LaunchArguments.DataRoot} が不正なため既定のデータルートを使います: '{explicitRoot}' ({ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// <c>-tq-documents-root</c> の値で <see cref="DocumentsPaths.Configure"/> を試みる（#112）。
        /// <see cref="TryConfigureDataRoot"/> と同じ方針で、不正な値でも起動は止めず、
        /// 実ユーザーの Documents（既定）へフォールバックして理由をログするだけに留める。
        /// </summary>
        /// <param name="explicitRoot"><c>-tq-documents-root</c> に渡された値。</param>
        /// <param name="forbiddenPrefix">
        /// 配下を拒否するフォルダ（<c>Awake</c> は <c>Application.dataPath</c> を渡す）。
        /// </param>
        /// <returns>設定に成功したら true、不正な値でフォールバックしたら false。</returns>
        internal static bool TryConfigureDocumentsRoot(string explicitRoot, string forbiddenPrefix = null)
        {
            try
            {
                var validated = RootPathValidator.Validate(
                    explicitRoot, $"{LaunchArguments.DocumentsRoot} の値", nameof(explicitRoot), forbiddenPrefix);
                DocumentsPaths.Configure(validated);
                Debug.Log($"[AppPathsBootstrap] {LaunchArguments.DocumentsRoot} 引数で Documents ルートを上書きしました: {validated}");
                return true;
            }
            catch (ArgumentException ex)
            {
                Debug.LogError(
                    $"[AppPathsBootstrap] {LaunchArguments.DocumentsRoot} が不正なため既定の Documents ルートを使います: '{explicitRoot}' ({ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// ルートを指定する環境変数の値を検証し、不正ならこのプロセスに限り無視する（PR #118 レビュー M-4）。
        /// </summary>
        /// <remarks>
        /// <see cref="AppPaths.DataRoot"/> / <see cref="DocumentsPaths.Root"/> は環境変数を
        /// 参照のたびに検証するため、不正な値のままだと呼び出しのたびに
        /// <see cref="ArgumentException"/> が飛ぶ（例: HostSetup 画面の <c>QuestionLibrary</c> 生成）。
        /// 起動時に一度だけ検証し、不正なら <b>プロセススコープの</b>環境変数を消して、
        /// 以降のフォールバック（明示設定 / 既定値 / 実ユーザーの Documents）が効くようにする。
        /// 消すのは本アプリが定義した <c>TSUMUGI_*</c> の変数のみで、OS・他プロセスの設定には影響しない。
        /// </remarks>
        /// <returns>不正な値を無視した場合は true、値が無い・妥当な場合は false。</returns>
        internal static bool TryDisableInvalidEnvironmentRoot(
            string variableName, string label, string forbiddenPrefix = null)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                RootPathValidator.Validate(value, $"環境変数 {variableName}", variableName, forbiddenPrefix);
                return false;
            }
            catch (ArgumentException ex)
            {
                Debug.LogError(
                    $"[AppPathsBootstrap] 環境変数 {variableName} が不正なため、このプロセスでは無視します"
                    + $"（{label} は既定へフォールバックします）: '{value}' ({ex.Message})");
                Environment.SetEnvironmentVariable(variableName, null);
                return true;
            }
        }
    }
}
