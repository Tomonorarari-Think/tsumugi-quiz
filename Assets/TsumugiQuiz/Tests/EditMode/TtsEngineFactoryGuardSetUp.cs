using System;
using System.Collections.Concurrent;
using System.Linq;
using NUnit.Framework;
using TsumugiQuiz.Tts;

/// <summary>
/// EditMode テストアセンブリ全体で一度だけ走る前準備（#156）。<b>意図的に名前空間を付けていない</b>
/// （NUnit の <c>[SetUpFixture]</c> は名前空間なしで宣言するとアセンブリ全体を対象にできるため。
/// <see cref="TsumugiQuiz.Tests.EditMode.EditModeTestAssemblySetUp"/> は
/// <c>TsumugiQuiz.Tests.EditMode</c> 名前空間の子孫だけが対象になるが、将来その名前空間の
/// 外にテストクラスが増えても本ガードだけは確実に効くようにする狙い）。
///
/// <para>
/// <see cref="TtsService.Initialize"/> / <see cref="TtsService.EnsureInitializedAsync"/> /
/// <see cref="TtsService.RetryInitializeAsync"/> は、<c>engineFactory</c> を省略すると
/// <see cref="TtsService.DefaultEngineFactoryOverrideForTesting"/>（null なら本番実装）に
/// フォールバックする（#156）。docs/tts.md §11.1「EditMode は <c>External/</c> 非依存」の方針を
/// コメントだけでなくテスト基盤として強制するため、EditMode 実行中はこの既定値を
/// <see cref="FailFactory"/>（呼ばれたら即座に例外を投げる）にしておく。
/// </para>
///
/// <para>
/// <b>#156 レビュー M-1</b>: <see cref="FailFactory"/> が投げる例外は <c>TtsService.CreateEngine</c> の
/// <c>catch (Exception)</c> で捕まり、<c>Debug.LogError</c> による通知になるだけである。多くのテストが
/// <c>LogAssert.ignoreFailingMessages = true</c> を設定しており、その状態ではこの LogError が出ても
/// そのテスト自体は失敗しない。そのため、<see cref="FailFactory"/> は呼ばれるたびに呼び出し元の
/// スタックトレースを <see cref="Invocations"/> へ記録し、<see cref="RemoveGuardAndAssertNoInvocations"/>
/// （<c>OneTimeTearDown</c>、EditMode アセンブリ全体の実行が終わったタイミングで 1 回だけ走る）で
/// 記録が空でなければ <c>Assert.Fail</c> する。個々のテストの <c>ignoreFailingMessages</c> 設定に
/// 左右されない、確実な検知経路にするための二重の仕組み。
/// あわせて、例外メッセージの先頭に付けた <c>[TtsEngineFactoryGuardSetUp]</c> タグは
/// <c>scripts/log-scan.ps1</c>（<c>scripts/verify.ps1</c> のログ走査）にも検出パターンとして
/// 追加してある（<c>scripts/tests/log-scan.tests.ps1</c> 参照）。
/// </para>
///
/// <para>
/// フェイクエンジンで実際に初期化を進めたい個々のテストは、自分の <c>[SetUp]</c> で
/// <see cref="TtsService.DefaultEngineFactoryOverrideForTesting"/> を明示的にフェイクへ差し替え、
/// <c>[TearDown]</c> で <b><see cref="FailFactory"/> に戻す</b>こと（<c>null</c> に戻すと、
/// このガードが以後実行される他のテストで効かなくなってしまう。
/// <c>TsumugiQuiz.Tests.EditMode.UI.TtsStatusPanelTests</c> を参照）。
/// </para>
/// </summary>
[SetUpFixture]
public sealed class TtsEngineFactoryGuardSetUp
{
    /// <summary>
    /// <see cref="FailFactory"/> が呼ばれた際の呼び出し元スタックトレースを記録する
    /// （#156 レビュー M-1）。スレッドセーフにするため <see cref="ConcurrentQueue{T}"/>
    /// （初期化はワーカースレッドの <c>Task.Run</c> から呼ばれるため）。
    /// </summary>
    private static readonly ConcurrentQueue<string> Invocations = new ConcurrentQueue<string>();

    /// <summary>
    /// engineFactory 省略時のフォールバックとして呼ばれたら、呼び出し元スタックトレースを
    /// <see cref="Invocations"/> に記録したうえで即座に失敗させる、EditMode 実行全体で
    /// 共有するフェイク。個々のテストは TearDown でこの値へ戻すことで、このガードを
    /// EditMode 実行中ずっと維持する（<see cref="TtsService.DefaultEngineFactoryOverrideForTesting"/>）。
    /// </summary>
    internal static readonly TtsSynthesisEngineFactory FailFactory = (location, settings) =>
    {
        Invocations.Enqueue(new System.Diagnostics.StackTrace(true).ToString());

        throw new InvalidOperationException(
            "[TtsEngineFactoryGuardSetUp] TtsService が engineFactory 省略のまま本番実装（voicevox_core）へ" +
            "フォールバックしようとしました。EditMode は External/ 非依存（docs/tts.md §11.1）。" +
            "呼び出し元のテストで TtsService.DefaultEngineFactoryOverrideForTesting を" +
            "フェイクエンジンへ明示的に差し替えること（#156）。");
    };

    [OneTimeSetUp]
    public void InstallGuard() => TtsService.DefaultEngineFactoryOverrideForTesting = FailFactory;

    /// <summary>
    /// EditMode 実行全体を通じて 1 回でも <see cref="FailFactory"/> が呼ばれていれば、
    /// ここで確実に失敗させる（#156 レビュー M-1）。個々のテストが
    /// <c>LogAssert.ignoreFailingMessages = true</c> を設定していても、この検査はすり抜けない。
    /// </summary>
    [OneTimeTearDown]
    public void RemoveGuardAndAssertNoInvocations()
    {
        TtsService.ResetForTesting();

        if (Invocations.IsEmpty)
        {
            return;
        }

        var details = string.Join(
            "\n----------\n",
            Invocations.Select((stackTrace, index) => $"[{index + 1}] {stackTrace}"));

        Assert.Fail(
            "[TtsEngineFactoryGuardSetUp] engineFactory 省略時の本番実装フォールバックが "
            + $"{Invocations.Count} 回検出されました（#156）。呼び出し元スタックトレース:\n{details}");
    }
}
