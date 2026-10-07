using System;
using System.Collections.Generic;
using TsumugiQuiz.Core;
using TsumugiQuiz.Core.Network;
using TsumugiQuiz.Room;
using Unity.Netcode;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// ルーム設定（docs/room-settings.md §1/§2 の「ルーム設定」の行）をクライアントへ配るための
    /// 同期用データ（<c>NetworkVariable&lt;RoomSettingsPayload&gt;</c>、docs/network.md §1.2 / §12、issue #27）。
    /// サーバーが書き、クライアントは読むだけ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// NGO の <c>NetworkVariable&lt;T&gt;</c> は、<see cref="INetworkSerializable"/> な型については
    /// <c>unmanaged</c> な構造体（<c>InitializeSerializer_UnmanagedINetworkSerializable</c>）か
    /// 参照型（<c>InitializeSerializer_ManagedINetworkSerializable</c>）のいずれかしか扱えない。
    /// 本プロジェクトは前者（構造体）を採用しているため、可変長の文字列配列
    /// （<c>questions.setIds</c> / <c>questions.tagFilter</c>）はこのペイロードに載せられない。
    /// この 2 つは「サーバーが出題列を組み立てるための入力」でしかなく、クライアントの表示・判定・
    /// ローカル合成のどれにも使わないため、サーバー側の <see cref="RoomSettings"/> にのみ保持する
    /// （docs/network.md §12、docs/room-settings.md §4）。
    /// </para>
    /// <para>
    /// <c>BufferSerializer</c> がフィールドを <c>ref</c> で受け取るため、
    /// 本プロジェクトの「不変データ優先」の方針の例外としてフィールドは可変（public）にしている
    /// （<see cref="ScoreEntry"/> と同じ）。値を書き換えるのではなく、
    /// <see cref="FromRoomSettings"/> で作り直して使うこと。
    /// </para>
    /// </remarks>
    public struct RoomSettingsPayload : INetworkSerializable, IEquatable<RoomSettingsPayload>
    {
        /// <summary><c>host.role</c>（<see cref="Core.Network.HostRole"/> のバイト表現）。</summary>
        public byte HostRole;

        /// <summary><c>questions.typeFilter</c>（<see cref="Room.QuestionTypeFilter"/> のバイト表現）。</summary>
        public byte QuestionTypeFilter;

        /// <summary><c>score.penaltyType</c>（<see cref="PenaltyKind"/> のバイト表現）。</summary>
        public byte PenaltyType;

        /// <summary><c>questions.imageOnly</c>。</summary>
        public bool QuestionImageOnly;

        /// <summary><c>questions.shuffleOrder</c>。</summary>
        public bool QuestionShuffleOrder;

        /// <summary><c>choices.shuffleDisplay</c>。</summary>
        public bool ShuffleChoiceDisplay;

        /// <summary><c>buzz.allowDuringReading</c>。</summary>
        public bool AllowDuringReading;

        /// <summary><c>buzz.reopenAfterWrongAnswer</c>。</summary>
        public bool ReopenAfterWrongAnswer;

        /// <summary><c>answer.singleAttemptOnly</c>。</summary>
        public bool SingleAttemptOnly;

        /// <summary><c>display.showScores</c>（issue #194）。</summary>
        public bool ShowScores;

        /// <summary><c>network.allowLateJoin</c>。</summary>
        public bool AllowLateJoin;

        /// <summary><c>tts.enabled</c>。</summary>
        public bool TtsEnabled;

        /// <summary><c>room.maxPlayers</c>。</summary>
        public int MaxPlayers;

        /// <summary><c>questions.count</c>（0 = 全問）。</summary>
        public int QuestionCount;

        /// <summary><c>buzz.collectWindowMs</c>（ミリ秒。docs/room-settings.md §6 のとおり一次情報はミリ秒）。</summary>
        public int CollectWindowMs;

        /// <summary><c>score.correctPoints</c>。</summary>
        public int CorrectPoints;

        /// <summary><c>score.incorrectPoints</c>。</summary>
        public int IncorrectPoints;

        /// <summary><c>score.penaltyMinusPoints</c>。</summary>
        public int PenaltyMinusPoints;

        /// <summary><c>tts.readyTimeoutMs</c>。</summary>
        public int TtsReadyTimeoutMs;

        /// <summary><c>question.revealMsPerChar</c>（問題文の文字送り速度、issue #144）。</summary>
        public int QuestionRevealMsPerChar;

        /// <summary><c>buzz.timeLimitSec</c>。</summary>
        public double BuzzTimeLimitSec;

        /// <summary><c>answer.freeTextTimeLimitSec</c>。</summary>
        public double AnswerTimeLimitSec;

        /// <summary><c>answer.choiceTimeLimitSec</c>。</summary>
        public double ChoiceTimeLimitSec;

        /// <summary><c>result.autoAdvanceSec</c>（0 = 手動）。</summary>
        public double ResultAutoAdvanceSec;

        /// <summary><c>tts.speed</c>。</summary>
        public double TtsSpeed;

        /// <summary><c>tts.leadTimeSec</c>。</summary>
        public double TtsLeadTimeSec;

        /// <summary>
        /// 既定のルーム設定（<see cref="RoomSettings.Default"/>）に対応するペイロード。
        /// <c>NetworkVariable</c> の初期値として使う。
        /// </summary>
        public static RoomSettingsPayload Default => FromRoomSettings(RoomSettings.Default);

        /// <summary>
        /// <see cref="RoomSettings"/> からペイロードを組み立てる（サーバー側）。
        /// </summary>
        /// <param name="settings">配布するルーム設定。</param>
        /// <returns>組み立てたペイロード。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> が null のとき。</exception>
        public static RoomSettingsPayload FromRoomSettings(RoomSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var timeLimits = settings.TimeLimits;
            var scoring = settings.Scoring;
            var questions = settings.Questions;

            return new RoomSettingsPayload
            {
                HostRole = (byte)settings.HostRole,
                QuestionTypeFilter = (byte)questions.TypeFilter,
                PenaltyType = (byte)scoring.PenaltyType,
                QuestionImageOnly = questions.ImageOnly,
                QuestionShuffleOrder = questions.ShuffleOrder,
                ShuffleChoiceDisplay = settings.ShuffleChoiceDisplay,
                AllowDuringReading = settings.AllowDuringReading,
                ReopenAfterWrongAnswer = scoring.ReopenAfterWrongAnswer,
                SingleAttemptOnly = scoring.SingleAttemptOnly,
                ShowScores = settings.ShowScores,
                AllowLateJoin = settings.AllowLateJoin,
                TtsEnabled = settings.TtsEnabled,
                MaxPlayers = settings.MaxPlayers,
                QuestionCount = questions.Count,
                CollectWindowMs = ToMilliseconds(timeLimits.CollectWindowSec),
                CorrectPoints = scoring.CorrectPoints,
                IncorrectPoints = scoring.IncorrectPoints,
                PenaltyMinusPoints = scoring.PenaltyMinusPoints,
                TtsReadyTimeoutMs = settings.TtsReadyTimeoutMs,
                QuestionRevealMsPerChar = settings.QuestionRevealMsPerChar,
                BuzzTimeLimitSec = timeLimits.BuzzTimeLimitSec,
                AnswerTimeLimitSec = timeLimits.AnswerTimeLimitSec,
                ChoiceTimeLimitSec = timeLimits.ChoiceTimeLimitSec,
                ResultAutoAdvanceSec = settings.ResultAutoAdvanceSec,
                TtsSpeed = settings.TtsSpeed,
                TtsLeadTimeSec = settings.TtsLeadTimeSec,
            };
        }

        /// <summary>
        /// 受信したペイロードを検証前の生の値（<see cref="RoomSettingsInput"/>）へ戻す。
        /// 受信側は必ずこれを <see cref="RoomSettingsValidator.Validate"/> に通してから適用すること
        /// （境界での入力検証。バージョン差異・改造クライアントで範囲外の値が届きうる）。
        /// </summary>
        /// <param name="input">検証前の入力。</param>
        /// <param name="unknownEnumKeys">
        /// 未知の列挙値が入っていた設定キー（<c>host.role</c> / <c>questions.typeFilter</c> /
        /// <c>score.penaltyType</c>）。無ければ空。呼び出し元はこれをログ・警告一覧に載せる。
        /// </param>
        /// <returns>未知の列挙値が 1 つも無ければ true。</returns>
        public bool TryToRoomSettingsInput(out RoomSettingsInput input, out IReadOnlyList<string> unknownEnumKeys)
            => TryToRoomSettingsInput(null, null, out input, out unknownEnumKeys);

        /// <summary>
        /// ペイロードへ載らない <c>questions.setIds</c> / <c>questions.tagFilter</c> を補って戻す。
        /// サーバーが自分のルーム設定を検証し直すときに使う（クライアントは補う値を持たないので null）。
        /// </summary>
        /// <param name="setIds"><c>questions.setIds</c>。無ければ null。</param>
        /// <param name="tagFilter"><c>questions.tagFilter</c>。無ければ null。</param>
        /// <param name="input">検証前の入力。</param>
        /// <param name="unknownEnumKeys">未知の列挙値が入っていた設定キー。</param>
        /// <returns>未知の列挙値が 1 つも無ければ true。</returns>
        public bool TryToRoomSettingsInput(
            IReadOnlyList<string> setIds,
            IReadOnlyList<string> tagFilter,
            out RoomSettingsInput input,
            out IReadOnlyList<string> unknownEnumKeys)
        {
            List<string> unknown = null;
            var hostRole = ToHostRole(HostRole, ref unknown);
            var typeFilter = ToTypeFilter(QuestionTypeFilter, ref unknown);
            var penaltyKind = ToPenaltyKind(PenaltyType, ref unknown);

            input = new RoomSettingsInput
            {
                HostRole = HostRoles.ToKey(hostRole),
                MaxPlayers = MaxPlayers,
                QuestionsTypeFilter = QuestionTypeFilters.ToKey(typeFilter),
                QuestionsImageOnly = QuestionImageOnly,
                QuestionsCount = QuestionCount,
                QuestionsShuffleOrder = QuestionShuffleOrder,
                ChoicesShuffleDisplay = ShuffleChoiceDisplay,
                BuzzTimeLimitSec = BuzzTimeLimitSec,
                BuzzAllowDuringReading = AllowDuringReading,
                BuzzCollectWindowMs = CollectWindowMs,
                BuzzReopenAfterWrongAnswer = ReopenAfterWrongAnswer,
                AnswerFreeTextTimeLimitSec = AnswerTimeLimitSec,
                AnswerChoiceTimeLimitSec = ChoiceTimeLimitSec,
                AnswerSingleAttemptOnly = SingleAttemptOnly,
                ScoreCorrectPoints = CorrectPoints,
                ScoreIncorrectPoints = IncorrectPoints,
                ScorePenaltyType = PenaltyKinds.ToKey(penaltyKind),
                ScorePenaltyMinusPoints = PenaltyMinusPoints,
                TtsEnabled = TtsEnabled,
                TtsSpeed = TtsSpeed,
                TtsReadyTimeoutMs = TtsReadyTimeoutMs,
                TtsLeadTimeSec = TtsLeadTimeSec,
                NetworkAllowLateJoin = AllowLateJoin,
                ResultAutoAdvanceSec = ResultAutoAdvanceSec,
                QuestionRevealMsPerChar = QuestionRevealMsPerChar,
                DisplayShowScores = ShowScores,

                // questions.setIds / questions.tagFilter はペイロードに載らない（本クラスの remarks 参照）。
                // サーバーが自分の設定を検証し直すときだけ、引数で補える。
                QuestionsSetIds = ToList(setIds),
                QuestionsTagFilter = ToList(tagFilter),
            };

            unknownEnumKeys = unknown ?? EmptyKeys;
            return unknown == null;
        }

        /// <inheritdoc />
        public void NetworkSerialize<TSerializer>(BufferSerializer<TSerializer> serializer)
            where TSerializer : IReaderWriter
        {
            serializer.SerializeValue(ref HostRole);
            serializer.SerializeValue(ref QuestionTypeFilter);
            serializer.SerializeValue(ref PenaltyType);
            serializer.SerializeValue(ref QuestionImageOnly);
            serializer.SerializeValue(ref QuestionShuffleOrder);
            serializer.SerializeValue(ref ShuffleChoiceDisplay);
            serializer.SerializeValue(ref AllowDuringReading);
            serializer.SerializeValue(ref ReopenAfterWrongAnswer);
            serializer.SerializeValue(ref SingleAttemptOnly);
            serializer.SerializeValue(ref ShowScores);
            serializer.SerializeValue(ref AllowLateJoin);
            serializer.SerializeValue(ref TtsEnabled);
            serializer.SerializeValue(ref MaxPlayers);
            serializer.SerializeValue(ref QuestionCount);
            serializer.SerializeValue(ref CollectWindowMs);
            serializer.SerializeValue(ref CorrectPoints);
            serializer.SerializeValue(ref IncorrectPoints);
            serializer.SerializeValue(ref PenaltyMinusPoints);
            serializer.SerializeValue(ref TtsReadyTimeoutMs);
            serializer.SerializeValue(ref QuestionRevealMsPerChar);
            serializer.SerializeValue(ref BuzzTimeLimitSec);
            serializer.SerializeValue(ref AnswerTimeLimitSec);
            serializer.SerializeValue(ref ChoiceTimeLimitSec);
            serializer.SerializeValue(ref ResultAutoAdvanceSec);
            serializer.SerializeValue(ref TtsSpeed);
            serializer.SerializeValue(ref TtsLeadTimeSec);
        }

        /// <inheritdoc />
        public bool Equals(RoomSettingsPayload other) =>
            HostRole == other.HostRole
            && QuestionTypeFilter == other.QuestionTypeFilter
            && PenaltyType == other.PenaltyType
            && QuestionImageOnly == other.QuestionImageOnly
            && QuestionShuffleOrder == other.QuestionShuffleOrder
            && ShuffleChoiceDisplay == other.ShuffleChoiceDisplay
            && AllowDuringReading == other.AllowDuringReading
            && ReopenAfterWrongAnswer == other.ReopenAfterWrongAnswer
            && SingleAttemptOnly == other.SingleAttemptOnly
            && ShowScores == other.ShowScores
            && AllowLateJoin == other.AllowLateJoin
            && TtsEnabled == other.TtsEnabled
            && MaxPlayers == other.MaxPlayers
            && QuestionCount == other.QuestionCount
            && CollectWindowMs == other.CollectWindowMs
            && CorrectPoints == other.CorrectPoints
            && IncorrectPoints == other.IncorrectPoints
            && PenaltyMinusPoints == other.PenaltyMinusPoints
            && TtsReadyTimeoutMs == other.TtsReadyTimeoutMs
            && QuestionRevealMsPerChar == other.QuestionRevealMsPerChar
            && BuzzTimeLimitSec.Equals(other.BuzzTimeLimitSec)
            && AnswerTimeLimitSec.Equals(other.AnswerTimeLimitSec)
            && ChoiceTimeLimitSec.Equals(other.ChoiceTimeLimitSec)
            && ResultAutoAdvanceSec.Equals(other.ResultAutoAdvanceSec)
            && TtsSpeed.Equals(other.TtsSpeed)
            && TtsLeadTimeSec.Equals(other.TtsLeadTimeSec);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is RoomSettingsPayload other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            hash.Add(HostRole);
            hash.Add(QuestionTypeFilter);
            hash.Add(PenaltyType);
            hash.Add(ShowScores);
            hash.Add(MaxPlayers);
            hash.Add(QuestionCount);
            hash.Add(CollectWindowMs);
            hash.Add(CorrectPoints);
            hash.Add(IncorrectPoints);
            hash.Add(PenaltyMinusPoints);
            hash.Add(TtsReadyTimeoutMs);
            hash.Add(QuestionRevealMsPerChar);
            hash.Add(BuzzTimeLimitSec);
            hash.Add(AnswerTimeLimitSec);
            hash.Add(ChoiceTimeLimitSec);
            hash.Add(ResultAutoAdvanceSec);
            hash.Add(TtsSpeed);
            hash.Add(TtsLeadTimeSec);
            return hash.ToHashCode();
        }

        /// <inheritdoc />
        public override string ToString() =>
            $"role={HostRole} maxPlayers={MaxPlayers} "
            + $"buzz={BuzzTimeLimitSec}s answer={AnswerTimeLimitSec}s choice={ChoiceTimeLimitSec}s "
            + $"ttsEnabled={TtsEnabled} ttsSpeed={TtsSpeed}";

        /// <summary>未知の列挙値が無かったときに返す空の一覧。</summary>
        private static readonly IReadOnlyList<string> EmptyKeys = Array.Empty<string>();

        /// <summary>秒をミリ秒（四捨五入）へ変換する。</summary>
        private static int ToMilliseconds(double seconds) => (int)Math.Round(seconds * 1000.0);

        /// <summary>読み取り専用リストを <see cref="RoomSettingsInput"/> が要求する型へ写す。</summary>
        private static List<string> ToList(IReadOnlyList<string> values)
        {
            if (values == null)
            {
                return null;
            }

            var list = new List<string>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                list.Add(values[i]);
            }

            return list;
        }

        /// <summary>未知の値を検出したキーを記録する。</summary>
        private static void AddUnknown(ref List<string> unknown, string key)
        {
            unknown ??= new List<string>();
            if (!unknown.Contains(key))
            {
                unknown.Add(key);
            }
        }

        /// <summary>バイト値を <see cref="Core.Network.HostRole"/> へ戻す（未定義値は既定値 + 記録）。</summary>
        private static Core.Network.HostRole ToHostRole(byte value, ref List<string> unknown)
        {
            switch (value)
            {
                case (byte)Core.Network.HostRole.Player:
                    return Core.Network.HostRole.Player;
                case (byte)Core.Network.HostRole.Moderator:
                    return Core.Network.HostRole.Moderator;
                default:
                    AddUnknown(ref unknown, HostRoles.SettingsKey);
                    return RoomSettings.DefaultHostRole;
            }
        }

        /// <summary>バイト値を <see cref="Room.QuestionTypeFilter"/> へ戻す（未定義値は既定値 + 記録）。</summary>
        private static Room.QuestionTypeFilter ToTypeFilter(byte value, ref List<string> unknown)
        {
            switch (value)
            {
                case (byte)Room.QuestionTypeFilter.Both:
                    return Room.QuestionTypeFilter.Both;
                case (byte)Room.QuestionTypeFilter.FreeText:
                    return Room.QuestionTypeFilter.FreeText;
                case (byte)Room.QuestionTypeFilter.Choice:
                    return Room.QuestionTypeFilter.Choice;
                default:
                    AddUnknown(ref unknown, QuestionTypeFilters.SettingsKey);
                    return QuestionSelectionSettings.DefaultTypeFilter;
            }
        }

        /// <summary>バイト値を <see cref="PenaltyKind"/> へ戻す（未定義値は既定値 + 記録）。</summary>
        private static Core.PenaltyKind ToPenaltyKind(byte value, ref List<string> unknown)
        {
            switch (value)
            {
                case (byte)Core.PenaltyKind.SkipNext:
                    return Core.PenaltyKind.SkipNext;
                case (byte)Core.PenaltyKind.MinusPoints:
                    return Core.PenaltyKind.MinusPoints;
                case (byte)Core.PenaltyKind.None:
                    return Core.PenaltyKind.None;
                default:
                    AddUnknown(ref unknown, PenaltyKinds.SettingsKey);
                    return ScoreRules.DefaultPenaltyKind;
            }
        }
    }
}
