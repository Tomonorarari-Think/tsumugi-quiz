# 実装 issue の共通ルール（サブエージェント向け）

統括（Fable）が実装 issue を委任するときに、issue 本文と一緒に渡す共通ルール。

## 作業場所
- 指示された worktree（例 `E:\Claude\tsumugi-quiz\.claude\worktrees\i1`）の中だけで作業する。メインツリー（`E:\Claude\tsumugi-quiz` 直下）はユーザーが Unity Editor で開いているので、そこで Unity をバッチ起動しない・ファイルを変更しない
- ブランチは worktree 作成時に `feature/<issue番号>-<説明>` として develop から切ってある。`git branch --show-current` で確認する

## 読むもの（作業前）
1. `CLAUDE.md`（規約・禁止事項）
2. `gh issue view <番号>` で issue 本文（目的・参照ドキュメント・作業内容・受け入れ条件）
3. issue が参照する `docs/*.md` の該当節
4. `docs/tasks/setup-brief.md` §2（仮決め一覧。すべてユーザー承認済み）

## 実装
- 名前空間・asmdef・フォルダは `docs/architecture.md` に従う。`TsumugiQuiz.Core` は Unity API 非依存（asmdef の `noEngineReferences: true`）
- テストを先に書く（EditMode、`Assets/TsumugiQuiz/Tests/EditMode/<層>/`）。issue の受け入れ条件にあるケースを網羅する
- 不変データ優先、境界での入力検証、エラーを握りつぶさない。1 ファイル 400 行目安
- 新しい OSS を導入するときは `docs/licenses.md` に追記し、ライセンスファイルを同梱する
- uGUI（Canvas）は使わない。UI は UI Toolkit

## コード配置規約
- EditMode / PlayMode の両方から使うテスト用フェイク・ファクトリ（例: `FakeTtsSynthesisEngine`、`TestWavFactory`、`FakeNatDevice` / `FakeNatDiscovery` / `FakeIpLookupClient`）は、それぞれの `Tests/EditMode/` `Tests/PlayMode/` 配下に重複して置かず、`Assets/TsumugiQuiz/Tests/Shared/TsumugiQuiz.Tests.Shared.asmdef`（`defineConstraints: UNITY_INCLUDE_TESTS`、`includePlatforms` は空 = EditMode/PlayMode 両方から参照可）配下の `Tests/Shared/<層>/`（namespace `TsumugiQuiz.Tests.Shared.<層>`）に置く。フェイクを `internal` のままにしたい場合は `Tests/Shared/AssemblyInfo.cs` の `InternalsVisibleTo` に呼び出し側のテストアセンブリ名を追加する。一方のプラットフォームでしか使わないフェイク（例 `NeverReadyReadingPlayback`、`FakeConsentStorage`）はこれまで通り各プラットフォームのテスト asmdef 配下に置いてよい
- 実装 asmdef（`TsumugiQuiz.Core` / `TsumugiQuiz.Network` / `TsumugiQuiz.Tts` 等）の Runtime 側 `InternalsVisibleTo` は `TsumugiQuiz.Tests.EditMode` / `TsumugiQuiz.Tests.PlayMode` にしか付与していない。`TsumugiQuiz.Tests.Shared` はそれらの `internal` 型にアクセスできないため、共有フェイクから実装側の `internal` 型を使う必要が生じた場合は、実装側の `AssemblyInfo.cs` の `InternalsVisibleTo` に `TsumugiQuiz.Tests.Shared` を追加すること

## 検証（必須）
- `pwsh ./scripts/verify.ps1 -Platform EditMode` を worktree 内で実行し、テスト件数・パス数を報告に書く
- Unity を起動する Bash 呼び出しは **前景・`timeout: 600000`**。`run_in_background` は使わない（完了通知が届かず停止する）
- 初回起動は Library のインポートで数分かかる。`Library/UnityLockfile` が残っていたら Unity プロセスが生きていないか確認してから削除する
- ログ（`Logs/verify-EditMode.log`）の error/exception を必ず grep する。終了コードだけで成功と判断しない

## コミット・PR
- 変更を Conventional Commits でコミットする（例 `feat: 参加コードのエンコード/デコードを実装 (#1)`）。`External/`、`Library/`、`Builds/`、`Logs/` を含めない（`git status --short` で確認）
- `git push -u origin <ブランチ>` のあと `gh pr create --base develop --title "<type>: <要約> (#<番号>)" --body-file <一時ファイル>` で PR を作る。本文に `Closes #<番号>`、実装概要、verify.ps1 の結果（件数）、未対応事項を書く
- **マージはしない**（統括がレビュー後にマージする）
- 一時ファイルは `<scratchpad>\` に置く

## 報告
- 作成・変更ファイル一覧、verify.ps1 の実測結果、PR の URL、判断に迷った点・未確定事項、統括に確認したい選択肢（あれば A/B 形式で）
