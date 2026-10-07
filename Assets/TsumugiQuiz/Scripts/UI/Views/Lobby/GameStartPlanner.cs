using System;
using System.Collections.Generic;
using TsumugiQuiz.Network;
using TsumugiQuiz.Questions;
using TsumugiQuiz.Room;

namespace TsumugiQuiz.UI.Views.Lobby
{
    /// <summary>
    /// ホストが「ゲーム開始」を押したときに、読み込み済みの問題セットとルーム設定
    /// （<c>questions.*</c>）から <c>GameSession.Configure</c> / <c>GameSession.StartSession</c> に
    /// 渡す材料を組み立てる純 C#（issue #95）。Unity API に依存しないため EditMode でテストできる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 絞り込み（<c>setIds</c> / <c>typeFilter</c> / <c>imageOnly</c> / <c>tagFilter</c> /
    /// <c>shuffleOrder</c> / <c>count</c>）そのものは #19 で実装済みの
    /// <see cref="QuestionSelector"/> が唯一の出所で、本クラスはそれを再実装しない。
    /// ここでやるのは「実際に <c>StartSession</c> を呼ぶ前に、1 問も出せない状態を見つけて
    /// ユーザーへ理由を返す」ことだけ（<c>StartSession</c> は false を返すだけで理由を UI に伝えられない）。
    /// </para>
    /// <para>
    /// 置き場所を <c>TsumugiQuiz.UI</c> にしているのは、(1) 文言がユーザー向け表示そのものであること、
    /// (2) <see cref="QuestionRepositoryResult"/>（Questions）と <see cref="QuestionSelectionSettings"/>
    /// （Room）と <see cref="QuestionSelector"/>（Network）の 3 つを同時に参照する必要があり、
    /// それができるのが UI 層だけであること（docs/architecture.md §3 の依存方向）による。
    /// </para>
    /// </remarks>
    public static class GameStartPlanner
    {
        /// <summary>候補数の判定だけに使う固定シード（並びは <c>StartSession</c> 側で決まるので影響しない）。</summary>
        private const int ProbeSeed = 0;

        /// <summary>
        /// 読み込み結果とルーム設定から「ゲーム開始」の下準備を組み立てる。
        /// </summary>
        /// <param name="result"><c>QuestionRepository.LoadAll()</c> の結果。</param>
        /// <param name="questions">ルーム設定の問題選択（<c>RoomSettingsSync.Current.Questions</c>）。null なら既定値。</param>
        /// <param name="questionsFolderPath">問題フォルダの絶対パス（エラー文言に添える）。null / 空なら省略する。</param>
        /// <returns>組み立てた計画。開始できない場合は理由つき。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="result"/> が null のとき。</exception>
        public static GameStartPlan Plan(
            QuestionRepositoryResult result,
            QuestionSelectionSettings questions,
            string questionsFolderPath = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var effective = questions ?? QuestionSelectionSettings.Default;
            var warnings = CollectLoadWarnings(result);
            var sets = result.Sets;

            if (sets == null || sets.Count == 0)
            {
                return BuildNoSetsPlan(result, questionsFolderPath, warnings);
            }

            // 候補数だけを知りたいので、シャッフルは無効にして無駄な並べ替えを避ける
            // （CandidateCount は count による切り詰めの前の値なので、count の指定にも影響されない）。
            var probeSettings = effective.ShuffleOrder ? effective.WithShuffleOrder(false) : effective;

            // logExclusions: false — ここは「下見」で、実際の出題列は GameSession.StartSession が
            // 同じ候補プールに対してもう一度作る。ログを出すのはそちら 1 回だけにする
            // （PR #104 レビュー L-2。除外の内訳は Warnings 経由で呼び出し側へ返す）。
            var selection = QuestionSelector.SelectFromSets(sets, probeSettings, ProbeSeed, logExclusions: false);

            for (var i = 0; i < selection.UnknownSetIds.Count; i++)
            {
                warnings.Add($"ルーム設定の setId「{selection.UnknownSetIds[i]}」に一致する問題セットがありません。");
            }

            if (selection.MissingTypeCount > 0)
            {
                warnings.Add($"出題形式（type）が設定されていない問題 {selection.MissingTypeCount} 件を除外しました。");
            }

            if (selection.CandidateCount == 0)
            {
                return GameStartPlan.Blocked(
                    GameStartBlocker.NoMatchingQuestions,
                    "ルーム設定の絞り込み条件に合う問題が 1 件もありません"
                    + $"（読み込み済み {selection.PoolCount} 問）。問題セット・出題形式・タグの設定を見直してください。",
                    selection.PoolCount,
                    warnings);
            }

            return GameStartPlan.Ready(
                sets,
                RepositoryQuestionSource.FromResult(result),
                selection.PoolCount,
                selection.CandidateCount,
                warnings);
        }

        /// <summary>問題セットが 1 件も読み込めなかった場合の計画（読み込み失敗と 0 件を区別する）。</summary>
        private static GameStartPlan BuildNoSetsPlan(
            QuestionRepositoryResult result, string questionsFolderPath, List<string> warnings)
        {
            var hasFolderPath = !string.IsNullOrEmpty(questionsFolderPath);
            var folderSuffix = hasFolderPath ? $"\n問題フォルダ: {questionsFolderPath}" : string.Empty;

            var failureReason = FirstFailureReason(result);
            if (failureReason != null)
            {
                // 理由の文言（例「問題フォルダが見つかりません: <パス>」）に既にパスが入っている場合は
                // 重ねて出さない（PR #104 レビュー L-3）。
                var reasonSuffix =
                    hasFolderPath && failureReason.Contains(questionsFolderPath) ? string.Empty : folderSuffix;

                return GameStartPlan.Blocked(
                    GameStartBlocker.LoadFailed,
                    $"問題データを読み込めませんでした: {failureReason}{reasonSuffix}",
                    0,
                    warnings);
            }

            return GameStartPlan.Blocked(
                GameStartBlocker.NoQuestionSets,
                $"出題できる問題セットがありません。問題フォルダに問題データ（*.json）を追加してください。{folderSuffix}",
                0,
                warnings);
        }

        /// <summary>読み込み時の注意点（スキップされたセット・フォルダ単位のエラー）を集める。</summary>
        private static List<string> CollectLoadWarnings(QuestionRepositoryResult result)
        {
            var warnings = new List<string>();

            var folderErrors = result.FolderErrors;
            if (folderErrors != null)
            {
                for (var i = 0; i < folderErrors.Count; i++)
                {
                    if (!string.IsNullOrEmpty(folderErrors[i]))
                    {
                        warnings.Add(folderErrors[i]);
                    }
                }
            }

            var skipped = result.SkippedSets;
            if (skipped != null && skipped.Count > 0)
            {
                warnings.Add($"読み込めなかった問題セットが {skipped.Count} 件あります（ホスト設定画面で内容を確認できます）。");
            }

            return warnings;
        }

        /// <summary>
        /// 「読み込みに失敗した」と言える理由の先頭 1 件（フォルダ単位のエラー → スキップされたセットの順）。
        /// どちらも無ければ null（＝ フォルダは読めたが問題セットが置かれていないだけ）。
        /// </summary>
        private static string FirstFailureReason(QuestionRepositoryResult result)
        {
            var folderErrors = result.FolderErrors;
            if (folderErrors != null)
            {
                for (var i = 0; i < folderErrors.Count; i++)
                {
                    if (!string.IsNullOrEmpty(folderErrors[i]))
                    {
                        return folderErrors[i];
                    }
                }
            }

            var skipped = result.SkippedSets;
            if (skipped != null && skipped.Count > 0)
            {
                return $"{skipped.Count} 件の問題セットが検証エラーでスキップされました";
            }

            return null;
        }
    }
}
