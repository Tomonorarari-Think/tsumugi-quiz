using System;
using System.IO;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// <c>-tq-</c> 系コマンドライン引数を読み、起動時の自動化をまとめる（docs/network.md §10.3、issue #8）。
    /// <list type="bullet">
    ///   <item><description>初期 View の決定（<see cref="DetermineInitialView"/>）。同意ゲート（#37）は迂回しない:
    ///   未同意なら常に <see cref="ViewNames.Terms"/> を返す</description></item>
    ///   <item><description>ホスト自動開始（<c>-tq-host</c>）・参加コード自動入力＆接続（<c>-tq-join</c>）の
    ///   1 プロセス 1 回だけのトリガー（<see cref="TryConsumeAutoHost"/> / <see cref="TryConsumeAutoJoin"/>）</description></item>
    ///   <item><description>プレイヤー名・ポートの CLI 引数取得（既存の PlayerPrefs より優先させるために、
    ///   呼び出し側の View がこれで上書きする）</description></item>
    ///   <item><description>参加コードの書き出し（<see cref="TsumugiQuiz.Core.AppPaths.DataRoot"/> 配下の
    ///   <c>join-code.txt</c>。<c>-tq-data-root</c> が指定されていれば <c>AppPathsBootstrap</c>（#71）経由で
    ///   そちらが使われる）</description></item>
    ///   <item><description>ウィンドウ配置（<c>-tq-window</c>）</description></item>
    /// </list>
    ///
    /// 本クラスは <c>TsumugiQuiz.UI</c> アセンブリ内（HostSetupView / JoinView /
    /// DefaultViewControllerRegistrations）からのみ使う想定のため <c>internal</c>。
    /// テストからは <c>InternalsVisibleTo</c>（<c>Scripts/UI/AssemblyInfo.cs</c>）でアクセスできる。
    ///
    /// 状態はプロセス（Unity アプリ 1 起動）につき 1 度だけ解析される静的な保持。テストからは
    /// <see cref="SetOptionsForTesting"/> / <see cref="ResetForTesting"/> で差し替え・リセットできる。
    /// </summary>
    internal static class LaunchOptionsRunner
    {
        private const string JoinCodeFileName = "join-code.txt";

        private static CommandLineOptions _options;
        private static bool _autoHostConsumed;
        private static bool _autoJoinConsumed;
        private static bool _windowPlacementApplied;

        private static CommandLineOptions Options
            => _options ??= CommandLineOptions.Parse(Environment.GetCommandLineArgs());

        /// <summary>
        /// 同意状況をふまえた初期 View 名を決定する。
        /// 未同意なら <c>-tq-host</c> / <c>-tq-join</c> の指定に関わらず常に Terms を返す
        /// （requirements.md FR-71・FR-74。自動化が同意ゲートを迂回しないようにするため）。
        /// </summary>
        internal static string DetermineInitialView(bool hasConsented)
        {
            if (!hasConsented)
            {
                return ViewNames.Terms;
            }

            if (Options.Contains(LaunchArguments.Host))
            {
                return ViewNames.HostSetup;
            }

            if (Options.TryGetString(out _, LaunchArguments.Join))
            {
                return ViewNames.Join;
            }

            return ViewNames.Title;
        }

        /// <summary>
        /// <c>-tq-host</c> が指定されていれば、プロセスにつき最初の 1 回だけ true を返す
        /// （HostSetup 画面へ戻ってきたときに毎回自動開始が再発火しないようにするため）。
        /// </summary>
        internal static bool TryConsumeAutoHost()
        {
            if (_autoHostConsumed || !Options.Contains(LaunchArguments.Host))
            {
                return false;
            }

            _autoHostConsumed = true;
            return true;
        }

        /// <summary>
        /// <c>-tq-join &lt;code&gt;</c> が指定されていれば、プロセスにつき最初の 1 回だけ
        /// 参加コードを返す。
        /// </summary>
        internal static bool TryConsumeAutoJoin(out string joinCode)
        {
            joinCode = null;

            if (_autoJoinConsumed || !Options.TryGetString(out var code, LaunchArguments.Join))
            {
                return false;
            }

            _autoJoinConsumed = true;
            joinCode = code;
            return true;
        }

        /// <summary>
        /// <c>-tq-name</c> の生値。既存の PlayerPrefs より優先して使うために、呼び出し側の View が
        /// <see cref="TsumugiQuiz.Core.Network.PlayerNameValidator.TryNormalize"/> で検証してから
        /// 入力欄へ反映する（M-4: 正規化はこの 1 系統に統一し、ここでは検証しない）。
        /// </summary>
        internal static bool TryGetPlayerName(out string playerName)
            => Options.TryGetString(out playerName, NetworkRuntimeOptions.PlayerNameArgument);

        /// <summary>
        /// <c>-tq-port</c>（別名 <c>-port</c>）の値。<c>0</c> は「OS に空きポートを選ばせる」という
        /// 正当な指定としてそのまま返す。
        ///
        /// M-3: これは <see cref="NetworkRuntimeOptions"/> の解釈（<c>0</c> は「未指定」として
        /// <see cref="NetworkConstants.DefaultPort"/> にフォールバックする）とは異なる、意図的な差である。
        /// 自動ホスト開始（<c>-tq-host</c>）は同一 PC での複数プロセス起動を主眼にしており、
        /// ポート衝突を避けるため <c>-tq-port 0</c> で OS に空きポートを選ばせたいという
        /// <c>scripts/run-multi.ps1</c> 側の要求を素直に反映する。
        /// </summary>
        internal static bool TryGetPort(out ushort port)
            => Options.TryGetUInt16(out port, NetworkConstants.PortArgument, NetworkConstants.PortArgumentAlias);

        /// <summary>
        /// 参加コードを <see cref="TsumugiQuiz.Core.AppPaths.DataRoot"/> 配下の <c>join-code.txt</c> に
        /// 書き出す（<c>scripts/run-multi.ps1</c> がクライアント起動用に読み取る）。
        /// M-6: 呼び出しは <c>-tq-host</c> による自動ホスト開始が完了したときだけに限る
        /// （<see cref="TsumugiQuiz.UI.Views.HostSetup.HostSetupView"/> 側で 1 回だけ呼ぶ。通常の対話的な
        /// ホスト開始では呼ばない）。<c>join-code.txt</c> は LAN 用参加コードの平文であり、
        /// 暗号化やアクセス制御は行わない（同一 PC 上の他プロセスから読める前提）。
        /// H-2: 一時ファイルへ書いてからリネームすることでアトミックに書き込む
        /// （読み取り側が書き込み途中の不完全な内容を読むことを防ぐ）。
        /// 書き込みに失敗しても起動を止めない（警告ログのみ）。
        /// </summary>
        internal static void WriteJoinCodeFile(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                var finalPath = AppPaths.Combine(JoinCodeFileName);
                var tempPath = finalPath + ".tmp";
                File.WriteAllText(tempPath, joinCode);

                // H-2: 一時ファイル→リネームでアトミックに書く。File.Move(string,string,bool) は
                // このプロジェクトのスクリプティングランタイムに無い（実測: CS1739）ため、
                // 既存の最終ファイルを消してから 2 引数版の File.Move を使う。
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);
                Debug.Log($"[LaunchOptionsRunner] 参加コードを書き出しました: {finalPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LaunchOptionsRunner] join-code.txt の書き出しに失敗しました: {ex}");
            }
        }

        /// <summary>
        /// <c>-tq-window</c>（"x,y,w,h"）が指定されていれば、メインウィンドウの位置・サイズを
        /// それに合わせる。複数プロセスを画面上に並べて手動検証するために使う
        /// （docs/network.md §10.3）。プロセスにつき 1 回だけ適用する。
        /// バッチモード（ウィンドウが存在しない）では何もしない。
        /// </summary>
        internal static void ApplyWindowPlacement()
        {
            if (_windowPlacementApplied || !Options.TryGetWindowRect(out var rect, LaunchArguments.Window))
            {
                return;
            }

            _windowPlacementApplied = true;

            if (Application.isBatchMode)
            {
                return;
            }

            try
            {
                Screen.SetResolution(rect.Width, rect.Height, FullScreenMode.Windowed);
                Screen.MoveMainWindowTo(Screen.mainWindowDisplayInfo, new Vector2Int(rect.X, rect.Y));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LaunchOptionsRunner] ウィンドウ配置の適用に失敗しました: {ex}");
            }
        }

        /// <summary>テストから解析済みオプションを注入する（消費済みフラグもリセットする）。</summary>
        internal static void SetOptionsForTesting(CommandLineOptions options)
        {
            _options = options ?? CommandLineOptions.Empty;
            _autoHostConsumed = false;
            _autoJoinConsumed = false;
            _windowPlacementApplied = false;
        }

        /// <summary>テストから状態をすべてリセットする（次回アクセス時に実引数を再解析する）。</summary>
        internal static void ResetForTesting()
        {
            _options = null;
            _autoHostConsumed = false;
            _autoJoinConsumed = false;
            _windowPlacementApplied = false;
        }
    }
}
