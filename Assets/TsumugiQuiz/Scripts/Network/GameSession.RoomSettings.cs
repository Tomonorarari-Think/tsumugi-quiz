using TsumugiQuiz.Core;
using TsumugiQuiz.Room;
using UnityEngine;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// <see cref="GameSession"/> のうち、ルーム設定（<see cref="RoomSettingsSync"/>）との連携部分（issue #27）。
    /// </summary>
    /// <remarks>
    /// docs/room-settings.md §4 の「ゲーム開始操作の時点で設定値を確定（ロック）する」を実装する。
    /// 進行を始める直前（<see cref="GameSession.StartQuestion"/> の全検証を通過した時点）に
    /// <see cref="CommitRoomSettingsForStart"/> を呼び、サーバーが実際に使う値をそのまま
    /// <see cref="RoomSettingsSync"/> へ書いてからロックする。書き戻す値は
    /// <see cref="RoomSettingsValidator"/> のクランプ（docs/room-settings.md §5）を通るため、
    /// <b>範囲内の値であれば「クライアントが読む設定」と「サーバーが進行に使う設定」は一致する</b>。
    /// 範囲外の値を <see cref="GameSession.Configure"/> へ直接渡した場合はルーム設定側
    /// （＝ クライアント表示）だけが丸められてずれるので、
    /// <see cref="WarnIfCommittedSettingsDiffer"/> が差分を警告ログに出す。
    /// </remarks>
    public sealed partial class GameSession
    {
        /// <summary>同じ <c>NetworkObject</c> 上のルーム設定同期（無い構成もありうる）。</summary>
        private RoomSettingsSync _settingsSync;

        /// <summary>直近に <see cref="Configure"/> / <see cref="StartSession"/> で確定した得点設定。</summary>
        private ScoringSettings _scoringSettings = ScoringSettings.Default;

        /// <summary>同期できない制限時間の警告を、1 セッションにつき 1 回だけにするための記録。</summary>
        private bool _warnedLimitsNotSynced;

        /// <summary>
        /// 直近の <see cref="CommitRoomSettingsForStart"/> 以降に、<see cref="Configure"/> /
        /// <see cref="StartSession"/> が進行設定（<c>_limits</c> / <c>_scoringSettings</c>）を決め直したか。
        /// ロック中の開始で「確定済みのルーム設定と食い違う」警告を、決め直した直後の 1 回だけ出すために使う（#154 L1）。
        /// </summary>
        private bool _progressSettingsChangedSinceCommit;

        /// <summary>
        /// 同じ <c>NetworkObject</c> 上の <see cref="RoomSettingsSync"/>。
        /// プレハブに載っていない構成（コンポーネント順の検証用プレハブなど）では null。
        /// </summary>
        public RoomSettingsSync SettingsSync =>
            _settingsSync != null ? _settingsSync : (_settingsSync = GetComponent<RoomSettingsSync>());

        /// <summary>
        /// ゲーム開始操作の時点でルーム設定を確定（ロック）する（サーバーのみ）。
        /// ロック済みなら何もしない（2 問目以降の <see cref="StartQuestion"/> ではここを素通りする）。
        /// </summary>
        private void CommitRoomSettingsForStart()
        {
            var sync = SettingsSync;
            if (sync == null || !sync.IsSpawned || !IsServer)
            {
                WarnIfLimitsNotSynced();
                return;
            }

            if (!sync.IsLocked)
            {
                sync.TrySetSettings(BuildRoomSettingsForStart(sync.Current));
                WarnIfCommittedSettingsDiffer(sync.Current);
            }
            else if (_progressSettingsChangedSinceCommit)
            {
                WarnIfLockedSettingsDiffer(sync.Current);
            }

            _progressSettingsChangedSinceCommit = false;
            sync.LockForGameStart();
        }

        /// <summary>
        /// ルーム設定がロックされたまま進行設定を決め直した場合（例: 全問終了後にロビーへ戻らず、
        /// 明示した <see cref="SessionSettings"/> で <see cref="StartSession"/> を呼んだ）に、
        /// 確定済みのルーム設定との食い違いを警告する（#154 L1）。
        /// </summary>
        /// <remarks>
        /// ロック中は <see cref="RoomSettingsSync.Current"/> を書き換えない（docs/room-settings.md §4）ため、
        /// クライアントは古い制限時間・得点設定を読み続け、残り時間表示がずれる。
        /// 開始そのものは拒否しない（既存の呼び出し経路を壊さないため）。是正はロビーへ戻ってから設定し直すこと。
        /// </remarks>
        /// <param name="committed">確定済み（ロック中）のルーム設定。</param>
        private void WarnIfLockedSettingsDiffer(RoomSettings committed)
        {
            var differences = RoomSettingsDiff.Describe(_limits, _scoringSettings, committed);
            if (differences.Count == 0)
            {
                return;
            }

            Debug.LogWarning(
                "[GameSession] ルーム設定がロック中のため、進行設定の変更をクライアントへ配れません。"
                + "クライアントの残り時間・得点表示がホストとずれます（ロビーへ戻ってから設定し直してください）: "
                + string.Join(" / ", differences));
        }

        /// <summary>
        /// 確定したルーム設定（クライアントが読む値）と、サーバーが進行に使う値
        /// （<c>_limits</c> / <c>_scoringSettings</c>）の食い違いを警告する。
        /// </summary>
        /// <remarks>
        /// <see cref="RoomSettingsSync.TrySetSettings"/> は渡された値を
        /// <see cref="RoomSettingsValidator"/> に通すため、範囲外の値は境界へクランプされる
        /// （docs/room-settings.md §5）。一方サーバーの進行は <see cref="Configure"/> /
        /// <see cref="StartSession"/> で受け取った値をそのまま使い続けるので、
        /// 範囲外の値を直接渡された場合だけ両者がずれる。ずれを黙って進めると
        /// 「クライアントの残り時間・得点表示がホストと違う」ことになるため、ここでログに残す
        /// （挙動は変えない。是正はホスト側 UI（#28）の役割）。
        /// </remarks>
        /// <param name="committed">確定して配ったルーム設定。</param>
        private void WarnIfCommittedSettingsDiffer(RoomSettings committed)
        {
            var differences = RoomSettingsDiff.Describe(_limits, _scoringSettings, committed);
            if (differences.Count == 0)
            {
                return;
            }

            Debug.LogWarning(
                "[GameSession] 進行設定がルーム設定の範囲外だったためクランプされました。"
                + "サーバーの進行と、クライアントが読む表示用の値がずれます: "
                + string.Join(" / ", differences));
        }

        /// <summary>
        /// サーバーが実際に使っている進行設定を、ロビーで設定されたルーム設定へ重ねる。
        /// </summary>
        /// <param name="current">ロビーで設定された現在のルーム設定。</param>
        /// <returns>確定させるルーム設定。</returns>
        private RoomSettings BuildRoomSettingsForStart(RoomSettings current)
        {
            // 「今回の進行で実際に使う値」から組み立てる。制限時間と得点は Configure / StartSession が
            // 確定させた _limits / _scoringSettings が唯一の権威で、出題・自動進行・途中参加は
            // StartSession が確定させた _sessionSettings（単問モードでは未確定なのでルーム設定）を使う。
            var session = new SessionSettings(
                _sessionSettings?.Questions ?? current.Questions,
                _limits,
                _scoringSettings,
                _sessionSettings?.ResultAutoAdvanceSec ?? current.ResultAutoAdvanceSec,
                _sessionSettings?.AllowLateJoin ?? current.AllowLateJoin);

            return current.WithSession(session);
        }

        /// <summary>
        /// ロビーへ戻ったのでルーム設定のロックを解除する（サーバーのみ、#20 の <see cref="ReturnToLobby"/>）。
        /// <see cref="RoomSettingsSync"/> が無い構成では何もしない。
        /// </summary>
        private void UnlockRoomSettingsForLobby()
        {
            var sync = SettingsSync;
            if (sync == null || !sync.IsSpawned || !IsServer)
            {
                return;
            }

            sync.Unlock();
        }

        /// <summary>
        /// 前回の進行設定（<see cref="ActiveSessionSettings"/>）を捨てる（サーバーのみ）。
        /// <see cref="RoomSettingsSync.Unlock"/>（ロビーへ戻る、#20）から呼ばれ、
        /// 次の <see cref="StartSession"/> でロビーのルーム設定が確実に使われるようにする。
        /// </summary>
        internal void ForgetSessionSettings()
        {
            if (IsSpawned && !IsServer)
            {
                return;
            }

            _sessionSettings = null;
        }

        /// <summary>
        /// <see cref="RoomSettingsSync"/> が無い構成で既定値以外の制限時間を使うときに、
        /// クライアントの残り時間表示がずれることを警告する。
        /// </summary>
        private void WarnIfLimitsNotSynced()
        {
            if (_warnedLimitsNotSynced || ReferenceEquals(_limits, QuizTimeLimits.Default))
            {
                return;
            }

            _warnedLimitsNotSynced = true;

            Debug.LogWarning(
                "[GameSession] RoomSettingsSync が無いため、既定値以外の制限時間をクライアントへ同期できません。"
                + $"クライアントの残り時間表示は既定値（早押し {QuizTimeLimits.DefaultBuzzTimeLimitSec} 秒 / "
                + $"回答 {QuizTimeLimits.DefaultAnswerTimeLimitSec} 秒 / "
                + $"選択式 {QuizTimeLimits.DefaultChoiceTimeLimitSec} 秒）のままになります。");
        }
    }
}
