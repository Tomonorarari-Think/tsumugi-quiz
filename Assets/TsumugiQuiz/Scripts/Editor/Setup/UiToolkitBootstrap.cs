using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TsumugiQuiz.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace TsumugiQuiz.Editor.Setup
{
    /// <summary>
    /// UI Toolkit 基盤（PanelSettings アセットの生成、日本語 FontAsset の生成、
    /// Main シーンへの UIDocument / ViewRouter 配置）を行う一時的なセットアップスクリプト。
    /// ViewRouter の View 定義（_viewDefinitions）はエディタからしか設定できない
    /// （VisualTreeAsset の参照を Inspector 相当の方法で埋め込む必要がある）ため、
    /// バッチモードから `-executeMethod TsumugiQuiz.Editor.Setup.UiToolkitBootstrap.RunAndExit` で実行する。
    /// </summary>
    public static class UiToolkitBootstrap
    {
        private const string MainScenePath = "Assets/TsumugiQuiz/Scenes/Main.unity";
        private const string PanelSettingsPath = "Assets/TsumugiQuiz/Settings/panel-settings.asset";
        private const string ThemePath = "Assets/TsumugiQuiz/UI/Styles/tsumugi-theme.tss";
        private const string TermsViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/terms-view.uxml";
        private const string TitleViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/title-view.uxml";
        private const string HostSetupViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/host-setup-view.uxml";
        private const string JoinViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/join-view.uxml";
        private const string LobbyViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/lobby-view.uxml";
        private const string ResultViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/result-view.uxml";
        private const string CreditsViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/credits-view.uxml";
        private const string GameViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/game-view.uxml";
        private const string PlaceholderViewUxmlPath = "Assets/TsumugiQuiz/UI/Views/placeholder-view.uxml";
        private const string ModeratorControlsUxmlPath = "Assets/TsumugiQuiz/UI/Views/moderator-controls.uxml";
        private const string FontSourcePath = "Assets/TsumugiQuiz/UI/Fonts/NotoSansJP-Regular.otf";
        private const string FontAssetPath = "Assets/TsumugiQuiz/UI/Fonts/notosansjp-regular-sdf.asset";
        private const string UiGameObjectName = "UI";

        [MenuItem("TsumugiQuiz/Setup/Setup UI Toolkit Base")]
        public static void SetupFromMenu() => Setup();

        /// <summary>バッチモード実行用エントリポイント。</summary>
        public static void RunAndExit()
        {
            var ok = Setup();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool Setup()
        {
            try
            {
                // PanelSettings / FontAsset は作成/読み込み直後の参照ではなく、Main シーンを開いた後に
                // パスから読み直したものを使う（EditorSceneManager.OpenScene(Single) を挟むと、
                // シーン読み込み前に取得したアセット参照が無効になる場合があるため）。
                CreateOrLoadPanelSettings();

                if (!CreateOrLoadJapaneseFontAsset())
                {
                    return false;
                }

                return WireMainScene();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UiToolkitBootstrap] セットアップ中に例外が発生しました: {ex}");
                return false;
            }
        }

        private static PanelSettings CreateOrLoadPanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null)
            {
                Debug.Log($"[UiToolkitBootstrap] 既存の PanelSettings を使用します: {PanelSettingsPath}");
                return existing;
            }

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            // #132: 基準解像度は 1920x1080 ではなく 1600x900。ScaleWithScreenSize は
            // 「実ウィンドウ / 基準解像度」でパネル全体を拡縮する。MatchWidthOrHeight の倍率は
            // 幅比と高さ比を match で線形補間した値（UnityCsReference の PanelSettingsUtility.ResolveScale:
            // Mathf.Lerp(実幅 / 基準幅, 実高 / 基準高, match)。docs/architecture.md §10.2）。
            // 基準が 1920x1080 だと、想定する最小ウィンドウ 900x750 で倍率が約 0.58
            // （= (900/1920 + 750/1080) / 2）まで落ち、--font-size-base: 18px が
            // 実測 10px 程度になって読めなかった（実測: menu-column 360px が 207px で描画）。
            //
            // 一方で下げすぎると逆に困る（#132 レビュー H-1）。ProjectSettings は
            // fullscreenMode=1（FullScreenWindow）+ defaultIsNativeResolution=1 のため、既定起動は
            // ディスプレイ解像度いっぱいのフルスクリーンであり、16:9 環境では論理ビューポートが
            // ちょうど基準解像度そのもの（= 縦が基準解像度の高さ）になる。1280x720 にすると
            // 論理縦幅が 720px しか無く、Game 画面が溢れる（#187 以降は問題〜判定結果を縦スクロール領域に
            // 入れているが、これは想定より長い問題のための保険で、通常の出題はスクロールさせない前提で詰めてある）。
            //
            // 1600x900 なら 16:9 フルスクリーンで論理縦 900px（screen-root の padding 32px*2 を
            // 引いて作業領域 836px）を確保でき、900x750 のウィンドウでも倍率 0.698
            // （= (900/1600 + 750/900) / 2、論理 1289.6x1074.6）で本文が 12.6px 相当になる。
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            EnsureFolderExists(PanelSettingsPath);

            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UiToolkitBootstrap] PanelSettings を作成しました: {PanelSettingsPath}");
            return settings;
        }

        /// <summary>
        /// Noto Sans JP（SIL OFL 1.1、Regular ウェイトのみ、Assets/TsumugiQuiz/UI/Fonts/ 同梱）から
        /// Dynamic な FontAsset を生成する（日本語グリフを事前ベイクせず実行時に SDF アトラスへ追加する）。
        /// </summary>
        private static bool CreateOrLoadJapaneseFontAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (existing != null)
            {
                Debug.Log($"[UiToolkitBootstrap] 既存の FontAsset を使用します: {FontAssetPath}");
                return true;
            }

            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (sourceFont == null)
            {
                Debug.LogError($"[UiToolkitBootstrap] フォントソースの読み込みに失敗しました: {FontSourcePath}");
                return false;
            }

            var fontAsset = FontAsset.CreateFontAsset(
                sourceFont,
                samplingPointSize: 90,
                atlasPadding: 9,
                renderMode: GlyphRenderMode.SDFAA,
                atlasWidth: 1024,
                atlasHeight: 1024,
                atlasPopulationMode: AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError("[UiToolkitBootstrap] FontAsset の生成に失敗しました（FontAsset.CreateFontAsset が null を返しました）。");
                return false;
            }

            fontAsset.name = "NotoSansJP-Regular SDF";

            EnsureFolderExists(FontAssetPath);
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            foreach (var atlasTexture in fontAsset.atlasTextures)
            {
                if (atlasTexture == null)
                {
                    continue;
                }

                atlasTexture.name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath);

            Debug.Log($"[UiToolkitBootstrap] FontAsset を作成しました: {FontAssetPath}");
            return true;
        }

        private static void EnsureFolderExists(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
        }

        private static bool WireMainScene()
        {
            if (!File.Exists(MainScenePath))
            {
                Debug.LogError($"[UiToolkitBootstrap] Main scene が見つかりません: {MainScenePath}");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                Debug.LogError($"[UiToolkitBootstrap] PanelSettings の読み込みに失敗しました: {PanelSettingsPath}");
                return false;
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                Debug.LogError($"[UiToolkitBootstrap] テーマの読み込みに失敗しました: {ThemePath}");
                return false;
            }
            panelSettings.themeStyleSheet = theme;
            EditorUtility.SetDirty(panelSettings);
            AssetDatabase.SaveAssets();

            var uiGameObject = FindRootGameObject(scene, UiGameObjectName);
            if (uiGameObject == null)
            {
                uiGameObject = new GameObject(UiGameObjectName);
            }

            var uiDocument = uiGameObject.GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = uiGameObject.AddComponent<UIDocument>();
            }

            // UIDocument は native コンポーネントで、プロパティへの直接代入だけでは
            // EditorSceneManager.SaveScene 時にシリアライズされないことがあるため、
            // ViewRouter 同様 SerializedObject 経由で設定する。
            var serializedDocument = new SerializedObject(uiDocument);
            var panelSettingsProp = serializedDocument.FindProperty("m_PanelSettings");
            if (panelSettingsProp == null)
            {
                Debug.LogError("[UiToolkitBootstrap] UIDocument の m_PanelSettings フィールドが見つかりません。");
                return false;
            }
            panelSettingsProp.objectReferenceValue = panelSettings;
            serializedDocument.ApplyModifiedPropertiesWithoutUndo();

            var viewRouter = uiGameObject.GetComponent<ViewRouter>();
            if (viewRouter == null)
            {
                viewRouter = uiGameObject.AddComponent<ViewRouter>();
            }

            var termsTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TermsViewUxmlPath);
            var titleTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TitleViewUxmlPath);
            var hostSetupTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HostSetupViewUxmlPath);
            var joinTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(JoinViewUxmlPath);
            var lobbyTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LobbyViewUxmlPath);
            var resultTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ResultViewUxmlPath);
            var creditsTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreditsViewUxmlPath);
            var gameTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GameViewUxmlPath);
            var placeholderTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PlaceholderViewUxmlPath);
            var moderatorControlsTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ModeratorControlsUxmlPath);

            if (termsTemplate == null || titleTemplate == null || hostSetupTemplate == null || joinTemplate == null ||
                lobbyTemplate == null || resultTemplate == null || creditsTemplate == null || gameTemplate == null ||
                placeholderTemplate == null || moderatorControlsTemplate == null)
            {
                Debug.LogError("[UiToolkitBootstrap] View テンプレート(UXML)の読み込みに失敗しました。");
                return false;
            }

            if (!TryPopulateViewDefinitions(
                viewRouter,
                uiDocument,
                termsTemplate,
                titleTemplate,
                hostSetupTemplate,
                joinTemplate,
                lobbyTemplate,
                resultTemplate,
                creditsTemplate,
                gameTemplate,
                placeholderTemplate,
                moderatorControlsTemplate))
            {
                return false;
            }

            EditorUtility.SetDirty(uiGameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            var saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
            {
                Debug.LogError("[UiToolkitBootstrap] Main シーンの保存に失敗しました。");
                return false;
            }

            Debug.Log("[UiToolkitBootstrap] Main シーンに UIDocument / ViewRouter を配置しました。");
            return true;
        }

        /// <summary>シーンのルートオブジェクトのみを対象に名前で検索する（GameObject.Find はシーン内の
        /// 全オブジェクトを対象にするため、意図しない同名オブジェクトに当たる可能性があり避ける）。</summary>
        private static GameObject FindRootGameObject(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            return null;
        }

        private static bool TryPopulateViewDefinitions(
            ViewRouter viewRouter,
            UIDocument uiDocument,
            VisualTreeAsset termsTemplate,
            VisualTreeAsset titleTemplate,
            VisualTreeAsset hostSetupTemplate,
            VisualTreeAsset joinTemplate,
            VisualTreeAsset lobbyTemplate,
            VisualTreeAsset resultTemplate,
            VisualTreeAsset creditsTemplate,
            VisualTreeAsset gameTemplate,
            VisualTreeAsset placeholderTemplate,
            VisualTreeAsset moderatorControlsTemplate)
        {
            var serializedRouter = new SerializedObject(viewRouter);

            var documentProp = serializedRouter.FindProperty("_document");
            if (documentProp == null)
            {
                Debug.LogError("[UiToolkitBootstrap] ViewRouter の _document フィールドが見つかりません。");
                return false;
            }
            documentProp.objectReferenceValue = uiDocument;

            // 司会専用モードの進行操作パネル（#20）。GameView が組み込む先を持たないため、
            // ViewRouter 経由の唯一のシーン参照として持たせる。
            var moderatorControlsProp = serializedRouter.FindProperty("_moderatorControlsTemplate");
            if (moderatorControlsProp == null)
            {
                Debug.LogError("[UiToolkitBootstrap] ViewRouter の _moderatorControlsTemplate フィールドが見つかりません。");
                return false;
            }
            moderatorControlsProp.objectReferenceValue = moderatorControlsTemplate;

            var definitionsProp = serializedRouter.FindProperty("_viewDefinitions");
            if (definitionsProp == null)
            {
                Debug.LogError("[UiToolkitBootstrap] ViewRouter の _viewDefinitions フィールドが見つかりません。");
                return false;
            }

            var definitions = new List<(string Name, VisualTreeAsset Template)>
            {
                (ViewNames.Terms, termsTemplate),
                (ViewNames.Title, titleTemplate),
                (ViewNames.HostSetup, hostSetupTemplate),
                (ViewNames.Join, joinTemplate),
                (ViewNames.Lobby, lobbyTemplate),
                (ViewNames.Result, resultTemplate),
                (ViewNames.Credits, creditsTemplate),
                (ViewNames.Game, gameTemplate),
            };
            definitions.AddRange(ViewNames.PlaceholderViews.Select(name => (name, placeholderTemplate)));

            definitionsProp.arraySize = definitions.Count;
            for (var i = 0; i < definitions.Count; i++)
            {
                var element = definitionsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("ViewName").stringValue = definitions[i].Name;
                element.FindPropertyRelative("Template").objectReferenceValue = definitions[i].Template;
            }

            serializedRouter.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
