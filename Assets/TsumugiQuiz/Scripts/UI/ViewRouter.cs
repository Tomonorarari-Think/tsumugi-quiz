using System;
using System.Collections.Generic;
using TsumugiQuiz.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// UI Toolkit の View（UXML）を切り替える画面遷移基盤。
    /// Main シーンの UIDocument と同じ GameObject にアタッチする。
    /// 遷移・履歴（戻る）ロジック自体は <see cref="ViewNavigationHistory"/>（純 C#）に、
    /// View 名→コントローラの割り当ては <see cref="ViewControllerRegistry"/> に委譲する。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ViewRouter : MonoBehaviour
    {
        /// <summary>
        /// SwapTo() が Instantiate() で生成した TemplateContainer に付与する USS クラス名
        /// （theme.uss の <c>.view-container</c>、#105）。TemplateContainer を
        /// Document.rootVisualElement いっぱいに伸ばすためのもの。
        /// </summary>
        private const string ViewContainerClass = "view-container";

        [SerializeField]
        private UIDocument _document;

        [SerializeField]
        private List<ViewDefinition> _viewDefinitions = new();

        [SerializeField]
        private string _initialViewName = ViewNames.Title;

        [SerializeField]
        [Tooltip(
            "司会専用モードの進行操作パネル（moderator-controls.uxml、#20）。GameView が組み込む先を持たない、" +
            "ViewRouter を経由した唯一のシーン参照のため、ここに保持する（UiToolkitBootstrap が設定する）。")]
        private VisualTreeAsset _moderatorControlsTemplate;

        private readonly Dictionary<string, VisualTreeAsset> _templates = new();
        private readonly ViewControllerRegistry _controllerRegistry = new();
        private readonly ViewNavigationHistory _history = new();

        private IView _currentController;

#if UNITY_EDITOR
        /// <summary>
        /// 直近の <see cref="SwapTo"/> で描画に使った <see cref="UIDocument.rootVisualElement"/> への参照
        /// （issue #137）。Editor の UI Toolkit ライブリロード（UXML 保存時の
        /// <c>UIDocument.RecreateUI()</c>）は rootVisualElement を別インスタンスに作り直すため、
        /// 毎フレーム比較して検知する。Player ビルドにはライブリロードが存在しないため Editor 専用にする。
        /// </summary>
        private VisualElement _lastKnownRoot;
#endif

        /// <summary>
        /// 初期 View 名の決定処理を差し替えるフック。null の場合は Inspector で設定した
        /// <see cref="_initialViewName"/> をそのまま使う。
        /// 既定では Awake 内で <see cref="Views.DefaultViewControllerRegistrations.ConfigureInitialView"/> が、
        /// 利用規約の同意状況（<see cref="ConsentGate"/>）に基づいて Terms / Title を振り分ける処理を設定する
        /// （requirements.md FR-71・FR-74）。EditMode テスト等、Awake が呼ばれない環境では null のままなので、
        /// これまでどおり <see cref="_initialViewName"/> の値が使われる。
        /// </summary>
        public Func<string> InitialViewNameOverride { get; set; }

        /// <summary>現在表示中の View 名。まだ何も表示していない場合は null。</summary>
        public string CurrentViewName => _history.Current;

        /// <summary>
        /// 現在表示中の View コントローラ（#101 の PlayMode テスト用）。
        /// 表示中の View が内部状態（<c>GameView</c> のスケジュールコールバックが生きているか等）を
        /// 正しく後始末したかをテストから確かめるためだけに公開しており、アプリ側からは使わない。
        /// </summary>
        internal IView CurrentController => _currentController;

        /// <summary>戻り先の履歴があるか。</summary>
        public bool CanGoBack => _history.CanGoBack;

        /// <summary>
        /// 司会専用モードの進行操作パネル（<c>moderator-controls.uxml</c>）のテンプレート（#20）。
        /// <see cref="Views.Game.GameView"/> が <c>ModeratorControlsPanel.Create</c> に渡すために参照する。
        /// </summary>
        public VisualTreeAsset ModeratorControlsTemplate => _moderatorControlsTemplate;

        /// <summary>
        /// UIDocument への参照。Awake が実行されない文脈（MonoBehaviour のライフサイクルを
        /// 明示的に駆動できない EditMode テストなど）でも動くよう、初回アクセス時に
        /// GetComponent で解決する（[RequireComponent] により同じ GameObject に必ず存在する）。
        /// </summary>
        private UIDocument Document => _document != null ? _document : (_document = GetComponent<UIDocument>());

        private void Awake()
        {
            if (Document == null)
            {
                Debug.LogError("[ViewRouter] UIDocument が見つかりません。同じ GameObject にアタッチしてください。");
                enabled = false;
                return;
            }

            RegisterTemplates();
            DefaultViewControllerRegistrations.RegisterDefaults(_controllerRegistry);
            DefaultViewControllerRegistrations.ConfigureInitialView(this);

            // #127 レビュー H-1: 読み上げの初期化を最初に始めるのは、ロビーでスポーンする
            // TtsSyncCoordinator（Network 層。UI 層を参照できない）になりうる。同意ゲートが
            // 素通りしないよう、アプリ起動時のこの時点で TtsService へ登録しておく（初期化は始めない）。
            DefaultViewControllerRegistrations.ConfigureTtsConsentGate();

            ValidateTemplateCompleteness();
        }

        private void Start()
        {
            ShowInitialView();
        }

#if UNITY_EDITOR
        /// <summary>
        /// issue #137: Editor の UI Toolkit ライブリロード（UXML 保存時）は
        /// <see cref="UIDocument.RecreateUI"/> で <see cref="UIDocument.rootVisualElement"/> を
        /// 作り直すが、<see cref="ViewRouter"/> は <see cref="Start"/> 済みのため何もしないと
        /// 画面が空になる。毎フレーム参照を比較し、作り直されていたら現在の View を再表示する
        /// （<see cref="SwapTo"/> をそのまま再利用するため、二重購読・二重生成は起きない）。
        /// Player ビルドにはライブリロードが無いためこのチェック自体が走らないよう Editor 専用にしている。
        /// レビュー M-1: 判定基準は <see cref="_currentController"/>（View コントローラを持たない
        /// プレースホルダー View では常に null）ではなく <see cref="_lastKnownRoot"/> の有無にしている。
        /// <see cref="_lastKnownRoot"/> は <see cref="SwapTo"/> で必ず設定されるため、「まだ一度も
        /// View を表示していない」ことの判定として正しく機能し、コントローラ無しの View でも
        /// 再表示の対象になる。
        /// </summary>
        private void Update()
        {
            if (_lastKnownRoot == null)
            {
                return;
            }

            var root = Document != null ? Document.rootVisualElement : null;
            if (root == null || ReferenceEquals(root, _lastKnownRoot))
            {
                return;
            }

            var viewName = _history.Current;
            if (string.IsNullOrEmpty(viewName))
            {
                return;
            }

            Debug.Log($"[ViewRouter] ライブリロードで UIDocument.rootVisualElement が作り直されたため、'{viewName}' を再表示します。");
            SwapTo(viewName);
        }
#endif

        /// <summary>
        /// Inspector で設定した初期 View（既定は <see cref="ViewNames.Title"/>）を表示する。
        /// 通常は Start から自動的に呼ばれるが、MonoBehaviour のライフサイクルを明示的に駆動できない
        /// EditMode テストなどからも直接呼び出せるよう公開している。
        /// </summary>
        public void ShowInitialView()
        {
            var viewName = InitialViewNameOverride != null ? InitialViewNameOverride() : _initialViewName;
            if (!string.IsNullOrEmpty(viewName))
            {
                ShowView(viewName);
            }
        }

        private void RegisterTemplates()
        {
            foreach (var definition in _viewDefinitions)
            {
                if (string.IsNullOrEmpty(definition.ViewName) || definition.Template == null)
                {
                    Debug.LogError($"[ViewRouter] View定義が不正です（ViewName='{definition.ViewName}'）。Inspector の設定を確認してください。");
                    continue;
                }

                _templates[definition.ViewName] = definition.Template;
            }
        }

        /// <summary>
        /// architecture.md §2 の View 一覧（<see cref="ViewNames.RegisteredViews"/>）のうち、
        /// テンプレートが登録されていないものがあれば fail-fast でエラーを出す。
        /// </summary>
        private void ValidateTemplateCompleteness()
        {
            foreach (var viewName in ViewNames.RegisteredViews)
            {
                if (!_templates.ContainsKey(viewName))
                {
                    Debug.LogError($"[ViewRouter] '{viewName}' に対応するテンプレートが _viewDefinitions に登録されていません。");
                }
            }
        }

        /// <summary>
        /// 指定した View 名に対する画面コントローラ（<see cref="IView"/>）を登録する。
        /// 同じ View 名に対して呼び出すと上書きされる。
        /// </summary>
        public void RegisterController(string viewName, Func<IView> controllerFactory)
        {
            _controllerRegistry.Register(viewName, controllerFactory);
        }

        /// <summary>
        /// 指定した View 名にテンプレートを登録する。通常は Inspector（_viewDefinitions）経由で設定するが、
        /// コードから動的に View を追加したい場合や、EditMode テストから直接セットアップしたい場合に使う。
        /// </summary>
        public void RegisterTemplate(string viewName, VisualTreeAsset template)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new ArgumentException("viewName が null または空です。", nameof(viewName));
            }

            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            _templates[viewName] = template;
        }

        /// <summary>
        /// 指定した View に切り替える。
        /// </summary>
        /// <param name="viewName">切り替え先の View 名。</param>
        /// <param name="addToHistory">
        /// true の場合、現在の View を履歴に積んでから切り替える（<see cref="GoBack"/> で戻れるようになる）。
        /// 履歴を操作したくない内部呼び出し（<see cref="GoBack"/> からの再表示など）では false を指定する。
        /// </param>
        public void ShowView(string viewName, bool addToHistory = true)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new ArgumentException("viewName が null または空です。", nameof(viewName));
            }

            // 未登録の View は履歴を汚す前に弾く。
            if (!_templates.ContainsKey(viewName))
            {
                Debug.LogError($"[ViewRouter] 未登録の View です: '{viewName}'");
                return;
            }

            if (addToHistory)
            {
                if (viewName == _history.Current)
                {
                    // 同一 View への連打（二重クリック等）は無視する。
                    return;
                }

                // SwapTo() の中で View コントローラの OnShow が router.CanGoBack を参照するため、
                // 実際の切り替えより先に履歴を更新しておく必要がある。
                _history.Push(viewName);
            }

            SwapTo(viewName);
        }

        /// <summary>
        /// 直前の View に戻る。戻り先の履歴がない場合は何もせず警告を出す。
        /// </summary>
        public void GoBack()
        {
            if (!_history.CanGoBack)
            {
                Debug.LogWarning("[ViewRouter] 戻り先の履歴がないため GoBack を無視しました。");
                return;
            }

            // 履歴は「戻る」時点で先に pop してから View を切り替える（切り替え先の View コントローラの
            // OnShow が router.CanGoBack / CurrentViewName を参照した際に、正しい戻り先の状態を見せるため）。
            var target = _history.GoBack();
            ShowView(target, addToHistory: false);
        }

        /// <summary>
        /// レビュー M-12（#14）: ViewRouter 自身（延いては UIDocument）が破棄されるとき、
        /// 表示中の View コントローラに <see cref="IView.OnHide"/> を必ず届ける。
        /// <see cref="ShowView"/> を経由せずシーンごと破棄される経路（PlayMode テストのシーン切替、
        /// アプリ終了時など）でも、View 側が確保したリソース（<c>GameView</c> の
        /// <c>InputAction</c>・イベント購読・<c>IVisualElementScheduledItem</c> 等）を確実に解放させるため。
        /// </summary>
        private void OnDestroy()
        {
            _currentController?.OnHide();
            _currentController = null;
        }

        private void SwapTo(string viewName)
        {
            var template = _templates[viewName];

            var root = Document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[ViewRouter] UIDocument.rootVisualElement が null です。PanelSettings の設定を確認してください。");
                return;
            }

            _currentController?.OnHide();

#if UNITY_EDITOR
            // issue #137: この SwapTo が使う root を、Update() のライブリロード検知の基準にする。
            _lastKnownRoot = root;
#endif

            root.Clear();

            var instance = template.Instantiate();
            // #105: VisualTreeAsset.Instantiate() が返す TemplateContainer は既定では中身に合わせて
            // 縮む（height: auto）ため、view-container（theme.uss）でウィンドウ高いっぱいに伸ばす。
            // 各 View の最上位要素（.screen-root 等）の flex-grow: 1 は、この親が伸びて初めて効く。
            instance.AddToClassList(ViewContainerClass);
            root.Add(instance);

            var controller = _controllerRegistry.CreateController(viewName);
            _currentController = controller;
            controller?.OnShow(new ViewContext(viewName, instance, this));
        }
    }
}
