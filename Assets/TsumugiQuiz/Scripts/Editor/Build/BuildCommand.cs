using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TsumugiQuiz.Editor
{
    /// <summary>
    /// scripts/build.ps1 から `-executeMethod TsumugiQuiz.Editor.BuildCommand.BuildWindows` で
    /// 呼び出される Windows Standalone (x64) ビルドの実行スクリプト。
    /// </summary>
    public static class BuildCommand
    {
        private const string OutputDirectory = "Builds/Windows";
        private const string OutputExeName = "TsumugiQuiz.exe";

        // issue #131: scripts/build.ps1 -OutputDir <path> 使用時、-buildOutputDir <path> で
        // 出力先を明示的に渡す（既定は Application.dataPath から求めたプロジェクトルート基準の
        // Builds/Windows。レビュー M-3）。
        private const string OutputDirArgName = "-buildOutputDir";

        public static void BuildWindows()
        {
            var scriptingBackend = ResolveScriptingBackend();

            var buildSettings = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            if (buildSettings != scriptingBackend)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, scriptingBackend);
            }

            string outputDir;
            try
            {
                outputDir = ResolveOutputDirectory();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCommand] 出力先の解決に失敗しました: {ex.Message}");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, OutputExeName);

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildCommand] EditorBuildSettings にビルド対象シーンがありません。");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,

                // BuildOptions.NoUniqueIdentifier は付けないこと（issue #204）。Application.buildGUID が全 0 になり
                // （Unity の XML ドキュメント: "Will force the buildGUID to all zeros."）、どのビルドも同じ識別子になるので、
                // 接続承認でのビルドの照合（docs/network.md §2.3「バージョンとビルドの一致」）が効かなくなる。
                options = BuildOptions.None,
            };

            Debug.Log($"[BuildCommand] Building {outputPath} (scriptingBackend={scriptingBackend}, scenes={string.Join(", ", scenes)})");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[BuildCommand] Build result: {summary.result}, size={summary.totalSize} bytes, errors={summary.totalErrors}, warnings={summary.totalWarnings}");

            var success = summary.result == BuildResult.Succeeded;
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static ScriptingImplementation ResolveScriptingBackend()
        {
            var args = Environment.GetCommandLineArgs();
            var useIl2Cpp = args.Any(a => string.Equals(a, "-IL2CPP", StringComparison.OrdinalIgnoreCase));
            return useIl2Cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x;
        }

        // issue #131: -buildOutputDir <path> が指定されていればそれを使う（scripts/build.ps1 -OutputDir 由来）。
        // 未指定時は Application.dataPath から求めたプロジェクトルート基準の Builds/Windows
        // （レビュー M-3: Directory.GetCurrentDirectory() は Unity バッチモードの起動時カレント
        // ディレクトリに依存し、呼び出し元次第で変わりうる不安定な基準のため使わない）。
        // issue #142 項目 4: 相対パス指定時の解決基準を、既定値・保護ディレクトリガードと同じ
        // プロジェクトルート（GetProjectRoot()）に統一する（以前は Path.GetFullPath の仕様上、
        // Unity バッチモードの起動時カレントディレクトリ基準になっており、基準が不統一だった）。
        // 絶対パスが渡された場合は ResolveOutputDirPath 内の Path.Combine の仕様上そのまま使われる
        // ため、既存の絶対パス指定の挙動は変わらない。
        // レビュー L-1: パス解決の失敗（不正な文字等）を try/catch で分かりやすい例外にラップし、
        // さらに Assets/ Library/ 配下を指定した場合は拒否する（ビルド成果物の大量のファイルで
        // プロジェクトを汚染したり、Unity のインポート処理と競合したりする事故を防ぐため）。
        private static string ResolveOutputDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], OutputDirArgName, StringComparison.OrdinalIgnoreCase))
                {
                    string resolved;
                    try
                    {
                        resolved = ResolveOutputDirPath(args[i + 1], GetProjectRoot());
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"{OutputDirArgName} に指定されたパスを解決できません: '{args[i + 1]}'", ex);
                    }

                    AssertNotUnderProtectedDirectory(resolved);
                    return resolved;
                }
            }

            return Path.Combine(GetProjectRoot(), OutputDirectory);
        }

        // issue #142 項目 4: ResolveOutputDirectory から Unity API 非依存の純粋関数として切り出した
        // パス解決ロジック。EditMode テスト（Tests/EditMode/Build/BuildCommandTests.cs）から
        // 直接検証できるよう internal にしている（TsumugiQuiz.Editor.AssemblyInfo.cs の
        // InternalsVisibleTo("TsumugiQuiz.Tests.EditMode") 参照）。
        // rawValue が絶対パスの場合、Path.Combine の仕様により projectRoot 側は無視されそのまま使われる。
        // レビュー M-1: rawValue が null/空白の場合、Path.Combine(projectRoot, "") が projectRoot を
        // そのまま返し「-buildOutputDir の値が空でもプロジェクトルートへ無言で解決される」事故に
        // つながるため拒否する。同じ理由で、解決結果がプロジェクトルート自体と一致する場合
        // （例: rawValue = "." や ""）も拒否する（プロジェクトルート直下へビルド成果物を大量に
        // 書き込む事故を防ぐ。Assets/ Library/ 配下の拒否は AssertNotUnderProtectedDirectory が別途担う）。
        internal static string ResolveOutputDirPath(string rawValue, string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                throw new ArgumentException(
                    $"{OutputDirArgName} の値が空です。", nameof(rawValue));
            }

            var resolved = Path.GetFullPath(Path.Combine(projectRoot, rawValue));

            // レビュー L-7: rawValue の末尾に区切り文字が付いているかどうかで戻り値が変わらないよう、
            // Path.GetFullPath が保持する末尾の区切り文字を取り除く（ドライブのルート自体
            // （例: "C:\"）は区切り文字が無いと別の意味になるため対象外とする）。
            var pathRoot = Path.GetPathRoot(resolved);
            if (string.IsNullOrEmpty(pathRoot) || !string.Equals(resolved, pathRoot, StringComparison.OrdinalIgnoreCase))
            {
                resolved = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            var normalizedProjectRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(resolved, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{OutputDirArgName} にプロジェクトルート自体は指定できません: '{rawValue}' -> {resolved}", nameof(rawValue));
            }

            return resolved;
        }

        // レビュー M-3: Application.dataPath（常に "<プロジェクトルート>/Assets" を指す。
        // Unity バッチモードの起動時カレントディレクトリに依存しない安定した基準）からプロジェクト
        // ルートを求める。
        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        // レビュー L-1/M-3: -buildOutputDir に Assets/ や Library/ 配下を指定することを拒否する。
        // 保護対象の基準も GetProjectRoot()（Application.dataPath 由来）に統一する。
        private static void AssertNotUnderProtectedDirectory(string resolvedOutputDir)
        {
            var projectRoot = GetProjectRoot();
            var protectedDirNames = new[] { "Assets", "Library" };
            foreach (var dirName in protectedDirNames)
            {
                var protectedPath = Path.GetFullPath(Path.Combine(projectRoot, dirName))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var candidate = resolvedOutputDir
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (candidate.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{OutputDirArgName} に '{dirName}' 配下のパスは指定できません: {resolvedOutputDir}");
                }
            }
        }
    }
}
