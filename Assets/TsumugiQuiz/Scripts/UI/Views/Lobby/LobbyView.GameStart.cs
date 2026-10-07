using System;
using System.Collections;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Questions.Images;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// <see cref="LobbyView"/> のうち「ゲーム開始」の配線（issue #95、docs/network.md §12.2）。
    /// ホストが押した時点で問題フォルダを読み直し、ルーム設定（<see cref="RoomSettingsSync.Current"/>）から
    /// 出題の材料を組み立てて <see cref="GameSession.Configure"/> → <see cref="GameSession.StartSession"/> を呼ぶ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>なぜロビーで読み込むのか</b>: 問題フォルダはゲーム中いつでも編集されうるので、
    /// 「ゲーム開始」を押した時点の内容で出題列を固定する（<see cref="GameSession.Configure"/> は
    /// 供給元を確定させ、以後 1 ゲーム中は変化しない）。ホスト設定画面（#29 の
    /// <see cref="QuestionLibrary"/>）はフォルダの作成・サンプル配置・読み込み状況の表示を担当し、
    /// 出題の供給元はこちらで改めて <see cref="QuestionRepository"/> から読む（監視スレッドを
    /// ロビーに持ち込まないため）。
    /// </para>
    /// <para>
    /// <b>サーバー権威</b>: <see cref="GameSession.Configure"/> / <see cref="GameSession.SetQuestionSets"/> /
    /// <see cref="GameSession.StartSession"/> はいずれもサーバー（ホスト）専用。クライアントの
    /// 「ゲーム開始」ボタンは <see cref="LobbyView.OnShow"/> で非表示にしてある。
    /// </para>
    /// <para>
    /// <b>画面遷移</b>: Game View への遷移は <see cref="GameSession.Phase"/> の変化
    /// （<see cref="HandleSessionPhaseChanged"/>）で行う。ホストだけでなくクライアントも同じ合図で
    /// 追従するので、ロビーに居る全員が出題開始とともに Game View へ移る
    /// （<c>ResultView.HandlePhaseChanged</c> と同じ作法）。判定は次の 2 段構えにしている。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>変化の通知</b>（<c>OnValueChanged</c>）: <see cref="QuizPhase.Lobby"/> 以外なら遷移する。
    ///     進行中に合流したクライアントが結果表示（<see cref="QuizPhase.Result"/>）の途中で
    ///     追いついた場合も Game View → Result View（#20）とたどれるようにするため
    ///   </description></item>
    ///   <item><description>
    ///     <b>購読直後の 1 回だけの現在値判定</b>: <see cref="IsQuestionInProgress"/>（問題が進行中の
    ///     フェーズ）に限る。<see cref="QuizPhase.Result"/> / <see cref="QuizPhase.Finished"/> を
    ///     含めてしまうと、「ロビーへ戻る」直後のクライアントが Lobby → Game へ跳ね返される
    ///     （<c>ReturnToLobby</c> の <c>NetworkVariable</c> 更新と RPC の到着順は保証されないため、
    ///     ロビー表示の時点でまだ <see cref="QuizPhase.Result"/> のことがある。PR #104 レビュー H-A）
    ///   </description></item>
    ///   <item><description>
    ///     <b>合流の合図</b>（<see cref="GameSession.SessionResynced"/> /
    ///     <see cref="GameSession.TryConsumePendingResync"/>、#117）: 結果表示中
    ///     （<see cref="QuizPhase.Result"/>）・全問終了後（<see cref="QuizPhase.Finished"/>）に
    ///     合流した場合は、サーバーが名指しで送ってきたこの合図を根拠に
    ///     Game View / Result View へ移る（<see cref="ApplyResyncedState"/>）。
    ///     「ロビーへ戻る」では飛ばない合図なので、上記の跳ね返りを再発させない
    ///   </description></item>
    /// </list>
    /// </remarks>
    public sealed partial class LobbyView
    {
        private const string SessionStartFailedMessage =
            "出題を開始できませんでした。問題データとルーム設定を確認してください。";

        private const string QuestionFolderUnavailableMessage =
            "問題フォルダの場所を特定できませんでした。ホスト設定画面で問題フォルダを確認してください。";

        /// <summary>出題開始 / 追従のために購読している <see cref="GameSession"/>。</summary>
        private GameSession _gameSession;

        /// <summary><see cref="GameSession"/> が同期されるのを待って購読するコルーチン。</summary>
        private Coroutine _gameSessionCoroutine;

        private void OnStartGameClicked()
        {
            if (!_isHost || _networkService == null)
            {
                return;
            }

            ShowStatus(string.Empty);

            if (!TryStartSession(out var failureMessage))
            {
                ShowStatus(failureMessage);
                return;
            }

            // 通常は StartSession 中の Phase 同期（HandleSessionPhaseChanged）で既に Game View へ
            // 遷移しており、その時点で OnHide が走って _router は null になっている。
            // ここは Phase の通知が届かなかった場合の保険（二重遷移はこのガードで防ぐ）。
            _router?.ShowView(ViewNames.Game);
        }

        /// <summary>
        /// 問題フォルダを読み込み、ルーム設定から出題の材料を組み立ててセッションを開始する（ホストのみ）。
        /// </summary>
        /// <param name="failureMessage">失敗したときのユーザー向け文言。</param>
        /// <returns>セッションを開始できたら true。</returns>
        private bool TryStartSession(out string failureMessage)
        {
            var session = _networkService.ActiveGameSession;
            if (session == null)
            {
                Debug.LogWarning("[LobbyView] GameSession がスポーンされていないため出題を開始できません。");
                failureMessage = GameSessionUnavailableMessage;
                return false;
            }

            if (!TryLoadQuestions(out var result, out var folderPath, out failureMessage))
            {
                return false;
            }

            GameStartPlan plan;
            SessionSettings settings;
            try
            {
                // #27 の契約（docs/network.md §12.2）: 進行に使う設定は RoomSettingsSync.Current が出所。
                // ここでクランプ済みの値を Configure へ渡すので、CommitRoomSettingsForStart の
                // 差分警告（WarnIfCommittedSettingsDiffer）とは二重にならない。
                settings = ResolveSessionSettings(session);
                plan = GameStartPlanner.Plan(result, settings.Questions, folderPath);
                LogPlanWarnings(plan);

                if (!plan.CanStart)
                {
                    Debug.LogWarning($"[LobbyView] 出題を開始できません（{plan.Blocker}）: {plan.FailureMessage}");
                    failureMessage = plan.FailureMessage;
                    return false;
                }

                session.Configure(plan.PoolSource, settings.TimeLimits, scoring: settings.Scoring);
                session.SetQuestionSets(plan.Sets);

                // issue #185: 画像付きの問題（imagePath）を配信するため、問題フォルダを基準に画像を解決する
                // 供給元を渡す（Configure の後に呼ぶ契約、GameSession.Images.cs）。imagePath は問題セット
                // ファイルからの相対パスで、問題ファイルは問題フォルダ直下 1 階層のみ（docs/question-data.md §4）
                // なので、基準は読み込んだ問題フォルダそのものになる。配信器は GameSession と同じ
                // NetworkObject に載っていて設定を保持し続けるため、ResultView の「もう一度（同じ設定で）」
                // （StartSession を呼び直すだけ）でも同じ供給元が使われる。
                // 設定できなくても（配信器が無い構成）出題は止めない。理由は ConfigureImages が警告に残す。
                session.ConfigureImages(new QuestionImageSource(folderPath));
            }
            catch (Exception ex)
            {
                // ルーム設定の解決・出題材料の組み立て・Configure のいずれかで想定外の例外が出た場合
                // （PR #104 レビュー M-2）。握りつぶさず、画面には開始できなかったことだけを出す。
                Debug.LogError($"[LobbyView] 出題の準備に失敗しました: {ex}");
                failureMessage = SessionStartFailedMessage;
                return false;
            }

            Debug.Log(
                $"[LobbyView] 出題を開始します（問題セット {plan.Sets.Count} 件 / 読み込み {plan.PoolCount} 問 / "
                + $"絞り込み後 {plan.CandidateCount} 問 / questions.count = {settings.Questions.Count}）。");

            // 引数なしで呼び、「引数 > RoomSettingsSync.Current > 前回 > 既定」の解決を
            // GameSession 側（唯一の出所）に任せる（docs/network.md §12.2）。
            if (!session.StartSession())
            {
                failureMessage = SessionStartFailedMessage;
                return false;
            }

            failureMessage = null;
            return true;
        }

        /// <summary>問題フォルダを読み込む。フォルダの場所すら決められない場合だけ false を返す。</summary>
        private static bool TryLoadQuestions(
            out QuestionRepositoryResult result, out string folderPath, out string failureMessage)
        {
            result = null;
            folderPath = null;

            try
            {
                folderPath = QuestionRepository.GetDefaultQuestionsFolderPath();
                result = new QuestionRepository(folderPath).LoadAll();
            }
            catch (Exception ex)
            {
                // LoadAll はフォルダ不在・列挙失敗を FolderErrors に畳んで返すので、ここに来るのは
                // Documents フォルダ自体を解決できない等のまれなケース（DocumentsPaths.Root）。
                Debug.LogError($"[LobbyView] 問題フォルダを読み込めませんでした: {ex}");
                failureMessage = QuestionFolderUnavailableMessage;
                return false;
            }

            failureMessage = null;
            return true;
        }

        /// <summary>
        /// 進行に使うルーム設定を解決する。
        /// </summary>
        /// <remarks>
        /// <see cref="RoomSettingsSync"/> が無い構成（コンポーネント順の検証用プレハブ等）では、
        /// <see cref="GameSession.StartSession"/> 側の解決順（引数 &gt; <c>RoomSettingsSync.Current</c> &gt;
        /// 前回の進行設定 &gt; 既定値、docs/network.md §12.2）と食い違わないよう、
        /// 同じ順で「前回の進行設定 → 既定値」にフォールバックする（PR #104 レビュー L-4）。
        /// </remarks>
        private static SessionSettings ResolveSessionSettings(GameSession session)
        {
            var sync = session.SettingsSync;
            if (sync != null)
            {
                return sync.Current.Session;
            }

            return session.ActiveSessionSettings ?? RoomSettings.Default.Session;
        }

        /// <summary>
        /// 開始は妨げないが記録しておきたい注意点をログへ残す（握りつぶさない）。
        /// 件数分の行に分けず 1 本の警告に要約する（PR #104 レビュー L-2）。
        /// </summary>
        private static void LogPlanWarnings(GameStartPlan plan)
        {
            if (plan.Warnings.Count == 0)
            {
                return;
            }

            Debug.LogWarning("[LobbyView] 問題データの注意: " + string.Join(" / ", plan.Warnings));
        }

        /// <summary>
        /// <see cref="GameSession"/> が同期されるまで毎フレーム探し、見つかったら購読して終わる。
        /// </summary>
        /// <remarks>
        /// <c>LobbyState</c> 待ち（<c>LobbyView.LobbyRoutine</c>）とは独立したコルーチンにしている
        /// （PR #104 レビュー M-4）。<c>LobbyState</c> が時間内に見つからず <c>LobbyRoutine</c> が
        /// 打ち切られた場合でも、<see cref="GameSession"/> の購読だけは成立させるため。
        /// </remarks>
        private IEnumerator BindGameSessionRoutine()
        {
            // 最初に必ず 1 フレーム譲る（PR #104 レビュー M-B）。StartCoroutine は最初のセグメントを
            // 同期実行するため、ここで購読 → 画面遷移まで進んでしまうと、OnHide の
            // StopGameSessionRoutine が「まだ代入されていないコルーチンハンドル」を止められず、
            // 停止したはずのコルーチンが生き残る。
            yield return null;

            TryBindGameSession();
            while (_gameSession == null)
            {
                yield return null;
                TryBindGameSession();
            }

            _gameSessionCoroutine = null;
        }

        /// <summary><see cref="BindGameSessionRoutine"/> を開始する（<c>OnShow</c> から）。</summary>
        private void StartGameSessionRoutine()
        {
            StopGameSessionRoutine();
            _gameSessionCoroutine = _router != null ? _router.StartCoroutine(BindGameSessionRoutine()) : null;
        }

        /// <summary><see cref="BindGameSessionRoutine"/> を止める（<c>OnHide</c> から。冪等）。</summary>
        private void StopGameSessionRoutine()
        {
            if (_gameSessionCoroutine != null && _router != null)
            {
                _router.StopCoroutine(_gameSessionCoroutine);
            }

            _gameSessionCoroutine = null;
        }

        /// <summary>
        /// スポーン済みの <see cref="GameSession"/> を探して <see cref="GameSession.Phase"/> を購読する。
        /// ロビー滞在中は毎フレーム呼ばれるので、まだ同期されていない間は何もしない（冪等）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 購読を張った直後に<b>現在値でも一度判定する</b>（PR #104 レビュー H-1）。
        /// <c>NetworkVariable.OnValueChanged</c> は値が変わったときにしか発火しないため、
        /// すでに進行中のゲームへ途中参加（<c>network.allowLateJoin</c>）・再接続（#84）した
        /// クライアントは、これが無いと次の <see cref="QuizPhase.Reading"/> までロビーに取り残される。
        /// <c>GameView.TryAcquireSession</c> が <c>RefreshFromCurrentState()</c> を呼ぶのと同じ作法。
        /// </para>
        /// <para>
        /// ただしこの 1 回だけの判定は <see cref="IsQuestionInProgress"/>（問題が進行中のフェーズ）に
        /// 限る（PR #104 レビュー H-A）。<see cref="QuizPhase.Result"/> /
        /// <see cref="QuizPhase.Finished"/> まで含めると、「ロビーへ戻る」直後のクライアントが
        /// Lobby → Game へ跳ね返される（<c>GameSession.ReturnToLobby</c> は
        /// <c>PublishState()</c>（Phase → Lobby）→ <c>ReturnToLobbyRpc()</c> の順に行うが、
        /// <c>NetworkVariable</c> のデルタと RPC の到着順は保証されないため、
        /// クライアントがロビーを表示した時点でまだ <c>Result</c> のことがある）。
        /// </para>
        /// <para>
        /// <see cref="QuizPhase.Result"/> / <see cref="QuizPhase.Finished"/> で合流した場合は、
        /// 代わりに<b>サーバーが名指しで送ってきた再同期の状態</b>
        /// （<see cref="GameSession.TryConsumePendingResync"/>、#117）を根拠に遷移する。
        /// こちらは「ロビーへ戻る」では届かないので跳ね返りの原因にならない
        /// （<see cref="ApplyResyncedState"/>）。
        /// </para>
        /// </remarks>
        private void TryBindGameSession()
        {
            if (_gameSession != null || _networkService == null)
            {
                return;
            }

            // FindActiveGameSession は破棄済みの NetworkService でも例外を投げず null を返す
            // （#101、docs/network.md §1.2）。以前はここで ObjectDisposedException を捕まえていたが、
            // 中身が本呼び出しだけになりデッドコードだったため削除した（#116、PR #115 レビュー L-6）。
            var session = _networkService.FindActiveGameSession();
            if (session == null)
            {
                return;
            }

            _gameSession = session;
            _gameSession.Phase.OnValueChanged += HandleSessionPhaseChanged;
            _gameSession.SessionResynced += HandleSessionResynced;

            // 合流の合図（#117）は接続完了直後に届くため、ロビーを開くより先に来ていることがある。
            // 購読直後の判定より前に必ず取り出しておく（残したままにすると、次にロビーへ
            // 入り直したときに古い状態で遷移してしまう）。
            var hasResynced = _gameSession.TryConsumePendingResync(out var resynced);

            // 購読開始時点で「問題が進行中」なら、その場で Game View へ移る
            // （Result / Finished は下の再同期由来の判定に任せる）。
            var current = _gameSession.Phase.Value;
            if (IsQuestionInProgress(current))
            {
                HandleSessionPhaseChanged(current, current);
                return;
            }

            if (hasResynced)
            {
                ApplyResyncedState(resynced);
            }
        }

        /// <summary>
        /// ロビー滞在中にサーバーから合流の合図（<see cref="GameSession.SessionResynced"/>）が届いたとき（#117）。
        /// </summary>
        /// <remarks>
        /// 接続完了より後にロビーを開いた場合は <see cref="TryBindGameSession"/> が取り出すが、
        /// 購読を張ったあとに届くこともある（再接続・シーン同期の順序次第）。どちらの経路でも
        /// <see cref="GameSession.TryConsumePendingResync"/> 経由にして、同じ合図で二度遷移しないようにする。
        /// </remarks>
        /// <param name="snapshot">届いた進行状態（取り出しは <see cref="ApplyResyncedState"/> で行う）。</param>
        private void HandleSessionResynced(SessionStateSnapshot snapshot)
        {
            if (_gameSession == null || !_gameSession.TryConsumePendingResync(out var consumed))
            {
                return;
            }

            ApplyResyncedState(consumed);
        }

        /// <summary>
        /// 合流時点の進行状態にしたがって画面を移す（#117、docs/network.md §2.4 / §12.7）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 判断の根拠は <see cref="GameSession.Phase"/> の現在値ではなく、サーバーが
        /// 「あなたはいま合流した」と名指しで送ってきた <c>SessionStateRpc</c> の中身
        /// （<paramref name="snapshot"/>）。「ロビーへ戻る」ではこの合図が飛ばないので、
        /// PR #104 レビュー H-A の跳ね返り（ロビー表示直後に古い <see cref="QuizPhase.Result"/> を
        /// 観測して Game View へ戻る）は起こらない。
        /// </para>
        /// <para>
        /// <see cref="QuizPhase.Finished"/> だけは Result View（#20）へ直接移す。Game View は
        /// フェーズが <see cref="QuizPhase.Finished"/> へ<b>変わった</b>ときにしか Result View へ
        /// 送らないため、すでに終わっているルームへ合流した場合は Game View で止まってしまう。
        /// </para>
        /// <para>
        /// ただし遷移先を決めるときだけは、合図のフェーズより<b>現在値の
        /// <see cref="QuizPhase.Finished"/> を優先する</b>（PR #123 レビュー M-2）。
        /// 合図が届いてから遷移するまでの間にホストが全問終了へ進んでいた場合、合図のフェーズ
        /// （例 <see cref="QuizPhase.Result"/>）のまま Game View へ入ると、そのクライアントは
        /// <see cref="QuizPhase.Finished"/> への変化を取りこぼしていて順位表にたどり着けない。
        /// </para>
        /// </remarks>
        /// <param name="snapshot">サーバーから届いた合流時点の進行状態。</param>
        private void ApplyResyncedState(SessionStateSnapshot snapshot)
        {
            if (snapshot.Phase == QuizPhase.Lobby)
            {
                // サーバーは Lobby では再同期を送らない（QuizPhases.NeedsResync）ので通常は来ない。
                return;
            }

            if (_gameSession != null && _gameSession.Phase.Value == QuizPhase.Lobby)
            {
                // 合図と入れ違いでロビーへ戻された場合の保険。GameSession のスポーン payload は
                // RPC より先に届くので、合流直後にここへ入ることはない。
                return;
            }

            // 合図が届いてから遷移するまでの間にホストが全問終了へ進んでいたら、そちらを優先する
            // （PR #123 レビュー M-2）。この分岐は「合図がある場合」＝ 合流したときしか通らないので、
            // 「ロビーへ戻る」直後に古い Phase を観測して跳ね返る H-A とは競合しない。
            // 逆向き（現在値が Result で合図が Finished）に倒さないのは、Result View から
            // Game View へ戻す意味が無いため。
            var phase = _gameSession != null && _gameSession.Phase.Value == QuizPhase.Finished
                ? QuizPhase.Finished
                : snapshot.Phase;

            var viewName = ResolveResyncedViewName(phase);
            var router = _router;
            UnbindGameSession();
            router?.ShowView(viewName);
        }

        /// <summary>
        /// 合流時点のフェーズから、移る先の View 名を決める（#117）。
        /// </summary>
        /// <param name="phase">サーバーが送ってきた合流時点のフェーズ（<see cref="QuizPhase.Lobby"/> 以外）。</param>
        /// <returns>
        /// 全問終了（<see cref="QuizPhase.Finished"/>）なら <see cref="ViewNames.Result"/>、
        /// それ以外（結果表示中・問題進行中）は <see cref="ViewNames.Game"/>。
        /// </returns>
        internal static string ResolveResyncedViewName(QuizPhase phase) =>
            phase == QuizPhase.Finished ? ViewNames.Result : ViewNames.Game;

        /// <summary>
        /// 問題の進行中（読み上げ〜判定）のフェーズか。結果表示（<see cref="QuizPhase.Result"/>）・
        /// 全問終了（<see cref="QuizPhase.Finished"/>）・ロビーは含まない。
        /// </summary>
        /// <remarks>
        /// PR #104 レビュー L-1: 以前は「含めるフェーズを並べる <c>switch</c>」だったため、
        /// <see cref="QuizPhase"/> に進行中フェーズが増えると <c>default</c> へ落ちて静かに漏れた。
        /// 判定は除外リスト形の <see cref="QuizPhases.IsQuestionInProgress"/>（Core、EditMode でテスト）
        /// に集約し、ここはその呼び出しだけにしてある。
        /// </remarks>
        /// <param name="phase">判定するフェーズ。</param>
        /// <returns>問題が進行中なら true。</returns>
        private static bool IsQuestionInProgress(QuizPhase phase) => QuizPhases.IsQuestionInProgress(phase);

        /// <summary><see cref="TryBindGameSession"/> の購読を外す（冪等）。</summary>
        private void UnbindGameSession()
        {
            if (_gameSession == null)
            {
                return;
            }

            _gameSession.Phase.OnValueChanged -= HandleSessionPhaseChanged;
            _gameSession.SessionResynced -= HandleSessionResynced;
            _gameSession = null;
        }

        /// <summary>
        /// 進行が始まっていれば（<see cref="QuizPhase.Lobby"/> 以外）ロビーに居る全員が Game View へ移る。
        /// </summary>
        /// <remarks>
        /// 判定を <see cref="QuizPhase.Reading"/> だけにしないのは、途中参加・再接続したクライアントが
        /// 受付中（<see cref="QuizPhase.BuzzOpen"/>）や結果表示（<see cref="QuizPhase.Result"/>）の
        /// 最中に合流しうるため（PR #104 レビュー H-1）。<see cref="QuizPhase.Finished"/> でも
        /// Game View へ移るが、Game View 側が <c>HandlePhaseChanged</c> で即座に Result View へ送る（#20）。
        /// なお「ロビーへ戻る」直後の跳ね返り（レビュー H-A）は、<b>変化の通知</b>ではロビー表示中に
        /// <c>Result</c> → <c>Lobby</c> の変化しか届かない（＝ ここは素通りする）ため起きない。
        /// 起きうるのは購読直後の現在値判定のほうで、そちらは <see cref="IsQuestionInProgress"/> に絞ってある。
        /// </remarks>
        private void HandleSessionPhaseChanged(QuizPhase previous, QuizPhase current)
        {
            if (current == QuizPhase.Lobby)
            {
                return;
            }

            var router = _router;
            UnbindGameSession();
            router?.ShowView(ViewNames.Game);
        }
    }
}
