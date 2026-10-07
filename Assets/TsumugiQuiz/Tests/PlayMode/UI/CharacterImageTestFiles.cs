using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.Tests.Shared.Questions;
using TsumugiQuiz.UI;
using UnityEngine;

namespace TsumugiQuiz.Tests.PlayMode.UI
{
    /// <summary>
    /// 立ち絵ファイル（データルート配下の表情差分）を書くテストの共通処理（#86 のテストから #212 で切り出し）。
    /// </summary>
    internal static class CharacterImageTestFiles
    {
        /// <summary>
        /// 小さなテスト用 PNG を表情差分のファイル名で書く。どのファイルが読まれたかを幅で見分ける。
        /// </summary>
        public static void WriteExpression(string fileName, int width)
        {
            var path = AppPaths.Combine(CharacterImagePaths.DirectoryName, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, TestImageFactory.CreatePng(width, 4, seed: width));
        }

        /// <summary>
        /// 立ち絵ファイルを書き換えるテストなので、データルートが実行単位で隔離されていること
        /// （<c>scripts/verify.ps1</c> が設定する環境変数 <see cref="AppPaths.DataRootEnvironmentVariable"/> 由来）
        /// を確認する（PR #135 レビュー M3）。Editor から直接実行した場合など、実ユーザーの
        /// <c>Application.persistentDataPath</c> が使われる状況ではスキップする。
        /// </summary>
        public static void AssertIsolatedDataRoot()
        {
            var environmentRoot = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(environmentRoot))
            {
                Assert.Ignore(
                    $"環境変数 {AppPaths.DataRootEnvironmentVariable} が未設定です。"
                    + "実ユーザーのデータルートにある立ち絵を書き換えないため、このテストはスキップします"
                    + "（scripts/verify.ps1 経由で実行してください）。");
            }

            if (!RootPathValidator.IsSameOrUnder(AppPaths.DataRoot, Path.GetFullPath(environmentRoot)))
            {
                Assert.Ignore(
                    $"AppPaths.DataRoot（{AppPaths.DataRoot}）が環境変数 "
                    + $"{AppPaths.DataRootEnvironmentVariable}（{environmentRoot}）由来ではありません。"
                    + "隔離されたデータルートでないため、このテストはスキップします。");
            }
        }
    }

    /// <summary>
    /// データルート配下の立ち絵ファイルを退避・復元する（テストが実ユーザーのデータを壊さないため）。
    /// </summary>
    /// <remarks>
    /// PR #135 レビュー M3: 既存ファイルをメモリへ読み込んでから削除するのではなく、
    /// <c>&lt;ファイル名&gt;.testbak</c> へ <see cref="File.Move(string, string)"/> で退避する。
    /// テストプロセスが途中で落ちても実体がディスクに残り、手で戻せる
    /// （数 MB の PNG をメモリに抱えずに済む利点もある）。
    /// </remarks>
    internal sealed class CharacterImageFileScope
    {
        /// <summary>退避先に付ける拡張子。</summary>
        private const string BackupSuffix = ".testbak";

        /// <summary>
        /// 退避・復元の対象。従来の全身 PNG と、全状態の専用の表情差分（#212: 状態を増やしても漏れないよう
        /// <see cref="CharacterState"/> の全値から作る）。
        /// </summary>
        private static readonly string[] ManagedFileNames = BuildManagedFileNames();

        private static string[] BuildManagedFileNames()
        {
            var names = new List<string> { CharacterImagePaths.FileName };
            foreach (CharacterState state in Enum.GetValues(typeof(CharacterState)))
            {
                names.Add(CharacterImagePaths.GetFileName(state));
            }

            return names.ToArray();
        }

        /// <summary>退避したファイル（元のパス → 退避先のパス）。</summary>
        private readonly Dictionary<string, string> _movedFiles;

        private CharacterImageFileScope(Dictionary<string, string> movedFiles)
        {
            _movedFiles = movedFiles;
        }

        public static CharacterImageFileScope Backup()
        {
            var movedFiles = new Dictionary<string, string>();
            foreach (var path in ManagedPaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var backupPath = path + BackupSuffix;
                try
                {
                    if (File.Exists(backupPath))
                    {
                        // 前回の実行が異常終了して残った退避先。元ファイルが健在な今なら捨ててよい。
                        File.Delete(backupPath);
                    }

                    File.Move(path, backupPath);
                    movedFiles[path] = backupPath;
                }
                catch (Exception e)
                {
                    // 退避できないなら、そのファイルを上書きして失うより中断するほうが安全。
                    Assert.Inconclusive(
                        $"立ち絵ファイルの退避に失敗しました（{e.GetType().Name}）: {path}: {e.Message}");
                }
            }

            return new CharacterImageFileScope(movedFiles);
        }

        public void Restore()
        {
            foreach (var pair in _movedFiles)
            {
                try
                {
                    if (File.Exists(pair.Key))
                    {
                        File.Delete(pair.Key); // テストが書いた PNG
                    }

                    File.Move(pair.Value, pair.Key);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[CharacterImageFileScope] 復元に失敗しました（{e.GetType().Name}）: "
                        + $"{pair.Value} -> {pair.Key}。手で戻してください。");
                }
            }

            // テストが書いたファイルのうち、退避対象でなかったもの（元から存在しなかったもの）を消す。
            foreach (var path in ManagedPaths())
            {
                if (_movedFiles.ContainsKey(path))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CharacterImageFileScope] 削除に失敗しました（{e.GetType().Name}）: {path}");
                }
            }
        }

        private static IEnumerable<string> ManagedPaths()
        {
            foreach (var fileName in ManagedFileNames)
            {
                yield return AppPaths.Combine(CharacterImagePaths.DirectoryName, fileName);
            }
        }
    }
}
