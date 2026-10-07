using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// notosansjp-regular-sdf.asset（Dynamic FontAsset、#4 で導入）が
    /// EditMode/PlayMode テストやビルドのたびに git 差分を生む問題（#48）への対処。
    ///
    /// 原因: Dynamic FontAsset のプレースホルダー Atlas Texture2D（未使用時は 1x1）は
    /// CreateFontAsset() 直後は明示的にピクセルを設定しておらず、Editor セッションごとに
    /// 未初期化のメモリ内容がそのままシリアライズされ、YAML 上の `_typelessdata` の値が
    /// 実行環境ごとに変わりうる（実際の動的グリフ投入内容は Play Mode 終了時に破棄されるため
    /// 影響しない）。
    /// 対処:
    ///   1. プレースホルダーの Atlas Texture のピクセルを明示的にゼロで埋めて Apply() し、
    ///      シリアライズされるバイト列を確定させる。
    ///   2. Clear Dynamic Data On Build を明示的に有効化し、ビルド時に動的追加された
    ///      グリフをクリアして実行環境ごとの差分がビルド成果物に残らないようにする。
    /// 採用方針の詳細は docs/architecture.md を参照。
    /// </summary>
    public static class FontAssetSetup
    {
        private const string FontAssetPath = "Assets/TsumugiQuiz/UI/Fonts/notosansjp-regular-sdf.asset";

        [MenuItem("TsumugiQuiz/Setup/Normalize Japanese FontAsset")]
        public static void NormalizeFromMenu() => SetupPublic();

        /// <summary>バッチモード単体実行用エントリポイント（-executeMethod で直接呼び出す場合）。</summary>
        public static void RunAndExit()
        {
            var ok = SetupPublic();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>SetupAll から呼び出されるエントリポイント。</summary>
        public static bool SetupPublic()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError($"[FontAssetSetup] FontAsset が見つかりません: {FontAssetPath}");
                return false;
            }

            var changed = false;

            if (EnsureClearDynamicDataOnBuild(fontAsset))
            {
                changed = true;
            }

            if (NormalizePlaceholderAtlasTextures(fontAsset))
            {
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(fontAsset);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FontAssetSetup] FontAsset を正規化しました: {FontAssetPath}");
            }
            else
            {
                Debug.Log($"[FontAssetSetup] FontAsset は既に正規化済みです: {FontAssetPath}");
            }

            return true;
        }

        private static bool EnsureClearDynamicDataOnBuild(FontAsset fontAsset)
        {
            var serializedFontAsset = new SerializedObject(fontAsset);
            var clearOnBuildProp = serializedFontAsset.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuildProp == null)
            {
                Debug.LogError("[FontAssetSetup] m_ClearDynamicDataOnBuild フィールドが見つかりません。");
                return false;
            }

            if (clearOnBuildProp.boolValue)
            {
                return false;
            }

            clearOnBuildProp.boolValue = true;
            serializedFontAsset.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// 動的グリフがまだ 1 つも投入されていない（＝プレースホルダーのままの）Atlas Texture のみ、
        /// ピクセルをゼロで確定させる。実際にグリフが投入済みの Atlas は書き換えない。
        /// </summary>
        private static bool NormalizePlaceholderAtlasTextures(FontAsset fontAsset)
        {
            if (fontAsset.characterTable.Count > 0 || fontAsset.glyphTable.Count > 0)
            {
                // 既に動的グリフが投入されている環境（通常の Editor 利用中など）では、
                // 実際のアトラス内容を破棄しないよう何もしない。
                return false;
            }

            var changed = false;
            foreach (var atlasTexture in fontAsset.atlasTextures)
            {
                if (atlasTexture == null || !atlasTexture.isReadable)
                {
                    continue;
                }

                var pixelCount = atlasTexture.width * atlasTexture.height;
                var zeroPixels = new Color32[pixelCount]; // default(Color32) = (0, 0, 0, 0)
                atlasTexture.SetPixels32(zeroPixels);
                atlasTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                EditorUtility.SetDirty(atlasTexture);
                changed = true;
            }

            return changed;
        }
    }
}
