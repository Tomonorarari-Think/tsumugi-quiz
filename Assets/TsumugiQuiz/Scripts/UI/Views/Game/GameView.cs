using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// Game View のコントローラ（issue #14、docs/architecture.md §2「Game」、
    /// docs/network.md §1.2「実装済みの <c>NetworkVariable</c>」/§1.3「実装済みの RPC」/§6「早押し判定」）。
    /// <see cref="TsumugiQuiz.Network.GameSession"/>（#12/#13/#18）の <c>NetworkVariable</c> と
    /// イベント（<see cref="TsumugiQuiz.Network.GameSession.QuestionShown"/> 等）を購読して画面を更新する。
    /// 表示状態の計算（フェーズ文言・残り時間・ボタン活性・結果文言）は Unity API に依存しない
    /// <see cref="GameViewPresenter"/> に切り出してある（EditMode でテスト）。
    /// 責務ごとに分割（M-9: 1 ファイル 400 行以内）:
    /// 本ファイルは OnShow/OnHide・セッション取得・入力ハンドラ、
    /// <c>GameView.UiBinding.cs</c> は UI 要素の取得・イベント購読の配線と単純な表示更新、
    /// <c>GameView.Network.cs</c> は <c>GameSession</c> / <c>NetworkService</c> のイベント購読とハンドラ、
    /// <c>GameView.Input.cs</c> は早押しキー（Input System、既定 Space、K21）を扱う。
    /// </summary>
    /// <remarks>
    /// ロビー（#7）から Game への UI 遷移導線は本 issue の範囲外。
    /// <c>NetworkBootstrap.Instance.Service.FindActiveGameSession()</c>（docs/network.md §1.2）で
    /// 見つかった <see cref="TsumugiQuiz.Network.GameSession"/> を購読するだけで、
    /// Lobby → Game の暫定導線は付けない
    /// （PlayMode テストが <c>ViewRouter.ShowView(ViewNames.Game)</c> を直接呼んで駆動する）。
    /// 表示直後にまだ <see cref="TsumugiQuiz.Network.GameSession"/> が見つからない場合（レビュー M-9。
    /// 例えば Lobby → Game の遷移直後で NGO の同期がまだ届いていない場合）は、
    /// 見つかるまで <see cref="Tick"/> で再探索を続ける。
    /// </remarks>
    public sealed partial class GameView : IView
    {
        private ViewRouter _router;
        private VisualElement _root;
        private NetworkService _networkService;
        private GameSession _session;

        private Label _questionIndexLabel;
        private Label _phaseLabel;
        private Label _pausedBadgeLabel;
        private Label _questionTextLabel;
        private VisualElement _timeBarFill;
        private Label _timeRemainingLabel;

        /// <summary>早押しセクション（#132 レビュー H-1 で選択式・司会モードの両方から隠す対象）。</summary>
        private VisualElement _buzzSection;
        private Button _buzzButton;
        private Label _buzzResultLabel;

        private VisualElement _answerSection;
        private TextField _answerField;
        private Label _answerErrorLabel;
        private Button _answerSubmitButton;

        private VisualElement _choiceSection;
        private VisualElement _resultSection;
        private VisualElement _choiceButtonsContainer;

        private Label _resultLabel;
        private Label _scoreLabel;

        private VisualElement _hostControls;
        private Button _nextButton;
        private Button _exitButton;

        private VisualElement _exitConfirmContainer;
        private Label _exitConfirmLabel;
        private Button _confirmExitButton;
        private Button _cancelExitButton;

        private IVisualElementScheduledItem _tickScheduled;

        /// <summary>
        /// セッションの再探索が続いている間の文言（<see cref="TryAcquireSession"/>）。
        /// </summary>
        private const string SessionSearchingMessage = "セッションを探しています…";

        /// <summary>
        /// <see cref="Tick"/> が <see cref="NetworkService.IsDisposed"/> を検出したときの文言（#116）。
        /// 本番では <c>NetworkBootstrap.OnDestroy</c>（アプリ終了）でしか到達しないため、
        /// ここから Title 等への自動遷移はしない。<c>internal</c> にしてあるのは、テスト
        /// （<c>GameViewDisposedServiceTests</c>）がリテラルを二重管理せずこの定数を直接参照するため
        /// （#116 L-5、<c>InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")</c> 済み）。
        /// </summary>
        internal const string ConnectionEndedMessage = "接続が終了しました";

        /// <summary>この受付で既に押下を送ったか（ローカルの連打防止錠、docs/network.md §6.2）。</summary>
        private bool _hasBuzzedLocally;

        /// <summary>
        /// いま画面に出している問題のインデックス（<see cref="_currentQuestion"/> と対になる）。
        /// 何も出していなければ -1。
        /// </summary>
        /// <remarks>
        /// PR #104 レビュー L-2: 取りこぼし復元（<see cref="TryRestoreMissedQuestion"/>）の入口条件を
        /// 「<see cref="_currentQuestion"/> が null か」ではなく「表示中の問題が
        /// <c>GameSession.QuestionIndex</c> と一致しているか」で見るための記録。
        /// 前者だと 2 問目以降の <c>QuestionShown</c> を取りこぼしたときに前問の問題文が残る。
        /// 同じ問題に対して <see cref="RefreshFromCurrentState"/> を何度も走らせない役目も兼ねる
        /// （PR #104 レビュー M-C）。
        /// </remarks>
        private int _displayedQuestionIndex = -1;

        /// <summary>
        /// 現在提示されている問題（issue #17。選択式の選択肢・出題形式の判定に使う）。
        /// <see cref="HandleQuestionShown"/> で設定し、次の問題が来るまで保持する。
        /// </summary>
        private QuestionDto _currentQuestion;

        /// <summary>
        /// 直近のお手つき（誤答後の受付再開放、#18）で自分が対象外にされているか（レビュー H-5）。
        /// <see cref="HandleQuestionShown"/>（次の問題）でクリアされるまで、この問題の間は再度早押しできない。
        /// </summary>
        private bool _isExcludedFromBuzzing;

        /// <summary>
        /// クライアント ID からプレイヤー名を解決する関数。既定では <see cref="ResolvePlayerName"/>
        /// （#7 の <c>LobbyState.GetPlayersSnapshot()</c>）を使う。null、または解決できない
        /// クライアント ID を渡された場合は <see cref="GameViewPresenter.FormatPlayerFallbackName"/>
        /// にフォールバックする。外部から差し替えたい場合のためのフックとして公開のまま残す。
        /// </summary>
        public Func<ulong, string> PlayerNameResolver { get; set; }

        /// <summary>
        /// <see cref="Tick"/> がまだスケジュールされているか（#101 の PlayMode テスト用）。
        /// <see cref="StopTick"/> 後・<see cref="OnHide"/> 後は false になる。
        /// </summary>
        internal bool IsTickScheduled => _tickScheduled != null && _tickScheduled.isActive;

        /// <summary>自分がホスト（サーバー兼クライアント）か。</summary>
        private bool IsLocalHost => _session != null && _session.NetworkManager != null && _session.NetworkManager.IsHost;

        /// <summary>
        /// 自分のクライアント ID を取得する（レビュー L-14）。セッション・<c>NetworkManager</c> が
        /// 無く判定できない場合は false を返す。<c>ulong.MaxValue</c> 等の番兵値は使わない
        /// （実際のクライアント ID と衝突しうるため。0 は <c>NetworkManager.ServerClientId</c> と衝突する）。
        /// </summary>
        /// <param name="clientId">取得できたクライアント ID。取得できなければ既定値。</param>
        /// <returns>取得できたら true。</returns>
        private bool TryGetLocalClientId(out ulong clientId)
        {
            if (_session != null && _session.NetworkManager != null)
            {
                clientId = _session.NetworkManager.LocalClientId;
                return true;
            }

            clientId = default;
            return false;
        }

        /// <summary><see cref="TryGetLocalClientId"/> を <c>ulong?</c> で返す（Presenter への受け渡し用）。</summary>
        private ulong? LocalClientIdOrNull => TryGetLocalClientId(out var clientId) ? clientId : (ulong?)null;

        public void OnShow(ViewContext context)
        {
            _router = context.Router;
            _root = context.Root;

            if (!BindElements(_root))
            {
                Debug.LogError("[GameView] 必要な UI 要素が見つかりません。game-view.uxml を確認してください。");
                return;
            }

            ResetUi();
            InitializeQuestionReveal(_root);
            InitializeQuestionImage(_root);
            InitializeScroll(_root);
            InitializeParticipantPanel(_root); // #194
            _displayedQuestionIndex = -1;
            RegisterUiHandlers();
            EnableBuzzInput();
            InitializeCharacterView();

            _networkService = NetworkBootstrap.Instance != null ? NetworkBootstrap.Instance.Service : null;
            if (_networkService != null)
            {
                _networkService.DisconnectedFromHost += HandleDisconnectedFromHost;
            }

            // #7 のロビー名簿と接続する（既に外部から差し替えられていれば尊重する）。
            PlayerNameResolver ??= ResolvePlayerName;

            TryAcquireSession();

            // レビュー M-9: 表示直後にまだ GameSession が見つからない場合、Tick で再探索を続ける。
            _tickScheduled = _root.schedule.Execute(Tick).Every(GameViewPresenter.TimeDisplayIntervalMs);
        }

        public void OnHide()
        {
            StopTick();

            DisableBuzzInput();
            UnsubscribeSessionEvents();
            TeardownQuestionReveal();
            TeardownQuestionImage();
            TeardownScroll();
            TeardownParticipantPanel(); // #194
            TeardownModeratorMode();
            TeardownCharacterView();

            if (_networkService != null)
            {
                _networkService.DisconnectedFromHost -= HandleDisconnectedFromHost;
            }

            UnregisterUiHandlers();

            _router = null;
            _root = null;
            _networkService = null;
            _session = null;

            _questionIndexLabel = null;
            _phaseLabel = null;
            _pausedBadgeLabel = null;
            _questionTextLabel = null;
            _timeBarFill = null;
            _timeRemainingLabel = null;
            _buzzSection = null;
            _buzzButton = null;
            _buzzResultLabel = null;
            _answerSection = null;
            _answerField = null;
            _answerErrorLabel = null;
            _answerSubmitButton = null;

            ClearChoiceButtons();
            _choiceSection = null;
            _resultSection = null;
            _choiceButtonsContainer = null;
            _currentQuestion = null;
            _displayedQuestionIndex = -1;

            _resultLabel = null;
            _scoreLabel = null;
            _hostControls = null;
            _nextButton = null;
            _exitButton = null;

            _exitConfirmContainer = null;
            _exitConfirmLabel = null;
            _confirmExitButton = null;
            _cancelExitButton = null;
        }

        /// <summary>
        /// <see cref="_networkService"/> から <see cref="GameSession"/> を取得できるか試す（レビュー M-9）。
        /// 取得できたら購読し、現在値から表示を復元する。
        /// </summary>
        private void TryAcquireSession()
        {
            if (_session != null)
            {
                return;
            }

            // FindActiveGameSession は破棄済みの NetworkService でも例外を投げず null を返す（#101）。
            var found = _networkService?.FindActiveGameSession();
            if (found == null)
            {
                UpdatePhaseText(SessionSearchingMessage);
                return;
            }

            _session = found;
            SubscribeSessionEvents();
            RefreshFromCurrentState();
            SetupModeratorMode();
            BindParticipantPanelToSession(); // #194（司会かどうかが決まった後に読む）
            BindCharacterViewToSession();
        }

        /// <summary>
        /// 既にゲームが進行中の状態で View が表示された場合に、現在値から表示を復元する。
        /// 通常は Game View 表示前に出題が始まらないため使われないが、
        /// 途中から見ても壊れた表示にならないための保険（本 issue の受け入れ条件そのものではない）。
        /// </summary>
        private void RefreshFromCurrentState()
        {
            UpdatePhaseText(GameViewPresenter.PhaseLabel(_session.Phase.Value));
            UpdateBuzzButtonEnabled();
            UpdateAnswerSectionVisible();
            UpdateHostControlsVisible();
            UpdatePausedBadge();
            UpdateScoreLabel(TryGetLocalClientId(out var localClientId) ? _session.GetScore(localClientId) : 0);

            var questionIndex = _session.QuestionIndex.Value;
            if (questionIndex >= 0 && _session.Distributor != null
                && _session.Distributor.TryGetQuestion(questionIndex, out var question))
            {
                _questionIndexLabel.text = $"問題 {questionIndex + 1}";
                // issue #144 レビュー H-1: 判定前なら通常の出題と同じく文字送りし、取りこぼした再生開始時刻にも
                // 追いつく（受付前に全文を出さない）。判定後は全文（GameView.Reveal.cs）。
                RestoreQuestionReveal(questionIndex, question);
                _currentQuestion = question;
                _displayedQuestionIndex = questionIndex;
                ShowQuestionImageFor(questionIndex);
                HandleChoiceQuestionShown(question, questionIndex);
            }

            UpdateQuestionSectionsVisible();

            // #187 レビュー L1: View の復元でも、現在のフェーズの操作（早押し・回答欄・選択肢・判定結果）を表示範囲へ入れる。
            ScrollForPhase(_session.Phase.Value);
        }

        /// <summary>
        /// <see cref="_tickScheduled"/> から一定間隔で呼ばれる（レビュー M-9 / L-15）。
        /// セッションがまだ見つかっていなければ再探索し、見つかっていれば残り時間表示を更新する。
        /// </summary>
        /// <remarks>
        /// 進行中なのに問題が手元に無い場合は、配信済みの DTO から表示を復元し直す（#95）。
        /// ロビーから Game View へ切り替わるのと問題配信（<see cref="GameSession.QuestionShown"/>）の
        /// 到着が同じフレームに重なると、購読より先にイベントが飛んで問題文が空のままになりうるため
        /// （実機の 2 プロセス確認で、クライアント側に再現した）。
        /// 復元は<b>DTO が実際に届いてから、同じ問題につき 1 回だけ</b>行う（PR #104 レビュー M-C）。
        /// 途中参加・再接続したクライアントにも、サーバー（<c>LobbyState</c> → <c>GameSession.ResyncClient</c>）が
        /// 接続完了時に現在問の DTO を送り直すので、ここで復元できる（#109。PR #104 レビュー L-4）。
        /// </remarks>
        private void Tick()
        {
            // NetworkService が破棄された後（アプリ終了・Boot の NetworkBootstrap ごと破棄される
            // シーンアンロード）は、セッションが手に入ることは二度とない。再探索を続けても無駄なうえ、
            // 破棄済みインスタンスを毎フレーム触り続けることになるので、ここで購読ごと止める（#101）。
            // 停止前に文言も「セッションを探しています…」から差し替える（#116）。本番でこの経路に
            // 入るのは NetworkBootstrap.OnDestroy（アプリ終了）のときだけなので、Title 等への画面
            // 遷移はしない（表示が固まったままアプリごと終了する）。
            if (_networkService != null && _networkService.IsDisposed)
            {
                UpdatePhaseText(ConnectionEndedMessage);
                StopTick();
                return;
            }

            if (_session == null)
            {
                TryAcquireSession();
                if (_session == null)
                {
                    return;
                }
            }

            TryRestoreMissedQuestion();
            UpdateTimeDisplay();
            FlushParticipantPanel(); // #194: 変更があったときだけ描き直す
        }

        /// <summary>
        /// 取りこぼした問題提示（<see cref="GameSession.QuestionShown"/>）を配信済み DTO から復元する。
        /// </summary>
        private void TryRestoreMissedQuestion()
        {
            if (_session.Phase.Value == QuizPhase.Lobby)
            {
                return;
            }

            // PR #104 レビュー L-2: 「何も出していないか」ではなく「出している問題が現在問と違うか」で見る。
            var questionIndex = _session.QuestionIndex.Value;
            if (questionIndex < 0 || questionIndex == _displayedQuestionIndex)
            {
                return;
            }

            var distributor = _session.Distributor;
            if (distributor == null || !distributor.TryGetQuestion(questionIndex, out _))
            {
                // まだ配信が届いていない（または届かない）。次の tick で改めて見る。
                return;
            }

            // RefreshFromCurrentState が _displayedQuestionIndex を更新するので、
            // 同じ問題で復元を繰り返すことはない（PR #104 レビュー M-C）。
            RefreshFromCurrentState();
        }

        /// <summary>
        /// <see cref="Tick"/> のスケジュール（<see cref="_tickScheduled"/>）を止める（#101）。
        /// <see cref="IVisualElementScheduledItem.Pause"/> は「スケジューラから取り除く」操作なので、
        /// 呼んだ時点以降このコールバックは走らない。<see cref="OnHide"/>（<c>ViewRouter</c> の
        /// 画面切り替え・<c>OnDestroy</c> 経由を含む）と、<see cref="NetworkService"/> の破棄検出の
        /// 両方から呼ぶため、何度呼んでも安全にしてある。
        /// </summary>
        private void StopTick()
        {
            _tickScheduled?.Pause();
            _tickScheduled = null;
        }

        private void OnBuzzButtonClicked() => TryBuzz();

        private void TryBuzz()
        {
            if (_isModerator)
            {
                // 司会専用モードでは早押ししない（H3）。サーバー側でも BuzzRpc を拒否する。
                return;
            }

            if (_session == null
                || !GameViewPresenter.IsBuzzButtonEnabled(_session.Phase.Value, _hasBuzzedLocally, IsExcludedFromBuzzing))
            {
                return;
            }

            if (_session.RequestBuzz())
            {
                // 体感のため、勝者確定を待たず押した本人には即座に音を鳴らす（docs/network.md §6.2）。
                _hasBuzzedLocally = true;
                UpdateBuzzButtonEnabled();
                SePlayer.Instance?.Play(SeKind.Buzz);
            }
        }

        private void OnAnswerSubmitClicked() => TrySubmitAnswer();

        private void OnAnswerFieldKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                TrySubmitAnswer();
            }
        }

        private void TrySubmitAnswer()
        {
            if (_session == null || _answerField == null)
            {
                return;
            }

            // レビュー H-3: 送信前にローカルで検証し、RequestAnswer が拒否する理由（空／文字数超過）を
            // サーバーへ問い合わせずに案内する。
            var inputError = GameViewPresenter.AnswerInputError(_answerField.value);
            if (!string.IsNullOrEmpty(inputError))
            {
                ShowAnswerError(inputError);
                return;
            }

            if (_session.RequestAnswer(_answerField.value))
            {
                ShowAnswerError(string.Empty);

                // 設定値（answer.singleAttemptOnly）に関わらず常に 1 回のみ（#221、false の実装は #26）: 送信後は再送させない。
                _answerField.SetEnabled(false);
                _answerSubmitButton.SetEnabled(false);
            }
            else
            {
                // 入力自体は妥当だが（フェーズが変わった等で）サーバーに拒否された。
                ShowAnswerError("回答を送信できませんでした。回答権が失われていないか確認してください。");
            }
        }

        private void OnNextButtonClicked()
        {
            // #19 で複数問進行に対応した GameSession.NextQuestion() を呼ぶ。次の問題があれば
            // Result → Reading へ進み、無ければ内部で FinishSession() まで進めて Finished にする。
            // Finished への遷移・Result View への画面切り替えは HandlePhaseChanged（全ピア共通）が行う。
            _session?.NextQuestion();
        }

        /// <summary>
        /// 「退出」クリック時はいきなり切断せず、HostSetupView の戻る確認と同じ作法で
        /// インライン確認パネルを出す（統括判断: ゲーム中の離脱は誤操作の影響が大きいため）。
        /// ホストが退出するとゲームが終了して全員切断されるため、ホストと非ホストで文言を変える。
        /// </summary>
        private void OnExitButtonClicked()
        {
            _exitConfirmLabel.text = GameViewPresenter.ExitConfirmMessage(IsLocalHost);
            SetExitConfirmVisible(true);
        }

        private void OnConfirmExitClicked()
        {
            SetExitConfirmVisible(false);
            _networkService?.Stop();
            _router?.ShowView(ViewNames.Title);
        }

        private void OnCancelExitClicked()
        {
            SetExitConfirmVisible(false);
        }
    }
}
