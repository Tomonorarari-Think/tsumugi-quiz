using System;
using System.Threading;
using System.Threading.Tasks;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.QuestionEditor
{
    /// <summary>
    /// <see cref="QuestionEditorView"/> のうち、編集フォーム（issue #31）の「読み上げプレビュー」（issue #32）を
    /// 扱う部分クラス。編集中の <c>readingText</c>（空欄なら <c>text</c>）を <see cref="TtsService"/> で
    /// ローカル合成・再生するだけで、ネットワーク同期（<c>TtsSyncCoordinator</c>）は使わない。速度は既定の
    /// <c>1.0</c> 固定で試聴する。
    ///
    /// <see cref="TtsService.Instance"/> が無い・<see cref="TtsStatusPanel.ResolveReason"/> が理由を返す場合
    /// （External 未配置・未同意・ユーザーによる無効化等）は、フォーム内に短い見出しの案内 +
    /// 「読み上げの状態を確認」ボタンを出す。押すと <see cref="TtsStatusPanel"/>（#25 のフォールバック UI）を
    /// 全画面オーバーレイで開き、実際の「配置手順を表示」「再試行」「利用規約画面へ」等の導線はそちらに一本化する
    /// （PR #103 レビュー M1。フォームのラベルに存在しないボタンを案内する文言を出さないため）。
    /// </summary>
    public sealed partial class QuestionEditorView
    {
        /// <summary>再生完了を検知するポーリング間隔（ミリ秒）。<c>AudioSource</c> は完了イベントを持たないため。</summary>
        private const int TtsPreviewPollIntervalMs = 150;

        /// <summary>
        /// 「読み上げを試す」ボタンの文言（PR #103 再レビュー N4）。PlayMode テストがボタンの状態遷移を
        /// 文字列直書きではなくこの定数経由で確認できるようにする。
        /// </summary>
        internal const string TtsPreviewIdleButtonText = "読み上げを試す";

        /// <summary>合成中のボタン文言（同上、N4）。</summary>
        internal const string TtsPreviewSynthesizingButtonText = "準備中…";

        /// <summary>再生中（押すと停止できる）のボタン文言（同上、N4）。</summary>
        internal const string TtsPreviewPlayingButtonText = "停止";

        private enum TtsPreviewState
        {
            /// <summary>待機中（「読み上げを試す」を押せる）。</summary>
            Idle,

            /// <summary>合成中（ボタンは無効化）。</summary>
            Synthesizing,

            /// <summary>再生中（ボタンを押すと停止できる）。</summary>
            Playing,
        }

        private Button _formTtsPreviewButton;
        private Label _formTtsStatusLabel;
        private Button _formTtsCheckStatusButton;
        private TtsPreviewState _ttsPreviewState = TtsPreviewState.Idle;

        /// <summary>
        /// 直近の案内文言（PR #103 レビュー M5）。<see cref="RenderForm"/> のたびに
        /// <see cref="_formTtsStatusLabel"/> 自体は作り直されるため、表示中の文言はここに保持し、
        /// <see cref="BuildTtsPreviewSection"/> で復元する。
        /// </summary>
        private string _ttsPreviewStatusMessage = string.Empty;

        /// <summary>「読み上げの状態を確認」ボタンを表示するか（同上、M5）。</summary>
        private bool _ttsPreviewShowCheckStatusButton;

        private GameObject _ttsPreviewAudioObject;
        private AudioSource _ttsPreviewAudioSource;
        private TtsResult _ttsPreviewResult;
        private CancellationTokenSource _ttsPreviewCts;
        private IVisualElementScheduledItem _ttsPreviewPollItem;
        private VisualElement _ttsStatusOverlay;

        private void BuildTtsPreviewSection(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("menu-row");

            _formTtsPreviewButton = new Button(OnTtsPreviewButtonClicked)
            {
                text = DescribeTtsPreviewButtonText(),
                name = "question-form-tts-preview-button",
            };
            _formTtsPreviewButton.SetEnabled(_ttsPreviewState != TtsPreviewState.Synthesizing);
            row.Add(_formTtsPreviewButton);

            _formTtsCheckStatusButton = new Button(OnTtsCheckStatusButtonClicked)
            {
                text = "読み上げの状態を確認",
                name = "question-form-tts-check-status-button",
            };
            row.Add(_formTtsCheckStatusButton);
            parent.Add(row);

            _formTtsStatusLabel = new Label(string.Empty) { name = "question-form-tts-status-label" };
            _formTtsStatusLabel.AddToClassList("body-text");
            _formTtsStatusLabel.AddToClassList("host-setup-warning");
            parent.Add(_formTtsStatusLabel);

            // M5: 直近の案内・確認ボタンの表示状態を、作り直された要素へ復元する。
            ApplyTtsPreviewStatusLabel();
            ApplyTtsCheckStatusButtonVisibility();
        }

        private string DescribeTtsPreviewButtonText()
            => _ttsPreviewState switch
            {
                TtsPreviewState.Synthesizing => TtsPreviewSynthesizingButtonText,
                TtsPreviewState.Playing => TtsPreviewPlayingButtonText,
                _ => TtsPreviewIdleButtonText,
            };

        private async void OnTtsPreviewButtonClicked()
        {
            if (_ttsPreviewState == TtsPreviewState.Synthesizing)
            {
                // ボタンは合成中に無効化されるはずだが、念のため二重実行を防ぐ。
                return;
            }

            if (_ttsPreviewState == TtsPreviewState.Playing)
            {
                StopTtsPreview();
                return;
            }

            await StartTtsPreviewAsync();
        }

        private async Task StartTtsPreviewAsync()
        {
            var text = ResolvePreviewText();
            if (string.IsNullOrEmpty(text))
            {
                ShowTtsPreviewStatus("問題文（または読み上げ用テキスト）を入力してから試してください。");
                SetTtsCheckStatusButtonVisible(false);
                return;
            }

            var service = TtsService.Instance;
            if (service == null)
            {
                // Boot シーンを経由していない等、TtsService 自体が存在しない場合のフォールバック。
                // TtsStatusPanel.ResolveReason(null) は「未確認」（null）を返すため、ここだけは専用の文言にする。
                ShowTtsPreviewStatus("読み上げサービスが見つかりません。");
                SetTtsCheckStatusButtonVisible(true);
                return;
            }

            // 同意ゲート（requirements.md FR-74/75）・ユーザーによる無効化・配置不足は #25 のフォールバック UI
            // （TtsStatusPanel.ResolveReason）と同じ判定を再利用する（判定ロジックの単一の出所）。
            var reason = TtsStatusPanel.ResolveReason(service);
            if (reason.HasValue)
            {
                ShowTtsPreviewUnavailable(service, reason.Value);
                return;
            }

            SetTtsCheckStatusButtonVisible(false);
            SetTtsPreviewState(TtsPreviewState.Synthesizing);
            ShowTtsPreviewStatus(string.Empty);

            _ttsPreviewCts = new CancellationTokenSource();
            var token = _ttsPreviewCts.Token;

            try
            {
                // PR #103 レビュー H2 / 再レビュー N1: consentCheck と settingsProvider を明示的に渡す。
                // TtsService.Initialize は最初の1回しか実効を持たないため、ここで渡し忘れると
                // 「問題エディタが最初の初期化者になった場合」、以後の本編の読み上げでも
                // 同意の再確認（撤回後に止める、FR-74/75）や #28 のアプリ設定（AssetPathOverride・tts.*）が
                // 無視されてしまう。settingsProvider は SettingsView / GameView / TitleView と同じ
                // 共通ファクトリ（TtsSettingsProviderFactory）を使う。consentCheck も同様に
                // 共通ファクトリ（TtsConsentCheckFactory、#127）へ寄せ、GameView / TtsStatusPanel と
                // 同じ判定（ConsentGate.HasUserConsented）が渡ることを 1 か所で保証する。
                await service.EnsureInitializedAsync(
                    settingsProvider: TtsSettingsProviderFactory.BuildOrNull(),
                    consentCheck: TtsConsentCheckFactory.Build());

                if (token.IsCancellationRequested)
                {
                    return;
                }

                var reasonAfterInit = TtsStatusPanel.ResolveReason(service);
                if (reasonAfterInit.HasValue || !service.Status.IsReady)
                {
                    var resolvedReason = reasonAfterInit
                        ?? service.UnavailableReason
                        ?? TtsUnavailableReason.InitializationFailed;
                    ShowTtsPreviewUnavailable(service, resolvedReason);
                    SetTtsPreviewState(TtsPreviewState.Idle);
                    return;
                }

                var result = await service.SynthesizeAsync(text, cancellationToken: token);

                if (token.IsCancellationRequested)
                {
                    result?.ReleaseClip();
                    return;
                }

                if (result == null)
                {
                    ShowTtsPreviewStatus("読み上げの合成に失敗しました（詳細はログを参照してください）。");
                    SetTtsPreviewState(TtsPreviewState.Idle);
                    return;
                }

                PlayTtsPreviewResult(result);
            }
            catch (OperationCanceledException)
            {
                // 別の問題への切り替え・画面を閉じたことによる取り消し（StopTtsPreview 経由）。
                // ユーザーの意図した操作なので、エラー表示はしない。
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QuestionEditorView] 読み上げプレビューに失敗しました: {ex.Message}");
                Debug.LogException(ex);
                ShowTtsPreviewStatus("読み上げの再生に失敗しました（詳細はログを参照してください）。");
                SetTtsPreviewState(TtsPreviewState.Idle);
            }
        }

        /// <summary>
        /// 編集中の readingText（空なら text）を返す。両方空なら null。
        /// 前後の空白のみの入力を「未入力」と同じ扱いにするため <see cref="string.Trim"/> する
        /// （<see cref="TsumugiQuiz.Questions.Editing.QuestionFormConverter.ToQuestion"/> が保存時に
        /// 行うトリムと同じ判断基準を、プレビュー時にも揃えるため。PR #103 レビュー L3）。
        /// </summary>
        private string ResolvePreviewText()
        {
            if (_form == null)
            {
                return null;
            }

            var readingText = _form.ReadingText?.Trim();
            if (!string.IsNullOrEmpty(readingText))
            {
                return readingText;
            }

            var text = _form.Text?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private void PlayTtsPreviewResult(TtsResult result)
        {
            _ttsPreviewResult = result;
            EnsureTtsPreviewAudioSource();
            _ttsPreviewAudioSource.clip = result.Clip;
            _ttsPreviewAudioSource.Play();

            SetTtsPreviewState(TtsPreviewState.Playing);
            StartTtsPreviewPolling();
        }

        private void EnsureTtsPreviewAudioSource()
        {
            if (_ttsPreviewAudioObject != null)
            {
                return;
            }

            _ttsPreviewAudioObject = new GameObject("QuestionEditorTtsPreviewAudioSource");
            _ttsPreviewAudioSource = _ttsPreviewAudioObject.AddComponent<AudioSource>();
            _ttsPreviewAudioSource.playOnAwake = false;
        }

        private void StartTtsPreviewPolling()
        {
            StopTtsPreviewPolling();

            if (_questionEditFormContainer == null)
            {
                return;
            }

            _ttsPreviewPollItem = _questionEditFormContainer.schedule
                .Execute(CheckTtsPreviewPlaybackFinished)
                .Every(TtsPreviewPollIntervalMs);
        }

        private void StopTtsPreviewPolling()
        {
            _ttsPreviewPollItem?.Pause();
            _ttsPreviewPollItem = null;
        }

        /// <summary><see cref="AudioSource"/> は完了イベントを持たないため、ポーリングで自然終了を検知する。</summary>
        private void CheckTtsPreviewPlaybackFinished(TimerState timerState)
        {
            if (_ttsPreviewState != TtsPreviewState.Playing)
            {
                StopTtsPreviewPolling();
                return;
            }

            if (_ttsPreviewAudioSource != null && _ttsPreviewAudioSource.isPlaying)
            {
                return;
            }

            // 自然終了。合成の取り消し用トークンはもう使わないので、他の状態遷移経路（StopTtsPreview）と
            // 同じく null に戻しておく（PR #103 レビュー L4。次回の開始時にどのみち新しく作り直すが、
            // 古い参照を残さないことでフィールドの意味を「進行中の合成があるかどうか」に保つ）。
            _ttsPreviewCts = null;

            ReleaseTtsPreviewResult();
            StopTtsPreviewPolling();
            SetTtsPreviewState(TtsPreviewState.Idle);
        }

        /// <summary>
        /// 進行中の合成を取り消し、再生中なら停止し、確保していたクリップを解放する（issue #32）。
        /// 「停止」ボタン、別の問題への切り替え（<see cref="LoadFormFromSelection"/>）、
        /// 画面を閉じるとき（<see cref="DisposeTtsPreview"/>）のいずれからも呼ぶ。
        /// </summary>
        private void StopTtsPreview()
        {
            // Cancel() のみで Dispose() はしない。バックグラウンドで進行中の合成
            // （TtsService 内部の CreateLinkedTokenSource・Register）とレースすると
            // ObjectDisposedException になりうるため（WaitHandle には触れないので Dispose 自体は必須ではない）。
            _ttsPreviewCts?.Cancel();
            _ttsPreviewCts = null;

            StopTtsPreviewPolling();

            if (_ttsPreviewAudioSource != null && _ttsPreviewAudioSource.isPlaying)
            {
                _ttsPreviewAudioSource.Stop();
            }

            ReleaseTtsPreviewResult();
            SetTtsPreviewState(TtsPreviewState.Idle);
        }

        private void ReleaseTtsPreviewResult()
        {
            _ttsPreviewResult?.ReleaseClip();
            _ttsPreviewResult = null;

            if (_ttsPreviewAudioSource != null)
            {
                _ttsPreviewAudioSource.clip = null;
            }
        }

        /// <summary>
        /// 画面を閉じるとき（<see cref="QuestionEditorView.UnwireFormHandlers"/>）に呼ぶ。
        /// 再生・合成を止め、開いていた <see cref="TtsStatusPanel"/> オーバーレイを閉じたうえで、
        /// プレビュー専用に生成した <see cref="AudioSource"/> の <see cref="GameObject"/> も破棄する。
        /// </summary>
        private void DisposeTtsPreview()
        {
            StopTtsPreview();

            // PR #103 再レビュー N2: 画面を閉じるときはフォーム内の案内を作り直す意味がないため、
            // リフレッシュしない版を使う（このあと ClearFormElementReferences で要素自体が破棄される）。
            CloseTtsStatusOverlay(refresh: false);

            if (_ttsPreviewAudioObject != null)
            {
                UnityEngine.Object.Destroy(_ttsPreviewAudioObject);
                _ttsPreviewAudioObject = null;
                _ttsPreviewAudioSource = null;
            }
        }

        private void SetTtsPreviewState(TtsPreviewState state)
        {
            _ttsPreviewState = state;

            if (_formTtsPreviewButton == null)
            {
                return;
            }

            _formTtsPreviewButton.text = DescribeTtsPreviewButtonText();
            _formTtsPreviewButton.SetEnabled(state != TtsPreviewState.Synthesizing);
        }

        /// <summary>フォーム内の短い案内文言を表示する（不可なら空にして非表示にする）。</summary>
        private void ShowTtsPreviewStatus(string message)
        {
            _ttsPreviewStatusMessage = message ?? string.Empty;
            ApplyTtsPreviewStatusLabel();
        }

        private void ApplyTtsPreviewStatusLabel()
        {
            if (_formTtsStatusLabel == null)
            {
                return;
            }

            _formTtsStatusLabel.text = _ttsPreviewStatusMessage;
            _formTtsStatusLabel.style.display =
                string.IsNullOrEmpty(_ttsPreviewStatusMessage) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void SetTtsCheckStatusButtonVisible(bool visible)
        {
            _ttsPreviewShowCheckStatusButton = visible;
            ApplyTtsCheckStatusButtonVisibility();
        }

        private void ApplyTtsCheckStatusButtonVisibility()
        {
            if (_formTtsCheckStatusButton == null)
            {
                return;
            }

            _formTtsCheckStatusButton.style.display =
                _ttsPreviewShowCheckStatusButton ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// 利用できない理由を、短い見出しだけフォームへ表示し、「読み上げの状態を確認」ボタンを出す
        /// （PR #103 レビュー M1）。「配置手順を表示」「再試行」等の実際の導線は
        /// <see cref="OnTtsCheckStatusButtonClicked"/> が開く <see cref="TtsStatusPanel"/> 側にのみ置く。
        /// </summary>
        private void ShowTtsPreviewUnavailable(TtsService service, TtsUnavailableReason reason)
        {
            var message = TtsStatusMessages.For(reason, service?.UnavailableDetail);
            ShowTtsPreviewStatus(message.Headline);
            SetTtsCheckStatusButtonVisible(true);
        }

        private void OnTtsCheckStatusButtonClicked()
        {
            if (_root == null || _ttsStatusOverlay != null)
            {
                return;
            }

            // PR #103 再レビュー N1: SettingsView / GameView / TitleView と同じ共通ファクトリで
            // 保存済みアプリ設定（tts.*）を渡す。
            _ttsStatusOverlay = TtsStatusPanel.Create(
                TtsService.Instance, _router, onClosed: CloseTtsStatusOverlay,
                settingsProvider: TtsSettingsProviderFactory.BuildOrNull());

            // TitleView.OnTtsStatusButtonClicked と同じ作法: Document.rootVisualElement 直下
            // （= このインスタンスの親）に追加することで、画面いっぱいに覆う。
            var attachRoot = _root.parent ?? _root;
            attachRoot.Add(_ttsStatusOverlay);
        }

        /// <summary>「閉じる」等の通常の操作から呼ぶ（フォーム側の案内も最新の状態に合わせて更新する）。</summary>
        private void CloseTtsStatusOverlay() => CloseTtsStatusOverlay(refresh: true);

        /// <summary>
        /// オーバーレイを閉じる。<paramref name="refresh"/> が true なら、閉じた後（再試行で復旧した
        /// 可能性がある）フォーム側の短い案内も最新の状態に合わせる。画面を閉じるとき
        /// （<see cref="DisposeTtsPreview"/>）は、直後に要素自体が破棄されるため false を渡す
        /// （PR #103 再レビュー N2）。
        /// </summary>
        private void CloseTtsStatusOverlay(bool refresh)
        {
            if (_ttsStatusOverlay == null)
            {
                return;
            }

            _ttsStatusOverlay.RemoveFromHierarchy();
            _ttsStatusOverlay = null;

            if (refresh)
            {
                RefreshTtsPreviewAvailability();
            }
        }

        private void RefreshTtsPreviewAvailability()
        {
            var service = TtsService.Instance;
            var reason = TtsStatusPanel.ResolveReason(service);
            if (reason.HasValue)
            {
                ShowTtsPreviewUnavailable(service, reason.Value);
            }
            else
            {
                ShowTtsPreviewStatus(string.Empty);
                SetTtsCheckStatusButtonVisible(false);
            }
        }
    }
}
