using System.Runtime.CompilerServices;

// #13: 問題配信（QuestionDistributor）の進行状態・受信確認 RPC・スポーン API は
// アプリの他レイヤーから触る必要がないため internal にしている。
// PlayMode テストからだけ検証できるように公開する（Questions 層の AssemblyInfo と同じ慣行）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")]

// issue #8 レビュー M-6: AppPathsBootstrap.TryConfigureDataRoot は MonoBehaviour の Awake を
// 介さずに検証したいため internal にしてある。EditMode テストからも直接呼べるようにする。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]

// issue #155: RoomSettingsDraftScope（Tests/Shared/Room、EditMode/PlayMode 共通のテスト用フェイク置き場）が
// HostRolePreference.AppSettingsStoreFactory を差し替えて host.role の保存先を隔離するため、
// internal のまま TsumugiQuiz.Tests.Shared からもアクセスできるようにする（docs/tasks/impl-rules.md）。
// L-a（再レビュー）: HostRolePreference.AppSettingsStoreFactory の setter は本番コードから差し替え
// られないよう internal にしてある（本番はテスト用ファクトリを注入する経路を持たない）。
// Tests.PlayMode（HostSetupPreferencesScope 等）からも差し替えられるよう、上の
// InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")（本ファイル冒頭）で許可済み。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.Shared")]
