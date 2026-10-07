using NUnit.Framework;
using TsumugiQuiz.UI.Views.HostSetup;

namespace TsumugiQuiz.Tests.EditMode.UI.HostSetup
{
    /// <summary>
    /// <see cref="AutoHostStartCoordinator"/> の EditMode テスト（issue #15 レビュー M-3）。
    ///
    /// 実機（<c>run-multi.ps1</c>）で発見した回帰（<c>docs/tasks/m2-verification.md</c>「不具合3」）は、
    /// 「開始処理の完了コールバックが、開始処理そのものの呼び出し中に同期的に発火する」状況で、
    /// 戻り値を見てからフラグを立てる素朴な実装が破綻することが原因だった。本テストは、実 Netcode の
    /// タイミング（<c>NetworkManager.ShutdownInProgress</c> が false のときだけ同期発火する、という
    /// 実装依存の偶然の挙動）に頼らず、フェイクのコールバック（<see cref="RaiseResultDuringStartAction"/>
    /// 等）で同期発火を強制することで、この回帰を高速かつ確実に固定する。
    /// </summary>
    public class AutoHostStartCoordinatorTests
    {
        [Test]
        public void BeginAutoStart_CompletesSynchronouslyWithSuccess_MarksPendingJoinCodeWrite()
        {
            var coordinator = new AutoHostStartCoordinator();

            // フェイク: startAction 自身が、外側の BeginAutoStart 呼び出しが完了する前に
            // HandleResult(true) を同期的に呼ぶ（実機で確認した同期発火の状況を強制する）。
            var started = coordinator.BeginAutoStart(() =>
            {
                coordinator.HandleResult(true);
                return true;
            });

            Assert.IsTrue(started, "startAction 自体は true（開始できた）を返しているはず。");
            Assert.IsTrue(
                coordinator.PendingAutoHostJoinCodeWrite,
                "同期発火した HandleResult(true) が消費され、参加コードの書き出しが保留されているはず " +
                "（回帰: 戻り値を見てから要求フラグを立てる実装だとここが false のままになる）。");
        }

        [Test]
        public void BeginAutoStart_CompletesSynchronouslyWithFailure_DoesNotMarkPendingJoinCodeWrite()
        {
            var coordinator = new AutoHostStartCoordinator();

            var started = coordinator.BeginAutoStart(() =>
            {
                coordinator.HandleResult(false);
                return true;
            });

            Assert.IsTrue(started);
            Assert.IsFalse(
                coordinator.PendingAutoHostJoinCodeWrite,
                "同期発火が失敗（success=false）だった場合は参加コードを書き出そうとしないはず。");
        }

        [Test]
        public void BeginAutoStart_CompletesAsynchronously_DoesNotMarkPendingUntilHandleResultSucceeds()
        {
            var coordinator = new AutoHostStartCoordinator();

            // フェイク: startAction はコルーチンを開始しただけで、この時点では完了コールバック
            // （HandleResult）をまだ呼ばない（同期発火しない、通常想定される非同期完了のケース）。
            var started = coordinator.BeginAutoStart(() => true);

            Assert.IsTrue(started);
            Assert.IsFalse(
                coordinator.PendingAutoHostJoinCodeWrite,
                "startAction が true を返した直後（HandleResult 未着手）の時点では、まだ書き出しは保留されていないはず。");

            // 後から（非同期に）完了コールバックが届いたケース。
            var consumed = coordinator.HandleResult(true);

            Assert.IsTrue(consumed, "自動開始由来の要求が残っているため消費できるはず。");
            Assert.IsTrue(
                coordinator.PendingAutoHostJoinCodeWrite,
                "非同期の HandleResult(true) によって、参加コードの書き出しが保留されるはず。");
        }

        [Test]
        public void BeginAutoStart_CompletesAsynchronouslyWithFailure_DoesNotMarkPending()
        {
            var coordinator = new AutoHostStartCoordinator();

            var started = coordinator.BeginAutoStart(() => true);

            Assert.IsTrue(started);
            Assert.IsFalse(coordinator.PendingAutoHostJoinCodeWrite);

            // 後から（非同期に）完了コールバックが失敗で届いたケース。
            var consumed = coordinator.HandleResult(false);

            Assert.IsTrue(consumed, "自動開始由来の要求が残っているため消費できるはず（失敗という結果も消費する）。");
            Assert.IsFalse(
                coordinator.PendingAutoHostJoinCodeWrite,
                "非同期の HandleResult(false) の場合は参加コードを書き出そうとしないはず。");
        }

        [Test]
        public void BeginAutoStart_StartActionReturnsFalse_DoesNotLeaveRequestedFlagSet()
        {
            var coordinator = new AutoHostStartCoordinator();

            // バリデーション失敗等で何も開始しなかったケース（HandleResult は呼ばれない）。
            var started = coordinator.BeginAutoStart(() => false);

            Assert.IsFalse(started);

            // 後から手動開始（HandleResult が自動開始由来でない呼び出し）が来ても、
            // 誤って消費されない（= 要求フラグが正しく下ろされている）ことを確認する。
            var consumedByUnrelatedManualStart = coordinator.HandleResult(true);
            Assert.IsFalse(
                consumedByUnrelatedManualStart,
                "startAction が false を返した後は要求フラグが残っていないはず。");
            Assert.IsFalse(coordinator.PendingAutoHostJoinCodeWrite);
        }

        [Test]
        public void BeginAutoStart_StartActionThrows_DoesNotLeaveRequestedFlagSet()
        {
            var coordinator = new AutoHostStartCoordinator();

            // issue #15 レビュー LOW: startAction が例外を投げても要求フラグを残さない（try/finally）。
            Assert.Throws<System.InvalidOperationException>(() =>
                coordinator.BeginAutoStart(() => throw new System.InvalidOperationException("テスト用の例外")));

            var consumedAfterException = coordinator.HandleResult(true);
            Assert.IsFalse(
                consumedAfterException,
                "startAction が例外を投げた場合も要求フラグが残っていないはず。");
            Assert.IsFalse(coordinator.PendingAutoHostJoinCodeWrite);
        }

        [Test]
        public void HandleResult_WithoutBeginAutoStart_IsIgnored()
        {
            var coordinator = new AutoHostStartCoordinator();

            // 手動でのホスト開始（自動開始を要求していない）を模す。
            var consumed = coordinator.HandleResult(true);

            Assert.IsFalse(consumed, "自動開始を要求していない場合は無視されるはず（手動開始で誤爆しない）。");
            Assert.IsFalse(coordinator.PendingAutoHostJoinCodeWrite);
        }

        [Test]
        public void ConsumePendingJoinCodeWrite_OnlyReturnsTrueOnce()
        {
            var coordinator = new AutoHostStartCoordinator();
            coordinator.BeginAutoStart(() =>
            {
                coordinator.HandleResult(true);
                return true;
            });

            Assert.IsTrue(coordinator.ConsumePendingJoinCodeWrite(), "1 回目は保留中の書き出しを消費できるはず。");
            Assert.IsFalse(coordinator.ConsumePendingJoinCodeWrite(), "2 回目は既に消費済みのため false のはず。");
        }
    }
}
