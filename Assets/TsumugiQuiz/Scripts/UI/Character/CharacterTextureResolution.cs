using UnityEngine;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 状態ごとの立ち絵テクスチャの解決結果（issue #212、不変）。
    /// </summary>
    /// <remarks>
    /// <see cref="IsDedicated"/> は、その状態専用の表情差分（<see cref="CharacterImagePaths.GetFileName"/>）が
    /// 読めたかを表す。false なら別の表情・従来の全身 PNG へフォールバックしており、
    /// <see cref="CharacterView"/> はティント（#192）で結果を補う（<see cref="CharacterStateVisuals.GetTint"/>）。
    /// </remarks>
    public readonly struct CharacterTextureResolution
    {
        /// <param name="texture">表示するテクスチャ（読めなければ null）。</param>
        /// <param name="isDedicated">その状態専用のファイルから読んだか。</param>
        public CharacterTextureResolution(Texture2D texture, bool isDedicated)
        {
            Texture = texture;
            IsDedicated = texture != null && isDedicated;
        }

        /// <summary>表示するテクスチャ。読めなかった場合は null。</summary>
        public Texture2D Texture { get; }

        /// <summary>その状態専用の表情差分を読めたか（フォールバックしていないか）。</summary>
        public bool IsDedicated { get; }
    }
}
