using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// 1 問の進行に使う制限時間（docs/room-settings.md §2、docs/network.md §6.3）。
    /// 既定値は定数で持ち、実際の値はルーム設定（<c>TsumugiQuiz.Room.RoomSettings</c>、#26）から渡され、
    /// <c>TsumugiQuiz.Network.RoomSettingsSync</c> がクライアントへ同期する（#27）。
    /// 不変オブジェクトとして扱い、変更は新しいインスタンスの生成で表す。
    /// </summary>
    public sealed class QuizTimeLimits
    {
        /// <summary>早押し受付が開いてから誰も押さない場合のタイムアウト秒数（<c>buzz.timeLimitSec</c> 既定 10）。</summary>
        public const double DefaultBuzzTimeLimitSec = 10.0;

        /// <summary>自由入力の回答制限時間（<c>answer.freeTextTimeLimitSec</c> 既定 15）。</summary>
        public const double DefaultAnswerTimeLimitSec = 15.0;

        /// <summary>
        /// 選択式の回答制限時間（<c>answer.choiceTimeLimitSec</c> 既定 20、docs/room-settings.md §2）。
        /// 早押しを介さず、全員がこの時間内に選択できる（<b>仮決め: #17</b>）。
        /// </summary>
        public const double DefaultChoiceTimeLimitSec = 20.0;

        /// <summary>
        /// <c>buzz.collectWindowMs</c> の既定値（ミリ秒、docs/network.md §6.3）。
        /// ミリ秒を基準の定数にして、秒単位の <see cref="DefaultCollectWindowSec"/> 等をここから導出する
        /// （#26 統括判断 L2。JSON/UI 側の単位はミリ秒のため、ミリ秒側を一次情報にする）。
        /// </summary>
        public const int DefaultCollectWindowMs = 150;

        /// <summary><c>buzz.collectWindowMs</c> の下限（ミリ秒、docs/network.md §6.3）。</summary>
        public const int MinCollectWindowMs = 50;

        /// <summary><c>buzz.collectWindowMs</c> の上限（ミリ秒、docs/network.md §6.3）。</summary>
        public const int MaxCollectWindowMs = 500;

        /// <summary>最初の押下受信からの集計窓（秒）。<see cref="DefaultCollectWindowMs"/> から導出。</summary>
        public const double DefaultCollectWindowSec = DefaultCollectWindowMs / 1000.0;

        /// <summary>
        /// <c>buzz.timeLimitSec</c> / <c>answer.freeTextTimeLimitSec</c> の下限（秒、docs/room-settings.md §1）。
        /// <c>TsumugiQuiz.Room.RoomSettingsValidator</c> がクランプ範囲としてここを参照する（#26 統括判断 M7）。
        /// </summary>
        public const double MinBuzzOrAnswerTimeLimitSec = 1.0;

        /// <summary><c>buzz.timeLimitSec</c> / <c>answer.freeTextTimeLimitSec</c> の上限（秒）。</summary>
        public const double MaxBuzzOrAnswerTimeLimitSec = 60.0;

        /// <summary><c>buzz.collectWindowMs</c> の下限（秒）。<see cref="MinCollectWindowMs"/> から導出。</summary>
        public const double MinCollectWindowSec = MinCollectWindowMs / 1000.0;

        /// <summary><c>buzz.collectWindowMs</c> の上限（秒）。<see cref="MaxCollectWindowMs"/> から導出。</summary>
        public const double MaxCollectWindowSec = MaxCollectWindowMs / 1000.0;

        private static readonly QuizTimeLimits DefaultInstance = new QuizTimeLimits(
            DefaultBuzzTimeLimitSec, DefaultAnswerTimeLimitSec, DefaultChoiceTimeLimitSec, DefaultCollectWindowSec);

        /// <summary>
        /// 制限時間を指定して生成する（<see cref="ChoiceTimeLimitSec"/> は既定値のまま）。
        /// </summary>
        /// <param name="buzzTimeLimitSec">早押し受付のタイムアウト（秒）。0 より大きい有限の値。</param>
        /// <param name="answerTimeLimitSec">回答入力の制限時間（秒）。0 より大きい有限の値。</param>
        /// <param name="collectWindowSec">早押しの集計窓（秒）。0 以上の有限の値。</param>
        /// <exception cref="ArgumentOutOfRangeException">いずれかの値が範囲外のとき。</exception>
        public QuizTimeLimits(double buzzTimeLimitSec, double answerTimeLimitSec, double collectWindowSec)
            : this(buzzTimeLimitSec, answerTimeLimitSec, DefaultChoiceTimeLimitSec, collectWindowSec)
        {
        }

        /// <summary>
        /// 選択式の制限時間（<paramref name="choiceTimeLimitSec"/>）も指定して生成する（#17）。
        /// </summary>
        /// <param name="buzzTimeLimitSec">早押し受付のタイムアウト（秒）。0 より大きい有限の値。</param>
        /// <param name="answerTimeLimitSec">回答入力の制限時間（秒）。0 より大きい有限の値。</param>
        /// <param name="choiceTimeLimitSec">選択式の回答制限時間（秒）。0 より大きい有限の値。</param>
        /// <param name="collectWindowSec">早押しの集計窓（秒）。0 以上の有限の値。</param>
        /// <exception cref="ArgumentOutOfRangeException">いずれかの値が範囲外のとき。</exception>
        public QuizTimeLimits(
            double buzzTimeLimitSec, double answerTimeLimitSec, double choiceTimeLimitSec, double collectWindowSec)
        {
            RequirePositive(buzzTimeLimitSec, nameof(buzzTimeLimitSec));
            RequirePositive(answerTimeLimitSec, nameof(answerTimeLimitSec));
            RequirePositive(choiceTimeLimitSec, nameof(choiceTimeLimitSec));

            if (!double.IsFinite(collectWindowSec) || collectWindowSec < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(collectWindowSec), collectWindowSec, "collectWindowSec は 0 以上の有限の値である必要があります。");
            }

            BuzzTimeLimitSec = buzzTimeLimitSec;
            AnswerTimeLimitSec = answerTimeLimitSec;
            ChoiceTimeLimitSec = choiceTimeLimitSec;
            CollectWindowSec = collectWindowSec;
        }

        /// <summary>docs/room-settings.md の既定値。</summary>
        public static QuizTimeLimits Default => DefaultInstance;

        /// <summary>早押し受付のタイムアウト（秒）。</summary>
        public double BuzzTimeLimitSec { get; }

        /// <summary>回答入力の制限時間（秒）。</summary>
        public double AnswerTimeLimitSec { get; }

        /// <summary>選択式の回答制限時間（秒、<b>仮決め: #17</b>）。</summary>
        public double ChoiceTimeLimitSec { get; }

        /// <summary>早押しの集計窓（秒）。</summary>
        public double CollectWindowSec { get; }

        private static void RequirePositive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は 0 より大きい有限の値である必要があります。");
            }
        }
    }
}
