# CLAUDE.md

tsumugi-quiz は、友人同士でオンライン飲み会やオフ会の際に遊ぶことを想定した早押しクイズアプリである。参加者の 1 人がホストとしてサーバー機能を兼ねたクライアントを起動し、他の参加者は参加コードを入力して直接接続する。読み上げには VOICEVOX の音声ライブラリ「春日部つむぎ」を使い、各クライアントがローカルで音声合成を行う。外部の常設サーバー・アカウント登録・継続的な運用コストが発生しない構成を志向する。

## 関連ドキュメント
- [docs/requirements.md](docs/requirements.md) — 要件定義・確定済み技術決定
- [docs/architecture.md](docs/architecture.md) — 全体構成・シーン構成・レイヤー分け（asmdef）
- docs/network.md — ネットワーク実装詳細（NGO・参加コード・早押し判定）
- [docs/network-joincode.md](docs/network-joincode.md) — 参加コードのビット仕様・符号化・テストベクタ
- [docs/network-nat.md](docs/network-nat.md) — UPnP とグローバル IP の取得
- docs/tts.md — VOICEVOX 連携・再生同期の実装詳細
- [docs/tts-native-api.md](docs/tts-native-api.md) — voicevox_core ネイティブ API 連携の詳細
- [docs/question-data.md](docs/question-data.md) — 問題データの JSON スキーマ・エディタ仕様
- [docs/room-settings.md](docs/room-settings.md) — ルーム設定項目とプリセット
- [docs/licenses.md](docs/licenses.md) — 権利表記・クレジット文言の根拠
- [docs/dev-workflow.md](docs/dev-workflow.md) — 開発手順・ローカル検証・ビルド手順
- [docs/tasks/setup-brief.md](docs/tasks/setup-brief.md) — 統括が全エージェントに配布した共通ブリーフ

---

## 確定事項の要約
- Unity 6000.6.0f1（Unity 6.6）、2D、ビルドターゲットは Windows Standalone のみ
- UI は UI Toolkit のみ（uGUI はテンプレート依存として残すがコードから参照しない）
- ネットワークは Netcode for GameObjects + Unity Transport、Host モードによる直接接続
- voicevox_core をネイティブ同梱し、各クライアントがローカルで音声合成する
- 配布ターゲットは Windows のみ
- GitHub Actions は使わない。検証は `scripts/*.ps1` によるローカル実行で代替する

詳細・根拠は [docs/requirements.md](docs/requirements.md) §5、[docs/tasks/setup-brief.md](docs/tasks/setup-brief.md) §0 を参照。無断で変更しない。

---

## フォルダ構成

`Assets/TsumugiQuiz/` 配下に以下を配置する。

```
Assets/TsumugiQuiz/
├─ Scripts/
│  ├─ Core/       (TsumugiQuiz.Core     — 純C#、Unity API 非依存)
│  ├─ Questions/  (TsumugiQuiz.Questions)
│  ├─ Room/       (TsumugiQuiz.Room)
│  ├─ Network/    (TsumugiQuiz.Network)
│  ├─ Tts/        (TsumugiQuiz.Tts)
│  ├─ UI/         (TsumugiQuiz.UI)
│  └─ Editor/     (TsumugiQuiz.Editor)
├─ UI/{Views,Styles,Templates}
├─ Scenes/
├─ Prefabs/
├─ Audio/SE/
├─ Settings/
└─ Tests/{Shared, EditMode, PlayMode}   (TsumugiQuiz.Tests.Shared / .EditMode / .PlayMode)
```

asmdef は計 10 本（うちテスト 3 本。EditMode/PlayMode 共通のテスト用フェイクを集約する `TsumugiQuiz.Tests.Shared` を含む、#67）。依存方向は一方向のみ: **Core ← Questions/Room ← Network/Tts ← UI**（逆方向の参照は禁止）。`Core` は Unity API に依存しない純 C# を目標とする。名前空間はフォルダ構成と一致させる（例: `TsumugiQuiz.Network` 配下は `namespace TsumugiQuiz.Network`）。詳細は [docs/architecture.md](docs/architecture.md) §3 を参照。

---

## 命名規約
- C#: クラス・メソッド・プロパティは PascalCase、ローカル変数・引数は camelCase、private フィールドは `_camelCase`、定数は PascalCase
- asmdef・名前空間はフォルダ構成と一致させる
- UXML/USS はケバブケース（例: `game-view.uxml`）
- シーン名は PascalCase（例: `Boot.unity`、`Main.unity`）
- JSON のキーは camelCase
- ブランチ名は `feature/<issue番号>-<kebab-case の短い説明>`
- コミットメッセージは Conventional Commits（`feat` / `fix` / `docs` / `chore` / `refactor` / `test` / `build`）。本文は日本語可

---

## ブランチ運用（git flow）
- `main` — リリース。`develop` からのみマージする
- `develop` — 統合ブランチ、デフォルトブランチ
- `feature/<issue番号>-<説明>` — `develop` から切り、PR は `develop` 向けに作成する
- `release/*` / `hotfix/*` — 必要時のみ使用。`release/*` は `develop` から切って `main` と `develop` の両方へマージ、`hotfix/*` は `main` から切って両方へマージする

**作業開始時は必ず `develop` を最新化してから `feature` ブランチを切ること**（`git checkout develop && git pull --ff-only`）。**PR マージ後はローカルの `feature` ブランチも削除すること**（リモートは自動削除設定済みのため操作不要）。

## PR 運用
- 1 issue = 1 PR。PR 本文に対象 issue 番号を `Closes #n` の形で記載する
- PR 本文に `scripts/verify.ps1` の実行結果を貼る
- reviewer サブエージェントのレビューを経てから マージする
- マージは squash マージ

## エージェント運用
- 統括: Fable。implementer（Opus 固定・設計判断を伴う実装）、worker（Sonnet 固定・定型作業）、reviewer（Opus 固定・レビュー専任、読み取り専用）を使い分ける
- Unity を起動する Bash 呼び出しは前景・`timeout: 600000` を指定する（背景実行は完了通知が届かず停止するため禁止）
- サブエージェントは、統括から明示的に指示されない限り commit / push / PR 作成をしない
- 詳細は [docs/dev-workflow.md](docs/dev-workflow.md) を参照

---

## 禁止事項
- `External/` 配下の素材・ライブラリをコミットすること（git 管理外。`.gitignore` 参照）
- uGUI（`com.unity.ugui`）をコードから参照すること
- GitHub Actions ワークフローを追加すること
- 確定事項（本書・`docs/requirements.md` §5・`docs/tasks/setup-brief.md` §0）を無断で変更すること
- 推測でバージョン番号・API・実行結果を記載すること（必ず一次情報で確認する）
- force push、および `main` / `develop` ブランチへの直接 push（GitHub のブランチ保護は無料プランのプライベートリポジトリでは使えないため、`git config core.hooksPath scripts/git-hooks` でローカルの pre-push フックを有効化して防ぐ。初期セットアップ等で意図的に push する場合のみ `ALLOW_DIRECT_PUSH=1`）
- 費用が発生する外部サービス、アカウント登録必須の外部サービスの導入
- GPL / AGPL / LGPL 系ライブラリの導入（[docs/licenses.md](docs/licenses.md) 参照）

---

## コーディング規約の要点
- 不変データを優先する（既存オブジェクトの破壊的変更ではなく、変更後の新しいインスタンスを返す）
- ファイルは小さく保つ（目安 400 行、上限 800 行）。責務ごとに分割する
- 境界（JSON 入力、参加コード、ネットワーク受信データ）では必ず入力検証を行う
- エラーを握りつぶさない。ユーザー向けにはわかりやすいメッセージ、内部処理では詳細なログを残す
- `Core` 層は Unity API に依存せず、ユニットテスト可能な構成にする
- コメントは日本語可
