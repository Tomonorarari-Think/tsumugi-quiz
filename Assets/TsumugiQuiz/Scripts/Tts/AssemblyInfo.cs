using System.Runtime.CompilerServices;

// EditMode / PlayMode テストから internal な P/Invoke 層（TsumugiQuiz.Tts.Native）を検証するため。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")]
