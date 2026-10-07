using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;
using Unity.Netcode;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ホストが決めたルーム設定（docs/room-settings.md §1/§2）を
    /// <c>NetworkVariable&lt;RoomSettingsPayload&gt;</c> でクライアントへ配り、
    /// ゲーム開始操作の時点で確定（ロック）するコンポーネント（issue #27、docs/network.md §12）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>権限</b>: 書き込みはサーバー（ホスト）のみ（<c>NetworkVariableWritePermission.Server</c> = NGO 既定）。
    /// クライアントは <see cref="Current"/> を読むだけで、書こうとすると
    /// <see cref="TrySetSettings"/> が警告を出して false を返す。
    /// </para>
    /// <para>
    /// <b>ロック</b>: 「ゲーム開始」操作（<see cref="GameSession.StartQuestion"/> / <see cref="GameSession.StartSession"/>）
    /// の時点で <see cref="LockForGameStart"/> が呼ばれ、以後 <see cref="TrySetSettings"/> は拒否してログを出す
    /// （docs/room-settings.md §4）。ロビーへ戻る（#20）ときは <see cref="Unlock"/> で解除する。
    /// </para>
    /// <para>
    /// <b>再検証</b>: ホストが書く値もクライアントが受け取る値も、必ず
    /// <see cref="RoomSettingsValidator.Validate"/> を通してから <see cref="Current"/> に採用する
    /// （範囲外はクランプ、未知の列挙値は既定値へ）。ホストとクライアントで同じ検証を通すため、
    /// 「ホストが持つ値」と「クライアントが読む値」がクランプ結果まで含めて一致する。
    /// </para>
    /// <para>
    /// <b>UI からの書き込み（#28、Settings View）</b>: ホストはロビー表示中（ロック前）に
    /// <see cref="TrySetSettings"/> を呼んで値を差し替える。成功すると全クライアントへ同期され、
    /// <see cref="SettingsChanged"/> が全ピアで発火する。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RoomSettingsSync : NetworkBehaviour
    {
        /// <summary>ホストが指定した値が検証に引っかかったときのログ接頭辞。</summary>
        private const string InvalidSettingsLogPrefix =
            "[RoomSettingsSync] 指定されたルーム設定に不正な値がありました: ";

        /// <summary>ホストから届いた値が検証に引っかかったときのログ接頭辞。</summary>
        private const string ReceivedSettingsLogPrefix =
            "[RoomSettingsSync] ホストから届いたルーム設定に不正な値がありました: ";

        /// <summary>ルーム設定の同期値（サーバー書き込み・クライアント読み取り専用）。</summary>
        private readonly NetworkVariable<RoomSettingsPayload> _settings =
            new NetworkVariable<RoomSettingsPayload>(RoomSettingsPayload.Default);

        /// <summary>ゲーム開始で確定したか（true のあいだは変更できない）。</summary>
        private readonly NetworkVariable<bool> _locked = new NetworkVariable<bool>(false);

        /// <summary>
        /// いま有効なルーム設定。サーバーでは <see cref="TrySetSettings"/> の値を検証したもの
        /// （<c>questions.setIds</c> / <c>questions.tagFilter</c> を含む）、
        /// クライアントではペイロードを再検証したもの。
        /// </summary>
        private RoomSettings _current = RoomSettings.Default;

        /// <summary>直近の検証で出た警告（クランプ・未知の列挙値）。</summary>
        private IReadOnlyList<string> _warnings = Array.Empty<string>();

        /// <summary>スポーン前に <see cref="TrySetSettings"/> で渡された値（スポーン時に配る）。</summary>
        private RoomSettings _pendingInitial;

        /// <summary>同じ <c>NetworkObject</c> 上の読み上げ同期（<see cref="ApplyLocal"/> で使う）。</summary>
        private TtsSyncCoordinator _tts;

        /// <summary>同じ <c>NetworkObject</c> 上のゲーム進行（<see cref="Unlock"/> で使う）。</summary>
        private GameSession _session;

        /// <summary>
        /// ルーム設定の同期値。書き込み権限の確認・診断用で、UI 層からは使わない
        /// （UI は <see cref="Current"/> / <see cref="SettingsChanged"/> を読む）。
        /// </summary>
        internal NetworkVariable<RoomSettingsPayload> Settings => _settings;

        /// <summary>
        /// ロック状態の同期値。書き込み権限の確認・診断用で、UI 層からは使わない
        /// （UI は <see cref="IsLocked"/> / <see cref="LockChanged"/> を読む）。
        /// </summary>
        internal NetworkVariable<bool> Locked => _locked;

        /// <summary>ゲーム開始で確定済みか（true なら変更不可）。</summary>
        public bool IsLocked => _locked.Value;

        /// <summary>いま有効なルーム設定（null にはならない）。</summary>
        public RoomSettings Current => _current;

        /// <summary>
        /// 直近の検証で出た警告（範囲外のクランプ・未知の列挙値）。
        /// 正常な値だけなら空。
        /// </summary>
        public IReadOnlyList<string> LastValidationWarnings => _warnings;

        /// <summary>ルーム設定が変わったとき（全ピアで発火）。</summary>
        public event Action<RoomSettings> SettingsChanged;

        /// <summary>
        /// ロック状態が変わったとき（全ピアで発火）。
        /// スポーン時の初期状態では発火しないので、途中参加したクライアントは
        /// <see cref="IsLocked"/> を読んで初期表示を決めること。
        /// </summary>
        public event Action<bool> LockChanged;

        /// <summary>
        /// ルーム設定を差し替える（ホストのみ、ロック前のみ）。
        /// ロビーの設定 UI（#28）はこれを呼ぶ。渡した値は <see cref="RoomSettingsValidator"/> を通り、
        /// 範囲外はクランプされたうえで <see cref="Current"/> に入る。
        /// </summary>
        /// <param name="settings">新しいルーム設定。</param>
        /// <returns>差し替えられたら true。クライアントから呼んだ場合・ロック後は false。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null のとき。</exception>
        public bool TrySetSettings(RoomSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (IsSpawned && !IsServer)
            {
                Debug.LogWarning("[RoomSettingsSync] ルーム設定はホスト（サーバー）でのみ変更できます。");
                return false;
            }

            if (IsSpawned && _locked.Value)
            {
                Debug.LogWarning(
                    "[RoomSettingsSync] ゲーム開始で確定済みのため、ルーム設定は変更できません"
                    + "（docs/room-settings.md §4）。ロビーへ戻ってから変更してください。");
                return false;
            }

            if (!IsSpawned)
            {
                // スポーン前は NetworkVariable に書けないので、検証だけ済ませて保留し、
                // OnNetworkSpawn で配る（スポーン前でも Current は検証済みの値にする）。
                // 注意: ここで指定した host.role は、スポーン時の PublishServerSettings で
                // ReconcileHostRole が「設定側で変えていない」と判断するため、ロビー側の値に
                // 上書きされうる（_pendingInitial をそのまま _current にしているので差分が出ない）。
                // 現状のホスト開始経路（NetworkService.StartHost → スポーン → 設定 UI）では
                // スポーン前に設定を積むことはないため実害が無い。設計の整理は #28 で扱う
                // （docs/network.md §12.6）。
                _current = Validate(
                    RoomSettingsPayload.FromRoomSettings(settings),
                    settings.Questions.SetIds,
                    settings.Questions.TagFilter,
                    InvalidSettingsLogPrefix);
                _pendingInitial = _current;
                return true;
            }

            PublishServerSettings(settings);
            return true;
        }

        /// <summary>
        /// ゲーム開始操作の時点でルーム設定を確定（ロック）する（ホストのみ）。
        /// すでにロック済みなら何もしない。
        /// </summary>
        /// <returns>このコールでロックしたら true。</returns>
        public bool LockForGameStart()
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning("[RoomSettingsSync] ルーム設定の確定はホスト（サーバー）でのみ行えます。");
                return false;
            }

            if (_locked.Value)
            {
                return false;
            }

            _locked.Value = true;
            return true;
        }

        /// <summary>
        /// ロックを解除する（ホストのみ、<b>進行中は不可</b>）。ロビーへ戻ったときに呼ぶ
        /// （#20 の <c>ReturnToLobby</c>）。解除と同時に <see cref="GameSession"/> が保持している
        /// 「前回の進行設定」を捨て、次の開始でロビーの設定が確実に使われるようにする。
        /// </summary>
        /// <remarks>
        /// 出題・回答の最中に解除してしまうと、進行中のルールを書き換えられる状態になり、
        /// サーバーの進行（<c>QuizStateMachine</c>）とクライアントの表示が食い違う。
        /// そのため <see cref="GameSession.ServerPhase"/> が
        /// <see cref="QuizPhase.Lobby"/> / <see cref="QuizPhase.Finished"/> 以外のときは
        /// 警告ログを出して拒否する（docs/room-settings.md §3、docs/network.md §12.2）。
        /// </remarks>
        /// <returns>解除できたら true。ホスト以外・進行中は false。</returns>
        public bool Unlock()
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning("[RoomSettingsSync] ルーム設定のロック解除はホスト（サーバー）でのみ行えます。");
                return false;
            }

            var session = ResolveSession();
            var phase = session != null ? session.ServerPhase : QuizPhase.Lobby;
            if (phase != QuizPhase.Lobby && phase != QuizPhase.Finished)
            {
                Debug.LogWarning(
                    "[RoomSettingsSync] 進行中はルーム設定のロックを解除できません"
                    + $"（フェーズ: {phase}）。ロビーへ戻ってから解除してください。");
                return false;
            }

            _locked.Value = false;

            if (session != null)
            {
                session.ForgetSessionSettings();
            }

            return true;
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            _settings.OnValueChanged += HandleSettingsChanged;
            _locked.OnValueChanged += HandleLockChanged;

            if (IsServer)
            {
                // ホスト開始時の初期値（PR #92 再レビュー H-2）:
                // 「前回 Settings View で適用した下書き（app-settings.json の room.lastApplied）」を土台に、
                // host.role だけは HostSetup View の直前のトグル（PlayerPrefs）を重ねる
                // （ホストを開始する画面で今まさに切り替えた値が勝つ、という優先順位）。
                // 下書きが無い・壊れている場合は「標準」プリセット（docs/room-settings.md §3/§7.2）。
                var initial = _pendingInitial ?? LoadInitialSettingsFromDraft();
                _pendingInitial = null;
                _locked.Value = false;
                PublishServerSettings(initial);
            }
            else
            {
                AdoptPayload(_settings.Value);
            }

            ApplyLocal();
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            _settings.OnValueChanged -= HandleSettingsChanged;
            _locked.OnValueChanged -= HandleLockChanged;

            // 同じインスタンスが再スポーンされたとき、前回のロックを持ち越さない。
            // シャットダウン中は NetworkVariable への書き込みが配れないので触らない
            // （再スポーン時の OnNetworkSpawn で改めて false を書く）。
            if (IsSpawned
                && IsServer
                && NetworkManager != null
                && !NetworkManager.ShutdownInProgress)
            {
                _locked.Value = false;
            }

            _pendingInitial = null;
            _warnings = Array.Empty<string>();
        }

        /// <summary>
        /// サーバー側: 値を検証してから保持し、ペイロードへ写して配る。
        /// <c>questions.setIds</c> / <c>questions.tagFilter</c> はペイロードに載らないため、
        /// 検証の入力として明示的に引き継ぐ（サーバー側の <see cref="Current"/> には残る）。
        /// </summary>
        private void PublishServerSettings(RoomSettings settings)
        {
            settings = ReconcileHostRole(settings);

            var payload = RoomSettingsPayload.FromRoomSettings(settings);
            _current = Validate(
                payload,
                settings.Questions.SetIds,
                settings.Questions.TagFilter,
                InvalidSettingsLogPrefix);

            // 検証でクランプされた可能性があるので、配るのは検証後の値。
            var validatedPayload = RoomSettingsPayload.FromRoomSettings(_current);
            if (validatedPayload.Equals(_settings.Value))
            {
                // 同値なら NetworkVariable を汚さない（差分を出さない）。
                // setIds / tagFilter だけが変わった場合もここを通る（_current には反映済み）。
                //
                // ここでは ApplyLocal() を呼ばない（PR #91 レビュー LOW-3 への回答）。
                // ペイロードが同値なら RoomSettingsApplier が使う項目（host.role / room.maxPlayers /
                // network.allowLateJoin / tts.*）も必ず同値で、適用しても結果は変わらない。
                // 一方 RoomSettingsApplier は tts.enabled を TtsSyncCoordinator.ReadingEnabled へ
                // 書き戻すため、呼ぶと「実行時に SetReadingEnabled(false) した状態」を
                // ゲーム開始の確定（CommitRoomSettingsForStart → 同値の TrySetSettings）で
                // 踏み潰してしまう（docs/network.md §12.6 の ReadingEnabled と tts.enabled の関係）。
                SettingsChanged?.Invoke(_current);
                return;
            }

            _settings.Value = validatedPayload;
        }

        /// <summary>受信したペイロードを再検証して採用する（クライアント側）。</summary>
        private void AdoptPayload(RoomSettingsPayload payload)
        {
            _current = Validate(
                payload,
                null,
                null,
                ReceivedSettingsLogPrefix);
        }

        /// <summary>
        /// ペイロードを <see cref="RoomSettingsValidator"/> に通し、警告を
        /// <see cref="LastValidationWarnings"/> とログへ残す。
        /// </summary>
        private RoomSettings Validate(
            RoomSettingsPayload payload,
            IReadOnlyList<string> setIds,
            IReadOnlyList<string> tagFilter,
            string logPrefix)
        {
            var warnings = new List<string>();

            if (!payload.TryToRoomSettingsInput(setIds, tagFilter, out var input, out var unknownEnumKeys))
            {
                for (var i = 0; i < unknownEnumKeys.Count; i++)
                {
                    warnings.Add($"{unknownEnumKeys[i]} に未知の値が入っていたため既定値を使用します。");
                }
            }

            var result = RoomSettingsValidator.Validate(input);
            warnings.AddRange(result.Warnings);
            _warnings = warnings;

            if (warnings.Count > 0)
            {
                Debug.LogWarning(logPrefix + string.Join(" / ", warnings));
            }

            return result.Settings;
        }

        private void HandleSettingsChanged(RoomSettingsPayload previous, RoomSettingsPayload next)
        {
            if (!IsServer)
            {
                AdoptPayload(next);
            }

            ApplyLocal();
            SettingsChanged?.Invoke(_current);
        }

        private void HandleLockChanged(bool previous, bool next) => LockChanged?.Invoke(next);

        /// <summary>
        /// <c>host.role</c> をロビーと突き合わせる（サーバーのみ）。
        /// </summary>
        /// <remarks>
        /// <c>host.role</c> は <see cref="LobbyState.Role"/> にも同じ値が載っており、
        /// 名簿の席数計算（<c>LobbyRoster</c>、#7）と司会操作の判定（<c>IsHostModerator</c>、#20）は
        /// そちらを読む。ロビー側を直接書き換える経路（#5 の HostSetup / #20）と、
        /// 設定 UI からの <see cref="TrySetSettings"/> のどちらでも食い違わないよう、次の規則で解決する。
        /// <list type="bullet">
        ///   <item><description>
        ///     呼び出し元が <c>host.role</c> を変えた（現在値と違う）なら、その値を採ってロビーへ反映する
        ///   </description></item>
        ///   <item><description>
        ///     変えていないなら、ロビー側の値を採る（ロビーを直接書き換えた変更を踏み潰さない）
        ///   </description></item>
        /// </list>
        /// </remarks>
        private RoomSettings ReconcileHostRole(RoomSettings settings)
        {
            if (!IsServer)
            {
                return settings;
            }

            var lobby = LobbyState.Find(NetworkManager);
            if (lobby == null || !lobby.IsSpawned)
            {
                return settings;
            }

            if (settings.HostRole != _current.HostRole)
            {
                // 設定側で変更された。ロビーへは ApplyLocal（RoomSettingsApplier）が反映する。
                return settings;
            }

            return settings.HostRole == lobby.Role.Value
                ? settings
                : settings.WithHostRole(lobby.Role.Value);
        }

        /// <summary>いま有効な設定を、同じ <c>NetworkObject</c> 上・ロビーの各コンポーネントへ適用する。</summary>
        private void ApplyLocal()
        {
            // ロビーはサーバーだけが書き換えるので、クライアントでは探しにいかない。
            var lobby = IsServer ? LobbyState.Find(NetworkManager) : null;
            RoomSettingsApplier.Apply(_current, IsServer, lobby, ResolveTts());
        }

        /// <summary>
        /// <c>app-settings.json</c> の下書き（<see cref="RoomSettingsDraft"/>）を読み、
        /// <c>host.role</c> だけ <see cref="HostRolePreference"/>（HostSetup View の暫定保存先）で上書きする。
        /// <c>TsumugiQuiz.Room</c> は Unity API 非依存なので、読み込み時の警告はここでログに出す。
        /// </summary>
        private static RoomSettings LoadInitialSettingsFromDraft()
        {
            var draft = RoomSettingsDraft.Load(out var warnings);

            for (var i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning($"[RoomSettingsSync] room.lastApplied: {warnings[i]}");
            }

            return draft.WithHostRole(HostRolePreference.Load());
        }

        /// <summary>同じ <c>GameObject</c> 上の <see cref="TtsSyncCoordinator"/>（キャッシュする）。</summary>
        private TtsSyncCoordinator ResolveTts() =>
            _tts != null ? _tts : (_tts = GetComponent<TtsSyncCoordinator>());

        /// <summary>同じ <c>GameObject</c> 上の <see cref="GameSession"/>（キャッシュする）。</summary>
        private GameSession ResolveSession() =>
            _session != null ? _session : (_session = GetComponent<GameSession>());
    }
}
