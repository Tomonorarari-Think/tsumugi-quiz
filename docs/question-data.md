# 問題データ

## 目的
問題データの JSON スキーマ、フォルダ配置、読み込み方式、正規化・判定ルール、アプリ内エディタの仕様を定義し、question-data.md を読めば問題データに関わる実装がすべて一意に決まる状態にする。

## 関連ドキュメント
- [requirements.md](./requirements.md) — FR-10〜FR-12（出題形式）、FR-50〜FR-52（問題データ）
- [architecture.md](./architecture.md) — `TsumugiQuiz.Questions` asmdef、`QuestionRepository` / `QuestionDistributor`
- [room-settings.md](./room-settings.md) — 出題形式フィルタ・タグフィルタ・出題数などのルーム設定
- network.md — 画像の分割送信方式（別担当作成）
- docs/schemas/question-set.schema.json — 本書のスキーマの JSON Schema（draft 2020-12）実体
- docs/samples/sample-questions.json — サンプル問題ファイル

---

## 1. JSON スキーマ（確定版）

問題セットファイルは 1 ファイル = 1 セットで、以下の構造を持つ。

```json
{
  "schemaVersion": 1,
  "setId": "sample-set-01",
  "title": "サンプル問題セット",
  "description": "セットの説明（任意）",
  "questions": [ /* 問題オブジェクトの配列 */ ]
}
```

### セット直下フィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `schemaVersion` | integer | ○ | 現行は `1` 固定 |
| `setId` | string | ○ | セットの一意な識別子（英数字・`_`・`-`）。ファイル名とは独立の ID |
| `title` | string | ○ | 表示名（最大100文字）。平文として表示する（§1.1） |
| `description` | string | - | 説明（最大500文字） |
| `questions` | array | ○ | 問題オブジェクトの配列（1件以上）。`id` はセット内で一意 |

### 問題オブジェクトのフィールド

| フィールド | 型 | 必須 | 説明 |
|---|---|---|---|
| `id` | string | ○ | セット内で一意な問題 ID（最大100文字） |
| `type` | `"freeText"` \| `"choice"` | ○ | 出題形式 |
| `text` | string | ○ | 画面表示用の問題文（最大500文字）。平文として表示する（§1.1） |
| `readingText` | string | - | 読み上げ用テキスト（最大500文字）。省略時は `text` を読み上げる。読み上げには書かれたとおりの文字列を渡す（§1.1） |
| `answers` | string[] | `freeText` のとき必須 | 正解候補（1〜20件、各最大100文字）。いずれか一致で正解。結果の表示では平文として表示する（§1.1） |
| `choices` | string[] | `choice` のとき必須 | 選択肢（2〜8件、各最大100文字）。平文として表示する（§1.1） |
| `correctIndex` | integer | `choice` のとき必須 | 正解の `choices` インデックス（0始まり） |
| `imagePath` | string | - | 問題セットファイルからの相対パス（例: `images/q1.png`） |
| `tags` | string[] | - | タグ（最大20件、各最大100文字。省略時は空配列） |
| `difficulty` | integer(1〜5) | - | 難易度。省略時は `3` |

`type` が `freeText` のとき `choices`/`correctIndex` は指定不可、`choice` のとき `answers` は指定不可（`docs/schemas/question-set.schema.json` の `allOf`/`if-then` で機械的に検証可能）。

### 1.1 文字列は平文として表示する（#206、統括判断）

`title` / `text` / `answers` / `choices` などの文字列は**平文として表示する**。リッチテキストのタグ
（`<b>`・`<size=...>`・`<color=...>`・`<br>` など）は解釈せず、書かれたとおりの文字として表示する
（例: `"<b>東京</b>"` という選択肢は、太字の「東京」ではなく `<b>東京</b>` と表示される）。

- 装飾タグは仕様外とする。本書にも requirements にも定めがなく、問題文の文字送り（`RevealText`）はタグも
  1 文字ずつ数える（送りの途中でタグの断片が見える）ため
- 表示先: ゲーム画面の問題文（`question-text-label` と高さの先取り用の `question-text-sizer`）、選択肢のボタン、
  結果の表示（`result-label`）、問題エディタのセット一覧・問題一覧・削除の確認文・編集フォームの見出しと検証エラー、
  ホスト設定の問題の読み込み結果。いずれも `PlainText`
  （`enableRichText = false`、docs/architecture.md §10.10）を通す
- 読み上げ（TTS）に渡す文字列（`readingText`、省略時は `text`）には手を入れない。タグを書いた場合に
  読み上げでどう扱われるかは定めない（書かないこと）
- **見えない文字と積み重ねた結合記号（#209）**: 読み込み時（§2）も配信の受信時（§7）もエラーにせず、ファイルの中身も
  変えない。Game 画面に出す直前に、プレイヤー名と同じ規則（docs/network.md §9.2。双方向の制御・幅のない区切り・
  ソフトハイフンなどの見えない文字と、基底文字 1 つあたり 5 個目以降の結合記号を除く）で取り除く
  （`GameViewPresenter.ToQuestionDisplayText`。対象は問題文、選択肢のボタン、結果の表示の正解）
  - エラーにしない理由: 問題文には作者が意図して入れた文字がありうる（Web から貼り付けたときに入る U+200B・ソフトハイフン、
    アラビア文字やヘブライ文字を混ぜた文の U+200E / U+200F など）。1 文字のためにセットごと読み込めなくなる
    （§2「エラー時の扱い」）のは厳しすぎる。名前と違い、見た目が同じ別のものを作っても得がない（作者は 1 人）
  - 読み込み時に除かない理由: 除くと、問題エディタで保存したときにファイルが黙って書き換わる。また、クライアントは
    ホストから届いた問題データを長さと件数しか再検証しない（§7、`QuestionDto.TryValidate`）ので、ホストの読み込み時に
    除いても、改変されたホストへの備えにはならない。表示の直前ならホストの版によらず効く
  - 正解判定との関係: 判定の正規化（§5）はこの規則とは別で、変えていない。判定は UTF-16 の 1 単位ごとにカテゴリ Cf（と
    U+00AD）を除く（§5 の手順 4、例 11）ので、U+200B・双方向の制御など BMP の書式文字は、表示でも判定でも無視される。
    一方、補助面の書式文字（U+E0001 など）と、書式文字ではない見えない文字（U+3164 などのハングルの字母、U+034F など）は、
    表示では除くが判定では残る。結合記号の数も判定では変えない。これらはもともと画面で見えなかった文字で、#209 で判定を
    変えたわけではない（退行ではない）。正解（`answers`）にこうした文字を入れると、同じ見た目の文字を入力しても一致しない
    ことがある（PR #211 レビュー L-2）
  - 除いたときの表示への影響（PR #211 レビュー L-6）: U+200B（行を分けてよい位置の目印）を除くと、長い英単語などの折り返す
    位置が変わりうる。U+00AD（ソフトハイフン）を除くと、行末でハイフンを付けて分ける位置の指定がなくなる。U+200E / U+200F
    （LRM / RLM）を除くと、アラビア文字・ヘブライ文字と英数字・記号を混ぜた文で、記号や数字の並ぶ位置が変わりうる。
    いずれも表示だけの変化で、判定には影響しない（上の項目）
  - 限界は名前と同じ（docs/network.md §9.2 の「防げないもの」「正当なのに除かれるもの」）。語末の ZWJ（マラヤーラム語の
    チッル、デーヴァナーガリーの半字形）は問題文でも除き、正準等価の別の表し方（「が」と「か」+ U+3099）は揃えない
  - 改行・連続した空白は書かれたとおりに出す（名前と違って空白 1 つにまとめない）。長さで切り詰めもしない
  - 読み上げ（TTS）に渡す文字列には手を入れない（上の項目と同じ）。見えない文字が読み上げでどう扱われるかは確かめていない
  - 問題エディタとホスト設定の読み込み結果は、作者が自分のデータを確かめる場所なので、書かれたとおりに出す（除かない）

### JSON Schema 実体

`docs/schemas/question-set.schema.json`（draft 2020-12）に上記を機械可読な形で定義済み。CI は使わないため、`QuestionRepository`（Questions asmdef）の読み込み時にこのスキーマ相当のバリデーションを C# で実施する（後述 §3）。

---

## 2. バリデーション規則

`QuestionRepository` が問題セットを読み込む際に以下を検証する。

- **共通**
  - `schemaVersion` が `1` であること（異なる場合はスキップしログに記録）
  - `setId` / `title` / `questions` が必須項目として存在すること
  - `text` は 500 文字以内
  - `readingText` は指定されている場合 500 文字以内（`text` と同じ上限。配信用 DTO の上限（§7、network.md §8.6）と一致させ、読み込めた問題が配信できない状態を作らないため）
  - `id` は 100 文字以内
  - `id` はセット内で重複しないこと（重複時はそのセット全体をスキップ）
  - `tags` は 20 件以内、各要素は 100 文字以内
  - 見えない文字（書式文字など）と積み重ねた結合記号は検証しない（エラーにしない。Game 画面に出す直前に除く。§1.1、#209）
- **freeText**
  - `answers` が 1 件以上存在すること
  - `answers` は `QuestionLimits.MaxAnswerCount`（20件）以内であること（PR #93 レビュー L3、issue #32。
    `choices` の上限 8 件より緩くしているのは、表記ゆれを吸収する自由記述の性質を考慮したもの）
  - `answers` の各要素は 100 文字以内
  - `choices` / `correctIndex` が存在しないこと（存在する場合は警告してもエラー扱い）
- **choice**
  - `choices` が 2〜8 件であること
  - `correctIndex` が `0 <= correctIndex < choices.length` の範囲であること
  - `answers` が存在しないこと
- **画像**
  - `imagePath` が指定されている場合、セットファイルからの相対パスで画像ファイルが実在すること
  - `imagePath` は絶対パス不可。`../` 等でセットフォルダの外を指すものも不可
  - 画像サイズは 2MB 以内（超過はエラー）
  - 形式は PNG または JPG。拡張子に加えて **中身（先頭バイトの署名）** でも判定する（#16）
  - **幅・高さとも 4096px 以内**（`QuestionLimits.MaxImageDimension`、#16）。
    圧縮率の高い巨大画像で受信側のメモリを食い潰さないための上限で、
    読み込み時はファイル先頭 64KB のヘッダから判定する（PNG は IHDR、JPG は SOFn）。
    巨大な EXIF などで先頭 64KB から解像度を読めない JPG は読み込み時には判定せず、配信時に全体を読んで確かめる
  - 規則の実体は `TsumugiQuiz.Questions.Images.QuestionImagePathResolver` / `ImageFormatProbe` にあり、
    読み込み時の検証（`QuestionSetValidator`）と配信時（`QuestionDistributor`）が同じものを使う

### エラー時の扱い

- 検証に失敗した **問題セット単位でスキップ**する（1問の不正で他の問題まで巻き込まない設計とし、セット単位のスキップに統一する）
- スキップしたセットは起動時・再読込時に UI（HostSetup 画面）へ一覧表示する（ファイル名・エラー内容）
- ログには対象ファイルの絶対パスと、該当する問題の `id`（判別できる場合）・行番号相当の情報（JSON パーサがサポートする範囲で）を記録する

---

## 3. サンプルファイル

`docs/samples/sample-questions.json` に、`freeText` / `choice` を各1問、画像付きの問題を1問（`imagePath: "images/sample.png"` を参照）を収録している。画像ファイル自体は `docs/samples/` には置かず、アプリが初回起動時に書き出す分を `Assets/TsumugiQuiz/Resources/Questions/sample-image.bytes`（実体は 800x600 の PNG。猫のイラスト。`.bytes` 拡張子の `TextAsset` として同梱し、`QuestionLibrary` が `asset.bytes` をそのまま書き出す）に同梱している。この画像は本プロジェクトのために `scripts/gen-sample-image.py` で自作したもので、外部の素材や画像生成モデルの出力は使わず、スクリプト内の Pillow の図形描画だけで描いている（Pillow 12.3.0 で生成。issue #220。それまでは 1x1 の黒い画素だった）。描き直すときは同スクリプトを実行する。

**既存の問題フォルダのサンプル画像は更新されない**: `QuestionLibrary.EnsureFolderAndSample` は問題フォルダが無いときだけサンプルを書き出し、既存のファイルは上書きしない。#220 より前に作られた問題フォルダの `images/sample.png` は 1x1 の黒い画素のままなので、新しい画像が要る場合は `python scripts/gen-sample-image.py --out <問題フォルダ>\images\sample.png` で上書きする。

EditMode テスト `SampleImageResourceTests` が、PNG であること・配信の検証を通ること・表示できる大きさ（480px 以上）であることを確かめる。`docs/schemas/question-set.schema.json` に対する検証テスト、および `AnswerNormalizer` のテストデータとして EditMode テストから読み込む想定。

**単一ソース化（PR #42 統括申し送り M4）**: このファイルを問題データの唯一の正とする。EditMode テストは可能な限りこのファイルを実行時にコピーして読み込み、内容を手で複製したテストデータを持たない。ただし `QuestionLibrary`（issue #29）が初回起動時にビルド成果物へ書き出すサンプルは、Unity の `Resources` フォルダに実ファイルとして同梱する必要があるため
`Assets/TsumugiQuiz/Resources/Questions/sample-questions.json` に複製を保持する。この複製が本ファイルと乖離しないよう、EditMode テスト（`SampleQuestionsResourceSourceTests`）で内容の一致を検証している。

---

## 4. フォルダ・読み込みタイミング

- 問題フォルダ: `%USERPROFILE%\Documents\TsumugiQuiz\Questions\`（JSON ファイル置き場）と `...\Questions\images\`（画像置き場）（**仮決め K8**）
  - 「Documents」は `Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)` を基準とする。OneDrive でユーザーフォルダがリダイレクトされている環境でも、この API はリダイレクト先を返すため常にそちらを使う（PR #42 統括申し送り L1）。問題エディタ（#30〜#32）・「フォルダを開く」（#29）を含め、問題フォルダを参照する実装は必ず `QuestionRepository.GetDefaultQuestionsFolderPath()` を経由すること
  - **Documents ルートの解決順（issue #112）**: 上記の「Documents」は `TsumugiQuiz.Core.DocumentsPaths.Root` が唯一の出所で、次の 4 段階で決まる（`AppPaths`（`-tq-data-root`、#71）と同じ作法）。通常起動時は 4 が使われ、1〜3 はテスト・検証専用の差し替え口である
    1. `DocumentsPaths.Configure(root)` による明示設定。起動オプション `-tq-documents-root <絶対パス>`（`TsumugiQuiz.Core.LaunchArguments.DocumentsRoot`）は**この 1 への入力経路**で、Boot の `AppPathsBootstrap` が読み取って `Configure` を呼ぶ（不正な値なら起動は止めず、エラーログを出して既定へフォールバックする）
    2. 環境変数 `TSUMUGI_DOCUMENTS_ROOT`（`scripts/verify.ps1` が Unity バッチ起動時にプロセス単位で設定する。不正な値は Boot が検証してそのプロセスでは無視する。ただしこれは Boot（`AppPathsBootstrap`）を通る実行時のみ成立し、EditMode テストや Editor 上での直接実行など Boot を経由しない経路では検証されないため、不正な値が設定されたまま `DocumentsPaths.Root` を参照すると `ArgumentException` が飛ぶ、issue #122-4）
    3. `DocumentsPaths.ConfigureDefault(root)` で登録された既定値（EditMode / PlayMode のテストアセンブリが、実ユーザーの Documents を汚さないよう一時フォルダを登録する）
    4. 実ユーザーの Documents フォルダ（`Environment.GetFolderPath(SpecialFolder.MyDocuments)`）
  - 1〜2 の値は `TsumugiQuiz.Core.RootPathValidator` で検証・正規化する（空・相対パス・`Assets/` 配下は拒否、`..` は解決してから保持する）
  - プリセットフォルダ（`Documents\TsumugiQuiz\Presets\`、`RoomPresetStore.GetDefaultFolderPath()`）も同じ `DocumentsPaths` を経由するため、上記の解決順に従う
  - 実機確認で実ユーザーの Documents に触れたくない場合は `pwsh ./scripts/run-multi.ps1 -IsolateDocuments`（docs/dev-workflow.md §3.3）を使う
  - フォルダが存在しない場合、アプリ初回起動時に自動作成し、同時に `docs/samples/sample-questions.json` 相当のサンプル問題データ（画像付き）を書き出す（`TsumugiQuiz.Questions.QuestionLibrary`、issue #29）。既にファイルが存在する場合は上書きしない
- 読み込みタイミング:
  1. アプリ起動時に自動読み込み
  2. HostSetup 画面（#5 に引き継ぎ）の「再読込」ボタン押下時。`QuestionLibrary.Reload()` は同期版として提供する
  3. `Questions/` フォルダ（および `images/` サブフォルダ、issue #29 レビュー M8）の変更（ファイル追加・更新・削除・リネーム）を `FileSystemWatcher` で検知した場合。検知イベントは `ReloadDebouncer` で 500ms デバウンスしたうえで自動再読込する。この自動再読込のみ、実際のフォルダ I/O・検証はワーカースレッドで行い、結果の反映（`CurrentReport` 更新・`Changed` イベント発火）だけをメインスレッドで行う（issue #29 レビュー M10。手動の `Reload()` は同期のまま）
- サブフォルダの扱い: 問題ファイル自体は **1階層のみ読み込む（仮決め: 本ドキュメント）**。`Questions/` 直下の `*.json` のみを対象とし、サブフォルダ内の JSON は読み込まない。ただし `images/` サブフォルダの変更（画像の追加・更新・削除）は上記3の自動再読込のトリガーには含める（imagePath の検証結果が変わりうるため）。将来的に問題ファイル自体の再帰読み込みが必要になった場合は別途検討する
- ファイル数・サイズの上限（PR #42 統括申し送り L6、issue #29 レビュー M7/M14）:
  - フォルダ直下の `*.json` ファイル数は最大 **200件**（`Array.Sort` によるファイル名の順序で先頭200件を採用し、超過分は決定的にスキップする）。超過した場合は特定の1ファイルに紐づかない「フォルダ単位のエラー」として `QuestionRepositoryResult.FolderErrors` / `QuestionLoadReport.FolderErrors` に記録する（超過ファイル名は先頭5件を列挙し、残りは「他N件」と丸める）
  - 1ファイルあたりのサイズは最大 **5MB**。超過したファイルはパースを試みずスキップし、そのファイルに紐づく `QuestionSetLoadError`（`QuestionRepositoryResult.SkippedSets`）として記録する（フォルダ単位のエラーとは区別する）
  - 他プロセスがファイルを開いている等の一時的な `IOException` は、短い間隔を空けて最大2回までリトライしたうえで、通常の読み込み失敗とは異なる「一時的に読み取れませんでした（他アプリが使用中の可能性があります）」という文言で `SkippedSets` に記録する（issue #29 レビュー M9）。ファイルが存在しない等の恒常的なエラーはリトライしない
  - フォルダ自体が存在しない場合・列挙自体に失敗した場合も、`FolderErrors` に理由を記録する
- フォルダ作成・監視開始に失敗した場合（issue #29 レビュー H1）: `QuestionLibrary` のコンストラクタは例外を投げず、「監視なし・手動再読込のみ」に degrade したうえで、原因を `QuestionLoadReport.FolderErrors` に記録する。監視自体がエラーを報告した場合（H2）は、取りこぼした変更を回収するため即座に再読込を予約したうえで監視を再構築し、フォルダが無い等で再構築できない場合は次回の「再読込」時に再度アームを試みる

---

## 5. 正規化ルール（判定用）

自由入力（`freeText`）の回答判定は、入力文字列と `answers` の各候補をそれぞれ以下の手順で正規化し、一致するかどうかで判定する。あいまい一致は行わない。

1. Unicode 正規化 **NFKC** を適用する（全角英数・記号を半角へ、互換文字を統合）
2. カタカナをひらがなへ変換する（`U+30A1`〜`U+30F6` の範囲を `-0x60` する。ただし `ヴ`（U+30F4）は `ゔ`（U+3094）へ個別対応）
3. 英字を小文字化する
4. 空白文字および不可視の書式制御文字（ゼロ幅スペース・BOM 等、Unicode カテゴリ Cf）をすべて除去する
5. 前後の空白をトリムする（手順4で内部空白も除去済みのため、実質的には全角/半角スペースの完全除去で完了する）

**区別すること（あいまい一致にしない）**:
- 長音記号「ー」はそのまま保持し、母音の伸ばし表記との同一視は行わない
- 濁点・半濁点は区別する（例: 「は」と「ば」「ぱ」は別物として扱う）
- 小書き文字（「っ」「ゃ」「ゅ」「ょ」等）と通常サイズの文字は区別する（例: 「がっこう」と「がこう」は不一致）
- 波ダッシュ類（`〜`/`～`）や「ヶ」/「ケ」などの異体表記は統一しない（仕様）。表記ゆれを正解にしたい場合は `answers` 配列に併記すること

### 正規化の例（入力 → 正規化後）

| # | 入力 | 正規化後 | 備考 |
|---|---|---|---|
| 1 | `東京` | `東京` | 漢字はそのまま（カタカナ変換対象外） |
| 2 | `トウキョウ` | `とうきょう` | カタカナ→ひらがな |
| 3 | `とうきょう` | `とうきょう` | 変化なし |
| 4 | ` Tokyo ` | `tokyo` | 前後空白除去・小文字化 |
| 5 | `ｔｏｋｙｏ`（全角英字） | `tokyo` | NFKC で半角化 → 小文字化 |
| 6 | `パソコン` | `ぱそこん` | 濁点・半濁点は保持したままひらがな化 |
| 7 | `ヴァイオリン` | `ゔぁいおりん` | `ヴ`→`ゔ`、小書き文字はそのまま保持 |
| 8 | `コーヒー` | `こーひー` | 長音記号「ー」は変換・除去しない |
| 9 | `がっこう` | `がっこう` | 小書き「っ」を保持（「がこう」とは不一致） |
| 10 | `東京　です`（全角スペース含む） | `東京です` | 全角スペースも除去対象 |
| 11 | `とう​きょう`（ゼロ幅スペース U+200B 含む） | `とうきょう` | 不可視の書式制御文字（Cf）も除去対象 |

複数正解は `answers` 配列のいずれか1つに正規化後一致すれば正解とする（実装: `TsumugiQuiz.Core.AnswerMatcher.IsCorrect`）。

---

## 6. 選択式の判定

- `correctIndex` と、プレイヤーが選択した `choices` の元インデックスが一致すれば正解
- ルーム設定（room-settings.md）で選択肢の表示順シャッフルを有効にできるが、シャッフルは各クライアントの表示上のみで行い、**サーバーは常に元の `choices` 配列インデックス（シャッフル前）で判定する**。クライアントは選択時に「表示上の選択肢」から「元インデックス」へ変換してサーバーへ送信する（表示用シャッフルの対応表はクライアント側で保持）

---

## 7. クライアント配信（正解データを送らない）

**仮決め K14** の通り、サーバーは出題直前（次問の先読みを含む）に、正解情報を含まない DTO のみをクライアントへ配信する。

### 配信用 DTO のフィールド一覧

| フィールド | 型 | 備考 |
|---|---|---|
| `id` | string | 問題 ID |
| `type` | `"freeText"` \| `"choice"` | 出題形式 |
| `text` | string | 画面表示用問題文 |
| `readingText` | string | 読み上げ用テキスト（省略時のフォールバック済みの値） |
| `choices` | string[] | `choice` のときのみ。**`correctIndex` は含まない** |
| `imageData` | bytes（分割送信） | `imagePath` が存在する場合のみ。分割送信方式は network.md 参照 |
| `difficulty` | integer | 参考表示用 |
| `tags` | string[] | 参考表示用（フィルタ結果の確認用） |

`answers` と `correctIndex` は DTO に含めず、サーバー内部でのみ保持・比較する。

### サイズ上限（送信前・受信直後の両方で検証）

上限値は `TsumugiQuiz.Questions.QuestionLimits` を単一の出所とし、読み込み時の検証
（`QuestionSetValidator`、§2）と配信用 DTO（`QuestionDto`）が同じ値を参照する。
これにより「読み込めたのに配信できない問題」が生まれない。

| フィールド | 上限 | 上限を超えていたときの扱い |
|---|---|---|
| `id` | 100 文字 | 読み込み時にエラー（配信時も不正として出題を中止） |
| `text` | 500 文字 | 同上 |
| `readingText` | 500 文字 | 同上 |
| `choices` | 8 件 × 各 100 文字 | 同上 |
| `tags` | 20 件 × 各 100 文字 | 読み込み時にエラー。**配信時は切り詰めて送り、警告ログを残す**（参考表示用なので出題は止めない） |
| `difficulty` | 1〜5 | 読み込み時にエラー |

見えない文字と積み重ねた結合記号は、送信前・受信直後のどちらでも弾かない（表示の直前に除く。§1.1、#209）。

---

## 8. 問題エディタ仕様（アプリ内、UI Toolkit）

`QuestionEditor` View（UI asmdef）は以下の要素で構成する。

- **セット一覧**: `Documents/TsumugiQuiz/Questions/` 内の JSON ファイルを一覧表示。新規作成・削除・複製が可能
- **問題一覧**: 選択中セット内の問題を `id` / `type` / `text` の要約で一覧表示。並び替え・追加・削除が可能。要約は平文として表示する（§1.1）
- **編集フォーム**: `type`（`freeText`/`choice`）切り替えで表示項目が変化する
  - `freeText`: `text` / `readingText` / `answers`（複数入力可能なリスト UI） / `imagePath` / `tags` / `difficulty`
  - `choice`: `text` / `readingText` / `choices`（2〜8件の可変リスト） / `correctIndex`（選択肢からのラジオ選択） / `imagePath` / `tags` / `difficulty`
- **画像選択**: Windows 標準のファイル選択ダイアログは Unity Standalone ビルドから呼び出しにくいため、**（仮決め: 本ドキュメント）** `Documents/TsumugiQuiz/Questions/images/` フォルダ内のファイル一覧から選ぶ簡易 UI + 「フォルダを開く」ボタン（エクスプローラーで images フォルダを開き、利用者が画像をコピーしてから一覧を再読込する運用）とする
- **保存**: 編集内容を本書 §1 のスキーマに従って JSON 出力する。インデントは半角スペース2個で整形する
- **バリデーション表示**: 保存前に §2 のバリデーション規則を適用し、エラー箇所をフォーム上にハイライト表示する
- **読み上げプレビュー**: 編集中の `readingText`（または `text`）を TTS で試し読みできるボタンを設ける（tts.md のローカル合成経路を利用）

### セット一覧・問題一覧の仮決め（PR #88 レビュー M3）

- **新規作成・複製時の命名規則**: 新規作成は setId・ファイル名ともに `new-set` から始め、衝突する場合は
  `new-set-2`、`new-set-3`…と数値を増やして採番する。複製は元の setId・ファイル名に `-copy` を付け、
  衝突する場合は `-copy-2`、`-copy-3`…と増やす（`TsumugiQuiz.Questions.Editing.QuestionEditorNaming`）。
  複製時のタイトルは元タイトル + 「のコピー」とし、100文字（§1 の `title` 上限）を超える場合は
  「のコピー」を残したまま元タイトル側を切り詰める
- **削除できない問題**: セット内の問題が1件しかない場合、その問題は削除できない（§2「questions は1件以上」
  を維持するため）。削除ボタンはこの条件のとき無効化する
- **削除の確認**: セット・問題いずれの削除も、一覧画面内のインライン確認パネルを経由する（host-setup 画面の
  作法と同じ。別ウィンドウのモーダルダイアログは使わない）
- **問題エディタ用スキャナの上限**: `QuestionSetFileScanner` は `QuestionRepository`（§4）と同じ上限
  （フォルダ直下 `*.json` は最大200件、1ファイルは最大5MB）を共有する。上限超過分は一覧に表示せず、
  ファイル数上限超過は特定の1ファイルに紐づかない「フォルダ単位の警告」として一覧の上部に表示する。
  一時的な `IOException` のリトライは行わない（利用者操作のたびに読み直すエディタの性質上、
  `QuestionLibrary` ほどの堅牢さは不要と判断）
- **外部変更の検出**: セット一覧の読み込み時点のファイル最終更新日時を保持し、問題の追加・削除・並び替えで
  ファイルへ書き戻す直前に実ファイルの最終更新日時と比較する。他プロセス・他ウィンドウが同じファイルを
  書き換えていた場合は上書きせず、「ファイルが外部で変更されています。再読込してください。」と表示する。
  新規作成・複製も、in-memory の一覧からの採番だけに頼らず、書き込み時に対象ファイルが存在しないことを
  （`FileMode.CreateNew` で）確認し、存在する場合は上書きせず失敗させる

### 編集フォームの仮決め（PR #93 レビュー M2）

issue #31（編集フォーム・画像選択・バリデーション）の実装で決めた、本書が規定していなかった細部。

- **タグ入力**: `answers` / `choices` のような可変リスト UI ではなく、**カンマ区切りの単一行テキスト**
  （`TsumugiQuiz.Questions.Editing.QuestionFormConverter.ParseTags` / `FormatTags`）とする。
  区切り文字は**半角カンマのみ**（全角読点・全角カンマは区切らない）。各要素は前後の空白を落とし、
  空要素は捨てる。表示へ戻すときは `", "`（カンマ + 半角空白）で連結する
- **`correctIndex` のラジオ**: 選択肢のテキストをラベルにせず、「選択肢 N を正解にする」という
  **位置ベースのラベル**にする（選択肢テキストを1キー入力ごとに `RadioButtonGroup.choices` へ
  同期する実装を避けるため）。行の追加・削除時はフォーム全体を作り直すため、件数とラベルはずれない
- **難易度の入力**: `SliderInt` ではなく `IntegerField` とし、範囲外の入力は §1 の 1〜5 にクランプして
  表示値も書き戻す
- **`type` 切り替え時の入力値**: 切り替え先の側（`choice` なら `choices` 2件、`freeText` なら
  `answers` 1件）が §2 の最小件数に満たない場合だけ空行で初期化する。切り替え元の入力値
  （`freeText` → `choice` に切り替えたときの `answers` など）は**フォーム上は保持**し、保存時に
  `type` に応じて捨てる（§1 の「`freeText` に `choices` を指定することはできません」を満たすため）。
  保存する前に元の `type` へ戻せば、入力値はそのまま復元される
- **`answers` / `choices` の前後空白**: 保存時に各要素を `Trim()` する。空白のみの行は空文字列になり、
  §2 の「空文字列を含めることはできません」で保存前に弾かれる
- **`readingText` の空入力**: `Trim()` 後に空なら `null` として扱い、JSON に `"readingText": ""` を
  書き出さない（§1 の `readingText` は省略可）
- **未保存の入力の破棄**: 別の問題を選択した場合や再読込した場合、フォームの未保存の入力は
  **確認なしで破棄**される（モーダル確認ダイアログは #30 の方針に反するため使わない）。
  ただし issue #32 で、実際に入力値が変わっていた場合に限り、問題一覧の状態表示に
  「未保存の変更を破棄しました。」を出すようにした（保存直後の再読み込みでは出さない）。
  判定は素の `QuestionFormData` 同士を比較するのではなく、`QuestionFormConverter.ToQuestion` で
  正規化（トリム・空文字列→null化）した結果同士を `QuestionFormData.HasSameValuesAs` で比較する
  （PR #103 レビュー M2。末尾の空白や `readingText` の null と空文字列の違いだけを
  「変更あり」と誤検知しないため）
- **画像一覧の上限**: `Questions/images/` 直下の画像は `QuestionLimits.MaxQuestionImageFileCount`（200件、
  §4 の問題ファイル数上限と同じ値）で打ち切り、超過時は画像一覧の下に
  「上限を超えたため一部のみ表示しています」旨の警告を出す（セット一覧のフォルダ単位警告と同じ作法）
- **正解候補（answers）の件数上限**: `QuestionLimits.MaxAnswerCount`（20件）に達すると
  「正解候補を追加」ボタンを無効化する（`choices` の上限到達時と同じ作法、issue #32 / PR #93 レビュー L3）。
  この上限は #32 で導入したもので、**読み込み・保存の両方に同じ値を適用する**（PR #103 レビュー M3。
  リリース前でユーザーデータは存在しないため、読み込み時だけ緩めるような挙動の分岐はしない）。
  そのため `answers` が21件以上の問題を含む問題セットファイルは、`QuestionRepository` の読み込み時に
  §2 のバリデーションに違反するとして**そのセット全体が拒否（スキップ）される**（§2「エラー時の扱い」）
- **読み上げプレビュー**: 「読み上げを試す」ボタンで、編集中の `readingText`（空なら `text`）を
  `TsumugiQuiz.Tts.TtsService.Instance` でローカル合成・再生する（ネットワーク同期は使わない）。
  速度は既定の `1.0` 固定で試聴する（ルーム設定 `tts.speed` の反映は行わない）。
  合成中はボタンを無効化し、再生中は「停止」に変わり再押下で止められる。`TtsService.Instance` が無い、
  または `EnsureInitializedAsync` 後も `Status.IsReady` にならない場合（External 未配置・未同意・
  ユーザーによる無効化等）は、フォーム内に短い案内 + 「読み上げの状態を確認」ボタンを表示し、
  押すと `TsumugiQuiz.UI.TtsStatusPanel`（#25 のフォールバック UI）を全画面オーバーレイで開く
  （PR #103 レビュー M1。文言・「配置手順を表示」「再試行」等の導線を1箇所に一致させるため）

---

## 9. テスト方針

- 正規化（`AnswerNormalizer`）とバリデーション（`QuestionRepository` の検証ロジック）は `TsumugiQuiz.Core` / `TsumugiQuiz.Questions` に実装し、Unity API に極力依存しない形で `TsumugiQuiz.Tests.EditMode` から純 C# のユニットテストとして検証する
- `docs/samples/sample-questions.json` をテストデータとして読み込み、正常系（3問すべてが正しく読み込めること）と異常系（重複 id、範囲外 `correctIndex`、文字数超過などを注入したテスト用 JSON）の両方を検証する
- 正規化の例表（本書 §5 の10例）をそのままテストケースの入力・期待値として利用する
