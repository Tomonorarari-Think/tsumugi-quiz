using System.Runtime.CompilerServices;

// PR #92 再レビュー H-2: RoomSettingsDraft の internal なテスト用フック
// （AppSettingsStoreFactory / ResetCacheForTesting）を、EditMode / PlayMode の
// 両テストアセンブリから差し替えられるようにする（Tts/AssemblyInfo.cs と同じ方針）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")]

// PR #92 再レビュー M-1: EditMode / PlayMode 共通のテスト用スコープ
// （TsumugiQuiz.Tests.Shared.Room.RoomSettingsDraftScope）からも同じフックを使うため
// （docs/tasks/impl-rules.md「コード配置規約」）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.Shared")]
