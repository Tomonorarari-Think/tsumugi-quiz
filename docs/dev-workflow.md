# 開発ワークフロー

## 目的
Claude Code（統括 + サブエージェント）による tsumugi-quiz の開発手順、ローカル検証手順、ビルド手順を定義する。GitHub Actions は使わないため、PR 前のローカル検証を必須の代替手段とする。

## 関連ドキュメント
- [CLAUDE.md](../CLAUDE.md) — プロジェクト規約全般
- [licenses.md](./licenses.md) — 新規 OSS 導入時に追記が必要
- [tasks/setup-brief.md](./tasks/setup-brief.md) — K22〜K24（ブランチ運用・検証スクリプト・ネイティブ配置）

---

## 1. 役割分担

| 役割 | モデル | 担当範囲 |
|---|---|---|
| 統括（Fable） | Fable | 計画・設計判断・レビュー最終確認・issue 起票・PR マージ。指示書（`docs/tasks/issue-<n>-brief.md`）の作成 |
| implementer | Opus 固定 | 設計判断を伴う実装（ネットワーク、voicevox_core などネイティブ連携、複雑なゲームロジック、技術調査） |
| worker | Sonnet 固定 | 定型実装、ドキュメント整形・執筆、設定作業、GitHub issue 起票、スクリプト作成 |
| reviewer | Opus 固定、読み取り専用 | 実装・ドキュメントのレビュー。受け入れ条件との照合、権利表記漏れ、git 管理外素材の混入、テスト不足の指摘 |

- 各エージェントの定義は `.claude/agents/{implementer,worker,reviewer}.md` を参照
- **起動方法**: Agent ツールで `subagent_type` に `implementer` / `worker` / `reviewer` を指定する。エージェント定義が読み込まれていないセッション（別ツール・別環境からの起動など）では、`general-purpose` エージェントに `model` パラメータで `opus`（implementer/reviewer 相当）または `sonnet`（worker 相当）を明示して代替する
- Fable の API エラーが頻発する場合は **統括を Opus に切り替える**（サブエージェントの役割・モデル割当は変更しない）

---

## 2. issue 1 件の進め方

1. `git checkout develop && git pull --ff-only` で develop を最新化する（**作業開始時は必ず実施**。ff-only が失敗する場合は放置せず状況を確認する）
2. `feature/<issue番号>-<短い説明>` ブランチを develop から切る（例: `feature/12-add-buzz-window`）
3. 統括が `docs/tasks/issue-<n>-brief.md` に指示書を書く。内容は setup-brief.md の共通事項・確定事項を踏まえ、対象 issue のスコープ・受け入れ条件・参照ドキュメントを明記する
4. 統括が implementer / worker に作業を委任する
   - Unity を起動する Bash 呼び出しは **前景**・`timeout: 600000`（`run_in_background` は使わない。背景起動すると完了通知が届かず停止する既知の罠）
5. `scripts/verify.ps1` を実行し、必要なら `scripts/build.ps1` も実行する（§3 参照）
6. reviewer エージェントにレビューを依頼する。CRITICAL / HIGH 指摘があれば修正して再レビュー
7. `gh pr create --base develop` で develop 向けに PR を作成する。PR 本文には issue 番号（`Closes #n`）と `verify.ps1` の実行結果を貼る。マージは squash マージ
8. マージ後、`git checkout develop && git pull --ff-only && git branch -d feature/<issue番号>-<説明>` でローカルの feature ブランチを削除する（リモートブランチは自動削除設定済みのため操作不要）

---

## 3. ローカル検証手順

GitHub Actions は使わないため、**PR を作成する前に必ずローカルで検証を実行し、結果を PR 本文に貼ること**。

### 3.1 `scripts/verify.ps1`
- EditMode テストと PlayMode テストを実行し、結果 XML を保存する
- Unity を起動する前に、立ち絵生成スクリプトの Python 単体テスト（`scripts/tests/test_*.py`、#191）を実行する。
  Python は開発用の任意の依存なので、`python -c "import sys"` が成功しない環境（未導入・Microsoft Store の
  エイリアスだけがある等）ではスキップし、その旨をログに出す（スキップしても verify は失敗しない）
- 実行後、ログを **必ず grep してエラーの有無を確認する**（終了コードだけで判断しない。Unity はテスト失敗時でも 0 を返すことがあるため）

| オプション | 意味 |
|---|---|
| `-Platform EditMode` / `-Platform PlayMode` | 片方だけ実行する（既定は `All`） |
| `-IncludeNetwork` | `[Category("Network")]` のテストも実行する |

- **既定では `-testCategory "!Network"` を Unity に渡し、`[Category("Network")]` のテストを除外する**。
  対象は UPnP / NAT-PMP の実探索、IP 確認サービスへの HTTP アクセス、実 NIC の列挙など、
  実行環境のネットワーク構成に依存するテスト（オフラインだとタイムアウト分待たされる）
- ネットワーク周りを触った PR では `pwsh ./scripts/verify.ps1 -IncludeNetwork` も実行し、
  実測値（デバイス検出の可否、外部 IP、CGNAT 判定）を PR 本文に貼る

#### 3.1.1 テストデータの分離（`TSUMUGI_DATA_ROOT`、#71）

consent.json・TtsCache 等のデータ保存先は `TsumugiQuiz.Core.AppPaths`（`DataRoot` の単一入口）が解決する。
優先順位は「明示設定（テスト・`-tq-data-root` 起動引数） > 環境変数 `TSUMUGI_DATA_ROOT` > 既定値
（`Application.persistentDataPath`、Boot の `AppPathsBootstrap` が登録）」。

`scripts/verify.ps1` は Unity をバッチ起動する **プロセスにだけ** 環境変数
`TSUMUGI_DATA_ROOT=<プロジェクトルート>\Logs\test-data\<EditMode|PlayMode>` を設定し（実行前にフォルダをクリア）、
consent.json・TtsCache の書き込み先をこのプロセス専用のフォルダへ切り替える。これにより

- メインツリーで Unity Editor を開いたまま `verify.ps1` を実行しても、Editor 側の実データと競合しない
- 複数 worktree で同時に `verify.ps1 -Platform PlayMode` を実行しても、互いの consent.json を奪い合わない
  （`Sharing violation` / `IOException` の原因になっていた。#7 #52 #25 を参照）

Unity Editor の Test Runner ウィンドウから直接テストを実行した場合（環境変数が無い場合）は、
PlayMode テストアセンブリの `PlayModeTestAssemblySetUp`（`[SetUpFixture]`）が
`AppPaths.ConfigureDefault(Application.persistentDataPath)` を登録するため、従来どおり実データが使われる。

#### 3.1.2 Performance Testing パッケージの結果保存先（`-perfTestResults`、#175）

`com.unity.test-framework.performance`（6.6.0、builtin。`packages-lock.json` 上は `com.unity.collections`
の推移依存として解決されているだけで、本プロジェクトは `Unity.PerformanceTesting` 名前空間を
テストコードから直接参照していない。`grep -rn "Unity.PerformanceTesting" Assets/` で確認済み）は、
テスト実行終了時に `ICallbacks.RunFinished` で結果を書き出すコールバックを持つ。既定では
`Editor/PerformanceTestRunSaver.cs`（パッケージソースで確認）が使われ、`Application.persistentDataPath`
配下に固定パス（`TestResults.xml` / `PerformanceTestResults.json`）で読み書きする。この経路は
**`-testResults` の指定とは無関係**で、`Application.persistentDataPath` は会社名・製品名だけで決まる
ため全 worktree・全 Editor プロセスで同一パスになる。複数 worktree で同時に verify を実行すると、
一方が書き込み中のこのファイルにもう一方が `File.CreateText` / XML 読み込みで触れて
`IOException: Sharing violation` になり、`Debug.LogException` 経由でログに出た例外を
`Test-LogHasErrors` が拾って本当はテスト成功なのに検証失敗と誤判定する（実測: PR #169 作業中、
2026-09-30。issue #175 に採取ログ・スタックトレースあり）。

パッケージの `Editor/TestRunnerInitializer.cs`（`[InitializeOnLoad]`、ソースで確認）は、コマンドライン
引数に `-runTests`（または `-runEditorTests`）と `-perfTestResults <path>` が両方渡された場合のみ、
コールバックを `Editor/CmdLineResultsSavingCallbacks.cs` に切り替える。この実装は
`Application.persistentDataPath` を一切参照せず、実行中のテスト結果（`ITestResultAdaptor`、
インメモリ）から直接パフォーマンステストデータを取り出して指定パスへ JSON で書き込むだけなので、
`TestResults.xml` の読み書き自体が発生しなくなる。

`scripts/verify.ps1` はこれを利用し、Unity 起動引数に
`-perfTestResults Logs\test-results\<EditMode|PlayMode>-perf-results.json`（プロジェクト内、worktree ごとに
分離済み）を渡す。パフォーマンステストを実際には使っていないため、このファイルは通常は生成されない
（`TestResultsParser.GetPerformanceTestRunData` が `null` を返し、書き込み自体がスキップされるため）。
狙いは「ファイルを作ること」ではなく、**`Application.persistentDataPath` 上の共有ファイルへの
アクセス自体を発生させないこと**にある。

検討した他の方針（採らなかった理由）:
- パッケージの除去: `com.unity.collections` の推移依存であり、直接 `manifest.json` に列挙されていない
  ため除去できない（`packages-lock.json` で確認）
- `scripts/log-scan.ps1` 側でこの `IOException` を無視パターンとして追加する案: ログ判定側での
  抑止は「本当は起きている競合を見えなくする」対症療法であり、他の `IOException`（実際のバグ）との
  判別パターンが複雑になる懸念があったため、コマンドライン引数で競合そのものを起こさなくする
  今回の方法を優先した

### 3.2 `scripts/build.ps1`
- Windows Standalone ビルドを実行し、既定では `Builds/Windows/` に出力する
- ビルドログを grep してコンパイルエラー・警告を確認する
- `-OutputDir <path>`（issue #131）を指定すると、任意の固定パスへ出力できる。実機確認を
  固定パスへ集約してファイアウォールの許可を1回で済ませる運用は 3.3.1 を参照。
  `-OutputDir` 使用時のみ、出力先の `TsumugiQuiz.exe` を実行中のプロセスがあれば
  上書きせずエラー終了し（プロセスは止めない）、`<OutputDir>/.build.lock` で複数
  worktree からの同時ビルドを直列化する（`scripts/build-output-lock.ps1`。既定の
  `Builds/Windows`（worktree ごとに異なるため競合しない）ではこのロック・プロセス検出は行わない）
- **ビルドするたびに、ネットワーク上は別のビルドになる**（issue #204）。ホストは、ビルドの識別子（Unity の
  `Application.buildGUID`。ビルドのたびに一意、Editor では空）が自分と違うクライアントを承認で拒否する
  （「バージョンが異なります（ホスト: x / あなた: y）。」。x / y はビルドの番号。docs/network.md §2.3「バージョンとビルドの一致」）。
  識別子は Unity がビルド時に作るので、ビルドの手順に追加の操作は無い。同じソースからビルドし直しても別のビルドになるので、
  マルチプロセス検証（3.3）・実機確認は**同じ出力フォルダの exe どうし**で行う。Editor の Play モードとビルドした exe は
  接続できない（Editor どうし・PlayMode テストは識別子が同じ空なので接続できる）。自分のビルド番号は、起動ログの
  `[NetworkBootstrap] 起動しました … buildNumber=N buildGuid=…` とクレジット画面の「（ビルド番号 N）」で確かめられる。
  `BuildCommand` に `BuildOptions.NoUniqueIdentifier` を付けないこと（Unity の XML ドキュメント: 「Will force the buildGUID to
  all zeros.」。どのビルドも識別子が同じ全 0 になり、ビルドの照合が効かなくなる）

### 3.3 マルチプロセス手動検証

`scripts/run-multi.ps1`（issue #8）が既定では `Builds/Windows/TsumugiQuiz.exe` を複数起動し、`-tq-` 系引数
（docs/network.md §10.3）でホストの自動開始・クライアントの自動参加までを自動化する。

**`runInBackground`（issue #153）**: `ProjectSettings` の `runInBackground` は `1`（バックグラウンドでも
Update・描画・NGO の送受信を継続する）にしてある。従来は `runInBackground: 0` のままだった
ため、1 台の PC でホスト・クライアントの複数プロセスを同時に動かす本節の手動検証で、前面にない
プロセスの画面が更新されず（issue #15 の実機確認で、前面にないクライアントを 60 秒間連写した画面が
ピクセル単位で同一だったことを実測）、ハンドシェイク・再接続の検証が成立しない原因になっていた
可能性がある。`runInBackground: 1` にした現在も、スクリーンショット・クリック／キー送出の対象は必ず
`SetForegroundWindow` で前面化してから操作すること（§3.3.1 の PID 照合ルールを参照。バックグラウンドで
進行が止まらなくなっただけで、入力送出には引き続きフォーカスが必要）。

```powershell
# ビルド → ホスト1 + クライアント2 を起動し、20秒後にスクリーンショット→全プロセス終了
pwsh ./scripts/build.ps1
pwsh ./scripts/run-multi.ps1 -Count 3 -KillAfter 20

# ホスト1 + クライアント1 だけでよい場合
pwsh ./scripts/run-multi.ps1 -Count 2 -KillAfter 20

# ビルドから一括で行う場合
pwsh ./scripts/run-multi.ps1 -BuildFirst -Count 3 -KillAfter 30
```

| オプション | 意味 |
|---|---|
| `-Count` | 起動するプロセス数（**ホスト込み**。既定 3 = ホスト1 + クライアント2。2〜8） |
| `-BuildFirst` | 起動前に `scripts/build.ps1` を実行する（`-BuildDir` 指定時は `scripts/build.ps1 -OutputDir <同じパス>` を呼ぶ） |
| `-BuildDir <path>` | ビルド済み `TsumugiQuiz.exe` を探すフォルダ（issue #131）。既定は自 worktree の `Builds\Windows`。`scripts/package-release.ps1 -BuildDir` と引数名・意味を揃えてある。固定パスでの実機確認（3.3.1）に使う |
| `-AllowForeignBuild` | `-BuildDir` 指定時、対象フォルダの `.build-info.json` の `gitCommit` が自分の worktree の現在の HEAD と異なる（または `.build-info.json` が無い）場合でも、既定の失敗を抑止してそのまま使う（issue #131 レビュー H-1。3.3.1 を参照） |
| `-KillAfter <秒>` | 指定秒数後、各プロセスのウィンドウをスクリーンショットしてから全プロセスを終了する（既定 0 = 起動したままにする。手動確認向け） |
| `-WindowWidth` / `-WindowHeight` | 各ウィンドウのサイズ（既定 640x480）。`-tq-window` でプロセスごとに配置する（ディスプレイの幅・高さの両方で折り返してグリッド状に並べる。L-6。1 画面に収まる数を超える分は同じマス目へ折り返して重ねて配置する簡易実装） |
| `-JoinCodeTimeoutSeconds` | ホストが参加コードを書き出すまでの待機上限（既定 60 秒） |
| `-HostPort` | ホストの待ち受けポート（既定 0 = OS に空きポートを選ばせる）。スクリーンショットを撮るときは `7777` のような固定値を指定し、撮影機の実際の待ち受けポートが参加コードに符号化されて写り込まないようにする（issue #132） |
| `-IsolateDocuments` | 指定すると、各プロセスの **Documents ルート**（問題フォルダ `Documents\TsumugiQuiz\Questions\`・プリセットフォルダの親）を `Logs/multi/documents-<役割>` へ隔離し、`-tq-documents-root` で渡す（issue #112）。隔離先にはサンプル問題セット（`Assets/TsumugiQuiz/Resources/Questions/sample-questions.json` と `sample-image.bytes` → `images/sample.png`）を複製するので、そのまま「ゲーム開始」まで確認できる。既定（未指定）は実ユーザーの Documents をそのまま使う。`-KeepDataRoots` と併用すると、データルートと同様に隔離先の Documents ルートも作り直さず既存のものを保持する（PR #118 レビュー L-3、issue #122-5） |
| `-KeepDataRoots` | 指定すると、各プロセスの一時データルート（`Logs/multi/data-root` / `data-root-Client<n>`）を実行のたびに作り直さず、既存のものをそのまま使う（既存の `consent.json` があれば同意記録の再コピーもスキップする）。既定（未指定）は毎回まっさらに作り直す。issue #15。**これ単独では #69 の再接続シナリオを再現できない**（詳細は下記「`-KeepDataRoots` の位置づけ」を参照） |

`scripts/run-multi.ps1` / `scripts/window-capture.ps1` は開発者のローカル環境専用の検証ツールであり、
配布物（`Builds/` の zip 等）には含めない（L-7）。

- ホストは `-tq-host -tq-port 0 -tq-name Host -tq-data-root <一時フォルダ> -tq-window ...` で起動する。
  HostSetup 画面が起動時に自動でホストを開始し（同意ゲート #37 は迂回しない。未同意状態なら Terms で
  止まる）、到達性解決後に LAN 参加コードを `<一時フォルダ>/join-code.txt` へ書き出す
  （**承認済み**: `-tq-port 0` は「OS に空きポートを選ばせる」の意味。`NetworkRuntimeOptions`（起動時ログ用、
  #2）とは異なり、自動ホスト開始が読む値は 0 をそのまま尊重する。詳細は
  `Assets/TsumugiQuiz/Scripts/Network/NetworkRuntimeOptions.cs` の XML doc、
  `Assets/TsumugiQuiz/Scripts/UI/LaunchOptionsRunner.cs` の `TryGetPort` を参照）
- `join-code.txt` は **LAN 用参加コードのみ**を平文で書き出す（暗号化・アクセス制御は行わない。
  同一 PC 上の他プロセスから読める前提）。インターネット用参加コードは対象外（同一 PC 内の検証では
  LAN 接続で十分なため）。書き込みは一時ファイル→リネームでアトミックに行う
- `run-multi.ps1` はこのファイルを読み取り（内容を Crockford Base32 の参加コード形式の正規表現で
  大文字小文字を区別して検証してから使う。L-3）、クライアントを `-tq-join <コード> -tq-name Client<n>
  -tq-data-root <クライアント専用の一時フォルダ> ...` で起動する（**M-3**: クライアントごとに
  `Logs/multi/data-root-Client<n>` という専用の一時データルートを持つ。ホストと共有すると、複数
  プロセスが同じ consent.json / session-token.json / TtsCache を同時に読み書きして競合するおそれが
  あるため）。Join 画面が起動時に自動で参加コードを入力して接続する
- 各プロセスのログは `Logs/multi/<name>.log`（`Host` / `Client1` / `Client2` ...）に分かれる。
  `-KillAfter` 指定時は `Logs/multi/<name>.png` にスクリーンショットも保存する
  （`System.Drawing`、`scripts/window-capture.ps1`。ウィンドウが見つからない・撮影に失敗した場合は
  警告を出してスキップし、全体は継続する）
- **`window-capture.ps1` の撮影範囲（#105 レビュー M-3・L-1）**: `DwmGetWindowAttribute`
  (`DWMWA_EXTENDED_FRAME_BOUNDS`) で実際に見えているウィンドウ範囲だけを撮る（`GetWindowRect` は
  Windows 11 の DWM が付ける不可視のリサイズ余白まで含んでしまい、背後の別ウィンドウが写り込むことが
  ある。失敗時のみ `GetWindowRect` にフォールバックする）。それでもタイトルバーを含めて撮影する場合は
  ウィンドウ角の角丸（Windows 11 の既定装飾）分だけ最外周にわずかに背後が透けて写ることがあるため、
  **タイトルバーを含める場合は角丸コーナー分（上下 8px 程度）も一緒にクロップする**こと
  （#105 当時の README 用の 3 枚はタイトルバーを含めて撮影し、この処理を行っていた。いまの README・手順書の画面
  （`docs/manual/images/`、PR #218）は PlayMode で UI を RenderTexture に描いて撮っており、この節の手順は使っていない）
- **撮影は表示スケール 100% で行う（#105 レビュー L-2）**: ディスプレイの拡大縮小設定が 100% 以外だと、
  DWM が返す物理座標と `Graphics.CopyFromScreen` が使う仮想化された論理座標がずれ、撮影結果がウィンドウ
  範囲からずれる（意図しない領域が写る／端が切れる）ことがある
- **撮影用に手動で起動したプロセスの後始末は、`run-multi.ps1`（または自分が直接 `Start-Process` 等で
  起動した呼び出し）自身が返した PID だけを使って `taskkill /PID <pid>` / `Stop-Process -Id <pid>` で
  行う**。`taskkill /IM TsumugiQuiz.exe` や `Get-Process -Name TsumugiQuiz | Stop-Process` のような
  イメージ名・プロセス名指定の一括終了は禁止。**`Get-Process -Name TsumugiQuiz` 等で列挙した結果から
  対象を選んで PID 指定で終了する方法も禁止**（結局は名前ベースの列挙から選んでいるだけで、同一
  マシンで並行して動いている別 worktree の検証プロセス（同じ実行ファイル名になる）を巻き込む事故を
  防げない。#105 レビュー・#113 レビューの両方で実際に発生した事故）。終了前には必ず
  `Get-Process -Id <pid> | Select-Object Path` 等で、そのプロセスの実行ファイルパスが自分の
  worktree 配下（例 `*\worktrees\i113\Builds\Windows\TsumugiQuiz.exe`）であることを確認すること。
  **issue #131 レビュー H-2: この実行ファイルパスによる確認は、3.3.1 の固定パス運用（`-BuildDir`
  で全 worktree が同じ exe パスを指す）では識別能力を失う。固定パス運用では実行ファイルパスの
  一致を確認しても「自分の worktree のプロセスか」は判別できないため、`run-multi.ps1` が
  自分の呼び出しの結果として返した PID のみが唯一の識別子になる。`Get-Process -Name
  TsumugiQuiz` からの列挙で対象を選ぶことは（パス確認の有無に関わらず）禁止**
- スクリプト自体が失敗した場合は、`-KillAfter` の指定に関わらず起動済みの全プロセスを強制終了して
  から終了する（孤児プロセスを残さないため）。失敗時は終了コードが非 0 になる（H-1・M-8）。
  Ctrl+C による中断は、PowerShell のホスト・実行状況によっては `finally` が実行されないことがあり、
  この後始末が必ず行われる保証はない（L-8）。その場合はタスクマネージャー等で手動終了すること
- **既知の制約（M-4、issue #155 で更新: L-4）**: `-tq-data-root` は `join-code.txt` の出力先だけを切り替える
  のではなく、issue #71 の `AppPathsBootstrap` により **`AppPaths.DataRoot` 全体**（`consent.json` に加え
  `session-token.json`・`TtsCache`・`app-settings.json` 等も含む）を切り替える。
  **`player.name`（PR #92 H2）・`network.port`（PR #92 H2）・`host.role`（issue #155）はいずれも
  `app-settings.json`（`AppSettingsStore`、`AppPaths.DataRoot` 配下）に保存されるため、`-tq-data-root`
  によるデータルート分離の対象に含まれる**（`-IsolateDocuments` は問題フォルダ側の分離であり無関係）。
  旧 `PlayerPrefs`（Windows ではレジストリ、companyName/productName キー）にはこれら 3 項目の暫定保存の
  名残りが残っていることがあるが、いずれも初回読み込み時に一度だけ `app-settings.json` へ引き継いで
  旧キーを削除するため、以後は参照しない。`-tq-name` / `-tq-port` は常に CLI 引数が優先されるため、
  仮に旧 `PlayerPrefs` の値が残っていても動作には影響しない
- **Documents（問題フォルダ）の扱い（issue #112）**: `-tq-data-root` は `AppPaths.DataRoot` を切り替えるだけで、
  問題フォルダ `%USERPROFILE%\Documents\TsumugiQuiz\Questions\` は切り替えない。既定ではビルドは
  実ユーザーの問題フォルダを読む（本番と同じ挙動）。`-IsolateDocuments` を付けると各プロセスへ
  `-tq-documents-root <プロジェクトルート>\Logs\multi\documents-<役割>` を渡し、実ユーザーの Documents に
  一切触れずに検証できる（解決順は docs/question-data.md §4 を参照）。並行して PlayMode テストを
  回す場合や、実ユーザーの問題セットに左右されずに確認したい場合はこちらを使う
  （PlayMode テスト側も #112 で `Documents` ルートを一時フォルダへ隔離済みなので、
  「テスト用の問題セット JSON が実機確認に混入する」経路は両側から塞いである）
- **Windows ファイアウォールの確認ダイアログ（issue #15 実機確認で実測）**: `Builds/Windows/TsumugiQuiz.exe`
  を（再ビルド後の初回起動を含めて）初めてこの実行ファイルとして起動すると、Windows セキュリティ
  （`PickerHost.exe`、「パブリック ネットワークとプライベート ネットワークにこのアプリへのアクセスを
  許可しますか？」）の確認ダイアログが表示されることがある。このダイアログはシステムモーダルで、
  表示中はデスクトップ全体の入力（他ウィンドウへのクリック・キー入力を含む）がブロックされる。
  検証を自動化する前に、一度手動でアプリを起動してこの確認に応答しておくこと。検証中に不意に
  表示された場合は、エージェントは自動クリック・自動キー送出で閉じようとせず（ユーザーの実際の
  セキュリティ判断を代行しない）、ユーザーにダイアログの解消を依頼してから自動化を再開すること。
- **前提条件（同意ゲート、実測で判明）**: `run-multi.ps1` は（`-KeepDataRoots` 未指定時）ホスト・
  クライアントそれぞれに毎回まっさらな一時データルートを作るため、そのままでは同意記録が無く、
  Terms 画面で止まって `-tq-host` / `-tq-join` が発火しない。本スクリプトは通常起動時のデータルート（`scripts/common.ps1` の `Resolve-AppDataRoot`
  が `ProjectSettings/ProjectSettings.asset` の companyName / productName から組み立てる
  `%USERPROFILE%\AppData\LocalLow\<companyName>\<productName>`、または環境変数
  `TSUMUGI_DATA_ROOT` があればそちら）に既に `consent.json` があれば、各プロセスの一時データルートへ
  コピーして引き継ぐ（同意を代行・偽装するのではなく、既に得た同意を参照するだけ）。まだ一度も
  同意していない環境では、先にアプリを通常起動して Terms 画面で同意してから `run-multi.ps1` を使うこと
- **`Resolve-AppDataRoot` の解決元ログ（issue #15）**: 各プロセスのデータルート初期化時に
  `通常起動時のデータルート解決元: <Source>（<Path>）` を出力する。`<Source>` は
  `Explicit`（呼び出し側の明示指定。本スクリプトでは未使用） / `Environment`（環境変数
  `TSUMUGI_DATA_ROOT`） / `ProjectSettings`（`ProjectSettings.asset` の companyName/productName から
  組み立てた既定パス）のいずれか。同意記録の引き継ぎ元を取り違えていないか実行時に確認できる
- **`-KeepDataRoots` の位置づけ（issue #15 レビュー H-2 で訂正）**: 「`run-multi.ps1` を 2 回実行して
  #69 の再接続を検証する」という手順は成立しない。`-tq-port 0` を指定しているためホストは
  実行のたびに別ポートで起動し直し、クライアントの再接続トークンは接続先の `アドレス:ポート` を
  キーに保存される（`Assets/TsumugiQuiz/Scripts/Network/NetworkService.SessionToken.cs`
  `LoadReconnectToken` / `Assets/TsumugiQuiz/Scripts/Core/Network/SessionTokenHostKey.cs`
  `TryCreate` を参照）。さらに「誰がどの席か」というサーバー側の名簿は `LobbyState` が
  ホストプロセスのメモリ上にのみ保持しており、ホストプロセスを終了・再作成すれば消える。
  `run-multi.ps1` を 2 回実行するとホストプロセスごと作り直されるため、たとえ `-KeepDataRoots` で
  `session-token.json` を保持していても、キーが変わって（かつサーバー側の名簿も無くなって）
  同じ席へ復帰する経路自体が成立しない。`-KeepDataRoots` 自体の役割は、`run-multi.ps1` を
  複数回実行する際に各プロセスの一時データルート（`session-token.json` 等）を作り直さず保持する
  ことだけであり、それ単独で #69 の再接続シナリオを再現できるとは断定しない。
- **#69 再接続の手動検証手順（訂正版）**: 「ホストは動かしたまま、クライアントだけを終了・再起動して
  同じホストへ再接続する」ことが本来の検証シナリオのため、`run-multi.ps1` の複数回実行ではなく
  次の手順で行う。
  1. `pwsh ./scripts/run-multi.ps1 -Count 2`（`-KillAfter` は指定せず、起動したままにする）
  2. `Logs/multi/Client1.log` 等でロビーへの参加を確認したら、タスクマネージャー等で `Client1` の
     プロセスのみを終了する（`Host` プロセスは終了させない。`session-token.json` は
     `Logs/multi/data-root-Client1` に残る）
  3. `Logs/multi/Host.log` から現在の参加コードを確認し、`Builds/Windows/TsumugiQuiz.exe` を
     `-tq-join <参加コード> -tq-name Client1 -tq-data-root <プロジェクトルート>\Logs\multi\data-root-Client1
     -logFile <プロジェクトルート>\Logs\multi\Client1-reconnect.log`
     で手動で再起動する（`run-multi.ps1` は使わない。使うとホストごと新しいポートで作り直されてしまうため。
     `-tq-data-root` はデータルート＝ `consent.json` / `session-token.json` 等の保存先を切り替えるだけで
     プレイヤーログの出力先は変えないため、`run-multi.ps1` が使う `Logs/multi/Client1.log` を上書きしない
     よう `-logFile` を明示し、別名 `Client1-reconnect.log` に出す）
  4. 同じホスト（同じ `アドレス:ポート`）に対して同じ `session-token.json` が送られ、同じ席へ
     復帰することを `Logs/multi/Client1-reconnect.log` / スクリーンショットで確認する

  本手順は未実施（issue #15 の不具合1によりロビーから先へ進めないため）。#95 マージ後に実施して
  `docs/tasks/m2-verification.md` に追記する。

#### 3.3.1 固定パスでの実機確認（ファイアウォール許可を1回で済ませる、issue #131）

worktree ごとに `scripts/build.ps1` の出力先（`<worktree>/Builds/Windows/TsumugiQuiz.exe`）が
異なると、実機確認のたびに Windows ファイアウォールの許可ダイアログ（`PickerHost.exe`、
システムモーダル）が出る。このダイアログはエージェントが自動操作できないため、実機確認を
固定パスへ集約し、そのパスの `TsumugiQuiz.exe` を最初の 1 回だけユーザーがファイアウォールで
許可しておく運用にする。

```powershell
# 実機確認の直前に必ず自分の worktree から固定パスへビルドし直す
# （メインツリー配下。.gitignore の /[Bb]uilds/ で除外される）
pwsh ./scripts/build.ps1 -OutputDir E:\Claude\tsumugi-quiz\Builds\verify\Windows

# 初回のみ: ユーザーが E:\Claude\tsumugi-quiz\Builds\verify\Windows\TsumugiQuiz.exe を
# 一度起動し、Windows ファイアウォールの確認ダイアログで「アクセスを許可する」を選ぶ
# （ダイアログの操作自体はエージェントが行わない）。

# 以後の実機確認は固定パスを -BuildDir で指定する
pwsh ./scripts/run-multi.ps1 -BuildDir E:\Claude\tsumugi-quiz\Builds\verify\Windows -Count 2 -IsolateDocuments -KillAfter 30
```

**固定パスは複数 worktree が共有する資源である**。他 worktree が最後にビルドしたバイナリを
自分の実機証跡と誤認する事故を防ぐため、**実機確認の直前には必ず自分の worktree から
`-BuildFirst -BuildDir <固定パス>`（または `build.ps1 -OutputDir <固定パス>`）でビルドし直すこと**。
**未コミットの変更がある状態で実機確認する場合は、コミット照合だけでは同一性を保証できないため
（下記の出所照合・`dirty` を参照）、必ず `-BuildFirst`（または直前の `build.ps1 -OutputDir`）で
その場の作業ツリーからビルドし直すこと。**

固定パスはメインツリー（`E:\Claude\tsumugi-quiz`）配下だが、メインツリーの `.gitignore` に
`/[Bb]uilds/` があるため `Builds/verify/Windows` は git 管理外になる（`git check-ignore -v` で確認済み）。

並行する worktree が同じ固定パスへ同時にビルドすると出力が競合するため、`build.ps1 -OutputDir`
使用時は次の安全策が入っている（3.2 も参照）。

- 出力先の `TsumugiQuiz.exe` を実行中のプロセス（`Get-CimInstance Win32_Process` で
  `ExecutablePath` を照合）があれば、上書きせずエラー終了する（**プロセスは止めない**。
  実機確認中の別 worktree の検証を壊さないため）
- `<OutputDir>/.build.lock`（ビルドを実行した pwsh の PID とタイムスタンプを記録）で
  複数 worktree からの同時ビルドを直列化する。ロック取得時に既存のロックファイルがあっても、
  記録された PID のプロセスが既に終了していれば「古いロック」とみなして無視し、上書きする
- **出所の記録・照合（issue #131 レビュー H-1・M-1・M-2）**: ビルド成功後、
  `<OutputDir>/.build-info.json`（`gitCommit` / `gitBranch` / `sourceWorktree` / `builtAtUtc` /
  `dirty`、camelCase）を書き出す（`dirty` はビルド時点で作業ツリーに未コミットの変更が
  あったかどうか）。`scripts/run-multi.ps1 -BuildDir <固定パス>` は起動時にこの内容を
  標準出力へ表示し、`scripts/common.ps1` の `Test-BuildProvenance` で実行元 worktree と照合する。
  **`gitCommit` の一致だけでなく `sourceWorktree`（大文字小文字無視・末尾の `\`/`/` 無視・
  絶対パスであれば `..` を含む表記も `GetFullPath` で正規化して吸収する。ただし相対値
  （例: `.`）はそのまま比較され、常に絶対パスの実行元 worktree とは一致しないため拒否扱いになる。
  issue #142 レビュー M-2）の一致も必須とする**（同じ HEAD の別 worktree ―― develop から
  切ったばかりの feature ブランチ等で日常的に起こる状況 ―― を誤って「同一」と判定しないため）。
  **`.build-info.json` が無い・
  壊れている・`gitCommit` を取得できない等、「検証できない」場合も不一致と同様に既定で失敗する**
  （以前の実装ではこれらのケースが暗黙的に素通りしていた）。意図的に他 worktree のビルド・
  検証不能なビルドをそのまま使う場合のみ `-AllowForeignBuild` を明示的に指定する。一致していても
  `dirty=true`（記録時に未コミットの変更があった）の場合は、コミット照合では同一性を保証できない
  旨の警告を出す。`.build-info.json` / `.build.lock` は `scripts/package-release.ps1` の既知の
  除外パターンにも追加済みで、固定パスを誤って `-BuildDir` に指定して配布 zip を作っても混入しない
  （issue #142 項目 5: `scripts/common.ps1` の `Test-GitWorkingTreeDirty` は `git` コマンドの
  実行自体が失敗した場合も `dirty=false` と記録する。「未コミットの変更が無いと確認できた」場合と
  「判定できなかった」場合を区別しないので注意すること）

固定パスの運用では、同一マシン上で他 worktree の `TsumugiQuiz.exe` が並行動作していることが
常態になる（同じ実行ファイル名・似た配置のウィンドウが複数存在しうる）。**この場合、実行
ファイルパスの一致確認は識別に使えない（3.3 の後始末ルールを参照）。自分が起動した呼び出し
（`run-multi.ps1` 等）が返した PID のみが唯一の識別子であり、`Get-Process -Name TsumugiQuiz`
からの列挙で対象を選ぶことは禁止**。**自分が起動していないプロセスは終了しないこと**（他
worktree の検証プロセスである可能性があるため）。スクリーンショット取得やクリック・キー送出の
前には、必ず対象ウィンドウのオーナー PID が自分が起動したプロセスの PID であることを検証すること。
`SetForegroundWindow` で前面化した後も、`WindowFromPoint` / `GetWindowThreadProcessId` 等で
そのハンドルの所有 PID を照合し、一致しない場合は送出・撮影を行わない（PR #124 レビュー L-a:
誤ったウィンドウを撮影・操作する事故の再発防止。`scripts/window-capture.ps1` は撮影直前に矩形
中心で同様の照合を行い、不一致なら重なっているウィンドウの所有プロセス名を含めて警告しスキップ
する実装で担保している。issue #131 レビュー L-8。**撮影スキップは他プロセスのウィンドウが
撮影対象に重なっていることが原因なので、警告に出たプロセスを終了・移動するなど重なりを除いてから
撮り直すこと**）。


**ファイアウォール許可ダイアログの再発について（issue #131 レビュー M-5、未実測の留保）**:
上記の手順でユーザーが一度「アクセスを許可する」を選んだ後、2 回目以降の実行でダイアログが
出ないことは、本 issue の実装時点では未実測（ユーザーによる許可操作を待っている状態のため）。
再発した場合は、自動操作せずユーザーにダイアログの解消を依頼すること（3.3 の既存の注記を参照）。

**スクリーンショットに参加コードを写す場合（PR #99 の事故を踏まえた運用）**: PR #99 のレビュー（H-1）で
マージ前に、実グローバル IP を復元できる参加コードが画像に写っていることが指摘された。以後、参加コードを含む
撮影では次の手順を必須とする（PR #141 §7 で実施した手順）。

- ホスト設定の「グローバル IP を手入力する」欄に、実グローバル IP ではなく
  [RFC 5737](https://www.rfc-editor.org/rfc/rfc5737) のドキュメント用アドレス（TEST-NET-3）
  `203.0.113.10` を入力してインターネット用参加コードを生成する
- ホストは `-tq-port 7777`（固定値）で起動する
- 生成された参加コードを `docs/network-joincode.md` §1.5 のデコード手順で検証し、
  `203.0.113.10:7777` のみが復元されること（実グローバル IP・実ポートを含まないこと）を確認してから
  スクリーンショットを採用する
- LAN 用参加コード（実装上、実 LAN IP から生成されるため上記の置き換えができない）・実グローバル IP・
  PC 名・ユーザー名が写っている画像は、そのまま公開しない。撮り直せない項目は黒塗りしてから使う

#### 3.3.2 確認項目チェックリスト

- [ ] `Logs/multi/Host.log` に `[HostSetupView] -tq-host 指定により自動でホストを開始します。` と
  `[HostSetupView] ホストを開始しました activePort=...` のログがあり、`Logs/multi/Client*.log` に
  `[JoinView] -tq-join 指定により自動で参加コードを入力して接続します。` のログがある
  （ロビーへの実際の参加は NGO 側がログしないため、`Logs/multi/Client*.png` のスクリーンショットで
  ロビー画面の参加者一覧を目視確認する。#7 のロビー参加通知・NGO の `OnClientConnected` 相当）
- [ ] 1 問フローが最後まで動く（出題 → 早押し → 判定 → 次の問題。issue #14）
- [ ] 読み上げが同期して再生される（voicevox_core・辞書等が `External/` から配置済みの環境でのみ確認可能。issue #23 の実測を引き継ぐ）
- [ ] ルーム設定の変更が全クライアントに反映される
- [ ] （該当 PR がネットワーク周りを触った場合）実ルーターでの UPnP 動作を
  `pwsh ./scripts/verify.ps1 -IncludeNetwork -Platform PlayMode` で確認する（issue #3 の実ルーター確認を引き継ぐ。
  同一 PC 内のマルチプロセスは同一ネットワーク上のループバックに閉じるため、実ルーターの UPnP 応答や
  グローバル IP 取得の検証は代替できない）
- 確認結果（実測ログの抜粋・スクリーンショットの有無・気づいた不具合）は、issue #15 が定める
  `docs/tasks/m2-verification.md`（M2 で 1 問プレイを通しで確認する専用issue、#15 本文を参照）に
  記録する。issue #8 の PR 時点では #15 が未着手のため、本 issue の検証結果は PR 本文に記載する

#### 3.3.3 実機の自動操作で判明していること（2026-09-30 時点）

- **`SendInput`（user32.dll）はこのアプリのウィンドウに届かないことがある。クリックは `mouse_event`、
  キー入力は `keybd_event`（仮想キーコード指定）なら届く**（issue #15 コメント、2026-09-30）
- **`keybd_event` にはスキャンコードを付けること（#148、PR #169 で実測）。** `keybd_event(vk, 0, flags, 0)` のように
  `bScan = 0` で送ると、文字入力（`WM_CHAR`）経由の操作（テキスト入力欄での英字入力・`Ctrl+A`・回答送信の Enter）
  には反映されるが、**Input System を経由する操作には反映されない**。Input System はキーを物理位置（スキャンコード）で
  識別するため。該当する操作は次の 2 つ。
  - UI Toolkit のフォーカス移動・ボタン実行（Tab / Shift+Tab / Enter。InputForUI 経由。docs/architecture.md §10.9）
  - Input System の `InputAction`（早押しの Space、`<Keyboard>/space`）

  `keybd_event(vk, (byte)MapVirtualKey(vk, 0), flags, 0)` のようにスキャンコードを渡すと、Title で Tab = 0x0F による
  フォーカス移動と、Enter = 0x1C によるフォーカス中ボタンの実行が動作した（2026-09-30、ビルドで実測）。
  **早押しの Space（0x39）をスキャンコード付きで送った場合の反応は未実測**（Game 画面はネットワークが要るため）。
  Input System 経由という同じ理由で反応すると推測しているが、実機確認で使う前に一度確かめること。
  以前この節に「`keybd_event` では Tab / Space に反応しない」と書いていた観測（issue #148 コメント）は、
  いずれも `bScan = 0` で送っていたためのものである。
  実キーボードでの Tab / Enter の確認は、ユーザーに依頼している（issue #148）。

  マウスクリックによる早押し（回答権の獲得）・選択式のボタン押下・ロビーでの操作は、いずれも問題なく
  機能する（issue #15 コメント、issue #148 コメント、いずれも 2026-09-30）。
- マウスカーソルを `SetCursorPos` でウィンドウ外へ逃がしただけでは Unity にマウス移動が届かず、直前に
  ホバーしていた要素の枠が残ったまま撮影されることがある。ウィンドウ内の地点へ移してから相対移動
  （`mouse_event` の `MOUSEEVENTF_MOVE`）を送ると解消する（PR #141 §5.8）。

**未確認（一次情報なし、issue #157 起票時点）**: `KEYEVENTF_UNICODE` によるキー送出が `TextField` へ
反映されるかどうか、および撮影前に描画完了を画素比較で判定する手法。いずれも本リポジトリの
issue/PR コメントに実測記録が見当たらない。実機確認で必要になった場合は、改めて実測してから
本節に追記すること。

上記の操作を安全に行うための規則（最前面ウィンドウの PID 照合、`run-multi.ps1` が返した PID・自分が
`Start-Process` で起動した PID のみを終了、ファイアウォールの確認ダイアログは自動操作しない、他 worktree
の `TsumugiQuiz.exe` に触らない）は §3.3・§3.3.1 に既出のため、ここでは繰り返さない。

### 3.4 テスト用フェイクの置き場所
- テスト用フェイク・ファクトリの置き場所の規約は `docs/tasks/impl-rules.md`「コード配置規約」を参照

### 3.5 PlayMode の UI シーンテストを書くときの必須ルール（#108）

- **Main シーンのルート（`UIDocument.rootVisualElement`）は必ず `MainSceneTestHelpers` 経由で取得する**
  （`LoadMainSceneAndGetRoot` / `LoadBootThenMainSceneAndGetRoot`、または少なくとも
  `FindViewRouterUIDocument()`）。これらは取得時に UI Toolkit の**ライブリロードを止める**
  （`UiToolkitLiveReloadGuard`）。止めないと、Editor 実行中にアセットの dirty カウントが変わった瞬間に
  `UIDocument.RecreateUI()` が走ってルートが差し替わり、テストが掴んでいた要素は
  「`Q()` では見つかるのに `element.panel` が永久に null」になる（＝ クリックが届かない）。
  `FindAnyObjectByType<UIDocument>()` を直接呼ぶ独自実装を増やさないこと。
- **teardown でのシーン破棄は必ず `MainSceneTestHelpers.UnloadMainSceneRoutine()` を使う**。
  生の `SceneManager.UnloadSceneAsync(mainScene)` は、ロード済みシーンが Main 1 枚だけのとき
  Unity に拒否されて（`Unloading the last loaded scene ... is not supported` の警告）**何もしない**ため、
  Main シーンが次のテストへ持ち越される。`UnloadMainSceneRoutine()` は空シーンを 1 枚作って
  アクティブにしてからアンロードする。
- teardown の順序は「`UnloadMainSceneRoutine()`（＝ View を畳む）→ シングルトン（`NetworkBootstrap` 等）の破棄」。
  逆順だと、表示中の View のコールバックが破棄済みの `NetworkService` を触りうる（#101）。
  ネットワークを使うテストでは、その前に `NetworkService.Stop()` を呼んでおく（アンロード中も通信が走り続けるのを避けるため）。
- **Main シーンを使わず自前で `UIDocument` を作るテスト**（`CharacterViewTests` のようなケース）でも、
  要素を掴んだまま複数フレーム待つなら `UiToolkitLiveReloadGuard.Disable(root.panel)` を呼ぶ。
  ライブリロードはパネル単位の設定なので、`MainSceneTestHelpers` 経由で作られていないパネルには効いていない。
- **唯一の例外は `ViewRouterLiveReloadRedrawTests`**（issue #137 の回帰テスト）。実際の
  `UIDocument.RecreateUI()` を起こすために、このテストの中だけ意図的にライブリロードを再度有効化する。
  ライブリロードの ON/OFF はパネル単位の設定でシーンをアンロードしても自動では戻らないため、
  **必ず元の状態に戻すこと**（`finally` で戻し、`[UnityTearDown]` でも保険として戻す。issue #137 レビュー H-1）。

### 3.6 Editor で Play 中に UXML を保存しても画面が空にならないことの手動確認（#137）

`ViewRouter` は #137 で、ライブリロード（`UIDocument.RecreateUI()`）による `rootVisualElement` の
作り直しを検知し、現在の View を自動で再表示するようになった（`#if UNITY_EDITOR` のみ、Player ビルドは影響なし）。
Editor での動作確認手順:

1. Unity Editor で `Assets/TsumugiQuiz/Scenes/Main.unity` を開き、Play する
2. 任意の画面（例: タイトル画面）が表示されていることを確認する
3. Play したまま、表示中の View に対応する UXML（例: タイトル画面なら
   `Assets/TsumugiQuiz/UI/Views/title-view.uxml`）を UI Builder または Inspector で開き、
   何か 1 箇所を編集して保存する（`Ctrl+S` 等。見た目に影響しない微小な変更でよい）
4. 保存後、数秒待つ（ライブリロードの監視間隔は実測 約1 秒）。画面が空白にならず、
   保存前と同じ View が表示され続けていれば OK（保存した変更が反映されていればなお良い）
5. Console に `[ViewRouter] ライブリロードで UIDocument.rootVisualElement が作り直されたため、
   '<View名>' を再表示します。` のログが出ていることを確認する（検知して再表示した証跡）
6. Play を止める

この手順は自動化していない（Editor の UI Builder 保存操作を安全に自動送出する手段が無いため）。
`ViewRouterLiveReloadRedrawTests`（PlayMode）は、UXML の dirty カウントを直接操作して同じ経路
（ライブリロード再生成 → 再表示）を再現する回帰テストで、こちらは `scripts/verify.ps1` で自動実行される。

**再表示による副作用（レビュー M-3 / L-7）**: この再表示は `ViewRouter.SwapTo()`（通常の画面遷移と同じ経路）
を使って View を**作り直す**ため、`OnHide → OnShow` が実際に走る。したがって:

- 入力中の値（テキストフィールド等）や、開いているオーバーレイ（TTS の読み上げステータス表示等）は
  リセットされる（View インスタンスごと作り直されるため）
- 特に **Join 画面で接続試行中（`-tq-join` 起動や手動での接続中）に UXML を保存すると、
  `JoinView.OnHide()` が `NetworkService.Stop()` を呼ぶため接続が中断される**。Editor での動作確認中に
  UXML を保存する際は、接続試行中でないタイミングを選ぶこと
- 対象は **UXML の保存（ライブリロード）のみ**。C# スクリプトの保存によるドメインリロードは、
  従来どおり `ViewRouter` 自体が破棄・再生成されるため、この仕組みとは無関係に画面がリセットされる
  （こちらは #137 以前からの既知の挙動で、変更していない）

---

## 4. Unity CLI

### 4.1 `unity` コマンド（1.0.0-beta.8、`%LOCALAPPDATA%\Unity\bin\unity.exe`）

| サブコマンド | 用途 |
|---|---|
| `unity open` | プロジェクトを Editor で開く |
| `unity test` | EditMode / PlayMode テストを実行 |
| `unity build` | ビルドプロファイル/ビルドターゲットを指定してビルド |
| `unity run` | ビルド済み実行ファイルを実行 |
| `unity status` | Editor/プロジェクトの状態確認 |
| `unity projects info` | プロジェクト情報の取得 |

### 4.2 Unity.exe 直接起動（`unity` コマンドで不足する場合の代替）

Editor 本体: `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`

```
# EditMode テスト実行例
Unity.exe -batchmode -nographics -quit `
  -projectPath "E:\Claude\tsumugi-quiz" `
  -runTests -testPlatform EditMode `
  -testResults "E:\Claude\tsumugi-quiz\Logs\editmode-results.xml" `
  -logFile "E:\Claude\tsumugi-quiz\Logs\editmode.log"

# ビルド実行例（カスタムビルドメソッド呼び出し）
Unity.exe -batchmode -nographics -quit `
  -projectPath "E:\Claude\tsumugi-quiz" `
  -executeMethod TsumugiQuiz.Editor.BuildCommand.BuildWindows `
  -logFile "E:\Claude\tsumugi-quiz\Logs\build.log"
```

- **実行後は必ずログファイルを grep してエラー・例外の有無を確認する。終了コードだけで成功・失敗を判断しない**
- PowerShell からのプロセス起動は `Start-Process -ArgumentList` の文字列連結ではなく `System.Diagnostics.ProcessStartInfo.ArgumentList`（配列）を使う（引数のエスケープ崩れを防ぐため。§5 の罠を参照）

---

## 5. 前プロジェクトで判明した罠

- サブエージェントが `run_in_background` で Unity を起動すると、完了通知が届かずセッションが停止する。Unity を起動する Bash は必ず**前景**・`timeout: 600000` を指定する
- コンパイルエラーで Editor が異常終了した後、`Temp/UnityLockfile` が残り、次回起動時にロック競合が起きることがある。Editor が正常終了しないまま再実行する場合は当該ファイルの有無を確認する
- PowerShell で外部プロセスに複雑な引数を渡す場合、`Start-Process -ArgumentList "..."` の文字列結合ではなく `ProcessStartInfo.ArgumentList`（配列）を使う。文字列結合はスペースや引用符を含む引数（パス等）でエスケープが崩れやすい
- PowerShell 関数が配列を返す場合は呼び出し側で `@(...)` で受ける（単一要素のときに自動的にスカラーへ展開されてしまう問題を防ぐ）
- `.gitattributes` の改行コード正規化（eol 設定）により、チェックアウト直後から意図せず「modified」表示になることがある。その場合は変更を作業前に確認した上で `git fetch && git reset --hard origin/develop` で作業前の状態に揃える（**未コミットの変更がないことを確認してから実行する**）

---

## 6. External の準備手順

- `External/README.md` を参照（各素材の入手元・配置ルールの詳細）
- `scripts/fetch-voicevox.ps1` — voicevox_core・ONNX Runtime・Open JTalk 辞書などを External に取得するスクリプト
- `scripts/setup-external.ps1` — `External/` から `Assets/Plugins/` `Assets/StreamingAssets/` へネイティブ DLL・辞書・モデルをコピーする配置スクリプト（K24）。コピー先は git 管理外（`.gitignore` 参照）。立ち絵（issue #24）だけは例外で、二次配布禁止のため `Assets/` には一切コピーせず、`AppPaths.DataRoot`（`Application.persistentDataPath` 相当）配下へコピーする（`-DataRoot` で明示指定可、docs/tts.md §8.2 / External/README.md §5.5）

---

## 7. SE 生成手順

- `scripts/gen-se.py`（Python 3 + numpy）で効果音の wav を生成し、`Assets/TsumugiQuiz/Audio/SE/` にコミットする（K17）
- 生成対象: 早押し（`buzz.wav`）、正解（`correct.wav`）、不正解（`wrong.wav`）、タイムアップ（`timeup.wav`）、開始（`start.wav`）、参加（`join.wav`）
- 波形はサイン波・矩形波・ノイズ + ADSR エンベロープの組み合わせ（44.1kHz 16bit mono、各 0.2〜1.0 秒、ピーク -3dBFS 程度）。乱数（ノイズ生成のみ）はシード固定のため、再実行してもバイト単位で同一の wav が出力される（決定的）
- パラメータ（音程・長さ等）は仮決め。変更する場合はスクリプトを直接編集し、生成し直してからコミットする
- SE の発火は UI 層（`TsumugiQuiz.UI`）の責務。Network/Room が公開するイベント（早押し・正誤判定・タイムアップ・ゲーム開始・参加通知等）を UI 層が購読し、`SePlayer.Play(SeKind)` を呼び出す（architecture.md §3/§4）。イベント配線自体は発火元issue（早押し #12、正誤判定 #18、タイムアップ/ゲーム開始 #19、参加通知 #7 等）側で追加する

### 7.1 wav 生成・検証コマンド

```
# numpy 未導入の場合のみ
pip install numpy

# 6種類の wav を Assets/TsumugiQuiz/Audio/SE/ に生成
python scripts/gen-se.py

# 生成済み wav の検証（存在・チャンネル数・サンプル幅・サンプルレート・長さ・ピークに加え、
# 一時ディレクトリへの再生成結果とのバイト完全一致を毎回確認する）
python scripts/gen-se.py --check
```

- 生成後、Unity Editor が `Assets/TsumugiQuiz/Audio/SE/*.wav` の `.meta`（AudioImporter）を作成する。SE は短く再生頻度が高いため、既定設定（Vorbis 圧縮、非プリロード）ではなく次の設定にする（`SePlayerBootstrap` が自動設定する。§7.2 参照）:
  - `compressionFormat: PCM`（無圧縮。再生開始レイテンシと音質劣化を避ける）
  - `preloadAudioData: true`（都度ディスクから読むのではなく事前にメモリへ展開する）
  - wav 本体・`.meta` の両方をコミットする

### 7.2 Boot シーンへの SePlayer 配置

Boot シーンに常駐する `SePlayer`（`TsumugiQuiz.UI` asmdef、`Assets/TsumugiQuiz/Scripts/UI/SePlayer.cs`）へ 6 種の `AudioClip` を再アサインする場合や、上記の AudioImporter 設定を適用し直す場合は、以下のバッチコマンドを実行する（`scripts/gen-se.py` で音を作り直した後や、SE の種類を追加/削除した場合に再実行する）。`SePlayerBootstrap`（`Assets/TsumugiQuiz/Scripts/Editor/Setup/SePlayerBootstrap.cs`）が Boot シーンを開き、`SePlayer` の `AudioSource`/`AudioListener`（他に有効な AudioListener が無いときのフォールバック。M2）を用意し、AudioImporter 設定と 6 種のクリップ参照を書き込んで保存する。

```
Unity.exe -batchmode -nographics -quit `
  -projectPath "E:\Claude\tsumugi-quiz" `
  -executeMethod TsumugiQuiz.Editor.Setup.SePlayerBootstrap.RunAndExit `
  -logFile "E:\Claude\tsumugi-quiz\Logs\seplayer-bootstrap.log"
```

- `SePlayer.Play(SeKind)` で任意の View・イベントハンドラから SE を再生できる（`SePlayer.Instance.Play(SeKind.Buzz)` 等）
- 実行後は §4.2 と同様、ログファイルを grep してエラー・例外の有無を確認する

---

## 8. 配布パッケージ作成手順（issue #34 で実装・実測確定）

`scripts/package-release.ps1` が `scripts/build.ps1` の出力（`Builds/Windows/`）から配布 zip を
作成する。以下は実際に手元で実行して確認した手順（2026-09-14 初回実施、2026-09-17 に develop
最新化後に再実行して再確認、いずれも worktree `i34`）。

### 8.1 前提: External の配置

`External/voicevox_core/` は git 管理外のため、worktree ごとに配置が必要（本体の
`External/` を指す `-ExternalRoot` を渡せる。詳細は `scripts/setup-external.ps1` 冒頭コメント）。

```powershell
pwsh ./scripts/setup-external.ps1 -ExternalRoot <External/voicevox_core のパス> -SkipTsumugi
```

立ち絵（`-SkipTsumugi` を外した場合の `External/tsumugi/`）は配布物に一切含めないため
（docs/licenses.md §3 (d)）、配布パッケージ作成の前提としては不要。

### 8.2 ビルド

```powershell
pwsh ./scripts/build.ps1
```

出力: `Builds/Windows/TsumugiQuiz.exe` ほか一式（`TsumugiQuiz_Data/`、`UnityPlayer.dll`、
`MonoBleedingEdge/`（Mono バックエンド使用時のランタイム）、`D3D12/`、`dstorage.dll`、
`dstoragecore.dll` 等。Mono ビルドの実測で `Builds/Windows` 合計 285MB、うち
`TsumugiQuiz_Data/StreamingAssets` が 158MB（vvm 56MB + Open JTalk 辞書 100MB 相当））。

Unity が生成する `TsumugiQuiz_BackUpThisFolder_ButDontShipItWithYourGame/` は名前のとおり
配布に含めてはいけないフォルダなので、`package-release.ps1` は明示的な許可リストでコピー元を
選び、このフォルダを含まない。

### 8.3 配布 zip の作成

```powershell
pwsh ./scripts/package-release.ps1
```

内部で行うこと:

1. `Builds/Windows/` 直下のトップレベル項目が、コピー許可リスト（`$script:TopLevelItemsToCopy`、
   スクリプト冒頭）または既知の除外パターン（`*_BackUpThisFolder_ButDontShipItWithYourGame*` /
   `*_BurstDebugInformation_DoNotShip*`。実際のフォルダ名は productName が先頭に付く
   `TsumugiQuiz_BurstDebugInformation_DoNotShip`、レビュー M-10）のいずれかに一致するかを
   検証する。**この許可リストは
   `scripts/build.ps1`（既定 = Mono バックエンド）の実測出力に基づくものであり、
   `scripts/build.ps1 -IL2CPP` の出力構成では検証していない。** IL2CPP は `GameAssembly.dll` 等、
   許可リストに無い実行必須ファイルをトップレベルに生成するため、どちらにも一致しない項目が
   見つかった場合は一覧を表示してエラー終了する（黙って欠落したまま zip を作らないための
   安全策。レビュー H1）。IL2CPP を使う場合は出力を確認したうえで許可リストを更新すること
2. `Builds/Windows/` に voicevox_core 由来の必須ファイル
   （`Plugins/x86_64/{voicevox_core.dll, voicevox_onnxruntime.dll}`、
   `StreamingAssets/voicevox_core/{models/vvms/*.vvm, models/TERMS.txt, onnxruntime/TERMS.txt, c_api/LICENSE}`、
   Open JTalk 辞書 `sys.dic`）が実在するか検証する。`External/` は git 管理外なので、
   `setup-external.ps1` の実行漏れのままビルドすると欠落し、ここで明確なエラーになる
3. `scripts/gen-third-party-notices.ps1` を呼び出して `THIRD-PARTY-NOTICES.txt` を生成する
   （生成先は一旦 `Builds/THIRD-PARTY-NOTICES.txt` だが、ステージングへコピー後にこの
   中間生成物は削除する。正本は zip 内と `Assets/.../Resources/*.txt`）
4. `Builds/TsumugiQuiz-v<Version>-win-x64/`（既定の版番号は `ProjectSettings.asset` の
   `bundleVersion`。`-Version` 指定時は英数字・`.`/`_`/`-` のみ・先頭は英数字という形式を検証する）
   にビルド成果物一式 + `THIRD-PARTY-NOTICES.txt` + `LICENSE` + `NOTICE.md` + `README.txt`
   （手順書の案内・起動方法・遊び方・同じ zip を使うことの注意・立ち絵の自前配置の案内・権利表記。
   `VOICEVOX:春日部つむぎ` のクレジット文言も含む。改行は CRLF に統一）+ `manual/`
   （利用者向け手順書 `docs/manual/*.md` を pwsh 組み込みの `ConvertFrom-Markdown` で HTML にした
   `setup.html` / `usage.html` と、`docs/manual/images/` の画像。HTML は BOM なし UTF-8 で
   `<meta charset="utf-8">` を付ける。`<名前>.md` への相対リンクは `<名前>.html` に書き換え、
   zip の `manual` フォルダの外を指す参照・存在しない画像があれば失敗する）+ `tools/tsumugi-expressions/`
   （立ち絵の表情生成ツール、#219。`$script:ExpressionToolFiles` に書いたコードと設定だけで、
   生成の本体 `scripts/generate_tsumugi_expressions.py` とレイヤー対応表 `docs/tsumugi-expressions.sample.json`
   はリポジトリと同じファイルをコピーする（`expressions.json` は `$schemaNote` だけを zip 版の説明に差し替え、
   ほかの値が変わっていないことを読み直して確かめる）。アプリの同意の判定（PR #224 レビュー H1）に使う
   `Assets/TsumugiQuiz/Resources/Terms/*.txt` も `terms/` にそのままコピーする。`.bat` は ASCII・CRLF、`.ps1` は UTF-8（BOM 付き）にする。
   許可リスト以外のファイル（画像・PSD など）が入っていたら失敗する）をステージングする
5. 同梱してはいけないもの（立ち絵原本・表情差分（`tsumugi_*.png`、#219）・PSD・`*.pdb`・Live2D 素材（`*.moc3` / `*.model3.json`。
   本アプリの立ち絵は Live2D 化されていないが将来の誤混入への保険）・
   `TsumugiQuiz_BackUpThisFolder_ButDontShipItWithYourGame` 等）が紛れ込んでいないか
   ステージング後に検査する（見つかった場合はステージングを削除してエラー終了する）。
   **このファイル名検査はあくまで補助的なセーフティネットである。立ち絵非同梱の実質的な担保は
   docs/licenses.md §3 (d) の運用（原本を `Assets/` にもリポジトリにも置かない）と、実行時に
   `AppPaths.DataRoot` からのみ読み込む実装（docs/tts.md §8.2）である**
6. `Compress-Archive` で `Builds/TsumugiQuiz-v<Version>-win-x64.zip` を作成し、
   同梱ファイル一覧・サイズを表示する（前回実行の古い zip の削除は、5. の同梱禁止物検査の
   直前・つまりここまでの検証（ビルド成果物の存在・トップレベル項目・voicevox_core 必須
   ファイル・ステージング組み立て）をすべて通過した後まで遅らせる。レビュー M-11:
   引数の指定ミス等で早期に失敗するたびに正しい既存 zip を消してしまわないようにするため）
7. 既定ではステージングフォルダを削除し zip のみ残す（`-KeepStaging` で保持できる）

`scripts/package-release.ps1` は **PowerShell 7（`pwsh`）で実行すること**。手順書の HTML 化に使う
`ConvertFrom-Markdown` は PowerShell 6.1 以降の組み込みで、Windows PowerShell 5.1（`powershell.exe`）には無いため動かない
（無い場合はその旨を表示して失敗する）。

`-SelfTest` オプションで、同梱禁止物検査ロジック自体の自己診断だけを実行できる（実際の
ビルド・zip 作成は行わない。一時ディレクトリに `dummy.psd` を置いて検出できることを確認する）。
あわせて、手順書の HTML 化（日本語・表・相対リンク・相対パスの画像を含む小さな Markdown を変換し、
文字コード・リンクの書き換え・画像のコピー・`manual` フォルダの外を指す参照・ドライブ文字から始まる絶対パス・
存在しない画像の検出）も自己診断する。表情生成ツール（#219）は、実際のリポジトリのファイルを一時フォルダに書き出して
許可リスト・文字コード・改行の検査に通ること、画像（`tsumugi_idle.png`）と PSD を紛れ込ませると
許可リストの検査と同梱禁止物の検査の両方が検出することを自己診断する。HTML 化に失敗した場合は、ステージングフォルダと中間生成物の
`Builds/THIRD-PARTY-NOTICES.txt` を削除してから失敗で終わる。

手順書（`docs/manual/*.md`）では、見出しへのページ内リンク（`#…`）を使わないこと。
`ConvertFrom-Markdown`（Markdig）の見出しの自動 ID は ASCII 以外を落とす（日本語の見出しは
`section` / `section-1` … になる）ため、zip の HTML ではリンクが切れる。リポジトリ内の他の文書
（`../licenses.md` など）へのリンクも zip では切れるので、手順書どうしと外部 URL だけにする。

**Unity 帰属定型文の年について（issue #100、docs/licenses.md §11 Section 2.12 対応）**:
`Assets/TsumugiQuiz/Resources/Licenses/unity-packages-notices.txt` 冒頭の Unity 帰属定型文
（`Copyright © 2005-xxxx Unity Technologies.`）は、同梱ファイル自体は静的テキストのため年が
固定されている。この年は `scripts/gen-third-party-notices.ps1` が `THIRD-PARTY-NOTICES.txt` へ
連結する際に生成時点の現在年へ自動置換し（元ファイルは編集しない）、`scripts/package-release.ps1`
は生成結果の年が現在年と一致するかを検査してから配布 zip の作成を続行する（不一致ならビルドを
失敗させる）。開発者が手動で年を書き換える運用は不要。アプリ内クレジット画面側は
`Application.productName` / `DateTime.Now.Year`（ローカル時刻）を使い実行時に自動算出する。

### 8.4 実測結果（2026-09-17、develop 最新（60ebd64、#87/#88 取り込み後）マージ後に再実行、Mono ビルド、bundleVersion=1.0）

- ビルド所要時間: 59 秒（`scripts/build.ps1`、Unity 側の集計で errors=0, warnings=2。ログ上は
  `SePlayer.cs` の `FindObjectsSortMode` 非推奨 API 由来の CS0618 警告で、いずれもビルド結果には
  影響しない）
- zip: `TsumugiQuiz-v1.0-win-x64.zip`、**128.14 MB**（134,364,900 bytes）、ステージング上のファイル数 230
- `THIRD-PARTY-NOTICES.txt`: 443.6 KB（454,253 bytes。`Resources/Licenses` 10 件 + `Resources/Terms` 4 件を連結、
  CRLF 統一済み）
- zip 内容を `System.IO.Compression.ZipFile` で全件確認し、立ち絵（`tsumugi_v2.png` 等）・PSD・
  `.pdb`・`BackUpThisFolder` のいずれも含まれていないことを確認済み
- `pwsh ./scripts/package-release.ps1 -SelfTest` が `dummy.psd` を正しく検出することを確認済み
  （exit 0。誤検出（正常ファイルの誤検知）も無いことを確認）
- `Builds/Windows/` に許可リストに無いダミーファイル（`GameAssembly.dll` を模したファイル）を
  置いて実行し、一覧表示のうえ終了コード 1 になることを確認済み（H1 の実地検証）。同様に、
  `TsumugiQuiz_Data/` 配下にダミー `dummy.psd` を置いた状態でも、ステージング後の禁止物検査で
  検出され終了コード 1 になることを実地確認済み
- `-Version` に不正な形式（`1.0/../evil`）を指定すると検証で例外を送出し終了コード 1 になることを確認済み
- 終了コードは `$script:ExitCode` に集約し、スクリプト末尾の 1 箇所（`exit $script:ExitCode`）でのみ
  プロセスを終了する構成にした（レビュー L4）

### 8.4.1 PlayMode の環境依存スキップ（M7）

`pwsh ./scripts/verify.ps1 -Platform PlayMode` は、External（voicevox_core 一式）が配置済みか
どうかで一部テストが `Assert.Ignore` によりスキップされる（`VoicevoxTestFixture.IsAvailable` で
分岐。終了コードには影響しない）。

**以下の総数（`total=`）は develop `60ebd64` 時点のものであり、以降のテスト追加・削除で増減する
（レビュー L-11）。総数そのものではなく `failed=0` であることと skip の内訳が本質的な確認事項。**

- External 配置済み（通常の開発機の状態、本 PR の zip 生成時と同じ）: `total=119 passed=117
  failed=0`、skipped 2 件
  （`TtsStatusFallbackUiTests.External未配置ならMissingCoreDllの案内が出て利用不可表示になる`、
  `VoicevoxDllSearchPathTests.絶対パス指定だけでONNXRuntimeがロードできる`。後者は「ONNX Runtime が
  既にロード済みのため検索パスの測定はできません」という、テスト実行順序に依存する既知のスキップ）
- **External 未配置側の分岐も 2026-09-17 に実地確認した**: `Assets/Plugins/voicevox_core` /
  `Assets/StreamingAssets/voicevox_core`（いずれも `External/` 由来で git 管理外、worktree 内の
  コピーのみ）を一時的に退避（`External/` 自体は動かさない）して実行したところ
  `total=119 passed=108 failed=0`（11 件スキップ、うち `TtsStatusFallbackUiTests.
  External配置済みならReady表示になる` を含む）となり、
  `TtsStatusFallbackUiTests.External未配置ならMissingCoreDllの案内が出て利用不可表示になる` が
  実際に実行されて成功することを確認済み。確認後、退避したフォルダを元に戻し、
  `total=119 passed=117 failed=0` に復帰することも確認済み（2 回連続実行して安定を確認）

EditMode（External 配置済み、develop 最新化後）: `total=1578 passed=1577 failed=0`、skipped 1 件
（`CharacterImagePathsTests.ResolveImagePath_DataRootNotConfigured_Throws`。理由:
`環境変数 TSUMUGI_DATA_ROOT が設定されているため、このテストの前提が成り立ちません。` —
`verify.ps1` が worktree 分離用に設定する環境変数由来の想定内スキップで、本 issue の変更とは無関係）。

### 8.5 Unity スプラッシュ設定

docs/licenses.md §11 の方針（Unity Personal のためデフォルトのまま無効化しない）どおり、
`ProjectSettings/ProjectSettings.asset` は変更していない（`m_ShowUnitySplashScreen: 1` /
`m_ShowUnitySplashLogo: 1` を実測で確認済み）。

### 8.6 配布前チェックリスト

- [ ] `scripts/verify.ps1 -Platform EditMode` / `-Platform PlayMode` が両方通過している
- [ ] 参加者全員に**同じ zip** を配る（issue #204。ビルドし直した exe は別のビルドとして接続を拒否されるため、配布後に作り直した場合は全員分を差し替える）。ホスト自身もその zip（または zip を作った `Builds` フォルダ）から起動する。zip を作った後にビルドし直すと、ホストの exe だけが別のビルドになり、参加者全員が拒否される。承認ペイロードの形式か照合する項目の意味を変えた版では `ProtocolConstants.Version` を上げてある（docs/network.md §2.3「バージョンとビルドの一致」）
- [ ] `scripts/package-release.ps1` が正常終了し、`THIRD-PARTY-NOTICES.txt` の Unity 帰属定型文の年が現在年と一致している（issue #100、レビュー M-3。不一致ならスクリプトが失敗するため通常は手動確認不要だが、目視でも確認する）
- [ ] **クレジット画面（`CreditsView`）を実機（ビルド後の実行ファイル）で開き、Unity 帰属定型文の `®`（was made with Unity®）と `©`（Copyright ©）が文字化けせず正しく描画されていることをスクリーンショットで確認する**（issue #100、レビュー L-3。UI Toolkit のフォント・エンコーディング起因の文字化けは EditMode/PlayMode の文字列一致テストでは検出できないため、目視確認が必要）
- [ ] `README.md` のクレジット節に固定で書いてある Unity 帰属定型文の年（`Copyright © 2005-xxxx`）が現在年と一致していることを確認する（issue #100、レビュー R-5。アプリ内クレジット画面・`THIRD-PARTY-NOTICES.txt` は実行時/生成時に自動算出されるが、README.md は静的記述のため手動更新が必要）
- [ ] **ボタン・入力欄・ドロップダウン・トグルにフォーカス／ホバーしたときの枠と背景がテーマ色で描かれていることを、ビルドしたプレイヤーのスクリーンショットのピクセル値で確認する**（issue #132。Unity 既定テーマの青 `#006aa6` やグレー `#d1d1d1` / `#959595` / `#808080` が出ていたら、theme.uss の状態の規則が既定テーマに詳細度で負けている。PlayMode の `ButtonStateStyleTests` / `TextFieldFocusRingTests` は早期検知用で、**Unity のバージョンを上げたときは必ず実機で実施する**）。フォーカス枠は issue #174 で `#2c2320`（`--color-focus-ring` / `--color-focus-ring-on-primary`、ダーク基調では `#fcecdf`）に変更している。Title でボタンに、入力欄で TextField 系にフォーカスさせ、枠のピクセル値がこの値になっていることを確認する（docs/architecture.md §10.6・§10.9）

## 9. ログ判定ルール（scripts/log-scan.ps1）

`scripts/verify.ps1` は Unity バッチモードのログ（`Logs/verify-<Platform>.log`）をテスト結果 XML と
併せてチェックする。判定ロジックは `scripts/log-scan.ps1`（`common.ps1` から dot-source）に切り出して
あり、擬似ログによる回帰テスト `scripts/tests/log-scan.tests.ps1` を持つ（#61）。

### 9.1 経緯

旧実装は `\berror\b|\bexception\b` をログ全体に単純適用しており、次のようなテスト由来の行に誤反応する
おそれがあった（実際に PR #22 #25 #18 で実装側の回避策が必要になった）。

- メソッドシグネチャの引数型に `System.Exception` のような例外型がそのまま現れる行
  （例: `SomeClass:Handler (System.Exception ex)`）
- スタックトレース行・ジェネリック型引数に `Exception`/`Error` を含む識別子
  （例: `EmitExceptionAsError<...>`、`<ExecuteEnumerableAndRecordExceptions>d__4:MoveNext ()`）

`scripts/log-scan.ps1` は「Unity が実際に報告するエラー行」だけを狭く定義し、スタックトレース行・
メソッドシグネチャ行を明示的に除外することでこれを解消する。

### 9.2 ヒットさせる対象

行頭一致・部分一致は `scripts/log-scan.ps1` の `Test-LogScanIsErrorLine` を参照。概要:

| 対象 | マッチ方法 | 実例 |
|---|---|---|
| C# コンパイルエラー | `error CS\d+`（部分一致） | `Assets\Foo.cs(9,38): error CS1002: ; expected` |
| コンパイル失敗の確定行 | `^Scripts have compiler errors\.` | 同上 |
| `Error:` で始まる行 | 行頭一致 | — |
| `ERROR` で始まる行 | 行頭一致 | — |
| 例外型名 + コロン、行頭一致 | `^[A-Za-z_][A-Za-z0-9_.]*Exception:\s` | `NullReferenceException: ...` |
| `Failed to` で始まる行 | 行頭一致 | `Failed to resolve package dependencies` |
| `NullReferenceException` / `UnityException` | 部分一致（明示的に列挙） | — |
| バッチモードの致命的中断 | `^Aborting batchmode` | — |
| テストランナーの失敗（保険） | `^\s*Test Failed\b` / `^\s*\d+\)\s+\S.*:\S` | 下記 9.4 参照 |

### 9.3 除外する対象（`Test-LogScanIsStackTraceLine`）

- `^\s*at\s`（.NET 形式のスタックトレース行）
- `\(at\s[^)]+:\d+\)`（Unity 形式のスタックトレース行、例: `Foo:Bar () (at Assets/Foo.cs:10)`）
- `[0x...] in ...`（IL オフセット付きスタックトレース断片）
- `Namespace.Type:Method (...)` / `Namespace.Type.Method (...)` 形式のメソッドシグネチャ行
  （引数に例外型名を含んでいてもシグネチャ自体はエラーではないため）

`$script:LogScanIgnorePatterns`（`scripts/log-scan.ps1`）には、実際に誤検知が確認された狭いパターンの
みを残す。現状は次の 1 件のみ:

- `^\[Licensing::Module\] Error: Access token is unavailable`
  （オフライン/未アクティベート環境で常に出るライセンストークン警告。実害なし）

過去の実装では asmdef 未使用警告（`will not be compiled, because it has no scripts associated with it`）
や `EmitExceptionAsError` をここに追加していたが、上記の行頭一致・スタックトレース除外により
そもそもマッチしなくなったため削除した（実測: 2026-09-13、develop 2e384cb 相当のログで確認）。

### 9.4 `LogAssert.Expect` で想定済みのログの扱い

テストが `LogAssert.Expect` で想定済みの警告・エラーログを意図的に出す場合（例:
`PortMappingServiceTests`、`TtsServiceFakeEngineTests`、`QuestionLibraryTests` 等）、実際のログ出力は
`[タグ] メッセージ: 例外型名: 詳細` のように**タグ（角括弧）から始まる**。

`Test-LogScanIsErrorLine` の「例外型名 + コロン、行頭一致」は文字通り**行頭**でしか一致しないため、
このような行は構造的にヒットしない（タグが先頭にある限り、例外型名は行頭に来ない）。したがって
`LogAssert.Expect` 済みのログのために追加のログ判定側 ignore パターンを用意する必要は、原則として無い。

新しくテストで意図的にエラー/警告ログを出す場合は、**行頭を `[何らかのタグ]` から始める**規約を守ること。
やむを得ず行頭が `Error:`/`ERROR`/`Failed to`/例外型名 になる場合のみ、`$script:LogScanIgnorePatterns`
に狭いパターンを追加し、`scripts/tests/log-scan.tests.ps1` に該当ケースを足すこと。

### 9.5 テスト失敗そのものの検出について（実測に基づく重要な注意）

2026-09-13 に、故意に失敗する EditMode テスト（`Assert.Fail` および未処理の `NullReferenceException` を
投げるテスト）を一時的に追加して `pwsh ./scripts/verify.ps1 -Platform EditMode` を実行したところ、
**これらのテスト失敗はログファイル（`Logs/verify-EditMode.log`）に一切出力されなかった**
（Unity 6000.6.0f1 のバッチモードでの実測）。失敗は次の 2 か所にのみ反映される。

1. `Logs/test-results/EditMode-results.xml` の `failed` 件数
2. Unity プロセスの終了コード（このケースでは `2`）

このため、**テスト失敗の一次的な検出は `scripts/verify.ps1` 側の XML パースと終了コードチェックが担う**
（既存実装のまま）。`scripts/log-scan.ps1` の `^\s*Test Failed\b` / `^\s*\d+\)\s+\S.*:\S` は、将来の Unity
バージョンやテストランナーの出力差異に備えた保険的なパターンであり、本プロジェクトの現行環境では
実際にマッチする状況を確認できていない。

一方、コンパイルエラーは実測でログに残ることを確認済み（9.2 の実例、および `error CS\d+` の一致）。
コンパイルエラー時は XML 自体が生成されないため、`scripts/verify.ps1` の「XML 未生成」チェックと
`scripts/log-scan.ps1` の両方で検出できる。

## ブランチ保護の代替（pre-push フック）

GitHub のブランチ保護（classic / ruleset）は無料プランのプライベートリポジトリでは有効化できない（2026-09-13 に `gh api` で両方 403 を実測）。代わりにローカルの pre-push フックで `main` / `develop` への直接 push を止める。

```powershell
git config core.hooksPath scripts/git-hooks   # クローン直後に 1 回実行
ALLOW_DIRECT_PUSH=1 git push origin develop   # 意図的に直接 push する場合のみ（bash）
```

PR 経由のみというルールは CLAUDE.md の禁止事項にも記載し、運用で担保する。
