using System;
using System.IO;
using NUnit.Framework;
using TsumugiQuiz.Core;
using TsumugiQuiz.UI;
using UnityEngine;

namespace TsumugiQuiz.Tests.EditMode.UI.Character
{
    /// <summary>
    /// <see cref="CharacterImageLoader"/> の読み込み・フォールバックを検証する（issue #24）。
    /// EditMode（Editor プロセス内）で実行するため <see cref="Texture2D"/> 等の Unity API を直接使える。
    /// 個々のケースは <see cref="CharacterImageLoader.LoadFrom"/> に明示パスを渡して検証し、
    /// <see cref="AppPaths"/> の静的状態には依存しない（唯一 <c>Load_NoArg_UsesAppPathsDataRoot</c> を除く）。
    /// </summary>
    public class CharacterImageLoaderTests
    {
        private string _tempRoot;

        [SetUp]
        public void CreateTempRoot()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "TsumugiQuizCharacterImageLoaderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void DeleteTempRoot()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        [Test]
        public void LoadFrom_FileMissing_ReturnsPlaceholder()
        {
            var result = CharacterImageLoader.LoadFrom(Path.Combine(_tempRoot, "tsumugi_v2.png"));

            Assert.IsTrue(result.IsPlaceholder);
            Assert.IsNull(result.Texture);
        }

        [Test]
        public void LoadFrom_EmptyPath_ReturnsPlaceholder()
        {
            var result = CharacterImageLoader.LoadFrom(string.Empty);

            Assert.IsTrue(result.IsPlaceholder);
        }

        [Test]
        public void LoadFrom_ValidPngPresent_ReturnsTexture()
        {
            var path = Path.Combine(_tempRoot, "tsumugi_v2.png");
            WriteValidPng(path);

            var result = CharacterImageLoader.LoadFrom(path);

            try
            {
                Assert.IsFalse(result.IsPlaceholder);
                Assert.IsNotNull(result.Texture);
                Assert.That(result.Texture.width, Is.EqualTo(4));
                Assert.That(result.Texture.height, Is.EqualTo(4));
            }
            finally
            {
                CharacterImageLoader.DestroyTexture(result.Texture);
            }
        }

        [Test]
        public void LoadFrom_CorruptFile_ReturnsPlaceholder()
        {
            var path = Path.Combine(_tempRoot, "tsumugi_v2.png");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03 });

            var result = CharacterImageLoader.LoadFrom(path);

            Assert.IsTrue(result.IsPlaceholder);
            Assert.IsNull(result.Texture);
        }

        /// <summary>
        /// ディレクトリをファイルパスとして渡した場合も例外を投げずプレースホルダへ落とす（堅牢性の確認）。
        /// </summary>
        [Test]
        public void LoadFrom_PathIsDirectory_ReturnsPlaceholderWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                var result = CharacterImageLoader.LoadFrom(_tempRoot); // ディレクトリそのものを渡す
                Assert.IsTrue(result.IsPlaceholder);
            });
        }

        /// <summary>レビュー M1: 上限（32 MiB）を超えるファイルは読み込まない。</summary>
        [Test]
        public void LoadFrom_FileExceedsSizeLimit_ReturnsPlaceholderWithoutReadingBytes()
        {
            var path = Path.Combine(_tempRoot, "tsumugi_v2.png");

            using (var stream = new FileStream(path, FileMode.Create))
            {
                stream.SetLength(CharacterImageLoader.MaxFileSizeBytes + 1);
            }

            var result = CharacterImageLoader.LoadFrom(path);

            Assert.IsTrue(result.IsPlaceholder);
        }

        /// <summary>
        /// レビュー L-b: テスト名を実際の検証内容に合わせて修正
        /// （旧名 <c>LoadFrom_FileAtSizeLimit_StillAttemptsToRead</c> は「上限ちょうど」を検証しておらず、
        /// 単に上限を大きく下回る通常サイズのファイルを書いていただけだった）。
        /// 上限を大きく下回る正常なファイルが、サイズ上限チェック（レビュー M1）によって
        /// 誤って弾かれないことを確認する（<c>LoadFrom_ValidPngPresent_ReturnsTexture</c> の
        /// サイズ上限チェック観点での重複確認）。
        /// </summary>
        [Test]
        public void LoadFrom_FileWellUnderSizeLimit_IsNotRejectedForSize()
        {
            var path = Path.Combine(_tempRoot, "tsumugi_v2.png");
            WriteValidPng(path);
            Assert.Less(new FileInfo(path).Length, CharacterImageLoader.MaxFileSizeBytes);

            var result = CharacterImageLoader.LoadFrom(path);

            try
            {
                Assert.IsFalse(result.IsPlaceholder, "上限を大きく下回るファイルがサイズ理由で弾かれてはいけない。");
            }
            finally
            {
                CharacterImageLoader.DestroyTexture(result.Texture);
            }
        }

        /// <summary>
        /// issue #190: 縮小表示（ScaleToFit）でのエイリアシングを抑えるため、
        /// 読み込んだテクスチャはミップマップを持ち（mipmapCount &gt; 1）、Trilinear フィルタを
        /// 明示していることを確認する。PR #195 レビュー M1: CPU 側のコピーを解放するため
        /// isReadable が false になっていることも確認する（呼び出し側はピクセル読み取りを行わない）。
        /// </summary>
        [Test]
        public void LoadFrom_ValidPngPresent_TextureHasMipmapsAndTrilinearFilter()
        {
            var path = Path.Combine(_tempRoot, "tsumugi_v2.png");
            WriteValidPng(path);

            var result = CharacterImageLoader.LoadFrom(path);

            try
            {
                Assert.IsFalse(result.IsPlaceholder);
                Assert.Greater(result.Texture.mipmapCount, 1, "縮小表示時のエイリアシングを抑えるためミップマップが必要（issue #190）。");
                Assert.That(result.Texture.filterMode, Is.EqualTo(FilterMode.Trilinear));
                Assert.IsFalse(result.Texture.isReadable, "CPU 側のコピーは Apply(makeNoLongerReadable: true) で解放するはず（PR #195 レビュー M1）。");
            }
            finally
            {
                CharacterImageLoader.DestroyTexture(result.Texture);
            }
        }

        [Test]
        public void DestroyTexture_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => CharacterImageLoader.DestroyTexture(null));
        }

        [Test]
        public void Load_NoArg_UsesAppPathsDataRoot()
        {
            AppPaths.Configure(_tempRoot);
            try
            {
                var path = Path.Combine(_tempRoot, "tsumugi", "tsumugi_v2.png");
                WriteValidPng(path);

                var result = CharacterImageLoader.Load();

                try
                {
                    Assert.IsFalse(result.IsPlaceholder, "AppPaths.DataRoot 配下に配置した画像を読めるはず。");
                }
                finally
                {
                    CharacterImageLoader.DestroyTexture(result.Texture);
                }
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        // ---- 表情差分のフォールバック（issue #86） ----------------------------------

        [Test]
        public void LoadIfExists_FileMissing_ReturnsPlaceholderWithoutWarning()
        {
            // 表情差分が未生成の環境では「候補が無い」のが正常系なので、警告ログを出さない
            // （出すと LogAssert.NoUnexpectedReceived を使うテストの妨げにもなる）。
            var result = CharacterImageLoader.LoadIfExists(Path.Combine(_tempRoot, "tsumugi_correct.png"));

            Assert.IsTrue(result.IsPlaceholder);
            Assert.IsNull(result.SourcePath);
        }

        [Test]
        public void Load_State_PrefersExpressionFile()
        {
            AppPaths.Configure(_tempRoot);
            try
            {
                // 幅で「どのファイルが読まれたか」を判別する。
                WriteValidPng(ExpressionPath(CharacterImagePaths.CorrectFileName), width: 5);
                WriteValidPng(ExpressionPath(CharacterImagePaths.IdleFileName), width: 6);
                WriteValidPng(ExpressionPath(CharacterImagePaths.FileName), width: 7);

                var result = CharacterImageLoader.Load(CharacterState.Correct);
                try
                {
                    Assert.IsFalse(result.IsPlaceholder);
                    Assert.AreEqual(5, result.Texture.width, "正解の表情差分が最優先で読まれるはず。");
                    // SourcePath は FileInfo.FullName（正規化済み）なので、ファイル名で突き合わせる。
                    Assert.AreEqual(CharacterImagePaths.CorrectFileName, Path.GetFileName(result.SourcePath));
                }
                finally
                {
                    CharacterImageLoader.DestroyTexture(result.Texture);
                }
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_State_FallsBackToIdleExpression_WhenStateFileMissing()
        {
            AppPaths.Configure(_tempRoot);
            try
            {
                WriteValidPng(ExpressionPath(CharacterImagePaths.IdleFileName), width: 6);
                WriteValidPng(ExpressionPath(CharacterImagePaths.FileName), width: 7);

                var result = CharacterImageLoader.Load(CharacterState.Wrong);
                try
                {
                    Assert.IsFalse(result.IsPlaceholder);
                    Assert.AreEqual(6, result.Texture.width, "未生成の表情は待機の差分にフォールバックするはず。");
                }
                finally
                {
                    CharacterImageLoader.DestroyTexture(result.Texture);
                }
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_State_FallsBackToLegacyWholeBodyPng_WhenNoExpressionGenerated()
        {
            AppPaths.Configure(_tempRoot);
            try
            {
                // #24 までの環境（setup-external.ps1 が置いた tsumugi_v2.png だけがある）。
                WriteValidPng(ExpressionPath(CharacterImagePaths.FileName), width: 7);

                foreach (CharacterState state in System.Enum.GetValues(typeof(CharacterState)))
                {
                    var result = CharacterImageLoader.Load(state);
                    try
                    {
                        Assert.IsFalse(result.IsPlaceholder, $"{state} は従来の全身 PNG にフォールバックするはず。");
                        Assert.AreEqual(7, result.Texture.width);
                    }
                    finally
                    {
                        CharacterImageLoader.DestroyTexture(result.Texture);
                    }
                }
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_State_NothingPlaced_ReturnsPlaceholder()
        {
            AppPaths.Configure(_tempRoot);
            try
            {
                var result = CharacterImageLoader.Load(CharacterState.Reading);

                Assert.IsTrue(result.IsPlaceholder, "1 枚も無ければ非表示（プレースホルダ）扱いにする。");
            }
            finally
            {
                AppPaths.Reset();
            }
        }

        [Test]
        public void Load_State_DataRootNotConfigured_ReturnsPlaceholderInsteadOfThrowing()
        {
            AppPaths.Reset();
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable)))
            {
                Assert.Ignore($"環境変数 {AppPaths.DataRootEnvironmentVariable} が設定されているため、このテストの前提が成り立ちません。");
            }

            CharacterImageLoader.Result result = default;
            Assert.DoesNotThrow(() => result = CharacterImageLoader.Load(CharacterState.Idle));
            Assert.IsTrue(result.IsPlaceholder);
        }

        private string ExpressionPath(string fileName) =>
            Path.Combine(_tempRoot, CharacterImagePaths.DirectoryName, fileName);

        private static void WriteValidPng(string path, int width = 4)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var source = new Texture2D(width, 4, TextureFormat.RGBA32, mipChain: false);
            try
            {
                for (var y = 0; y < 4; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        source.SetPixel(x, y, Color.magenta);
                    }
                }

                source.Apply();
                File.WriteAllBytes(path, source.EncodeToPNG());
            }
            finally
            {
                CharacterImageLoader.DestroyTexture(source);
            }
        }
    }
}
