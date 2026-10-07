using System;

namespace TsumugiQuiz.UI.Views.HostSetup
{
    /// <summary>
    /// <c>-tq-host</c> 自動開始（issue #8）まわりの 2 段階の状態（「自動開始を要求中か」「参加コードの
    /// 書き出しが保留中か」）を追跡する、Unity 非依存の小さな状態機械（issue #15 レビュー M-3）。
    /// </summary>
    /// <remarks>
    /// 実測で判明した罠（詳細は <c>docs/tasks/m2-verification.md</c>）: ホスト開始の完了コールバックは、
    /// <see cref="BeginAutoStart"/> に渡す <c>startAction</c> の呼び出し中に**同期的に**発火することがある
    /// （NGO の <c>NetworkManager.ShutdownInProgress</c> が false の通常の初回開始では、コルーチンが
    /// 1 度も yield せずに完了まで進むため）。そのため「戻り値を見てから要求フラグを立てる」書き方
    /// （<c>_requested = startAction();</c>）では、右辺の評価中に完了コールバックが先に届いてしまい、
    /// フラグがまだ立っていないと誤認して参加コードの書き出しが永久に保留されないままになる。
    /// この状態機械は、呼び出し前にフラグを立てておき、<c>startAction</c> が false（早期 return）を
    /// 返したときだけ後から下ろすことでこれを避ける。<see cref="HostSetupView.LaunchOptions"/> から
    /// 抽出したのは、この同期発火のケースを実 Netcode に頼らずフェイク（ラムダ）で強制し、
    /// 高速な EditMode テストで固定するため。
    /// </remarks>
    internal sealed class AutoHostStartCoordinator
    {
        private bool _autoHostStartRequested;
        private bool _pendingAutoHostJoinCodeWrite;

        /// <summary>参加コードの書き出しが保留中（未消費）かどうか。テストからの確認用。</summary>
        public bool PendingAutoHostJoinCodeWrite => _pendingAutoHostJoinCodeWrite;

        /// <summary>
        /// 自動開始を試みる。<paramref name="startAction"/> の呼び出し中に <see cref="HandleResult"/> が
        /// 同期的に呼ばれても正しく処理できる（呼び出し前にフラグを立てておくため）。
        /// <paramref name="startAction"/> が例外を投げた場合も、フラグを残さない（LOW: try/finally）。
        /// </summary>
        /// <param name="startAction">
        /// ホスト開始を試みる処理。実際にコルーチン等を開始できたら true、
        /// バリデーション失敗等で何も開始しなかったら false を返す。
        /// </param>
        /// <returns><paramref name="startAction"/> の戻り値。</returns>
        public bool BeginAutoStart(Func<bool> startAction)
        {
            if (startAction == null)
            {
                throw new ArgumentNullException(nameof(startAction));
            }

            _autoHostStartRequested = true;
            var started = false;
            try
            {
                started = startAction();
                return started;
            }
            finally
            {
                if (!started)
                {
                    _autoHostStartRequested = false;
                }
            }
        }

        /// <summary>
        /// ホスト開始コルーチンの完了を受け取る。<see cref="BeginAutoStart"/> 呼び出し中に同期的に
        /// 呼ばれることがある（remarks 参照）。自動開始を要求していない（= 手動開始）場合は無視する。
        /// </summary>
        /// <param name="success">ホスト開始が成功したか。</param>
        /// <returns>この呼び出しが自動開始由来で、実際に消費した（要求中だった）なら true。</returns>
        public bool HandleResult(bool success)
        {
            if (!_autoHostStartRequested)
            {
                return false;
            }

            _autoHostStartRequested = false;
            // 失敗時（success = false）も無条件に上書きしてよい。自動開始（-tq-host）は
            // プロセスにつき最初の 1 回しか行われない（LaunchOptionsRunner.TryConsumeAutoHost が
            // 消費する）ため、ここに到達する時点で未消費の true が残っていることはない。
            _pendingAutoHostJoinCodeWrite = success;
            return true;
        }

        /// <summary>
        /// 参加コードの書き出しが保留中なら消費して true を返す（1 回限り）。保留していなければ false。
        /// </summary>
        public bool ConsumePendingJoinCodeWrite()
        {
            if (!_pendingAutoHostJoinCodeWrite)
            {
                return false;
            }

            _pendingAutoHostJoinCodeWrite = false;
            return true;
        }
    }
}
