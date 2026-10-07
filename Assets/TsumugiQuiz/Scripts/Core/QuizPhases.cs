using System;

namespace TsumugiQuiz.Core
{
    /// <summary>
    /// <see cref="QuizPhase"/> の「どのフェーズをひとまとまりとして扱うか」を 1 か所に集めた述語
    /// （PR #104 レビュー L-1、#109）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判定を <c>switch</c> の列挙（含めるフェーズを並べる形）で書くと、<see cref="QuizPhase"/> に
    /// 新しい進行中フェーズを足したときに <c>default</c> へ落ちて静かに漏れる。
    /// そのため本クラスの述語はすべて<b>除外リスト形</b>（「これとこれ以外」）で書き、
    /// 新しいフェーズは既定で「進行中」側に入るようにしてある。
    /// </para>
    /// <para>
    /// 例外は <see cref="IsDefined"/>（受信値の検証）だけ。ここは未知の値を通してはいけないので
    /// 除外リスト形にできず、列挙の定義そのものを見る（PR #123 レビュー L-4）。
    /// </para>
    /// <para>
    /// 純 C#（Unity API 非依存）なので EditMode で全フェーズを網羅したテストができる
    /// （<c>QuizPhasesTests</c>）。<see cref="QuizPhase"/> に値を足したらそのテストも更新すること。
    /// </para>
    /// </remarks>
    public static class QuizPhases
    {
        /// <summary>
        /// 問題の進行中（読み上げ〜判定）か。
        /// ロビー（<see cref="QuizPhase.Lobby"/>）・結果表示（<see cref="QuizPhase.Result"/>）・
        /// 全問終了（<see cref="QuizPhase.Finished"/>）は含まない。
        /// </summary>
        /// <remarks>
        /// ロビーから Game View へ移るかどうかの<b>購読直後の 1 回だけの判定</b>に使う
        /// （<c>LobbyView.TryBindGameSession</c>）。<see cref="QuizPhase.Result"/> /
        /// <see cref="QuizPhase.Finished"/> を含めると、「ロビーへ戻る」直後のクライアントが
        /// Lobby → Game へ跳ね返される（PR #104 レビュー H-A）。
        /// </remarks>
        /// <param name="phase">判定するフェーズ。</param>
        /// <returns>問題が進行中なら true。</returns>
        public static bool IsQuestionInProgress(QuizPhase phase) =>
            phase != QuizPhase.Lobby && phase != QuizPhase.Result && phase != QuizPhase.Finished;

        /// <summary>
        /// ゲームが進行中か（docs/network.md §2.3 の 5）。未開始（<see cref="QuizPhase.Lobby"/>）と
        /// 終了済み（<see cref="QuizPhase.Finished"/>）以外のすべて（結果表示を含む）。
        /// </summary>
        /// <remarks>途中参加（<c>network.allowLateJoin</c>）の可否判定に使う。</remarks>
        /// <param name="phase">判定するフェーズ。</param>
        /// <returns>進行中なら true。</returns>
        public static bool IsGameInProgress(QuizPhase phase) =>
            phase != QuizPhase.Lobby && phase != QuizPhase.Finished;

        /// <summary>
        /// 合流したクライアントへ現在の進行状態・現在問を送り直す必要があるか（#109）。
        /// ロビー（＝ まだ 1 問も出していない）以外はすべて対象。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="QuizPhase.Result"/> / <see cref="QuizPhase.Finished"/> も対象に含める。
        /// 問題データ（DTO）・画像は RPC でしか配らないので、どのフェーズで合流しても
        /// 配信キャッシュへ載せておくため。
        /// </para>
        /// <para>
        /// #117 以降は<b>画面遷移の根拠にもなる</b>。受信側は送られてきたフェーズを 1 件だけ保持し
        /// （<c>GameSession.TryConsumePendingResync</c>）、ロビーがそれを取り出して
        /// Result View（<see cref="QuizPhase.Finished"/>）/ Game View（それ以外）へ移る。
        /// ロビーから Game View へ移る<b>購読直後の判定</b>は
        /// <see cref="IsQuestionInProgress"/>（Result / Finished を含まない）のままにしてある
        /// （含めると「ロビーへ戻る」直後のクライアントが跳ね返る。PR #104 レビュー H-A）。
        /// </para>
        /// </remarks>
        /// <param name="phase">サーバー側の現在のフェーズ。</param>
        /// <returns>再同期が必要なら true。</returns>
        public static bool NeedsResync(QuizPhase phase) => phase != QuizPhase.Lobby;

        /// <summary>
        /// <see cref="QuizPhase"/> として定義済みの値か（ネットワーク受信値の検証用、#117）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ここだけは<b>除外リスト形にできない</b>（未知の値を通してはいけない）ので、
        /// 列挙の定義そのものを参照する <see cref="Enum.IsDefined(Type, object)"/> を使う。
        /// 「<see cref="QuizPhase.Lobby"/> 以上 <see cref="QuizPhase.Finished"/> 以下」のような
        /// 範囲判定にすると、その外側に足された値（<see cref="QuizPhase.ChoiceAnswering"/> = 8）が
        /// 未定義扱いになって静かに捨てられる（#117 で実際に起きていた）。
        /// </para>
        /// <para>
        /// 受信 1 件につき 1 回しか通らない検証経路でのみ使うこと（毎フレーム・毎 tick の判定には使わない）。
        /// </para>
        /// </remarks>
        /// <param name="phase">検証するフェーズ。</param>
        /// <returns>定義済みの値なら true。</returns>
        public static bool IsDefined(QuizPhase phase) => Enum.IsDefined(typeof(QuizPhase), phase);
    }
}
