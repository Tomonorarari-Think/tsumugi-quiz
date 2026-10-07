using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Network;
using TsumugiQuiz.Tts;
using TsumugiQuiz.UI.Views.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI
{
    /// <summary>
    /// 立ち絵の状態切り替え（待機/読み上げ中/正解/不正解）を行う UI Toolkit の再利用部品（issue #24）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Assets/TsumugiQuiz/UI/Templates/character-view.uxml</c> から生成された
    /// <see cref="VisualElement"/> をコンストラクタで受け取り、内部の名前付き要素
    /// （<c>character-image</c> 等）を配線する。<c>GameView</c>（#14）は自身の UXML にこのテンプレートを
    /// <c>&lt;ui:Instance&gt;</c> で埋め込み、生成された要素を渡す。
    /// </para>
    /// <para>
    /// 状態遷移そのものは Unity 非依存の <see cref="CharacterStateMachine"/> に委譲する。
    /// 本クラスは次のイベント源を購読して状態機械へ伝え、結果を VisualElement に反映するだけ（購読とハンドラは <c>CharacterView.Events.cs</c>、#212）。
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="TtsSyncPlayer.ReadingStarted"/> / <see cref="TtsSyncPlayer.ReadingCompleted"/>
    ///     — このクライアントで実際に読み上げ音声が再生されている区間（docs/tts.md §8.1）
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="GameSession.QuestionResolved"/> — 1 問の正誤判定（TTS の状態とは独立）
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="GameSession.BuzzLocked"/> / <see cref="GameSession.BuzzReopened"/> /
    ///     <see cref="GameSession.ChoiceResolved"/> — 回答権の確定・誤答の瞬間・選択式の一斉判定（#212）
    ///   </description></item>
    /// </list>
    /// <para>
    /// 依存方向（docs/architecture.md §3、K6）: <c>TsumugiQuiz.UI</c> は <c>TsumugiQuiz.Tts</c> /
    /// <c>TsumugiQuiz.Network</c> の公開イベントを購読するだけで、逆方向（Tts/Network → UI）の参照は無い。
    /// </para>
    /// <para>
    /// 状態ごとの見た目（#86 / #212）: <see cref="CharacterState"/> ごとに別の画像（表情差分）へ差し替える。
    /// 画像はユーザーが <c>scripts/generate-tsumugi-expressions.ps1</c> で自分の環境の PSD から生成し、
    /// <see cref="CharacterImagePaths"/> が指すデータルート配下に置く（配布物には含めない、
    /// docs/licenses.md §3）。未生成の表情は（#212 の一部の表情は意味の近い表情を経由して）待機 → 従来の全身 PNG の順にフォールバックし、
    /// どれも無ければ従来どおり非表示。表情差分が無い環境でも #24 の演出
    /// （バウンス・立ち絵ティント（#192、<see cref="Image.tintColor"/>）・図形マーク）は従来どおり働く。
    /// ティントは、その状態専用の表情差分が読めたときは掛けず、フォールバックしたときだけ掛ける
    /// （#212、<see cref="CharacterStateVisuals.GetTint"/>）。
    /// 表情は各クライアントのローカル表示であり、ネットワーク同期はしない。
    /// </para>
    /// <para>
    /// 見た目（#191）: 立ち絵は右列の幅いっぱいのカード（<c>character-image-frame</c>）に載せ、カードの縦横比は
    /// 表示中の画像に合わせる（<see cref="ApplyFrameAspectRatio"/>）。読み上げ中のバウンスは画像だけを
    /// 下端中央を支点にわずかに拡大する（USS）。
    /// </para>
    /// <para>
    /// 表示・非表示は同意状況（既定 <see cref="TtsConsentCheckFactory.Build"/>
    /// = <see cref="ConsentGate.HasUserConsented"/>、#139）と、立ち絵画像が
    /// 実際に読み込めたかの両方で決まる（FR-74、レビュー H1）。未同意・未配置のいずれでも
    /// <c>character-root</c> を <see cref="DisplayStyle.None"/> にして完全に隠す
    /// （プレースホルダのシルエット枠・案内ラベルは表示しない）。
    /// 画像の解決（<c>textureResolver</c>）は同意済みで最初に表示されるタイミングまで遅延する
    /// （レビュー M6。読み込み自体のキャッシュは呼び出し側 <c>GameView</c> の責務）。
    /// 表示 ON/OFF のアプリ設定（<c>character.enabled</c>、既定 true）は #28 で接続済み。
    /// 呼び出し側の <c>GameView.Character.cs</c>（<c>IsCharacterEnabled</c>）が
    /// <c>AppSettingsStore</c> から読み、false なら本クラス自体を生成しない。
    /// </para>
    /// <para>
    /// 同意の再評価の契機（#139、FR-75 / NFR-08）: 生成時のほか、
    /// <see cref="GameSession.QuestionShown"/>（出題ごと）と
    /// <see cref="AttachToPanelEvent"/>（Game View へ戻ってきたとき）でも
    /// <see cref="RefreshVisibility"/> を呼ぶ。読み上げ側（<c>TtsSyncPlayer</c> / <c>TtsService</c>、#127）が
    /// 出題ごと・再生開始ごとに同じ判定関数を評価するのに合わせてあり、
    /// <b>進行中に撤回すると次の出題で立ち絵が消え、読み上げも止まる</b>。
    /// </para>
    /// </remarks>
    public sealed partial class CharacterView : IDisposable
    {
        /// <summary>状態機械の Tick 間隔（ミリ秒）。</summary>
        public const int TickIntervalMs = 100;

        /// <summary>読み上げ中バウンス演出のクラス切替間隔（ミリ秒）。</summary>
        public const int BounceToggleIntervalMs = 260;

        /// <summary>正解時に character-image へ付与する USS クラス（#192、theme-views-game.uss 参照）。</summary>
        private const string TintCorrectClassName = "character-image--tint-correct";

        /// <summary>不正解時に character-image へ付与する USS クラス（#192、theme-views-game.uss 参照）。</summary>
        private const string TintWrongClassName = "character-image--tint-wrong";

        private readonly VisualElement _root;
        private readonly VisualElement _imageFrame;
        private readonly Image _image;
        private readonly VisualElement _markCorrect;
        private readonly VisualElement _markWrongA;
        private readonly VisualElement _markWrongB;

        private readonly CharacterStateMachine _stateMachine = new();
        private readonly Func<CharacterState, CharacterTextureResolution> _textureResolver;

        private TtsSyncPlayer _ttsSyncPlayer;
        private GameSession _gameSession;
        private Func<bool> _consentCheck;

        /// <summary>このクライアントの ID を返す関数（#212。回答権の自分 / 他人の判定に使う。null 可）。</summary>
        private Func<ulong?> _localClientIdProvider;

        /// <summary>
        /// このクライアントが、いまの選択式の問題で選べる立場かを返す関数（#212。null 可 = 選べる立場ではない扱い）。
        /// 選ばなかったときに時間切れにするか、全体の結果にするかの判断に使う（<see cref="CharacterChoiceOutcome"/>）。
        /// </summary>
        private Func<bool> _isLocalChoiceAnswererProvider;

        private IVisualElementScheduledItem _tickItem;
        private IVisualElementScheduledItem _bounceItem;
        private bool _bounceUp;

        /// <summary>
        /// いま <c>character-image</c> に適用済みのテクスチャに対応する状態（#86）。
        /// null なら一度も解決していない。同じ状態に留まっている限り再解決しない。
        /// </summary>
        private CharacterState? _appliedTextureState;

        private bool _hasImage;

        /// <summary>
        /// 適用中のテクスチャが、その状態専用の表情差分か（#212）。false ならフォールバック中で、
        /// 結果の状態ではティントを掛ける（<see cref="CharacterStateVisuals.GetTint"/>）。
        /// </summary>
        private bool _hasDedicatedImage;

        /// <summary>
        /// 直近の同意判定結果（#86）。状態（表情）が変わるたびに
        /// <see cref="ConsentGate.HasUserConsented"/> を呼ぶと毎回 <c>consent.json</c> と
        /// 規約テキストを読むことになるため、<see cref="RefreshVisibility"/> でだけ更新する。
        /// </summary>
        /// <remarks>
        /// <para>
        /// PR #135 レビュー L3: このキャッシュは <see cref="CharacterView"/> 1 インスタンスの寿命でしか
        /// 持ち越されない。View の再表示ごとに <c>GameView</c> が新しいインスタンスを生成するため
        /// （<c>ViewControllerRegistry</c>）、<c>TermsView</c> で同意を撤回して Game View に戻れば
        /// その時点で再評価される。
        /// </para>
        /// <para>
        /// issue #139: それだけでは「Game View を表示したままゲームが進む間」の撤回に追従できないため、
        /// <see cref="Bind"/> で <see cref="GameSession.QuestionShown"/> を購読し、
        /// <b>出題のたびに</b> <see cref="RefreshVisibility"/> で再評価する
        /// （加えて <see cref="OnAttachToPanel"/>、＝ 画面へ戻ってきたとき）。
        /// 読み上げ側（#127）が判定関数を出題ごとに評価するのと同じ粒度で、撤回は<b>次の出題から</b>効く。
        /// 表示中のフレームで即座に消えるわけではない（判定のたびに <c>consent.json</c> と規約テキストを
        /// 読み直すコストを避けるため。100ms ごとの <see cref="OnTick"/> では評価しない）。
        /// </para>
        /// </remarks>
        private bool _consented;

        private bool _disposed;

        /// <summary>
        /// 直近に本イベントで通知した可視性（issue #172）。<see cref="UpdateTextureAndDisplay"/> が
        /// 呼ばれるたびではなく、<see cref="IsVisible"/> が実際に変化したときだけ
        /// <see cref="VisibilityChanged"/> を発火するための記録。null は「まだ一度も通知していない」。
        /// </summary>
        private bool? _lastNotifiedVisible;

        /// <summary>現在の表示状態（テスト・診断用）。</summary>
        public CharacterState State => _stateMachine.State;

        /// <summary>いま実際に表示されているか（同意済み かつ 画像が読み込めた場合のみ true）。</summary>
        public bool IsVisible => _root.style.display == DisplayStyle.Flex;

        /// <summary>
        /// <see cref="IsVisible"/> が実際に変化したときに発火する（issue #172）。
        /// <c>GameView</c> は本イベントを購読し、立ち絵の有無に応じたレイアウト調整
        /// （<c>game-content-main</c> の幅・立ち絵外枠の表示）を、ポーリングではなくこのイベント起点で行う。
        /// コンストラクタ内で最初に確定する可視性（<see cref="RefreshVisibility"/> の初回呼び出し）は、
        /// 呼び出し側がまだ購読していないため本イベント経由では届かない。呼び出し側は生成直後に
        /// <see cref="IsVisible"/> を一度読んで初期状態を反映してから購読すること。
        /// </summary>
        public event Action<bool> VisibilityChanged;

        /// <param name="root">
        /// <c>character-view.uxml</c> から生成されたルート要素（<c>character-root</c>）。
        /// 想定する子要素（<c>character-image-frame</c> / <c>character-image</c> /
        /// <c>character-mark-correct</c> / <c>character-mark-wrong-a</c> /
        /// <c>character-mark-wrong-b</c>）が見つからない場合はエラーログを出す
        /// （テンプレートの構造が壊れている場合の早期発見用）。
        /// </param>
        /// <param name="textureResolver">
        /// 表情ごとの立ち絵テクスチャの解決関数（既定 null なら
        /// <see cref="CharacterImageLoader.LoadExpression"/> を呼ぶ。未配置の表情は
        /// 待機 → 従来の全身 PNG の順にフォールバックする、#86）。
        /// 関数を渡した場合はテクスチャだけが返るので、専用の表情差分かどうかは分からず、結果の状態では常にティントを掛ける
        /// （#212。専用かどうかも返す版は、もう一方のコンストラクタ）。
        /// null を渡した場合は既定の <see cref="CharacterImageLoader.LoadExpression"/> を使うので、
        /// 専用の表情差分かどうかを判定し、フォールバックしたときだけティントを掛ける。
        /// </param>
        /// <param name="consentCheck">
        /// 同意判定を差し替える（テスト用。既定 null なら <see cref="TtsConsentCheckFactory.Build"/>
        /// = <see cref="ConsentGate.HasUserConsented"/>）。
        /// #139: 読み上げ（#127）と同じファクトリを経由させ、「同意済みか」の判定の出所を 1 つに保つ。
        /// </param>
        public CharacterView(
            VisualElement root,
            Func<CharacterState, Texture2D> textureResolver = null,
            Func<bool> consentCheck = null)
            : this(root, WrapTextureOnlyResolver(textureResolver), consentCheck)
        {
        }

        /// <summary>
        /// その状態専用の表情差分を読めたか（<see cref="CharacterTextureResolution.IsDedicated"/>）も返す解決関数を
        /// 受け取る版（#212。<c>GameView</c> はこちらを使い、フォールバックしたときだけティントを掛ける）。
        /// </summary>
        /// <param name="root"><c>character-view.uxml</c> から生成されたルート要素（<c>character-root</c>）。</param>
        /// <param name="textureResolver">
        /// 表情ごとの解決関数（null なら <see cref="CharacterImageLoader.LoadExpression"/>）。
        /// </param>
        /// <param name="consentCheck">同意判定の差し替え（テスト用。null なら既定）。</param>
        public CharacterView(
            VisualElement root,
            Func<CharacterState, CharacterTextureResolution> textureResolver,
            Func<bool> consentCheck = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _textureResolver = textureResolver ?? CharacterImageLoader.LoadExpression;
            _consentCheck = consentCheck ?? TtsConsentCheckFactory.Build();

            _imageFrame = _root.Q<VisualElement>("character-image-frame");
            _image = _root.Q<Image>("character-image");
            _markCorrect = _root.Q<VisualElement>("character-mark-correct");
            _markWrongA = _root.Q<VisualElement>("character-mark-wrong-a");
            _markWrongB = _root.Q<VisualElement>("character-mark-wrong-b");

            if (_imageFrame == null || _image == null
                || _markCorrect == null || _markWrongA == null || _markWrongB == null)
            {
                Debug.LogError("[CharacterView] character-view.uxml の想定要素が見つかりません。テンプレートを確認してください。");
            }

            _stateMachine.StateChanged += ApplyState;
            ApplyState(_stateMachine.State);

            // レビュー M6: 同意状況を先に確認し、実際に表示される場合だけ立ち絵を読み込む。
            RefreshVisibility();

            _root.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            _root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            if (_root.panel != null)
            {
                StartScheduling();
            }
        }

        /// <summary>
        /// 同意判定を差し替える（テスト用）。null を渡すと既定（<see cref="TtsConsentCheckFactory.Build"/>
        /// = <see cref="ConsentGate.HasUserConsented"/>）に戻る。
        /// </summary>
        public void SetConsentCheckForTesting(Func<bool> consentCheck)
        {
            _consentCheck = consentCheck ?? TtsConsentCheckFactory.Build();
            RefreshVisibility();
        }

        /// <summary>
        /// 同意状況を再確認して表示・非表示を更新する。<c>TermsView</c> での撤回操作後などに呼ぶ。
        /// 同意済みで、現在の状態（<see cref="State"/>）の立ち絵をまだ読み込んでいなければ
        /// このタイミングで読み込む（レビュー M6 / 表情差分は #86）。
        /// </summary>
        /// <remarks>
        /// #139: 本メソッドは <see cref="HandleQuestionShown"/>（出題ごと）と
        /// <see cref="OnAttachToPanel"/> からも自動的に呼ばれる。1 回あたり <c>consent.json</c> と
        /// 規約テキストの読み込みが走る（<see cref="ConsentGate.HasUserConsented"/>）ため、
        /// 毎フレーム・毎 Tick では呼ばない。
        /// </remarks>
        public void RefreshVisibility()
        {
            if (_disposed)
            {
                // Dispose 中の Unbind → 状態リセット → ApplyState から呼ばれうる。
                // 破棄後にテクスチャを解決し直さない。
                return;
            }

            _consented = SafeCheckConsent();
            UpdateTextureAndDisplay();
        }

        /// <summary>
        /// いまの状態（表情）に対応する立ち絵を適用し、表示・非表示を更新する（#86）。
        /// 同意判定はキャッシュ（<see cref="_consented"/>）を使い、ここでは再確認しない。
        /// </summary>
        private void UpdateTextureAndDisplay()
        {
            if (_consented)
            {
                ApplyTextureForState(_stateMachine.State);
            }
            else
            {
                // PR #135 レビュー L4: 撤回（FR-75）で非同意に戻ったら、隠すだけでなく
                // テクスチャの参照自体を手放す。次に同意したときは改めて解決し直す。
                ClearTexture();
            }

            var visible = _consented && _hasImage;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // #212: ティントは「その状態専用の表情差分か」で変わるので、テクスチャを決めた後に掛ける。
            ApplyTint(_stateMachine.State);

            // issue #172: 実際に可視性が変化したときだけ通知する（毎回発火すると呼び出し側の
            // レイアウト更新が無駄に走る。EnableInClassList 自体は冪等だが、意図を明確にする）。
            if (_lastNotifiedVisible != visible)
            {
                _lastNotifiedVisible = visible;
                VisibilityChanged?.Invoke(visible);
            }
        }

        /// <summary>適用済みのテクスチャ参照を捨てる（テクスチャの所有権は呼び出し側にあるので破棄はしない）。</summary>
        private void ClearTexture()
        {
            _appliedTextureState = null;
            ApplyTexture(default);
        }

        /// <summary>読み上げ・判定のイベント購読を解除する。テクスチャの所有権は呼び出し側にあるため破棄しない。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Unbind();
            StopScheduling();
            _stateMachine.StateChanged -= ApplyState;

            _root.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            _root.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            if (_image != null)
            {
                _image.image = null;
            }
        }

        private bool SafeCheckConsent()
        {
            try
            {
                return _consentCheck != null && _consentCheck();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterView] 同意確認に失敗したため非表示にします: {e.GetType().Name}");
                return false;
            }
        }

        private CharacterTextureResolution SafeResolveTexture(CharacterState state)
        {
            try
            {
                return _textureResolver != null ? _textureResolver(state) : default;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterView] 立ち絵の解決に失敗しました（{e.GetType().Name}、{state}）: {e.Message}");
                return default;
            }
        }

        /// <summary>
        /// テクスチャだけを返す解決関数（#86 までの形）を包む。専用の差分かどうかは分からないので、
        /// 常に「フォールバック」として扱う（結果の状態ではティントを掛ける。#192 の挙動を保つ）。
        /// </summary>
        private static Func<CharacterState, CharacterTextureResolution> WrapTextureOnlyResolver(
            Func<CharacterState, Texture2D> textureResolver)
        {
            if (textureResolver == null)
            {
                return null;
            }

            return state => new CharacterTextureResolution(textureResolver(state), isDedicated: false);
        }

        /// <summary>
        /// 指定した状態の立ち絵（表情差分、#86）を解決して適用する。
        /// 同じ状態に対しては 1 度しか解決しない（解決結果自体のキャッシュは <c>GameView</c> の責務）。
        /// </summary>
        private void ApplyTextureForState(CharacterState state)
        {
            if (_appliedTextureState == state)
            {
                return;
            }

            _appliedTextureState = state;
            ApplyTexture(SafeResolveTexture(state));
        }

        private void ApplyTexture(CharacterTextureResolution resolution)
        {
            var texture = resolution.Texture;
            _hasImage = texture != null;
            _hasDedicatedImage = resolution.IsDedicated;

            if (_image != null)
            {
                _image.image = texture;
            }

            ApplyFrameAspectRatio(texture);
        }

        /// <summary>
        /// 立ち絵のカード（<c>character-image-frame</c>）の縦横比を、実際に表示する画像に合わせる（#191）。
        /// </summary>
        /// <remarks>
        /// USS の既定（<c>aspect-ratio: 0.771</c>）は生成スクリプトのバストアップの縦横比。全身の
        /// <c>tsumugi_v2.png</c> へのフォールバック（約 1:2）や、<c>--crop</c> で範囲を変えた画像でも
        /// カードが画像にぴったり沿う（下端の切れ目がカードの下辺に揃う）ように、画像の縦横比で上書きする。
        /// テクスチャが無いときは上書きを外して USS の値に戻す。
        /// </remarks>
        private void ApplyFrameAspectRatio(Texture2D texture)
        {
            if (_imageFrame == null)
            {
                return;
            }

            _imageFrame.style.aspectRatio = texture != null && texture.width > 0 && texture.height > 0
                ? new StyleRatio(new Ratio((float)texture.width / texture.height))
                : new StyleRatio(StyleKeyword.Null);
        }

        /// <summary>
        /// パネルへ接続されたとき。スケジュールを開始するほか、同意状況を評価し直す（#139）。
        /// <c>GameView</c> は View の表示ごとに新しい <see cref="CharacterView"/> を生成するため
        /// 通常は構築時の評価で足りるが、要素を作り置きして後からパネルへ足す使い方
        /// （撤回操作の画面から Game View へ戻る経路を含む）でも取りこぼさないようにしておく。
        /// </summary>
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            StartScheduling();
            RefreshVisibility();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt) => StopScheduling();

        private void StartScheduling()
        {
            if (_tickItem != null)
            {
                return;
            }

            _tickItem = _root.schedule.Execute(OnTick).Every(TickIntervalMs);
            _bounceItem = _root.schedule.Execute(ToggleBounce).Every(BounceToggleIntervalMs);
        }

        private void StopScheduling()
        {
            _tickItem?.Pause();
            _tickItem = null;
            _bounceItem?.Pause();
            _bounceItem = null;
        }

        private void OnTick(TimerState timerState)
        {
            _stateMachine.Tick(timerState.deltaTime / 1000d);
        }

        private void ToggleBounce(TimerState timerState)
        {
            if (_imageFrame == null)
            {
                return;
            }

            if (_stateMachine.State != CharacterState.Reading)
            {
                if (_bounceUp)
                {
                    _bounceUp = false;
                    _imageFrame.RemoveFromClassList("character-image-frame--bounce-up");
                }

                return;
            }

            _bounceUp = !_bounceUp;
            _imageFrame.EnableInClassList("character-image-frame--bounce-up", _bounceUp);
        }

        private void ApplyState(CharacterState state)
        {
            if (_markCorrect != null)
            {
                _markCorrect.style.display = CharacterStateVisuals.ShowsCorrectMark(state) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // #212: 時間切れ・回答できる人がいないも従来の時間切れと同じく ×。誤答の瞬間は結果が未確定なので出さない。
            var wrongDisplay = CharacterStateVisuals.ShowsWrongMark(state) ? DisplayStyle.Flex : DisplayStyle.None;
            if (_markWrongA != null)
            {
                _markWrongA.style.display = wrongDisplay;
            }

            if (_markWrongB != null)
            {
                _markWrongB.style.display = wrongDisplay;
            }

            if (state != CharacterState.Reading && _imageFrame != null)
            {
                _bounceUp = false;
                _imageFrame.RemoveFromClassList("character-image-frame--bounce-up");
            }

            // #86: 状態ごとの表情差分に差し替える（未配置なら待機 → 従来の全身 PNG にフォールバックし、
            // どれも無ければ非表示）。同意判定は RefreshVisibility が更新したキャッシュを使う。
            // ティント（#192）はテクスチャが決まってから UpdateTextureAndDisplay の中で掛ける（#212）。
            if (!_disposed)
            {
                UpdateTextureAndDisplay();
            }
            else
            {
                ApplyTint(state);
            }
        }

        /// <summary>
        /// 立ち絵のティント（#192）を掛ける。<see cref="CharacterStateVisuals.GetTint"/> に従い、
        /// その状態専用の表情差分を表示しているときは掛けず、フォールバックしているときだけ結果の色を掛ける（#212）。
        /// </summary>
        /// <remarks>
        /// 立ち絵の枠全体を覆う矩形（旧 character-tint-overlay）ではなく、character-image（Image）自身に
        /// USS クラスで --unity-image-tint-color を適用し、テクスチャの画素にだけ色を乗算する
        /// （透明部分・余白は影響を受けない）。
        /// </remarks>
        private void ApplyTint(CharacterState state)
        {
            if (_image == null)
            {
                return;
            }

            var tint = CharacterStateVisuals.GetTint(state, _hasDedicatedImage);
            _image.EnableInClassList(TintCorrectClassName, tint == CharacterTint.Correct);
            _image.EnableInClassList(TintWrongClassName, tint == CharacterTint.Wrong);
        }
    }
}
