using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 立ち絵画像（<see cref="CharacterImagePaths"/> が指す配置先）の読み込み（issue #24）。
    /// </summary>
    /// <remarks>
    /// 立ち絵素材は二次配布禁止（docs/licenses.md §3）のため <c>Assets/</c> にコミット・同梱せず、
    /// ユーザー自身が <see cref="TsumugiQuiz.Core.AppPaths.DataRoot"/> 配下へ配置したものを実行時に読む
    /// （<c>scripts/setup-external.ps1</c> は配置先を案内するのみで、ビルドへはコピーしない）。
    /// 未配置・破損・過大サイズの場合は例外を投げず、プレースホルダ扱い（<see cref="Result.IsPlaceholder"/> = true）
    /// で呼び出し側に返す。呼び出し側（<see cref="CharacterView"/>）はプレースホルダの場合、
    /// 立ち絵自体を表示しない（<c>character-root</c> を <c>DisplayStyle.None</c>）。
    /// </remarks>
    public static class CharacterImageLoader
    {
        /// <summary>読み込みを許可する最大ファイルサイズ（32 MiB）。既定の立ち絵 PNG は数 MB のため十分な余裕を持つ。</summary>
        public const long MaxFileSizeBytes = 32L * 1024 * 1024;

        /// <summary>読み込み結果。</summary>
        public readonly struct Result
        {
            /// <summary>読み込めたテクスチャ。プレースホルダの場合は null。</summary>
            public Texture2D Texture { get; }

            /// <summary>
            /// 実際に読み込めたファイルの絶対パス（プレースホルダの場合は null）。
            /// 表情差分のフォールバック（#86）でどの候補が採用されたかを呼び出し側が知るために使う。
            /// </summary>
            public string SourcePath { get; }

            /// <summary>テクスチャが読み込めず、表示しない（プレースホルダ）扱いにする必要があるか。</summary>
            public bool IsPlaceholder => Texture == null;

            public Result(Texture2D texture, string sourcePath = null)
            {
                Texture = texture;
                SourcePath = texture == null ? null : sourcePath;
            }
        }

        /// <summary>未配置・読み込み失敗時にログへ残す案内文言（UI には表示しない。L7: 文言は C# 側の本定数に一本化）。</summary>
        public const string NotPlacedMessage = "立ち絵が未配置です。";

        /// <summary>
        /// 立ち絵画像（従来の全身 PNG）を読み込む（<see cref="CharacterImagePaths.ResolveImagePath"/>）。
        /// ファイルが無い・壊れている・大きすぎる場合は例外を投げず、プレースホルダ用の <see cref="Result"/> を返す。
        /// </summary>
        /// <remarks>
        /// **#24 互換・テスト用**（PR #135 レビュー L6）。表情差分（#86）を考慮しないので、
        /// 本番の表示経路からは使わないこと。実行時は <see cref="Load(CharacterState)"/>
        /// （状態専用の差分 → 待機の差分 → 従来の全身 PNG の順にフォールバック）を使う。
        /// </remarks>
        public static Result Load()
        {
            string path;
            try
            {
                path = CharacterImagePaths.ResolveImagePath();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterImageLoader] パス解決に失敗しました（{e.GetType().Name}）: {e.Message}");
                return new Result(null);
            }

            return LoadFrom(path);
        }

        /// <summary>
        /// 指定した状態に対応する立ち絵画像（表情差分、#86）を読み込む。
        /// <see cref="CharacterImagePaths.GetFileNameCandidates"/> の優先順（状態専用の差分 →
        /// 待機の差分 → 従来の全身 PNG）に読み込みを試み、最初に成功したものを返す。
        /// どれも読み込めない場合は例外を投げず、プレースホルダ用の <see cref="Result"/> を返す。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        public static Result Load(CharacterState state) => LoadWithCandidateIndex(state, out _);

        /// <summary>
        /// <see cref="Load(CharacterState)"/> と同じ順に読み込み、その状態専用の表情差分だったか
        /// （<see cref="CharacterTextureResolution.IsDedicated"/>）も返す（#212。ティントの判断に使う）。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        public static CharacterTextureResolution LoadExpression(CharacterState state)
        {
            var result = LoadWithCandidateIndex(state, out var candidateIndex);
            return new CharacterTextureResolution(result.Texture, isDedicated: candidateIndex == 0);
        }

        private static Result LoadWithCandidateIndex(CharacterState state, out int candidateIndex)
        {
            candidateIndex = -1;

            IReadOnlyList<string> candidates;
            try
            {
                candidates = CharacterImagePaths.ResolveImagePathCandidates(state);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterImageLoader] パス解決に失敗しました（{e.GetType().Name}、{state}）: {e.Message}");
                return new Result(null);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                // 未配置の候補（表情差分を生成していない環境では普通に起きる）でログを汚さないよう、
                // ここでは「見つからない」を無言で次の候補へ送る。全滅したときだけまとめて警告する。
                var result = LoadIfExists(candidates[i]);
                if (!result.IsPlaceholder)
                {
                    candidateIndex = i;
                    return result;
                }
            }

            Debug.LogWarning($"[CharacterImageLoader] {NotPlacedMessage}（{state}: {string.Join(", ", candidates)}）。"
                + "External/README.md §5.5 / §5.7 の手順で配置・生成してください。");
            return new Result(null);
        }

        /// <summary>
        /// 指定したパスから立ち絵画像を読み込む。<see cref="Load()"/> の実処理で、テストから直接パスを渡すのにも使う。
        /// ファイルが無い場合は警告ログを残す（未配置の案内）。
        /// </summary>
        /// <param name="path">読み込む画像の絶対パス。</param>
        public static Result LoadFrom(string path) => LoadFrom(path, logIfMissing: true);

        /// <summary>
        /// 指定したパスにファイルがあれば読み込む。無い場合は警告ログを残さずプレースホルダを返す
        /// （表情差分のフォールバック候補を順に試すときに使う、#86）。
        /// ファイルが壊れている・大きすぎる場合は <see cref="LoadFrom(string)"/> と同じく警告を残す。
        /// </summary>
        /// <param name="path">読み込む画像の絶対パス。</param>
        public static Result LoadIfExists(string path) => LoadFrom(path, logIfMissing: false);

        private static Result LoadFrom(string path, bool logIfMissing)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogWarning("[CharacterImageLoader] パスが空です。");
                return new Result(null);
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterImageLoader] パスが不正です（{e.GetType().Name}、{path}）: {e.Message}");
                return new Result(null);
            }

            if (!fileInfo.Exists)
            {
                if (logIfMissing)
                {
                    Debug.LogWarning($"[CharacterImageLoader] {NotPlacedMessage}（{path}）。"
                        + "External/README.md §5.5 の手順で配置してください。");
                }

                return new Result(null);
            }

            if (fileInfo.Length > MaxFileSizeBytes)
            {
                Debug.LogWarning(
                    $"[CharacterImageLoader] 立ち絵のファイルサイズが上限（{MaxFileSizeBytes} バイト）を超えています"
                    + $"（{path}、実際: {fileInfo.Length} バイト）。読み込みを中止します。");
                return new Result(null);
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                // issue #190: 生成スクリプト（scripts/generate_tsumugi_expressions.py）が書き出す PNG は
                // 高さ最大 1280px（#191 で表示サイズに合わせて 2048 から変更。1920x1080 の表示の約 2 倍）で、
                // 表示先のカード（.character-image-frame）より大きく、縮小して表示される
                // （全身の tsumugi_v2.png は 4084px あり、さらに大きく縮小される）。mipChain: false のままだと GPU の
                // ミニフィケーションフィルタリングが働かずエイリアシング・モアレが出るため、
                // mipmap を生成する（mipChain: true）。
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
                if (!texture.LoadImage(bytes))
                {
                    DestroyTexture(texture);
                    Debug.LogWarning($"[CharacterImageLoader] 立ち絵の読み込みに失敗しました（{path}）。ファイルが壊れている可能性があります。");
                    return new Result(null);
                }

                // LoadImage は内部で Apply 相当の処理を行うが、mipChain: true で生成したミップレベルを
                // 確実に反映させるため明示的に呼ぶ（updateMipmaps: true）。
                // PR #195 レビュー M1: makeNoLongerReadable: true で CPU 側のコピーを解放する
                // （呼び出し側・テストのいずれも GetPixel* / EncodeTo* 等のピクセル読み取りを行わないため、
                // readable を維持する必要が無い）。これによりテクスチャは isReadable = false になるが、
                // ダイナミックアトラスからの除外は Size フィルタ（既定のバストアップ 987x1280px が
                // m_DynamicAtlasSettings.m_MaxSubTextureSize=64px を超える、panel-settings.asset）で
                // 引き続き成立するため、mipmap がアトラス化で失われる心配は無い
                // （Readability フィルタでの除外には依存しなくなる）。
                texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

                // UI Toolkit の Image（ScaleToFit）で縮小表示するため、ミップマップ間を補間する
                // Trilinear を使う（既定の Bilinear だとミップマップ間の遷移でちらつきが出うる）。
                // PR #195 レビュー L1: anisoLevel は画面に平行な均等縮小（本用途）には効果が無いため設定しない。
                texture.filterMode = FilterMode.Trilinear;

                return new Result(texture, fileInfo.FullName);
            }
            catch (Exception e)
            {
                // M1: ファイル I/O・デコードで起こりうる例外を広く捕捉し、読み込み失敗として扱う
                // （UnauthorizedAccessException・OutOfMemoryException 等、IOException 以外も対象）。
                Debug.LogWarning($"[CharacterImageLoader] 立ち絵の読み込み中にエラーが発生しました（{e.GetType().Name}、{path}）: {e.Message}");
                return new Result(null);
            }
        }

        /// <summary>
        /// 読み込んだテクスチャを解放する。所有者（GameView 側のキャッシュ等）が不要になったタイミングで呼ぶ。
        /// </summary>
        /// <param name="texture">解放するテクスチャ（null 可）。</param>
        public static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
