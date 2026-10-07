using System;
using TsumugiQuiz.Room;
using TsumugiQuiz.Tts;
using UnityEngine;

namespace TsumugiQuiz.UI.Views.Settings
{
    /// <summary>
    /// アプリ設定の保存が読み上げ（<see cref="TtsService"/>）へどう反映されたか（#138 レビュー M-2）。
    /// 保存直後のメッセージを出し分けるために <see cref="TtsAppSettingsReloader.ReloadIfNeeded"/> が返す。
    /// </summary>
    internal enum TtsReloadOutcome
    {
        /// <summary>何もしなかった（未初期化・未同意・変化なし・保存先を解決できない）。</summary>
        NotNeeded = 0,

        /// <summary>その場で再初期化を始めた。</summary>
        Started = 1,

        /// <summary>初期化中だったので、完了後に再評価するよう予約した。</summary>
        Deferred = 2,
    }

    /// <summary>
    /// 設定画面で <c>tts.*</c> を保存したときに、<b>アプリを再起動せずに</b>
    /// <see cref="TtsService"/> へ反映させる（issue #138）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TtsSettingsProviderFactory"/> が返す provider は「都度読み」なので、
    /// <b>まだ初期化していない</b> <see cref="TtsService"/> には保存した値がそのまま届く（何もしなくてよい）。
    /// 問題は<b>初期化済み</b>のときで、<see cref="TtsService.Settings"/> は
    /// <see cref="TtsService.Initialize"/> の 1 回だけ読まれ、話者・スタイル・配置
    /// （<c>AssetPathOverride</c>）は合成エンジンの生成時に固定される。したがって
    /// <b>再初期化（<see cref="TtsService.RetryInitializeAsync"/>）以外に反映する手段が無い</b>
    /// （docs/tts.md §6.5）。
    /// </para>
    /// <para>
    /// 再初期化を始める条件は次の 4 つを<b>すべて</b>満たすときだけに絞る。
    /// <list type="number">
    ///   <item><description>
    ///     すでに初期化を始めている（<see cref="TtsServiceState.NotInitialized"/> ではない）。
    ///     未初期化のまま再初期化を始めると、<b>設定を保存しただけで voicevox_core（ONNX Runtime）を
    ///     プロセス全体へロード</b>してしまう（docs/tts.md §6.5、#25 H-5 の方針に反する）
    ///   </description></item>
    ///   <item><description>
    ///     利用規約に同意している（FR-74 / FR-75）。撤回後にロードし直してはいけない。
    ///     判定は他の注入箇所と同じ <see cref="TtsConsentCheckFactory.Build"/> から取る（#138 レビュー M-4）
    ///   </description></item>
    ///   <item><description>保存先を解決できる（<c>AppPaths</c> 設定済み）</description></item>
    ///   <item><description>
    ///     保存内容が適用中の <see cref="TtsService.Settings"/> と<b>実際に違う</b>。
    ///     同じ値で作り直すと、モデルの読み込み（数秒）を無駄にやり直すことになる
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>初期化中（<see cref="TtsServiceState.Initializing"/>）は、その場で再初期化しない</b>
    /// （#138 レビュー H-1）。<see cref="TtsService.RetryInitializeAsync"/> は同期部分で直前のエンジンを
    /// 破棄する（<c>DisposeEngine</c>）が、初期化タスクが走っている最中はその完了を
    /// <b>最大 10 秒（<c>ShutdownWaitMs</c>）メインスレッドで待つ</b>。設定画面はロビーからも開けるため
    /// （<c>LobbyView</c> の「設定」）、ロビーでの先行初期化（<c>TtsSyncCoordinator.OnNetworkSpawn</c>）が
    /// 走っている最中に保存すると、そのまま画面が固まる。そこで初期化中は
    /// <see cref="TtsService.StatusChanged"/> を購読して<b>完了を待ってから</b>判定をやり直す。
    /// <see cref="TtsServiceState.Ready"/> まで進んでいれば直前のエンジンの破棄は即時に終わるので、
    /// そこでの再初期化はメインスレッドをブロックしない。
    /// 初期化が失敗して <see cref="TtsServiceState.NotAvailable"/> になった場合は再初期化せず、
    /// その旨をログに残すだけにする（配置が直っていない状態で読み直しても同じ失敗を繰り返すため。
    /// 直したあとは「音声合成」パネルの「再試行」から明示的にやり直せる）。
    /// </para>
    /// <para>
    /// 予約（購読）は<b>常に 1 つだけ</b>持つ。保存を連打しても購読が積み上がらないよう、
    /// 新しい予約を作る前に古い予約を解除し、届いた時点で自分自身も解除する。
    /// </para>
    /// <para>
    /// 読むのは <see cref="TtsSettingsProviderFactory.BuildOrNull"/> が作る provider、つまり
    /// <b>既定の保存先</b>（<c>AppPaths.DataRoot/app-settings.json</c>）である。
    /// ただし保存直後の判定には、呼び出し側が<b>いま保存した内容</b>（<c>justSaved</c>）を渡すので、
    /// <see cref="SettingsView.AppSettingsStoreFactory"/> をテスト用のパスへ差し替えている構成でも
    /// 判定がずれない（#138 レビュー M-5）。
    /// </para>
    /// </remarks>
    internal static class TtsAppSettingsReloader
    {
        /// <summary>初期化完了を待っている購読（無ければ null）。<see cref="_pendingService"/> と対で持つ。</summary>
        private static Action<TtsServiceStatus> _pendingHandler;

        private static TtsService _pendingService;

        /// <summary>
        /// テスト専用: 本番の再初期化（<see cref="TtsService.RetryInitializeAsync"/>）の代わりに呼ぶ処理。
        /// null（既定）なら本番の実装を使う。
        /// <see cref="TtsService.RetryInitializeAsync"/> は本物の voicevox_core を読み込みに行くため、
        /// <c>External/</c> 非依存が前提の EditMode テスト（docs/tts.md §11.1）からは差し替えて使う。
        /// </summary>
        internal static Action<TtsService, ITtsSettingsProvider> RestartOverrideForTesting { get; set; }

        /// <summary>
        /// 保存済みの <c>tts.*</c> を読み直し、必要なら <paramref name="service"/> の再初期化を始める
        /// （初期化中なら完了後に持ち越す）。完了は待たない（初期化はバックグラウンドスレッドで進み、
        /// 結果は <see cref="TtsService.Status"/> に出る）。
        /// </summary>
        /// <param name="service">対象。null（Boot を経由していない）なら何もしない。</param>
        /// <param name="justSaved">
        /// いま保存したアプリ設定。判定にはこの内容を使う（#138 レビュー M-5）。
        /// null なら保存先から読み直す（初期化完了を待ってからの再評価はこちら）。
        /// </param>
        /// <returns>何をしたか。保存直後のメッセージの出し分けに使う。</returns>
        public static TtsReloadOutcome ReloadIfNeeded(TtsService service, AppSettings justSaved = null)
        {
            if (service == null)
            {
                return TtsReloadOutcome.NotNeeded;
            }

            var provider = TtsSettingsProviderFactory.BuildOrNull();
            if (provider == null)
            {
                return TtsReloadOutcome.NotNeeded;
            }

            var desired = justSaved != null ? AppSettingsAdapters.ToTtsSettings(justSaved) : provider.Load();
            if (!NeedsReinitialize(service, desired))
            {
                return TtsReloadOutcome.NotNeeded;
            }

            if (service.Status.State == TtsServiceState.Initializing)
            {
                // H-1: いま RetryInitializeAsync を呼ぶと、進行中の初期化タスクの完了を
                // メインスレッドで最大 10 秒待つことになる。完了してから判定し直す。
                Defer(service);
                Debug.Log(
                    "[TtsAppSettingsReloader] 読み上げの初期化中です。完了後にアプリ設定を反映します" +
                    $"（{desired.Describe()}）。");
                return TtsReloadOutcome.Deferred;
            }

            // #138 レビュー L-3: いまここで再初期化するので、初期化完了待ちの予約が残っていても用済み。
            // 残したままだと、この再初期化が起こす状態遷移（NotInitialized → Initializing → …）を
            // 古い予約が拾ってしまう。実害のある動きにはならないが、購読を宙に浮かせない。
            CancelPending();

            Debug.Log($"[TtsAppSettingsReloader] アプリ設定が変わったため読み上げを初期化し直します（{desired.Describe()}）。");
            Restart(service, provider);
            return TtsReloadOutcome.Started;
        }

        /// <summary>
        /// 再初期化が必要かを判定する（<b>副作用なし</b>）。判定条件はクラスのコメントを参照。
        /// 「初期化中かどうか」はここでは見ない（必要かどうかと、いま実行してよいかは別の軸）。
        /// </summary>
        /// <param name="service">対象。null なら false。</param>
        /// <param name="desired">保存済みアプリ設定から作った、これから使いたい設定。null なら false。</param>
        internal static bool NeedsReinitialize(TtsService service, TtsSettings desired)
        {
            if (service == null || desired == null)
            {
                return false;
            }

            if (service.Status.State == TtsServiceState.NotInitialized)
            {
                // まだ初期化していない。次に誰かが初期化するとき、都度読みの provider が
                // 保存済みの最新値を読むので、ここで何かする必要は無い（#138）。
                return false;
            }

            if (!TtsConsentCheckFactory.Build()())
            {
                // 未同意・撤回後に voicevox_core を読み込み直さない（FR-74 / FR-75）。
                return false;
            }

            return !AreEquivalent(service.Settings, desired);
        }

        /// <summary>
        /// テスト専用: <c>TtsService.Update</c> が <see cref="TtsService.StatusChanged"/> を配る代わりに、
        /// 予約済みの継続へ状態を届ける。EditMode では <c>MonoBehaviour.Update</c> が走らないため、
        /// 「初期化完了後に 1 回だけ再初期化する」ことを確かめるにはこの入口が要る。
        /// </summary>
        /// <returns>届ける先（予約）があったか。</returns>
        internal static bool DeliverStatusForTesting(TtsServiceStatus status)
        {
            var handler = _pendingHandler;
            if (handler == null)
            {
                return false;
            }

            handler(status);
            return true;
        }

        /// <summary>
        /// テスト専用: 静的に持っている予約・差し替えを捨てる（テスト間で状態を持ち越さない）。
        /// </summary>
        internal static void ResetForTesting()
        {
            CancelPending();
            RestartOverrideForTesting = null;
        }

        /// <summary>初期化の完了を待つ予約を作る。古い予約は必ず捨ててから登録する（多重登録しない）。</summary>
        private static void Defer(TtsService service)
        {
            CancelPending();

            _pendingService = service;
            _pendingHandler = OnPendingStatusChanged;
            service.StatusChanged += _pendingHandler;
        }

        /// <summary>予約を解除する。予約が無ければ何もしない。</summary>
        private static void CancelPending()
        {
            if (_pendingHandler == null)
            {
                return;
            }

            // 予約先が破棄済み（シーン遷移・テストの後始末）なら購読の解除は不要・不可能。
            if (_pendingService != null)
            {
                _pendingService.StatusChanged -= _pendingHandler;
            }

            _pendingHandler = null;
            _pendingService = null;
        }

        /// <summary>
        /// 初期化中に保存された分の継続。<b>メインスレッドから呼ばれる</b>
        /// （<c>TtsService.Update</c> のポーリング経由）。
        /// </summary>
        private static void OnPendingStatusChanged(TtsServiceStatus status)
        {
            if (status.State == TtsServiceState.Initializing)
            {
                return;   // まだ終わっていない。
            }

            var service = _pendingService;
            CancelPending();   // 自己解除。以後の状態変化ではもう走らない。

            if (service == null)
            {
                return;
            }

            if (status.State == TtsServiceState.NotAvailable)
            {
                Debug.LogWarning(
                    "[TtsAppSettingsReloader] 読み上げを初期化できなかったため、保存したアプリ設定での再初期化は行いません" +
                    "（配置を直したあと「音声合成」の「再試行」からやり直せます）。");
                return;
            }

            // 待っている間にさらに保存された可能性があるので、保存先から読み直して判定し直す。
            ReloadIfNeeded(service);
        }

        /// <summary>再初期化の実行（テストからは差し替えられる）。</summary>
        private static void Restart(TtsService service, ITtsSettingsProvider provider)
        {
            var restartOverride = RestartOverrideForTesting;
            if (restartOverride != null)
            {
                restartOverride(service, provider);
                return;
            }

            RestartInitialization(service, provider);
        }

        /// <summary>
        /// <see cref="TtsService.RetryInitializeAsync"/> を投げっぱなしで呼ぶ。
        /// <c>async void</c> なので、<b>想定外の例外は必ずここで捕まえてログに残す</b>
        /// （<c>TtsStatusPanel.OnRetryClicked</c> の #97 L-1 と同じ理由）。
        /// 初期化の失敗そのものは <see cref="TtsService.Status"/> に落ちるので、ここへは来ない。
        /// </summary>
        private static async void RestartInitialization(TtsService service, ITtsSettingsProvider provider)
        {
            try
            {
                // #127: consentCheck を渡さないと TtsService 側の同意確認が null（＝制限なし）へ戻る。
                await service
                    .RetryInitializeAsync(
                        settingsProvider: provider, consentCheck: TtsConsentCheckFactory.Build())
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>適用中の設定と、これから使いたい設定が同じ内容か。どちらかが null なら false（作り直す）。</summary>
        private static bool AreEquivalent(TtsSettings applied, TtsSettings desired)
            => applied != null
               && desired != null
               && string.Equals(applied.SpeakerName, desired.SpeakerName, StringComparison.Ordinal)
               && string.Equals(applied.StyleName, desired.StyleName, StringComparison.Ordinal)
               && applied.CacheMaxBytes == desired.CacheMaxBytes
               && applied.CacheMaxEntries == desired.CacheMaxEntries
               && string.Equals(applied.AssetPathOverride, desired.AssetPathOverride, StringComparison.Ordinal);
    }
}
