using System;
using System.Threading;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.TextLayout;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 読み上げ（TTS）が欠落・利用不可のときのフォールバック UI（#25、docs/tts.md §9.1）。
    ///
    /// マークアップは <c>Resources/UI/tts-status-panel.uxml</c>（+ <c>theme.uss</c>）に持たせてあり
    /// （#25 M-1）、<see cref="Create"/> はそれを読み込んで組み立てた <see cref="VisualElement"/> を返す。
    /// Settings View（#28）・HostSetup（#5）にも同じ要素を組み込める（現時点では Title View の隅から呼ぶだけ）。
    ///
    /// 表示する理由（<see cref="TtsUnavailableReason"/>）の優先順位は
    /// <see cref="ResolveReason"/> に集約している: 同意ゲート（FR-74/75）→ 読み上げ設定 → <see cref="TtsService.Status"/>。
    /// </summary>
    public static class TtsStatusPanel
    {
        private const string TemplateResourcePath = "UI/tts-status-panel";
        private const string SetupInstructionsResourcePath = "Docs/tts-setup";

        /// <summary>uxml 側のオーバーレイ要素名。<see cref="Create"/> がこの名前で要素を取り出す。</summary>
        private const string OverlayName = "tts-status-overlay";

        // TODO(#36): 公開 Releases ページが用意できたら、配置手順の案内にそのリンクを追加する。
        // リポジトリが非公開のため、現状は外部 URL を使わずアプリ内蔵テキスト
        // （Resources/Docs/tts-setup.txt、SetupInstructionsResourcePath）だけを案内する（#25 H-2）。
        internal const string PublicReleasesUrlTodo36 = null;

        // NotoSansJP に収録されていない絵文字は「豆腐」になるため使わない（#25 M-7）。
        private const string ReadyIcon = "OK";
        private const string NeutralIcon = "-";
        private const string WarningIcon = "!";

        /// <summary>Title 隅などに出す 1 行の状態文言（Ready / 準備中 / 未確認 / 利用不可）。</summary>
        public static string DescribeCornerStatus(TtsService service)
        {
            if (!ConsentGate.HasUserConsented()) return "読み上げ: 利用不可";
            if (service == null) return "読み上げ: 未確認";
            if (!service.ReadingEnabled) return "読み上げ: 利用不可";

            return service.Status.State switch
            {
                TtsServiceState.Ready => "読み上げ: Ready",
                TtsServiceState.Initializing => "読み上げ: 準備中",
                TtsServiceState.NotInitialized => "読み上げ: 未確認",
                _ => "読み上げ: 利用不可",
            };
        }

        /// <summary>
        /// いま案内すべき理由。読み上げが問題なく使える状態、またはまだ確認していない状態
        /// （Ready / NotInitialized / Initializing）なら null（<see cref="Create"/> 側で 3 状態を出し分ける）。
        /// <see cref="TtsService.Status"/> だけでは分からない理由（同意・設定）をここで合流させる。
        /// </summary>
        public static TtsUnavailableReason? ResolveReason(TtsService service)
        {
            // FR-74/75: 未同意・撤回後は他の理由より優先して案内する。
            if (!ConsentGate.HasUserConsented()) return TtsUnavailableReason.ConsentNotGiven;

            if (service == null) return null; // 未確認（Instance が無い）。パネル側で「未確認」表示にする。
            if (!service.ReadingEnabled) return TtsUnavailableReason.UserSuppressed;

            var status = service.Status;
            if (status.State != TtsServiceState.NotAvailable) return null;

            return service.UnavailableReason ?? TtsUnavailableReason.InitializationFailed;
        }

        /// <summary>
        /// パネル本体を生成する。<paramref name="router"/> が渡され、理由が
        /// <see cref="TtsUnavailableReason.ConsentNotGiven"/> のときだけ利用規約画面への導線を出す。
        ///
        /// <b>ここでは <see cref="TtsService.EnsureInitializedAsync"/> を呼ばない</b>（#25 H-5）。
        /// 初期化は「再試行（確認する）」ボタン（<see cref="TtsService.RetryInitializeAsync"/>）からのみ行う。
        /// </summary>
        /// <param name="service">対象の <see cref="TtsService"/>。null でも表示できる（未確認扱い）。</param>
        /// <param name="router">「利用規約画面へ」ボタンの遷移先。null なら非表示。</param>
        /// <param name="onClosed">「閉じる」「読み上げなしで続行」「利用規約画面へ」の各操作後に呼ばれる。</param>
        /// <param name="settingsProvider">
        /// 「再試行」で <see cref="TtsService.RetryInitializeAsync"/> に渡すアプリ設定の読み込み元（issue #28 M1）。
        /// null なら <see cref="TtsService"/> 既定（<see cref="DefaultTtsSettingsProvider"/>）を使う。
        /// Settings View（<c>TsumugiQuiz.UI.Views.Settings.SettingsView</c>）はここへ、保存直後の
        /// アプリ設定から作った <see cref="ITtsSettingsProvider"/> を渡し、再試行のたびに最新の
        /// <c>tts.*</c> を反映できるようにする。
        /// </param>
        public static VisualElement Create(
            TtsService service, ViewRouter router = null, Action onClosed = null, ITtsSettingsProvider settingsProvider = null)
        {
            // #97: 「再試行」の完了後の UI 更新（ボタンの再有効化・Refresh、いずれも UI Toolkit の
            // 要素操作でメインスレッド専用）を、await の暗黙のコンテキスト捕捉に頼らず確実に
            // メインスレッドへ戻すため、Create の同期部分（＝メインスレッド）で捕まえておく。
            // HostConnectivityService / UnityWebRequestIpLookupClient と同じ作法。
            var mainThreadContext = SynchronizationContext.Current;

            var template = Resources.Load<VisualTreeAsset>(TemplateResourcePath);
            if (template == null)
            {
                Debug.LogError($"[TtsStatusPanel] テンプレートが見つかりません（Resources/{TemplateResourcePath}.uxml）。");
                return new VisualElement { name = OverlayName };
            }

            var instance = template.Instantiate();
            var overlay = instance.Q<VisualElement>(OverlayName);
            if (overlay == null)
            {
                Debug.LogError($"[TtsStatusPanel] '{OverlayName}' が見つかりません。tts-status-panel.uxml を確認してください。");
                return instance;
            }

            // #25 M-8: 呼び出し側（TitleView 等）が Document.rootVisualElement 直下に置けるよう、
            // テンプレートのラッパー（TemplateContainer）を経由せず overlay 自身を返す
            // （overlay 自身は position:absolute + inset:0 なので、直接の親が画面いっぱいなら全画面を覆う）。
            overlay.RemoveFromHierarchy();

            var icon = overlay.Q<Label>("tts-status-icon");
            var headline = overlay.Q<Label>("tts-status-headline");
            var guidance = overlay.Q<Label>("tts-status-guidance");
            var detailLabel = overlay.Q<Label>("tts-status-detail");
            var setupScroll = overlay.Q<ScrollView>("tts-setup-instructions-scroll");
            var setupText = overlay.Q<Label>("tts-setup-instructions-text");
            var retryButton = overlay.Q<Button>("tts-retry-button");
            var termsButton = overlay.Q<Button>("tts-terms-button");
            var continueButton = overlay.Q<Button>("tts-continue-button");
            var guideButton = overlay.Q<Button>("tts-setup-guide-button");
            var closeButton = overlay.Q<Button>("tts-close-button");

            if (icon == null || headline == null || guidance == null || detailLabel == null || setupScroll == null
                || setupText == null || retryButton == null || termsButton == null || continueButton == null
                || guideButton == null || closeButton == null)
            {
                Debug.LogError("[TtsStatusPanel] 必要な要素が見つかりません。tts-status-panel.uxml を確認してください。");
                return overlay;
            }

            var setupInstructionsVisible = false;

            void Refresh()
            {
                var reason = ResolveReason(service);

                // 折りたたみの開閉は理由に関わらず setupInstructionsVisible の値そのままを反映する
                // （uxml の初期値だけに頼らず、Refresh のたびに明示的に揃えておく）。
                SetDisplay(setupScroll, setupInstructionsVisible);

                if (!reason.HasValue)
                {
                    // Ready / NotInitialized（未確認）/ Initializing（確認中）の 3 状態を出し分ける。
                    var state = service?.Status.State ?? TtsServiceState.NotInitialized;
                    var isReady = state == TtsServiceState.Ready;

                    icon.text = isReady ? ReadyIcon : NeutralIcon;
                    // #199: 見出し・案内文は区切りの位置で改行する（PhraseWrappedText）。この 3 状態の文言は 1 行に収まる。
                    PhraseWrappedText.SetText(headline, state switch
                    {
                        TtsServiceState.Ready => "読み上げは利用できます。",
                        TtsServiceState.Initializing => "状態を確認しています…",
                        _ => "読み上げの状態はまだ確認していません。",
                    });
                    PhraseWrappedText.SetText(guidance, isReady
                        ? string.Empty
                        : "「再試行（確認する）」を押すと状態を確認できます。");
                    SetDisplay(detailLabel, false);

                    retryButton.text = state == TtsServiceState.NotInitialized ? "確認する" : "再試行";
                    SetDisplay(retryButton, true);
                    SetDisplay(termsButton, false);
                    SetDisplay(continueButton, !isReady);
                    SetDisplay(guideButton, false);
                    return;
                }

                var message = TtsStatusMessages.For(reason.Value, service?.UnavailableDetail);
                icon.text = WarningIcon;
                // #199: 理由別の見出し・案内文はパネルの幅を超えるものがあるため、語の途中ではなく区切りの位置で改行する。
                PhraseWrappedText.SetText(headline, message.Headline);
                PhraseWrappedText.SetText(guidance, message.Guidance);

                if (string.IsNullOrEmpty(message.Detail))
                {
                    SetDisplay(detailLabel, false);
                }
                else
                {
                    detailLabel.text = message.Detail;
                    SetDisplay(detailLabel, true);
                }

                var isConsentIssue = reason.Value == TtsUnavailableReason.ConsentNotGiven;

                // #25 H-1: UserSuppressed（設定で無効）のときも「配置手順」「再試行」は残す
                // （配置自体は問題ない可能性があるため）。同意未了だけは別の導線にする。
                retryButton.text = "再試行";
                SetDisplay(retryButton, !isConsentIssue);
                SetDisplay(guideButton, !isConsentIssue);
                SetDisplay(termsButton, isConsentIssue && router != null);
                SetDisplay(continueButton, true);

                if (isConsentIssue && setupInstructionsVisible)
                {
                    setupInstructionsVisible = false;
                    SetDisplay(setupScroll, false);
                    guideButton.text = "配置手順を表示";
                }
            }

            retryButton.clicked += () =>
                OnRetryClicked(service, settingsProvider, retryButton, headline, Refresh, mainThreadContext);

            // #25 H-1: 「読み上げなしで続行」は ReadingEnabled を変更せず、パネルを閉じるだけ。
            // ReadingEnabled はセッション限りの状態で、ユーザーが実際に設定を変えたときだけ変わる
            // （永続化は #28、docs/tts.md §9.1）。
            continueButton.clicked += () => onClosed?.Invoke();

            guideButton.clicked += () =>
            {
                setupInstructionsVisible = !setupInstructionsVisible;
                if (setupInstructionsVisible && string.IsNullOrEmpty(setupText.text))
                {
                    var asset = Resources.Load<TextAsset>(SetupInstructionsResourcePath);
                    setupText.text = asset != null
                        ? asset.text
                        : "配置手順を読み込めませんでした。External/README.md を参照してください。";
                }

                SetDisplay(setupScroll, setupInstructionsVisible);
                guideButton.text = setupInstructionsVisible ? "配置手順を閉じる" : "配置手順を表示";
            };

            termsButton.clicked += () =>
            {
                onClosed?.Invoke();
                router?.ShowView(ViewNames.Terms);
            };

            closeButton.clicked += () => onClosed?.Invoke();

            Refresh();

            if (service != null)
            {
                // #25 M-4: 購読は AttachToPanelEvent で開始し、DetachFromPanelEvent で解除する
                // （パネルとして実際に表示されている間だけ購読し、参照リークを防ぐ）。
                Action<TtsServiceStatus> handler = _ => Refresh();
                overlay.RegisterCallback<AttachToPanelEvent>(_ => service.StatusChanged += handler);
                overlay.RegisterCallback<DetachFromPanelEvent>(_ => service.StatusChanged -= handler);
            }

            return overlay;
        }

        /// <summary>
        /// 「再試行（確認する）」ボタンの処理（#25 C-1）。実行中はボタンを無効化し、完了したら戻す。
        /// <paramref name="settingsProvider"/>（issue #28 M1）を渡すことで、Settings View で保存した
        /// 最新の <c>tts.*</c> を反映して再初期化できる。
        ///
        /// <b>#97</b>: <c>await</c> 後の継続（このメソッドの <c>finally</c>）は、EditMode テストのように
        /// <see cref="SynchronizationContext"/> が捕捉されない環境ではスレッドプール上で走りうる。
        /// <paramref name="retryButton"/>・<paramref name="headline"/> の書き換えと <paramref name="refresh"/>
        /// （<c>Resources.Load</c> を経由しうる <c>ConsentGate.HasUserConsented</c> を含む）は
        /// いずれもメインスレッド専用の操作のため、<c>ConfigureAwait(false)</c> で暗黙の復帰に頼らず、
        /// <paramref name="mainThreadContext"/>（<see cref="Create"/> がメインスレッドで捕まえたもの）へ
        /// 明示的に <see cref="SynchronizationContext.Post"/> して実行する。
        ///
        /// <c>RetryInitializeAsync</c> 自体は失敗を <see cref="TtsService.Status"/> に落として
        /// 例外を投げない設計だが、<c>async void</c> ハンドラである以上、想定外の例外が漏れると
        /// アプリを落としかねない。<b>#97 L-1</b>: そのため握りつぶさずログに残し、<c>finally</c> で
        /// 必ずボタンを戻す・状態表示を更新することは保証する。
        /// </summary>
        private static async void OnRetryClicked(
            TtsService service, ITtsSettingsProvider settingsProvider, Button retryButton, Label headline,
            Action refresh, SynchronizationContext mainThreadContext)
        {
            if (service == null) return;

            retryButton.SetEnabled(false);
            PhraseWrappedText.SetText(headline, "確認しています…");

            try
            {
                // #127: consentCheck を渡さないと RetryInitializeAsync が TtsService の同意確認を
                // null（＝制限なし）へ戻してしまい、「再試行」を押した後は撤回しても合成が止まらなくなる。
                // 判定の出所は GameView / QuestionEditorView と同じ共通ファクトリに揃える。
                await service
                    .RetryInitializeAsync(
                        settingsProvider: settingsProvider, consentCheck: TtsConsentCheckFactory.Build())
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // #97 L-1: async void なので、想定外の例外はここで捕まえてログに残す。
                // 握りつぶした結果は下の finally の refresh() が状態表示に反映する。
                Debug.LogException(e);
            }
            finally
            {
                RunOnMainThread(mainThreadContext, () =>
                {
                    // #97 M-3: 「再試行」を押した後にパネルが閉じられた・シーン遷移した等で
                    // overlay から外れていれば、もう存在しない要素を触らない
                    // （retryButton.panel は要素がいずれかの Panel にアタッチされていなければ null）。
                    if (retryButton.panel == null) return;

                    retryButton.SetEnabled(true);
                    refresh();
                });
            }
        }

        /// <summary>
        /// <paramref name="action"/> を、いま既にメインスレッド（<paramref name="mainThreadContext"/> と
        /// 同じコンテキスト）上ならそのまま実行し、そうでなければ <see cref="SynchronizationContext.Post"/> で
        /// 移してから実行する（#97。<c>UnityWebRequestIpLookupClient</c> と同じ作法）。
        /// <paramref name="mainThreadContext"/> が null（同期コンテキストが存在しない環境）のときは
        /// その場で実行するしかない（呼び出し元がその場合の安全性を保証すること）。
        /// </summary>
        private static void RunOnMainThread(SynchronizationContext mainThreadContext, Action action)
        {
            if (mainThreadContext != null && SynchronizationContext.Current != mainThreadContext)
            {
                mainThreadContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
