using System.Runtime.CompilerServices;

// M-8（issue #6 レビュー）: JoinView.ConnectTimeoutSeconds など、PlayMode テストから
// internal メンバーを差し替えられるようにするための InternalsVisibleTo。
// TsumugiQuiz.Tests.PlayMode 側は、テスト終了後に既定値へ戻す責務を持つ
// （JoinViewSceneTests の [UnityTearDown] を参照）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.PlayMode")]

// issue #33 レビュー M-4: CreditsView の internal なテスト専用フック
// (ResolveDisplayText / LicenseLoadFailureMessage) を EditMode テストアセンブリから
// 呼び出せるようにする（Questions/AssemblyInfo.cs と同じ方針）。
[assembly: InternalsVisibleTo("TsumugiQuiz.Tests.EditMode")]
