using System;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Core.Reveal;

namespace TsumugiQuiz.Room
{
    /// <summary>
    /// ルーム設定の全項目を1つにまとめた不変オブジェクト（docs/room-settings.md §1/§2 の「ルーム設定」の行、issue #26）。
    /// ホストがゲーム開始時に確定させ、以後クライアントへ読み取り専用として同期する値の集合（docs §0/§4）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 問題選択・制限時間・得点・自動進行・途中参加の可否は既存の <see cref="Room.SessionSettings"/>
    /// （#19）にすでに実装されているため、それをそのまま内包する（重複実装を避ける）。
    /// 本クラスはそれ以外の項目（<c>host.role</c> / <c>room.maxPlayers</c> / <c>choices.shuffleDisplay</c> /
    /// <c>buzz.allowDuringReading</c> / <c>tts.*</c>）を追加する。<c>answer.choiceTimeLimitSec</c> は
    /// #17 で <see cref="Core.QuizTimeLimits.ChoiceTimeLimitSec"/> として実装済みのため、
    /// <see cref="Session"/>（<see cref="Room.SessionSettings.TimeLimits"/>）へ委譲する（#26 統括判断）。
    /// </para>
    /// <para>
    /// <c>GameSession</c> / <c>LobbyState</c> への実際の配線（<see cref="ToSessionSettings"/> 等の戻り値を
    /// <c>Configure</c> / <c>StartSession</c> / <c>ConfigureRoom</c> に渡すこと、および
    /// <c>TtsSyncCoordinator.ReadingEnabled</c> / <c>Speed</c> / <c>ReadyTimeoutSec</c> / <c>LeadTimeSec</c>
    /// への接続）は #27 で実装した（<c>TsumugiQuiz.Network.RoomSettingsSync</c> /
    /// <c>RoomSettingsApplier</c>、docs/network.md §12）。
    /// </para>
    /// </remarks>
    public sealed class RoomSettings
    {
        /// <summary><c>host.role</c> の既定値。</summary>
        public const HostRole DefaultHostRole = HostRole.Player;

        /// <summary><c>room.maxPlayers</c> の既定値。</summary>
        public const int DefaultMaxPlayers = 6;

        /// <summary><c>room.maxPlayers</c> の下限。</summary>
        public const int MinMaxPlayers = 2;

        /// <summary><c>room.maxPlayers</c> の上限。</summary>
        public const int MaxMaxPlayers = 12;

        /// <summary><c>choices.shuffleDisplay</c> の既定値。</summary>
        public const bool DefaultShuffleChoiceDisplay = true;

        /// <summary><c>buzz.allowDuringReading</c> の既定値（仮決め K12）。</summary>
        public const bool DefaultAllowDuringReading = true;

        /// <summary>
        /// <c>answer.choiceTimeLimitSec</c> の既定値（秒）。<see cref="QuizTimeLimits.DefaultChoiceTimeLimitSec"/>
        /// と同じ（既定値の出典を 1 か所にする、#26 統括判断）。
        /// </summary>
        public const double DefaultChoiceTimeLimitSec = QuizTimeLimits.DefaultChoiceTimeLimitSec;

        /// <summary><c>answer.choiceTimeLimitSec</c> の下限（秒）。</summary>
        public const double MinChoiceTimeLimitSec = 1.0;

        /// <summary><c>answer.choiceTimeLimitSec</c> の上限（秒）。</summary>
        public const double MaxChoiceTimeLimitSec = 60.0;

        /// <summary><c>tts.enabled</c> の既定値。</summary>
        public const bool DefaultTtsEnabled = true;

        /// <summary><c>tts.speed</c> の既定値。</summary>
        public const double DefaultTtsSpeed = 1.0;

        /// <summary><c>tts.speed</c> の下限。</summary>
        public const double MinTtsSpeed = 0.5;

        /// <summary><c>tts.speed</c> の上限。</summary>
        public const double MaxTtsSpeed = 2.0;

        /// <summary><c>tts.readyTimeoutMs</c> の既定値（ミリ秒）。</summary>
        public const int DefaultTtsReadyTimeoutMs = 3000;

        /// <summary><c>tts.readyTimeoutMs</c> の下限（ミリ秒）。</summary>
        public const int MinTtsReadyTimeoutMs = 0;

        /// <summary><c>tts.readyTimeoutMs</c> の上限（ミリ秒）。</summary>
        public const int MaxTtsReadyTimeoutMs = 15000;

        /// <summary><c>tts.leadTimeSec</c> の既定値（秒）。</summary>
        public const double DefaultTtsLeadTimeSec = 0.3;

        /// <summary><c>tts.leadTimeSec</c> の下限（秒）。</summary>
        public const double MinTtsLeadTimeSec = 0.1;

        /// <summary><c>tts.leadTimeSec</c> の上限（秒）。</summary>
        public const double MaxTtsLeadTimeSec = 2.0;

        /// <summary>
        /// <c>question.revealMsPerChar</c> の既定値（ミリ秒／文字、issue #144）。
        /// 出典は <see cref="QuestionRevealSchedule.DefaultMsPerChar"/>（既定値を 1 か所にする）。
        /// </summary>
        public const int DefaultQuestionRevealMsPerChar = QuestionRevealSchedule.DefaultMsPerChar;

        /// <summary><c>question.revealMsPerChar</c> の下限（0 = 文字送りせず一括表示）。</summary>
        public const int MinQuestionRevealMsPerChar = QuestionRevealSchedule.MinMsPerChar;

        /// <summary><c>question.revealMsPerChar</c> の上限。</summary>
        public const int MaxQuestionRevealMsPerChar = QuestionRevealSchedule.MaxMsPerChar;

        /// <summary>
        /// <c>display.showScores</c> の既定値（issue #194。既定は「表示する」でユーザー承認済み）。
        /// </summary>
        public const bool DefaultShowScores = true;

        private static readonly RoomSettings DefaultInstance = new RoomSettings();

        /// <summary>
        /// 設定値を指定して生成する。範囲外の値は呼び出し側の不具合として例外にする
        /// （プリセット/設定ファイル由来の値のクランプは <see cref="RoomSettingsValidator"/> が行う）。
        /// </summary>
        /// <param name="hostRole"><c>host.role</c>。</param>
        /// <param name="maxPlayers"><c>room.maxPlayers</c>（2〜12）。</param>
        /// <param name="session">
        /// 問題選択・制限時間・得点・自動進行・途中参加の設定。null なら <see cref="Room.SessionSettings.Default"/>。
        /// </param>
        /// <param name="shuffleChoiceDisplay"><c>choices.shuffleDisplay</c>。</param>
        /// <param name="allowDuringReading"><c>buzz.allowDuringReading</c>。</param>
        /// <param name="ttsEnabled"><c>tts.enabled</c>。</param>
        /// <param name="ttsSpeed"><c>tts.speed</c>（0.5〜2.0）。</param>
        /// <param name="ttsReadyTimeoutMs"><c>tts.readyTimeoutMs</c>（0〜15000ms）。</param>
        /// <param name="ttsLeadTimeSec"><c>tts.leadTimeSec</c>（0.1〜2.0 秒）。</param>
        /// <param name="questionRevealMsPerChar">
        /// <c>question.revealMsPerChar</c>（0〜500 ミリ秒／文字、0 = 一括表示。issue #144）。
        /// </param>
        /// <param name="showScores"><c>display.showScores</c>（issue #194）。</param>
        /// <exception cref="ArgumentOutOfRangeException">いずれかの値が範囲外のとき。</exception>
        /// <remarks>
        /// <c>answer.choiceTimeLimitSec</c> は <paramref name="session"/>（<see cref="Room.SessionSettings.TimeLimits"/>.
        /// <see cref="QuizTimeLimits.ChoiceTimeLimitSec"/>）で指定する。範囲（1〜60秒）は
        /// <see cref="RoomSettingsValidator"/> がクランプ時に適用し、本コンストラクタでは再検証しない
        /// （<c>buzz.timeLimitSec</c> 等、他の <see cref="QuizTimeLimits"/> 由来の値と同じ扱い）。
        /// </remarks>
        public RoomSettings(
            HostRole hostRole = DefaultHostRole,
            int maxPlayers = DefaultMaxPlayers,
            SessionSettings session = null,
            bool shuffleChoiceDisplay = DefaultShuffleChoiceDisplay,
            bool allowDuringReading = DefaultAllowDuringReading,
            bool ttsEnabled = DefaultTtsEnabled,
            double ttsSpeed = DefaultTtsSpeed,
            int ttsReadyTimeoutMs = DefaultTtsReadyTimeoutMs,
            double ttsLeadTimeSec = DefaultTtsLeadTimeSec,
            int questionRevealMsPerChar = DefaultQuestionRevealMsPerChar,
            bool showScores = DefaultShowScores)
        {
            RequireInRange(maxPlayers, MinMaxPlayers, MaxMaxPlayers, nameof(maxPlayers));
            RequireInRange(ttsSpeed, MinTtsSpeed, MaxTtsSpeed, nameof(ttsSpeed));
            RequireInRange(ttsReadyTimeoutMs, MinTtsReadyTimeoutMs, MaxTtsReadyTimeoutMs, nameof(ttsReadyTimeoutMs));
            RequireInRange(ttsLeadTimeSec, MinTtsLeadTimeSec, MaxTtsLeadTimeSec, nameof(ttsLeadTimeSec));
            RequireInRange(
                questionRevealMsPerChar, MinQuestionRevealMsPerChar, MaxQuestionRevealMsPerChar,
                nameof(questionRevealMsPerChar));

            HostRole = hostRole;
            MaxPlayers = maxPlayers;
            Session = session ?? SessionSettings.Default;
            ShuffleChoiceDisplay = shuffleChoiceDisplay;
            AllowDuringReading = allowDuringReading;
            TtsEnabled = ttsEnabled;
            TtsSpeed = ttsSpeed;
            TtsReadyTimeoutMs = ttsReadyTimeoutMs;
            TtsLeadTimeSec = ttsLeadTimeSec;
            QuestionRevealMsPerChar = questionRevealMsPerChar;
            ShowScores = showScores;
        }

        /// <summary>docs/room-settings.md の既定値（組み込みプリセット「標準」に相当）。</summary>
        public static RoomSettings Default => DefaultInstance;

        /// <summary>ホストの役割（<c>host.role</c>）。</summary>
        public HostRole HostRole { get; }

        /// <summary>最大参加人数（<c>room.maxPlayers</c>）。</summary>
        public int MaxPlayers { get; }

        /// <summary>問題選択・制限時間・得点・自動進行・途中参加の設定（#19 の <see cref="Room.SessionSettings"/>）。</summary>
        public SessionSettings Session { get; }

        /// <summary>選択肢の表示順をシャッフルするか（<c>choices.shuffleDisplay</c>）。</summary>
        public bool ShuffleChoiceDisplay { get; }

        /// <summary>
        /// 読み上げ中でも早押しを受け付けるか（<c>buzz.allowDuringReading</c>、仮決め K12）。
        /// false の場合は読み上げ完了後のみ受付を開始する。
        /// </summary>
        public bool AllowDuringReading { get; }

        /// <summary>
        /// 選択式の回答制限時間（<c>answer.choiceTimeLimitSec</c>）。#17 で実装された
        /// <see cref="Room.SessionSettings.TimeLimits"/>.<see cref="QuizTimeLimits.ChoiceTimeLimitSec"/> に委譲する
        /// （値を二重に持たない、#26 統括判断）。
        /// </summary>
        public double ChoiceTimeLimitSec => Session.TimeLimits.ChoiceTimeLimitSec;

        /// <summary>読み上げ ON/OFF（<c>tts.enabled</c>）。</summary>
        public bool TtsEnabled { get; }

        /// <summary>読み上げ速度（<c>tts.speed</c>）。</summary>
        public double TtsSpeed { get; }

        /// <summary>全クライアントの TTS Ready を待つ上限・ミリ秒（<c>tts.readyTimeoutMs</c>）。</summary>
        public int TtsReadyTimeoutMs { get; }

        /// <summary><c>playAtServerTime</c> の先行時間・秒（<c>tts.leadTimeSec</c>）。</summary>
        public double TtsLeadTimeSec { get; }

        /// <summary>
        /// 問題文を固定速度で文字送りするときの 1 文字あたりのミリ秒（<c>question.revealMsPerChar</c>、issue #144）。
        /// 部屋として読み上げが無い場合（<c>tts.enabled = false</c>、または再生開始時刻が届かないまま早押し受付が開いた）に使う。0 なら文字送りせず一括表示。
        /// </summary>
        public int QuestionRevealMsPerChar { get; }

        /// <summary>
        /// Game 画面の参加者パネルに全員の得点を表示するか（<c>display.showScores</c>、issue #194）。
        /// 見せ方の切り替えであり秘匿ではない（得点表は全員へ同期されている）。司会には設定に関わらず表示し、
        /// Result 画面の順位表には影響しない（統括判断 #194）。
        /// </summary>
        public bool ShowScores { get; }

        /// <summary>問題選択の設定（<c>questions.*</c>）。</summary>
        public QuestionSelectionSettings Questions => Session.Questions;

        /// <summary>制限時間（<c>buzz.timeLimitSec</c> / <c>answer.freeTextTimeLimitSec</c> ほか）。</summary>
        public QuizTimeLimits TimeLimits => Session.TimeLimits;

        /// <summary>得点・お手つきペナルティ・誤答後の再開放の設定（<c>score.*</c> ほか）。</summary>
        public ScoringSettings Scoring => Session.Scoring;

        /// <summary>結果表示から次の問題へ自動で進むまでの秒数（<c>result.autoAdvanceSec</c>）。</summary>
        public double ResultAutoAdvanceSec => Session.ResultAutoAdvanceSec;

        /// <summary>ゲーム進行中の途中参加を許可するか（<c>network.allowLateJoin</c>）。</summary>
        public bool AllowLateJoin => Session.AllowLateJoin;

        /// <summary>
        /// <c>GameSession.StartSession</c> に渡す設定に変換する（#27 で配線済み）。
        /// </summary>
        public SessionSettings ToSessionSettings() => Session;

        /// <summary>得点規則（<c>TsumugiQuiz.Core</c>）に変換する。</summary>
        public ScoringSettings ToScoringSettings() => Session.Scoring;

        /// <summary>制限時間（<c>TsumugiQuiz.Core</c>）に変換する。</summary>
        public QuizTimeLimits ToQuizTimeLimits() => Session.TimeLimits;

        /// <summary>問題選択の設定に変換する。</summary>
        public QuestionSelectionSettings ToQuestionSelectionSettings() => Session.Questions;

        /// <summary>ホストの役割だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithHostRole(HostRole hostRole) =>
            new RoomSettings(
                hostRole, MaxPlayers, Session, ShuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>最大参加人数だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithMaxPlayers(int maxPlayers) =>
            new RoomSettings(
                HostRole, maxPlayers, Session, ShuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>問題選択・制限時間・得点・自動進行・途中参加の設定だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithSession(SessionSettings session) =>
            new RoomSettings(
                HostRole, MaxPlayers, session, ShuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>選択肢の表示順シャッフルの可否だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithShuffleChoiceDisplay(bool shuffleChoiceDisplay) =>
            new RoomSettings(
                HostRole, MaxPlayers, Session, shuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>読み上げ中でも早押しを受け付けるかだけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithAllowDuringReading(bool allowDuringReading) =>
            new RoomSettings(
                HostRole, MaxPlayers, Session, ShuffleChoiceDisplay, allowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>
        /// 選択式の回答制限時間だけを差し替えた新しいインスタンスを返す
        /// （<see cref="Session"/> の <see cref="QuizTimeLimits"/> を新しい値で作り直す）。
        /// </summary>
        public RoomSettings WithChoiceTimeLimitSec(double choiceTimeLimitSec)
        {
            var newTimeLimits = new QuizTimeLimits(
                TimeLimits.BuzzTimeLimitSec, TimeLimits.AnswerTimeLimitSec, choiceTimeLimitSec, TimeLimits.CollectWindowSec);
            return WithSession(Session.WithTimeLimits(newTimeLimits));
        }

        /// <summary>読み上げ設定（ON/OFF・速度・Ready待ち・先行時間）だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithTts(bool ttsEnabled, double ttsSpeed, int ttsReadyTimeoutMs, double ttsLeadTimeSec) =>
            new RoomSettings(
                HostRole, MaxPlayers, Session, ShuffleChoiceDisplay, AllowDuringReading,
                ttsEnabled, ttsSpeed, ttsReadyTimeoutMs, ttsLeadTimeSec, QuestionRevealMsPerChar, ShowScores);

        /// <summary>問題文の文字送り速度（<c>question.revealMsPerChar</c>）だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithQuestionRevealMsPerChar(int questionRevealMsPerChar) =>
            new RoomSettings(
                HostRole, MaxPlayers, Session, ShuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, questionRevealMsPerChar, ShowScores);

        /// <summary>参加者パネルに全員の得点を表示するか（<c>display.showScores</c>）だけを差し替えた新しいインスタンスを返す。</summary>
        public RoomSettings WithShowScores(bool showScores) =>
            new RoomSettings(
                HostRole, MaxPlayers, Session, ShuffleChoiceDisplay, AllowDuringReading,
                TtsEnabled, TtsSpeed, TtsReadyTimeoutMs, TtsLeadTimeSec, QuestionRevealMsPerChar, showScores);

        private static void RequireInRange(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の範囲である必要があります。");
            }
        }

        private static void RequireInRange(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の有限の範囲である必要があります。");
            }
        }
    }
}
