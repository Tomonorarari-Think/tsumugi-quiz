using System.Collections.Generic;
using System.Globalization;
using TsumugiQuiz.Core;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.Network
{
    /// <summary>
    /// 「サーバーが進行に使う値」（<see cref="QuizTimeLimits"/> / <see cref="ScoringSettings"/>）と
    /// 「クライアントへ配ったルーム設定」（<see cref="RoomSettings"/>）の食い違いを列挙する（issue #27）。
    /// </summary>
    /// <remarks>
    /// <see cref="GameSession"/> は開始時に自分の進行設定をルーム設定へ書き戻すが、
    /// その値は <see cref="RoomSettingsValidator"/> のクランプ（docs/room-settings.md §5）を通るため、
    /// 範囲外の値を <c>GameSession.Configure</c> へ直接渡した場合だけ両者がずれる。
    /// ずれたまま黙って進行すると「クライアントの残り時間・得点表示がホストと違う」ことになるので、
    /// ここで差分を文章にして警告ログに出す（docs/network.md §12.2）。
    /// </remarks>
    internal static class RoomSettingsDiff
    {
        /// <summary>差分を報告するときの小数の書式（秒の値を読みやすく丸める）。</summary>
        private const string DoubleFormat = "0.###";

        /// <summary>
        /// 進行設定と確定したルーム設定を突き合わせ、食い違う項目の説明を返す。
        /// </summary>
        /// <param name="limits">サーバーが進行に使う制限時間。</param>
        /// <param name="scoring">サーバーが進行に使う得点設定。</param>
        /// <param name="committed">確定して配ったルーム設定。</param>
        /// <returns>食い違いの説明（一致していれば空）。</returns>
        internal static IReadOnlyList<string> Describe(
            QuizTimeLimits limits, ScoringSettings scoring, RoomSettings committed)
        {
            var differences = new List<string>();
            if (committed == null)
            {
                return differences;
            }

            if (limits != null)
            {
                var synced = committed.TimeLimits;
                AddIfDifferent(differences, "buzz.timeLimitSec", limits.BuzzTimeLimitSec, synced.BuzzTimeLimitSec);
                AddIfDifferent(
                    differences, "answer.freeTextTimeLimitSec", limits.AnswerTimeLimitSec, synced.AnswerTimeLimitSec);
                AddIfDifferent(
                    differences, "answer.choiceTimeLimitSec", limits.ChoiceTimeLimitSec, synced.ChoiceTimeLimitSec);
                AddIfDifferent(differences, "buzz.collectWindowMs", limits.CollectWindowSec, synced.CollectWindowSec);
            }

            if (scoring != null)
            {
                var synced = committed.Scoring;
                AddIfDifferent(differences, "score.correctPoints", scoring.CorrectPoints, synced.CorrectPoints);
                AddIfDifferent(differences, "score.incorrectPoints", scoring.IncorrectPoints, synced.IncorrectPoints);
                AddIfDifferent(
                    differences, "score.penaltyType", scoring.PenaltyType.ToString(), synced.PenaltyType.ToString());
                AddIfDifferent(
                    differences, "score.penaltyMinusPoints", scoring.PenaltyMinusPoints, synced.PenaltyMinusPoints);
                AddIfDifferent(
                    differences,
                    "buzz.reopenAfterWrongAnswer",
                    scoring.ReopenAfterWrongAnswer.ToString(),
                    synced.ReopenAfterWrongAnswer.ToString());
                AddIfDifferent(
                    differences,
                    "answer.singleAttemptOnly",
                    scoring.SingleAttemptOnly.ToString(),
                    synced.SingleAttemptOnly.ToString());
            }

            return differences;
        }

        private static void AddIfDifferent(List<string> differences, string key, double server, double synced)
        {
            // 経路の途中で丸めは入らない（同じ double をそのまま写している）ので、厳密比較でよい。
            if (server.Equals(synced))
            {
                return;
            }

            AddDifference(
                differences,
                key,
                server.ToString(DoubleFormat, CultureInfo.InvariantCulture),
                synced.ToString(DoubleFormat, CultureInfo.InvariantCulture));
        }

        private static void AddIfDifferent(List<string> differences, string key, int server, int synced)
        {
            if (server == synced)
            {
                return;
            }

            AddDifference(
                differences,
                key,
                server.ToString(CultureInfo.InvariantCulture),
                synced.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddIfDifferent(List<string> differences, string key, string server, string synced)
        {
            if (server == synced)
            {
                return;
            }

            AddDifference(differences, key, server, synced);
        }

        private static void AddDifference(List<string> differences, string key, string server, string synced) =>
            differences.Add($"{key}: サーバー {server} / ルーム設定 {synced}");
    }
}
