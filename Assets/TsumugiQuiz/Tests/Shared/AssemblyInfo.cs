using System.Runtime.CompilerServices;

// フェイク・ファクトリは internal のまま公開範囲を絞りつつ、EditMode / PlayMode の
// 両テストアセンブリから使えるようにする（#67）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")]
