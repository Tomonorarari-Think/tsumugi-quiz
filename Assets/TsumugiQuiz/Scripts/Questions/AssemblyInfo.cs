using System.Runtime.CompilerServices;

// H3: QuestionLibrary の internal なテスト専用フック（TriggerDebouncedReloadForTesting）を
// EditMode テストアセンブリから呼び出せるようにする。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]
