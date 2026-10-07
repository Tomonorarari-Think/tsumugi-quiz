using System;
using System.Collections.Generic;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using UnityEngine;
using UnityEngine.UIElements;

namespace TsumugiQuiz.UI.Views.Game
{
    /// <summary>
    /// <see cref="GameView"/> のうち、立ち絵（<see cref="CharacterView"/>、issue #24）の組み込みをまとめた部分。
    /// </summary>
    /// <remarks>
    /// 立ち絵テクスチャの読み込みはプロセス単位で 1 度だけ行い、静的フィールドにキャッシュする
    /// （レビュー M6）。<see cref="GameView"/> 自身は View を表示するたびに新しいインスタンスが
    /// 生成される（<c>ViewControllerRegistry</c>）ため、キャッシュをインスタンスフィールドに
    /// 持たせても OnShow のたびに失われてしまう。
    /// 表情差分（#86）はキャッシュを 2 段にする。状態 → テクスチャ（<see cref="_characterTextureByState"/>）で
    /// 状態ごとの解決を 1 回に抑え、さらにパス → テクスチャ（<see cref="_characterTextureByPath"/>）で
    /// 「複数の状態が同じファイルへフォールバックした場合」に同じ PNG を二重に読み込まないようにする。
    /// #212 で表情が 9 状態に増えたため、どれか 1 つが初めて必要になった時点（＝同意を確認して立ち絵を初めて
    /// 出すとき。<see cref="CharacterView"/> は同意済みのときだけ解決を呼ぶ）に全状態をまとめて読み込む。
    /// 初めての回答権・誤答の瞬間にデコードが重なって引っかかるのを避けるため。
    /// </remarks>
    public sealed partial class GameView
    {
        /// <summary>状態ごとに解決済みの立ち絵（テクスチャが null = 解決したが読み込めなかった）。</summary>
        private static readonly Dictionary<CharacterState, CharacterTextureResolution> _characterTextureByState = new();

        /// <summary>読み込み済みファイルの重複排除（フォールバックで同じ PNG に落ちる状態がある）。</summary>
        private static readonly Dictionary<string, Texture2D> _characterTextureByPath = new();

        /// <summary><c>character.enabled</c> の読み込み結果のプロセス単位キャッシュ（PR #92 レビュー L5）。</summary>
        private static bool? _cachedCharacterEnabled;

        /// <summary>立ち絵（issue #24）。<c>character-view.uxml</c> のルート要素（見つからなければ null）。</summary>
        private VisualElement _characterRoot;

        /// <summary>立ち絵の状態切り替え（issue #24）。<see cref="_characterRoot"/> が見つかった場合のみ生成する。</summary>
        private CharacterView _characterView;

        /// <summary>
        /// 中央列（issue #172 / #193）。立ち絵が非表示の間、<see cref="NoCharacterMainClassName"/> を
        /// 付ける（<see cref="UpdateGameContentLayoutForCharacterVisibility"/>）。
        /// </summary>
        private VisualElement _gameContentMain;

        /// <summary>
        /// 立ち絵テンプレートの外枠（<c>game-view.uxml</c> の <c>&lt;ui:Instance&gt;</c> 要素、issue #172）。
        /// <see cref="_characterRoot"/>（テンプレート内部のルート）とは別の要素で、
        /// <see cref="CharacterView"/> はこちらの表示・非表示は管理しない。<c>class="game-character-slot"</c>
        /// の <c>margin-left</c> は、中身（<see cref="_characterRoot"/>）が <c>DisplayStyle.None</c> でも
        /// 外枠自体が Flex のままだと残ってしまうため、外枠ごと非表示にして詰める。
        /// </summary>
        private VisualElement _characterSlot;

        /// <summary>
        /// 立ち絵が非表示の間、<c>game-content-main</c>（中央列）に付けるクラス（issue #172 / #193）。
        /// 右列（立ち絵）を隠した分だけ中央列が広がるが、<c>.game-content-main</c> の最大幅（880px）で頭打ちになり、
        /// 参加者パネルと中央列が行の中央に寄る。最大幅は #213 で立ち絵の有無にかかわらず付けるようにしたので、
        /// このクラス自体はスタイルを持たない（立ち絵の有無を見分けるフック。theme-views-game.uss 参照）。
        /// テストが直接参照できるよう internal にしてある。
        /// </summary>
        internal const string NoCharacterMainClassName = "game-content-main--no-character";

        /// <summary>
        /// <see cref="_characterRoot"/> が見つかっていれば <see cref="CharacterView"/> を生成する。
        /// 見つからない場合も GameView 本体の表示は続行する（<c>OnShow</c> から呼ぶ）。
        ///
        /// <c>character.enabled</c>（アプリ設定、issue #28）が false の場合は立ち絵を表示しない
        /// （<see cref="_characterRoot"/> ごと非表示にし、<see cref="CharacterView"/> は生成しない）。
        /// </summary>
        private void InitializeCharacterView()
        {
            // issue #172: 立ち絵の有無に関わらず本体パネルの幅調整対象を取得しておく
            // （_characterRoot が見つからない異常系でも UpdateGameContentLayoutForCharacterVisibility は
            // 呼べるようにする）。
            _gameContentMain = _root.Q<VisualElement>("game-content-main");
            _characterSlot = _root.Q<VisualElement>("character-view-instance");

            if (_characterRoot == null)
            {
                Debug.LogError("[GameView] character-root が見つかりません。game-view.uxml の Instance 埋め込みを確認してください。");
                UpdateGameContentLayoutForCharacterVisibility();
                return;
            }

            if (!IsCharacterEnabled())
            {
                _characterRoot.style.display = DisplayStyle.None;
                UpdateGameContentLayoutForCharacterVisibility();
                return;
            }

            _characterView = new CharacterView(_characterRoot, ResolveCharacterTexture);

            // issue #172 レビュー M2: 可視性の真実の源を CharacterView.VisibilityChanged 1 か所にする
            // （100ms ポーリングの Tick 呼び出しは廃止）。コンストラクタ内の初回評価
            // （RefreshVisibility）は購読前に走っているため、イベント経由では届かない。
            // 購読した後、一度だけ手動で同期する。
            _characterView.VisibilityChanged += OnCharacterVisibilityChanged;
            UpdateGameContentLayoutForCharacterVisibility();
        }

        /// <summary>
        /// <see cref="CharacterView.VisibilityChanged"/> のハンドラ（issue #172）。
        /// 同意撤回・出題ごとの再評価（<c>CharacterView.RefreshVisibility</c>）で可視性が動的に
        /// 変わったタイミングでだけ呼ばれる（100ms ポーリングではなくイベント駆動）。
        /// </summary>
        private void OnCharacterVisibilityChanged(bool visible) => UpdateGameContentLayoutForCharacterVisibility();

        /// <summary>
        /// 立ち絵が実際に表示されているか（issue #172）に応じて、中央列（<see cref="_gameContentMain"/>）の
        /// 修飾クラスと、右列＝立ち絵テンプレートの外枠（<see cref="_characterSlot"/>）の表示を更新する（#193）。
        /// <see cref="_characterView"/> が生成されていない（<c>character.enabled</c> = false、または
        /// テンプレート異常）場合は「非表示」として扱う。<see cref="InitializeCharacterView"/> からの初期同期と、
        /// <see cref="OnCharacterVisibilityChanged"/>（<see cref="CharacterView.VisibilityChanged"/> 購読）
        /// から呼ぶ。
        /// </summary>
        /// <remarks>
        /// <see cref="_characterSlot"/>（<c>&lt;ui:Instance&gt;</c> の外枠）は <see cref="_characterRoot"/>
        /// （テンプレート内部）が <see cref="DisplayStyle.None"/> になっても Flex のまま残り、
        /// <c>.game-character-slot</c> の幅（#193 で右列の枠になった）と <c>margin-left</c> が残ってしまう
        /// （実測: 実 issue #172 の調査で判明）。外枠ごと非表示にして右列を詰める。
        /// </remarks>
        private void UpdateGameContentLayoutForCharacterVisibility()
        {
            var characterVisible = _characterView != null && _characterView.IsVisible;

            if (_gameContentMain != null)
            {
                _gameContentMain.EnableInClassList(NoCharacterMainClassName, !characterVisible);
            }

            if (_characterSlot != null)
            {
                _characterSlot.style.display = characterVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// <c>character.enabled</c>（アプリ設定、issue #28）を読む。<c>AppPaths</c> が未設定
        /// （Boot を経由しないテスト等）の場合は既定値（true）にフォールバックする。
        /// 立ち絵テクスチャ（<see cref="_characterTextureByState"/>）と同様、プロセス単位で 1 度だけ
        /// ファイルを読み、以後はキャッシュを返す（PR #92 レビュー L5）。
        /// </summary>
        private static bool IsCharacterEnabled()
        {
            if (_cachedCharacterEnabled.HasValue)
            {
                return _cachedCharacterEnabled.Value;
            }

            bool enabled;
            try
            {
                enabled = new AppSettingsStore().Load().Settings.CharacterEnabled;
            }
            catch (InvalidOperationException)
            {
                enabled = AppSettings.DefaultCharacterEnabled;
            }

            _cachedCharacterEnabled = enabled;
            return enabled;
        }

        /// <summary>
        /// <see cref="_session"/> が見つかった時点で <see cref="_characterView"/> を TTS・判定結果へ接続する
        /// （<c>TryAcquireSession</c> から呼ぶ）。
        /// </summary>
        private void BindCharacterViewToSession()
        {
            // TtsSyncPlayer は GameSession と同じ GameObject（GameSession.prefab）に載っている
            // （docs/tts.md §6.6）。見つからない場合（プレハブに未配置・TTS 無効ビルド等）は
            // null のまま Bind し、判定結果（GameSession.QuestionResolved）だけ反映する。
            var ttsSyncPlayer = _session.GetComponent<TtsSyncPlayer>();

            // issue #28 M1 / issue #127: 読み上げの初期化（TtsSyncCoordinator が InitializeAsync を呼ぶ）より前に、
            // アプリ設定（tts.*）と利用規約の同意確認（FR-74 / FR-75）を差し込む（GameView.Tts.cs）。
            WireTtsSyncPlayer(ttsSyncPlayer);

            // #212: 回答権を得たのが自分か（BuzzSelf / BuzzOther）と、選択式で自分の正誤を優先するのに使う。
            // 選択式で選ばなかったときの表情（選べる立場なら時間切れ、司会専任・休みなら全体の結果）にも使う。
            _characterView?.Bind(ttsSyncPlayer, _session, () => LocalClientIdOrNull, IsLocalChoiceAnswerer);
        }

        /// <summary>
        /// このクライアントが、いまの選択式の問題で選べる立場か（#212）。司会専任のホストと、
        /// 同期された進行状態（#194）でこの問題が休み（次問休み）の参加者は選べない。
        /// </summary>
        private bool IsLocalChoiceAnswerer() =>
            _session != null
            && CharacterChoiceOutcome.IsLocalAnswerer(
                _isModerator, LocalClientIdOrNull, _session.GetQuestionProgress(), _session.QuestionIndex.Value);

        /// <summary>
        /// <see cref="_characterView"/> を破棄する（<c>OnHide</c> から呼ぶ）。issue #172 レビュー L6:
        /// <see cref="VisibilityChanged"/> の購読解除に加え、付け外ししていた修飾クラス・外枠の表示を
        /// 既定状態（立ち絵あり＝クラス無し・外枠 Flex）へ戻してから参照を手放す。
        /// </summary>
        private void TeardownCharacterView()
        {
            if (_characterView != null)
            {
                _characterView.VisibilityChanged -= OnCharacterVisibilityChanged;
            }

            _characterView?.Dispose();
            _characterView = null;
            _characterRoot = null;

            _gameContentMain?.RemoveFromClassList(NoCharacterMainClassName);
            if (_characterSlot != null)
            {
                _characterSlot.style.display = DisplayStyle.Flex;
            }

            _gameContentMain = null;
            _characterSlot = null;
        }

        /// <summary>
        /// テスト専用: 立ち絵の同意判定を差し替える（issue #172 M1）。<see cref="_characterView"/> が
        /// 生成されていなければ何もしない（<c>character.enabled</c> = false のときはテスト対象外）。
        /// <see cref="CharacterView.SetConsentCheckForTesting"/> は設定と同時に即時再評価するため、
        /// <see cref="OnCharacterVisibilityChanged"/>（<see cref="CharacterView.VisibilityChanged"/> 購読）が
        /// 正しく発火して <see cref="UpdateGameContentLayoutForCharacterVisibility"/> に反映されることを
        /// テストから確認できる。
        /// </summary>
        internal void SetCharacterConsentCheckForTesting(Func<bool> consentCheck) =>
            _characterView?.SetConsentCheckForTesting(consentCheck);

        /// <summary>
        /// 状態（表情、#86 / #212）ごとの立ち絵テクスチャをプロセス単位でキャッシュして返す（レビュー M6）。
        /// どれかの状態が初めて必要になったときに全状態をまとめて読み込み（#212）、以後は同じ参照を返す
        /// （View の表示のたび・状態が戻るたびに読み直さない）。
        /// 表情差分が未生成で複数の状態が同じファイル（待機の差分 or 従来の全身 PNG）へ
        /// フォールバックする場合、その PNG の読み込みも 1 回で済ませる。
        /// </summary>
        /// <param name="state">立ち絵の表示状態。</param>
        /// <returns>テクスチャと、その状態専用の表情差分だったか（ティントの判断に使う、#212）。</returns>
        /// <remarks>
        /// テストから直接呼べるよう <c>internal</c>（PR #135 レビュー M4）。
        /// 呼んだ後は <see cref="ResetCharacterTextureCacheForTesting"/> でキャッシュを戻すこと
        /// （本番ではプロセス終了まで保持する。9 枚で約 60MB、#212 で許容）。
        /// </remarks>
        internal static CharacterTextureResolution ResolveCharacterTexture(CharacterState state)
        {
            if (!_characterTextureByState.TryGetValue(state, out var cached))
            {
                PreloadCharacterTextures();
                _characterTextureByState.TryGetValue(state, out cached);
            }

            return cached;
        }

        /// <summary>
        /// 全状態の立ち絵をまとめて読み込む（#212）。読み込み済みの状態は飛ばす。
        /// かかった時間はログに残す（docs/tts.md §8.3.1 の実測の根拠）。
        /// </summary>
        private static void PreloadCharacterTextures()
        {
            var states = (CharacterState[])Enum.GetValues(typeof(CharacterState));

            // 配置先が決まらない（AppPaths 未設定 = Boot を経由しない起動）ならどの状態も解決できない。
            // 状態ごとに同じ警告を並べず、1 本だけ残して立ち絵なしで続行する。
            string idlePath;
            try
            {
                idlePath = CharacterImagePaths.ResolveImagePathCandidates(CharacterState.Idle)[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameView] 立ち絵のパス解決に失敗しました（{e.GetType().Name}）: {e.Message}");
                foreach (var state in states)
                {
                    _characterTextureByState[state] = default;
                }

                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var pathCountBefore = _characterTextureByPath.Count;

            foreach (var state in states)
            {
                if (!_characterTextureByState.ContainsKey(state))
                {
                    _characterTextureByState[state] = LoadCharacterTextureWithPathCache(state);
                }
            }

            stopwatch.Stop();

            var loadedCount = 0;
            var dedicatedCount = 0;
            foreach (var resolution in _characterTextureByState.Values)
            {
                if (resolution.Texture != null)
                {
                    loadedCount++;
                }

                if (resolution.IsDedicated)
                {
                    dedicatedCount++;
                }
            }

            if (loadedCount == 0)
            {
                // どの状態も読めなかった（1 枚も配置・生成されていない）。状態ごとに警告を並べず、1 本にまとめる。
                // 一部の状態だけ読めない場合（例: 待機の差分が無く、不正解の差分だけある）は、読めない状態では
                // 立ち絵を隠すだけで進行する（CharacterView）。下の読み込みログの状態数で分かるので警告は出さない。
                Debug.LogWarning($"[GameView] {CharacterImageLoader.NotPlacedMessage}"
                    + $"（{idlePath} ほか）。External/README.md §5.5 / §5.7 の手順で配置・生成してください。");
                return;
            }

            Debug.Log($"[GameView] 立ち絵を読み込みました（{_characterTextureByState.Count} 状態中 {loadedCount} 状態を表示可能、"
                + $"専用の表情差分 {dedicatedCount} 状態、PNG {_characterTextureByPath.Count - pathCountBefore} 件、"
                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms）。");
        }

        private static CharacterTextureResolution LoadCharacterTextureWithPathCache(CharacterState state)
        {
            IReadOnlyList<string> candidates;
            try
            {
                candidates = CharacterImagePaths.ResolveImagePathCandidates(state);
            }
            catch (Exception e)
            {
                // AppPaths 未設定（Boot を経由しない起動）など。立ち絵なしで続行する。
                Debug.LogWarning($"[GameView] 立ち絵のパス解決に失敗しました（{e.GetType().Name}、{state}）: {e.Message}");
                return default;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var path = candidates[i];

                // 先頭（添字 0）がその状態専用のファイル（CharacterImagePaths.GetFileNameCandidates）。
                var isDedicated = i == 0;
                if (_characterTextureByPath.TryGetValue(path, out var alreadyLoaded) && alreadyLoaded != null)
                {
                    return new CharacterTextureResolution(alreadyLoaded, isDedicated);
                }

                var result = CharacterImageLoader.LoadIfExists(path);
                if (result.IsPlaceholder)
                {
                    continue;
                }

                // Result.SourcePath は FileInfo.FullName（正規化済み）なので、候補のパス文字列と
                // 両方をキーにしておく（次回どちらで引いても当たる）。
                _characterTextureByPath[path] = result.Texture;
                if (!string.IsNullOrEmpty(result.SourcePath))
                {
                    _characterTextureByPath[result.SourcePath] = result.Texture;
                }

                return new CharacterTextureResolution(result.Texture, isDedicated);
            }

            // 未配置の警告は PreloadCharacterTextures が 1 本にまとめて出す。
            return default;
        }

        /// <summary>テスト専用: プロセス単位の立ち絵テクスチャキャッシュをリセットする（レビュー L5）。</summary>
        /// <remarks>
        /// PR #135 限定確認 LOW-2: 本メソッドはキャッシュ中のテクスチャを <b>破棄する</b>（再レビュー L2）。
        /// 呼ぶ前に、そのテクスチャを参照している <see cref="CharacterView"/> を
        /// <see cref="CharacterView.Dispose"/> しておくこと。破棄済みのテクスチャを
        /// <c>character-image</c> に載せたままにすると、描画時に "missing" なテクスチャを参照してしまう。
        /// </remarks>
        internal static void ResetCharacterTextureCacheForTesting()
        {
            // PR #135 再レビュー L2: キャッシュを捨てるだけだとテクスチャが解放されないまま
            // 孤児になる（本番はプロセス終了まで保持する設計なので問題にならないが、
            // テストは何度も作り直すため蓄積する）。
            // 状態側の値はパス側と同じ参照なので、パス側だけ破棄すれば足りる
            // （DestroyTexture は破棄済み・null に対して何もしないので二重呼び出しも安全）。
            foreach (var texture in _characterTextureByPath.Values)
            {
                CharacterImageLoader.DestroyTexture(texture);
            }

            _characterTextureByState.Clear();
            _characterTextureByPath.Clear();
        }

        /// <summary>
        /// プロセス単位の <c>character.enabled</c> キャッシュ（L5）を破棄し、次回の
        /// <see cref="InitializeCharacterView"/> で <c>app-settings.json</c> を読み直させる。
        /// Settings 画面（<c>SettingsView.AppTab.cs</c>）がアプリ設定を保存したときに呼ぶ
        /// （PR #92 再レビュー M-4）。立ち絵テクスチャのキャッシュは設定と無関係なので触らない。
        /// </summary>
        internal static void InvalidateCharacterEnabledCache() => _cachedCharacterEnabled = null;
    }
}
