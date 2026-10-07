# External/ — 外部素材・ライブラリ置き場

## 目的

権利上の理由で **git 管理外**にしなければならない素材とライブラリを置くディレクトリ。

- VOICEVOX の音声モデル（`.vvm`）、Open JTalk 辞書、`voicevox_core.dll`、`voicevox_onnxruntime.dll`
- 春日部つむぎ公式立ち絵素材（**二次配布禁止**）

`.gitignore` により、`External/` 配下は `.gitkeep` と本 README を除いてすべて無視される。
**このディレクトリの中身を絶対にコミットしないこと。**

ここに置いたファイルは `scripts/setup-external.ps1` が `Assets/` の所定の場所へコピーする（仮決め K24）。
`Assets/` 側のコピー先も `.gitignore` で除外されている。

## 関連ドキュメント

- [docs/tts.md](../docs/tts.md) — voicevox_core の使い方、配置構成、規約の確認結果
- [docs/licenses.md](../docs/licenses.md) — アプリ内クレジット画面の文言と根拠 URL
- [docs/tasks/setup-brief.md](../docs/tasks/setup-brief.md) — 共通ブリーフ（仮決め K24）

---

## 1. 現在配置済みのファイル

| パス | サイズ | 内容 | 入手元 |
|---|---|---|---|
| `voicevox_core/voicevox_core-windows-x64-0.17.0.zip` | 1,447,774 B | voicevox_core 0.17.0 の C API 本体。`include/voicevox_core.h`(77,868B) / `lib/voicevox_core.dll`(5,934,024B) / `lib/voicevox_core.lib` / `LICENSE`(MIT) / `README.txt` / `VERSION` | <https://github.com/VOICEVOX/voicevox_core/releases/tag/0.17.0> |
| `voicevox_core/open_jtalk_dic_utf_8-1.11.tar.gz` | 23,643,819 B | Open JTalk システム辞書。`char.bin` / `matrix.bin` / `sys.dic` / `unk.dic` / `*.def` / `COPYING`（修正 BSD、NAIST） | <https://github.com/r9y9/open_jtalk/releases> |
| `tsumugi/春日部つむぎ立ち絵_公式_v2.0.zip` | 14,127,150 B | 春日部つむぎ公式立ち絵素材 v2.0。`readme.txt` / `tsumugi_logo.png` / `…_v1.1.1.png` / `…_v2.0.png` / `…_v2.0.psd` | <https://tsukushinyoki10.wixsite.com/ktsumugiofficial> |

### 取得済み（2026-09-13、公式ダウンローダー実行結果・実測）

ユーザー承認（2026-09-13）を受けて `scripts/fetch-voicevox.ps1` 相当のダウンローダーを実行し、次を取得済み（すべて `External/voicevox_core/` 配下、git 管理外）。実行ログ: `Logs/voicevox-download.log`（git 管理外）。

| パス | サイズ | 内容 |
|---|---|---|
| `c_api/include/voicevox_core.h` | 実測 | C API ヘッダ |
| `c_api/lib/voicevox_core.dll` | 5,934,024 B | voicevox_core 0.17.0 本体 |
| `c_api/lib/voicevox_core.lib` | 24,956 B | インポートライブラリ |
| `c_api/LICENSE` / `c_api/README.txt` / `c_api/VERSION`（`0.17.0`） | — | MIT ライセンス・バージョン |
| `onnxruntime/lib/voicevox_onnxruntime.dll` | 12,312,440 B | **DLL 名にバージョン接尾辞は付かない**（実測。`voicevox_onnxruntime-1.17.3.dll` にはならなかった） |
| `onnxruntime/lib/voicevox_onnxruntime.lib` | 2,328 B | インポートライブラリ |
| `onnxruntime/TERMS.txt` | 960 B（22 行） | VOICEVOX ONNX Runtime 利用規約（原文は docs/licenses.md §7 に転記済み） |
| `onnxruntime/third-party-notices.html` | 420,920 B（7,611 行） | ONNX Runtime が依存する第三者コンポーネントの notices。配布時同梱する |
| `onnxruntime/VERSION_NUMBER`（`1.17.3`）/ `onnxruntime/GIT_COMMIT_ID` | — | ONNX Runtime のバージョン・コミット |
| `models/vvms/0.vvm` | 58,214,379 B | 春日部つむぎ ノーマル（スタイル ID 8）を含む音声モデル。モデルタグ **0.16.4**（実行ログで確認。0.17.0 はプレリリースのため選択されなかった） |
| `models/README.txt`（600 行）/ `models/TERMS.txt`（377 行） | — | VOICEVOX 音声モデル 利用規約・話者別クレジット文言（春日部つむぎ: `VOICEVOX:春日部つむぎ`） |
| `dict/open_jtalk_dic_utf_8-1.11/`（`char.bin` 等 + `COPYING`） | 合計約 104.8MB | Open JTalk 辞書。**タグ v1.11.1、辞書本体 1.11**（`Logs/voicevox-download.log` 420 行目「ダウンロードOpen JTalk辞書タグ: v1.11.1」と、実際に展開されたフォルダ名 `open_jtalk_dic_utf_8-1.11` の両方を実測。タグ名の末尾 `.1` は配布アーカイブのリリース通番であり、辞書本体のバージョン表記ではないため混同しないこと） |
| `download-windows-x64.exe` | — | 公式ダウンローダー本体 |

---

## 2. 公式ダウンローダーで取得する

### 2.1 スクリプトを使う（推奨）

```powershell
# リポジトリルートで実行
# まずは何が実行されるかを確認（ダウンロードしない）
.\scripts\fetch-voicevox.ps1 -DryRun

# 実際に取得する（対話。規約プロンプトに自分で y を入力する）
.\scripts\fetch-voicevox.ps1

# 規約を読んで同意済みであることを明示したうえで非対話実行する（実測 2026-09-13、§2.3 参照）
.\scripts\fetch-voicevox.ps1 -AcceptTerms
```

`scripts/fetch-voicevox.ps1` は次を行う。

1. `download-windows-x64.exe` を voicevox_core 0.17.0 のリリースから `External/voicevox_core/` へ取得する
   （`gh release download` が使えればそれを、なければ `Invoke-WebRequest` を使う）
2. ダウンローダーを `--output External\voicevox_core --only c-api onnxruntime models dict --devices cpu --models-pattern 0.vvm --c-api-version 0.17.0` で実行する（`-AcceptTerms` 指定時は標準入力で規約同意を渡し非対話実行、既定は対話実行）
3. 期待されるファイル（`voicevox_onnxruntime.dll` / `models/vvms/*.vvm` / `dict/open_jtalk_dic_utf_8-1.11`）の存在をチェックする

### 2.2 利用規約の同意プロンプトについて（重要）

**voicevox_core 0.17.0 のダウンローダーには非対話オプション（`--yes` 相当）が存在しない。**
`models` と `onnxruntime` をダウンロードするとき、次の 2 つの規約への同意が対話で求められる。

- VOICEVOX 音声モデル 利用規約
- VOICEVOX ONNX Runtime 利用規約

規約本文がページャで表示されるので、

1. 上下キー・スペースでスクロールして読む
2. `q` でページャを閉じる
3. `[y,n,r] :` に **`y`** を入力して Enter（`n` = 同意しない、`r` = もう一度読む）

**必ず PowerShell のコンソールで対話的に実行すること。** バックグラウンド実行やリダイレクトでは正しく動かない。

### 2.3 非対話実行（規約を読んで同意済みの場合、実測 2026-09-13）

規約を人間が事前に読んで同意済みであることが明確な場合（例: CI 相当のローカルスクリプト実行、二回目以降の再取得）は、標準入力からプロンプトへ `y` を流し込むことで非対話実行できることを実測した。

```powershell
# 実測: exit 0 で完走した
printf 'y\ny\n' | .\External\voicevox_core\download-windows-x64.exe `
    --output .\External\voicevox_core `
    --only c-api onnxruntime models dict `
    --devices cpu `
    --models-pattern 0.vvm `
    --c-api-version 0.17.0
```

- `models` と `onnxruntime` の 2 つの規約同意プロンプトそれぞれに `y` が渡るよう `y` を 2 回渡す（`printf 'y\ny\n'`、PowerShell では `'y','y' | & $DownloaderPath @dlArgs` でも同様に渡せる）
- 実行結果はログに保存すること（本プロジェクトでは `Logs/voicevox-download.log`、git 管理外）
- `scripts/fetch-voicevox.ps1` に `-AcceptTerms` スイッチとして実装済み（§2.1 参照）。既定は従来どおり対話実行のまま
- **前提**: 実行者が事前に規約本文（`models/TERMS.txt` 相当・`onnxruntime/TERMS.txt` 相当）を読んで同意していること。読まずに `y` を流し込むことは規約同意の趣旨に反するため行わないこと

### 2.4 手動で実行する場合

```powershell
# 1) ダウンローダーを取得（0.17.0 に固定）
gh release download 0.17.0 -R VOICEVOX/voicevox_core -p download-windows-x64.exe -D .\External\voicevox_core\

#    gh が使えない場合
Invoke-WebRequest `
    -Uri https://github.com/VOICEVOX/voicevox_core/releases/download/0.17.0/download-windows-x64.exe `
    -OutFile .\External\voicevox_core\download-windows-x64.exe

# 2) GitHub のレートリミット回避（推奨）
$env:GH_TOKEN = gh auth token

# 3) 実行
.\External\voicevox_core\download-windows-x64.exe `
    --output .\External\voicevox_core `
    --only c-api onnxruntime models dict `
    --devices cpu `
    --models-pattern 0.vvm `
    --c-api-version 0.17.0
```

主なオプション（`crates/downloader/src/main.rs` タグ 0.17.0 を読んで確認。全一覧は `docs/tts.md` §5.2）:

| オプション | 既定 | 説明 |
|---|---|---|
| `--only <TARGET>...` | — | `c-api` / `onnxruntime` / `additional-libraries` / `models` / `dict` から選ぶ |
| `--exclude <TARGET>...` | — | 除外する対象 |
| `-o, --output <DIR>` | `.\voicevox_core` | 出力先 |
| `--devices <DEVICE>...` | `cpu` | `cpu` / `directml` / `cuda` |
| `--models-pattern <GLOB>` | `*` | 取得する VVM のファイル名パターン |
| `--c-api-version <SEMVER>` | 範囲内の最新 | C API のバージョン |
| `--onnxruntime-version <SEMVER>` | 範囲内の最新 | ONNX Runtime のバージョン |
| `-t, --tries <N>` | `5` | リトライ回数 |
| `--help` | — | ヘルプ |

`--models-pattern 0.vvm` を **必ず指定すること**。省略すると全 VVM（`0.vvm`〜`24.vvm` + `n0.vvm` + `s0.vvm`、合計 1.5GB 超）をダウンロードする。
本プロジェクトが必要とするのは春日部つむぎ ノーマルを含む `0.vvm`（約 58MB）だけ。

---

## 3. 展開後の配置構成

`scripts/fetch-voicevox.ps1` の実行後、`External/` は次のようになる。

```
External/
├── README.md                                 ← このファイル（git 管理対象）
├── voicevox_core/
│   ├── .gitkeep
│   ├── voicevox_core-windows-x64-0.17.0.zip  （手動配置済み）
│   ├── open_jtalk_dic_utf_8-1.11.tar.gz      （手動配置済み）
│   ├── download-windows-x64.exe              ← fetch-voicevox.ps1 が取得
│   ├── c_api/
│   │   ├── include/voicevox_core.h
│   │   ├── lib/
│   │   │   ├── voicevox_core.dll             ★
│   │   │   └── voicevox_core.lib
│   │   ├── LICENSE                           （MIT）
│   │   ├── README.txt
│   │   └── VERSION
│   ├── onnxruntime/
│   │   └── lib/
│   │       └── voicevox_onnxruntime.dll      ★
│   ├── models/
│   │   ├── vvms/
│   │   │   └── 0.vvm                         ★  春日部つむぎ ノーマルを含む
│   │   ├── README.txt
│   │   └── TERMS.txt                         （VOICEVOX 音声モデル 利用規約）
│   └── dict/
│       └── open_jtalk_dic_utf_8-1.11/        ★
│           ├── char.bin / matrix.bin / sys.dic / unk.dic
│           ├── left-id.def / right-id.def / pos-id.def / rewrite.def
│           └── COPYING                       （修正 BSD / NAIST）
└── tsumugi/
    ├── .gitkeep
    ├── 春日部つむぎ立ち絵_公式_v2.0.zip       （手動配置済み）
    └── 春日部つむぎ立ち絵_公式_v2.0/          ← 展開後（§5）
        ├── readme.txt
        ├── tsumugi_logo.png
        ├── 春日部つむぎ立ち絵_公式_v1.1.1.png
        ├── 春日部つむぎ立ち絵_公式_v2.0.png
        └── 春日部つむぎ立ち絵_公式_v2.0.psd
```

★ = `scripts/setup-external.ps1` が `Assets/` へコピーするファイル。

> **実測（2026-09-13）**: `onnxruntime/lib/` の DLL 名にバージョン接尾辞は**付かなかった**（`voicevox_onnxruntime.dll` のまま。
> `voicevox_onnxruntime-1.17.3.dll` にはならなかった）。バージョンは同ディレクトリの `VERSION_NUMBER`（`1.17.3`）で確認する。
> `setup-external.ps1` はワイルドカードで拾う実装のままでよい（接尾辞が付くケースにも耐えるため）が、
> 実際には接尾辞なしの単一ファイルとして見つかる。

---

## 4. Assets へのコピー（setup-external.ps1）

```powershell
.\scripts\setup-external.ps1
```

コピー先（仮決め K24）:

```
Assets/Plugins/voicevox_core/x86_64/
    voicevox_core.dll              ← External/voicevox_core/c_api/lib/voicevox_core.dll
    voicevox_onnxruntime.dll       ← External/voicevox_core/onnxruntime/lib/voicevox_onnxruntime*.dll

Assets/StreamingAssets/voicevox_core/
    open_jtalk_dic_utf_8-1.11/     ← External/voicevox_core/dict/open_jtalk_dic_utf_8-1.11/
    models/
        0.vvm                      ← External/voicevox_core/models/vvms/0.vvm
```

立ち絵（issue #24）は voicevox_core と異なり **`Assets/` 配下には一切コピーしない**（§5.4/§5.5 参照。
配置先はデータルート、`AppPaths.DataRoot` 相当）。当初 `Assets/TsumugiQuiz/Art/Character/` に
書き出す想定だった（仮決め K24 時点のメモ）が、原本を `Assets/` に置くこと自体を避ける方針に変更した。

`Assets/Plugins/voicevox_core/` と `Assets/StreamingAssets/voicevox_core/` は **`.meta` ごと `.gitignore` 対象**。
External を配置していない環境では、TTS の自己診断（`docs/tts.md` §10.4）が `MissingNative` を返し、読み上げなしでゲームが動く。

> `voicevox_core-windows-x64-0.17.0.zip` の中の DLL を直接使うことも可能だが、
> `voicevox_onnxruntime.dll` はこの zip に**含まれていない**ため、ダウンローダーの実行は結局必要になる。

---

## 5. 立ち絵（External/tsumugi/）

### 5.1 展開

```powershell
Expand-Archive -Path .\External\tsumugi\春日部つむぎ立ち絵_公式_v2.0.zip `
               -DestinationPath .\External\tsumugi\ -Force
```

> **文字化けに注意**: zip 内のファイル名は Shift-JIS(CP932) で格納されており、`unzip` など一部のツールでは文字化けする。
> Windows のエクスプローラまたは PowerShell の `Expand-Archive` を使えば正しく展開できる。

### 5.2 構成

| ファイル | 内容 |
|---|---|
| `readme.txt` | 利用規約（**UTF-8、BOM なし**、CRLF 改行） |
| `tsumugi_logo.png` | ロゴ（117,659 B） |
| `春日部つむぎ立ち絵_公式_v1.1.1.png` | 旧バージョンの立ち絵（2,332,294 B） |
| `春日部つむぎ立ち絵_公式_v2.0.png` | 立ち絵 v2.0（2,157,311 B） |
| `春日部つむぎ立ち絵_公式_v2.0.psd` | 立ち絵 v2.0 のレイヤー付き PSD（14,856,318 B）。表情差分はここから書き出す |

> ブリーフでは `readme.txt` が「UTF-8 BOM」とされていたが、実測では **BOM なしの UTF-8** だった
> （先頭 3 バイトが `0D 0A 2D`。`EF BB BF` ではない）。読み込み時に BOM を期待しないこと。

### 5.3 規約の要点（`readme.txt` 原文より）

作成: **春日部つくし** / 追記日: 2022/05/20
公式 HP: <https://tsukushinyoki10.wixsite.com/ktsumugiofficial>

**【利用のルール】**

- **服を脱がせた状態での利用は厳禁**。着せ替え差分作成時のみ利用可
- **加筆、加工できます**。ただし良識の範囲内で行うこと
- 動画、サムネイルなどに使用可
- **この立ち絵を利用した動画は商用目的で利用することが出来る。ほか用途での営利目的の利用は出来ない**
- 春日部つむぎを利用した作品においていかなる損害が発生しても、制作者は一切責任を負わない
- 制作者が必要と判断した場合には、通知なくいつでも規約を変更できる

**【禁止すること】**

- 本素材を印刷したグッズの販売、頒布
- 誹謗中傷を内容に含むもの
- 第三者に不快感を与えるもの
- 第三者の権利等を侵害するもの
- 特定の思想・団体に関係するもの
- **二次配布、自作発言**
- その他、春日部つくしが不適切と判断する行為に使用すること

> 最新バージョンの規約がすべてのバージョンに適用される。

### 5.4 本プロジェクトでの扱い（重要な確認事項）

| 項目 | 判断 |
|---|---|
| git へのコミット | **不可**（二次配布にあたる）。`External/` を `.gitignore` で除外する |
| 配布 zip への同梱 | **不可（確定、issue #24 レビュー、B 案）**。`Assets/` にも一切コピーしない。ユーザーが `AppPaths.DataRoot` 配下に自分で配置する方式にする（§5.5） |
| 加工（表情差分の作成） | 可（「加筆、加工できます」）。ただし良識の範囲内。**生成はユーザーのローカル環境で行い、生成した PNG もリポジトリ・配布物に含めない**（§5.7、docs/licenses.md §3.1、issue #86） |
| 服を脱がせた状態 | **厳禁**。表情差分の書き出し時に、素体レイヤーのみの状態を出力しないこと |
| クレジット表記 | `docs/licenses.md` に記載し、アプリ内クレジット画面に表示する |

> **確定（issue #24 レビュー、統括判断、2026-09-14）**: 立ち絵の原本はアプリの配布物に同梱しない（B 案）。
> voicevox の音声モデルと違い、立ち絵は「ユーザーが自分で入手して配置する」方式にする。
> 配置先は `Assets/` 配下ではなく `AppPaths.DataRoot`（`Application.persistentDataPath` 相当、#71）。
> 「git にコミットしない」ことと「`Assets/` に置かない」ことは別の要求である点に注意
> （`Assets/StreamingAssets/` のような git 管理外フォルダであっても、`Assets/` である以上ビルドに
> 同梱されるため、原本の置き場所として使わない）。表情差分の作成・その配布可否は issue #86 で扱う。

### 5.5 データルートへの配置（`scripts/setup-external.ps1`、issue #24）

zip の展開と、開発環境での動作確認用のデータルートへのコピーは `scripts/setup-external.ps1` が行う
（§5.1 の手動展開コマンドは参考・トラブルシュート用に残す）。**Assets/ へは一切コピーしない**
（レビュー H1）。

```powershell
pwsh ./scripts/setup-external.ps1
```

- `External/tsumugi/` 直下の `*.zip`（ファイル名はワイルドカードで検索する。通常
  `春日部つむぎ立ち絵_公式_v2.0.zip`）を `External/tsumugi/extracted/` へ展開する
  （`Expand-Archive`。§5.1 と同じ、文字化けの心配はない）。
- 展開後、ファイル名に `v2.0` を含み `v1.1.1` を含まない PNG（= 立ち絵 v2.0 の全身 PNG 1 枚）を
  データルート（`AppPaths.DataRoot` 相当。既定は `%USERPROFILE%\AppData\LocalLow\Tomonorarari-Think\TsumugiQuiz\`、
  `ProjectSettings/ProjectSettings.asset` の `companyName`/`productName` から解決する。環境変数
  `TSUMUGI_DATA_ROOT` が設定されていればそちらを優先する）配下の `tsumugi/tsumugi_v2.png` へコピーする。
  **PSD・v1.1.1 PNG・ロゴ・zip 自体はコピーしない**（二次配布禁止、§5.4）。
- データルートが解決できない場合は展開のみ行い、コピー先を手動で案内する。
- zip が未配置（`External/tsumugi/` に無い）でもスクリプト全体を失敗させない。警告を出して
  スキップし、アプリ側は `CharacterView`（`TsumugiQuiz.UI`、issue #24）が立ち絵を表示しないまま
  進行する（`character-root` を非表示にするだけで、クイズ自体は続行できる）。
- `-TsumugiRoot <パス>` で他所（本体ツリー等）の `External/tsumugi/`（zip の置き場所）を参照できる
  （`Assets/` 配下は指定不可、レビュー L4）。展開先は常に実行した worktree 内に置かれる。
  立ち絵の配置だけ省略したい場合は `-SkipTsumugi`。

実行時、`CharacterImagePaths.ResolveImagePath()`（`AppPaths.Combine("tsumugi", "tsumugi_v2.png")`）が
指すパスを `CharacterImageLoader`（`TsumugiQuiz.UI`）が読み込む。`GameView` がプロセス単位でキャッシュし、
View の表示のたびに読み直さない（docs/tts.md §8.1「レビュー M6」）。

### 5.6 PSD レイヤー構成（issue #24 で調査、表情差分の生成は §5.7）

表情差分の生成手順は §5.7（issue #86）。本節は生成に使うレイヤー構成の一覧。
差分が未生成の環境では、v2.0 の全身 PNG 1 枚を「待機」の絵として使い、他 3 状態は UI 側の演出
（バウンス・ティント（`Image.tintColor`、#192）・マーク）で表現する（docs/tts.md §8.1/§8.2）。

PSD（Python `psd-tools` 1.19.0 で調査、2026-09-13。サイズ 2037×4084、`ColorMode.RGB`）のレイヤー構成
（`!` 始まりはグループ、`*` 始まりは差分候補。インデントは階層）:

```
!体部分（グループ、全身の素体・髪・服）
  髪　サイドテール（反転用）[非表示]
  髪　ロング
  後頭部
  体
  髪　サイドテール
  制服（グループ）
    靴 / 体影 / スカート / シャツ / セーター / シュシュ / ネクタイ / ネックレス / 缶バッチなど / 腕章
  私服（グループ、既定非表示。着せ替え差分用、readme.txt の「服を脱がせた状態厳禁」に関連）
    サンダル / ズボン / インナー / トップス / 首輪 / ブレスレット / 髪飾り（反転） / 髪飾り
  髪　もみあげ / 顔 / 髪　前髪 / ヘアピン

!口（グループ、表情差分の候補。既定表示は「*ω」）
  *３ / *ω　わ / *ω（既定表示） / *べー / *へっ / *わあ！ / *わ / *お / *え / *う /
  *いー / *い / *あ / *綴じ　ギザギザ / *綴じ　へ / *綴じ　むっ / *綴じ　にこ / *綴じ　－

!目（グループ、表情差分の候補。既定表示は「*普通」）
  *赤目 / *白目 / *瞳小 / *見開く / *ジト目２ / *ジト目（横目） / *ジト目 / *ウインク /
  *ハイライト無（横目） / *ハイライト無 / *微笑む（横目） / *微笑む / *普通（横目） /
  *普通（既定表示） / *＞＜ / *まる / *なごみ / *笑う / *綴じ

!眉（グループ、表情差分の候補。既定表示は「*普通」）
  *ん？ / *真面目 / *悲しい / *怒る / *困る / *普通（既定表示）

!アクセサリー（グループ、表情の強調用。既定表示は「ホクロ」のみ）
  どんより / がーん / しいたけ目 / ハート目 / 赤面 / ほっぺ赤 / うるうる / 涙１（目綴じ） /
  涙１ / 汗２ / 汗１ / ？ / ！ / ホクロ（反転用） / ホクロ（既定表示）
```

上記グループから目・口・眉・アクセサリーの表示レイヤーを組み合わせて書き出す。実際の組み合わせは
§5.7 の生成スクリプトが `docs/tsumugi-expressions.sample.json` から読む（issue #86 で実装）。
「私服」グループ・素体のみの状態は readme.txt の禁止事項（服を脱がせた状態）に触れるため、
書き出し対象に含めない（生成スクリプトが書き出し直前に検証して弾く）。

### 5.7 表情差分の生成（`scripts/generate-tsumugi-expressions.ps1`、issue #86 / #212）

場面ごとの表情 9 枚（待機 / 読み上げ中 / 回答権の獲得（自分・他人）/ 誤答の瞬間 / 正解 / 不正解の確定 /
時間切れ / 回答できる人がいない）を、**自分の環境の PSD から自分で生成する**（#86 は 4 枚、#212 で 9 枚に増やした）。
生成した PNG は git 管理外のユーザーデータで、リポジトリ・配布物には一切含めない
（docs/licenses.md §3.1。二次配布禁止条項に触れないための方針）。

> **配布 zip 版（#219）**: リポジトリを持たない利用者向けに、同じ生成の本体
> （`scripts/generate_tsumugi_expressions.py`）とレイヤー対応表（`docs/tsumugi-expressions.sample.json`）を、
> `scripts/package-release.ps1` が配布 zip の `tools/tsumugi-expressions/` にコピーする。zip 版の入口は
> `scripts/tsumugi_expressions_standalone.py`（立ち絵 zip から PSD を一時フォルダに取り出し、データルートを
> `AppPaths.DataRoot` と同じ規則で決める）と `scripts/tsumugi-expressions-tool/`（`make-expressions.bat` /
> `install-libraries.bat` / `make-expressions.ps1` / `README.txt`）。利用者向けの手順は docs/manual/setup.md の 6.4。
> 本節の開発者向けの手順は変わらない。依存ライブラリの指定は両方とも `scripts/tsumugi-expressions-requirements.txt`。
> **zip 版はアプリの同意の記録を必須とする**（PR #224 レビュー H1、NFR-08）。データルートの `consent.json` に
> アプリと同じ条件の有効な同意が無ければ、終了コード 5 で止まる（判定は `scripts/tsumugi_app_consent.py`）。
> 本節の `generate-tsumugi-expressions.ps1` は同意の記録を確かめない。開発者は素材を自分で入手して規約を確認した
> うえで扱う前提で、出力先も `External/` やテスト用のデータルートを使うことが多いため（docs/licenses.md §3.1）。

```powershell
# 1. zip を External/tsumugi/ に置いて展開する（§5.5）
pwsh ./scripts/setup-external.ps1

# 2. Python の psd-tools（MIT）/ Pillow（MIT-CMU）を入れる（docs/licenses.md §15）
pwsh ./scripts/generate-tsumugi-expressions.ps1 -InstallDeps

# 3. 生成する（既定の出力先は <データルート>/tsumugi/）
pwsh ./scripts/generate-tsumugi-expressions.ps1
```

| オプション | 意味 |
|---|---|
| `-DryRun` | レイヤー名の検証・安全確認だけ行い、PNG を書き出さない |
| `-TsumugiRoot <パス>` | zip / 展開結果の置き場所（既定: この worktree の `External/tsumugi`） |
| `-PsdPath <パス>` | PSD を直接指定する（既定: `-TsumugiRoot` 配下を再帰検索） |
| `-ConfigPath <パス>` | レイヤー対応表 JSON（既定: `External/tsumugi/expressions.json` → 無ければ `docs/tsumugi-expressions.sample.json`） |
| `-DataRoot <パス>` / `-OutputDir <パス>` | 出力先。`Assets/` 配下とリポジトリ内（`External/` 以外）は拒否する |
| `-Crop <範囲>` | 切り出し範囲（#191）。`bustup`（既定、頭から腰の上まで。PSD に対する比率 `0.22,0.01,0.90,0.45` = 1385x1797px）/ `full`（切り出さない全身。#191 以前と同じ）/ `左,上,右,下`（0.0〜1.0 の比率）。範囲外・左≧右などは書き出さずに終了する。表情レイヤーが範囲からはみ出す指定では警告を出す |
| `-MaxHeight <px>` | 切り出した後の最大高さ（等比縮小、既定 1280。`0` で切り出したままの原寸）。既定値は表示サイズ（1920x1080 の表示の約 2 倍、4K 16:9 で等倍）から決めた（#190 / #191、根拠は `scripts/generate_tsumugi_expressions.py` の `DEFAULT_MAX_HEIGHT`） |
| `-Only <key>` | 特定の表情だけ書き出す（複数指定可） |
| `-InstallDeps` | `python -m pip install -r scripts/tsumugi-expressions-requirements.txt`（psd-tools / Pillow）を実行する |

出力（`<データルート>/tsumugi/`。既定では 9 枚とも 987x1280 のバストアップ。すべて `ホクロ` は表示したまま）:

| ファイル | 状態（場面） | 既定のレイヤー組み合わせ（口 + 目 + 眉 + アクセサリー） |
|---|---|---|
| `tsumugi_idle.png` | 待機 | `*ω` + `*普通` + `*普通` |
| `tsumugi_reading.png` | 読み上げ中 | `*あ` + `*普通` + `*普通` |
| `tsumugi_buzz_self.png` | 自分が回答権を得た（#212） | `*わ` + `*見開く` + `*普通` + `！` |
| `tsumugi_buzz_other.png` | 他の参加者が回答権を得た（#212） | `*お` + `*普通（横目）` + `*真面目` |
| `tsumugi_wrong_moment.png` | 誤答の瞬間（受付の開き直し、#212） | `*え` + `*瞳小` + `*困る` + `がーん` + `汗２` |
| `tsumugi_correct.png` | 正解 | `*わあ！` + `*笑う` + `*普通` + `ほっぺ赤` |
| `tsumugi_wrong.png` | 不正解の確定（#212 で変更） | `*綴じ　ギザギザ` + `*＞＜` + `*困る` + `汗１` |
| `tsumugi_timeout.png` | 時間切れ（#212） | `*綴じ　へ` + `*ジト目` + `*困る` + `汗１` + `どんより`（#86 の不正解の組み合わせ） |
| `tsumugi_no_eligible.png` | 回答できる人がいない（#212） | `*う` + `*普通` + `*ん？` + `？` |

> **#212 で表情を増やしたので、#86 の 4 枚を生成済みの環境も本スクリプトを再実行して作り直すこと。**
> 作り直すまでは、増えた場面は意味の近い既存の表情（誤答の瞬間・時間切れ → `tsumugi_wrong.png`、
> 回答権 → `tsumugi_idle.png`）で表示され、結果の場面では従来どおりティント（#192）で補う。
> また `tsumugi_wrong.png`（不正解の確定）は組み合わせを変えたので、作り直すまでは以前の顔
> （どんより。#212 からは時間切れの顔）のままになる。

組み合わせを変えたい場合は `docs/tsumugi-expressions.sample.json` を
`External/tsumugi/expressions.json`（git 管理外）にコピーして編集する。

**安全確認（自動・設定では無効化できない）**: 次の 3 つは生成スクリプト本体に固定されており、
設定ファイル（`expressions.json`）では緩められない（PR #135 レビュー H1・H2）。
いずれかに触れると **1 枚も書き出さずに終了コード 4 で異常終了**する。

1. `layers` に書けるグループは `!口` / `!目` / `!眉` / `!アクセサリー` のホワイトリストのみ。
   `!体部分` / `制服` / `私服` を指定することはできない
   （readme.txt「服を脱がせた状態での利用は厳禁です」）。
2. 書き出し直前に PSD の実状態を検証し、`!体部分` / `制服` が表示・`私服` が非表示であることを確認する。
   設定ファイルの `safety` セクションは**追加**しかできず、この組み込み分を削れない
   （`safety` を丸ごと省いても同じ検証が走る）。検証対象のグループが PSD に無ければ
   「検証不能」として中止する。
3. 出力先が `Assets/` 配下やリポジトリ内（`External/` 以外）なら拒否する。
   PowerShell ラッパーと Python 本体の両方に実装してあるので、
   `python scripts/generate_tsumugi_expressions.py` を直接実行しても効く。
   「リポジトリ内」は**出力先から上へ辿って** `ProjectSettings/ProjectSettings.asset` か
   `.git` を持つ祖先を探して判定するため、worktree から本体ツリーの `Assets/` を
   指定した場合も拒否する（PR #135 再レビュー M1）。ホームやデータルート自体が
   git 管理下の環境では、`-OutputDir` で管理外のパスを明示すること。

> **終了コードを見るときは `pwsh -File` で実行する**: 安全確認で拒否した場合の終了コードは 4
> （Python 側の `EXIT_SAFETY` と同じ）だが、`pwsh -Command "& ./scripts/generate-tsumugi-expressions.ps1 ..."`
> の形で呼ぶとスクリプトの `exit` が握り潰されて 1 になる。CI 的に結果を判定する用途では
> `pwsh -File ./scripts/generate-tsumugi-expressions.ps1 ...` を使うこと（PR #135 限定確認 LOW-3、実測）。

**未生成でも動く**: アプリは 表情差分 →（誤答の瞬間・時間切れは `tsumugi_wrong.png`、回答できる人がいないは
`tsumugi_timeout.png` → `tsumugi_wrong.png` を挟んで）→ `tsumugi_idle.png` → `tsumugi_v2.png` の順にフォールバックし、
どれも無ければ立ち絵を表示しないまま進行する（`CharacterImagePaths.GetFileNameCandidates`、docs/tts.md §8.3.1）。
その状態専用の差分が無くてフォールバックしたときだけ、正解・不正解・時間切れ・回答できる人がいないの場面で
ティント（#192）を掛ける。

> **生成したらアプリを再起動すること**: 立ち絵テクスチャはプロセス単位でキャッシュされる
> （`GameView.Character.cs`、docs/tts.md §8.3）。アプリを起動したまま PNG を差し替えても
> 反映されない。

> **#191 より前に生成した環境**: 以前のスクリプトは全身（2037x4084 を高さ 2048 に縮小）で書き出していた。
> そのままでも表示できる（カードの縦横比は読み込んだ画像に合わせる。右列に収まらないときは高さで頭打ちになる）が、
> バストアップで大きく表示するには本スクリプトを再実行してすべて作り直すこと。
>
> **全部の表情を同じ設定でそろえて作ること**: カードの縦横比は表示中の画像に合わせるため、`-Only` で一部の表情だけを
> 別の `-Crop` / `-MaxHeight` で作り直すと、表情が切り替わるたびにカードの大きさが変わり、立ち絵の位置もずれる。
> 設定を変えたときは、9 枚すべてを同じ設定で作り直すこと。

> **注（実測 2026-09-18）**: zip 同梱の完成版 PNG（`春日部つむぎ立ち絵_公式_v2.0.png`、= §5.5 が
> `tsumugi_v2.png` として配置するもの）は**私服＋ウインク**の絵だが、PSD の既定表示状態は**制服**で、
> 生成される表情差分も制服になる。すべて生成すれば揃うが、一部だけ生成して
> `tsumugi_v2.png` にフォールバックする状態だと服装が混在して見える。

---

## 6. ライセンス一覧（`docs/licenses.md` と同期させること）

| 対象 | ライセンス / 規約 | 再配布 |
|---|---|---|
| voicevox_core（`voicevox_core.dll`） | MIT（Copyright (c) 2021 Hiroshiba Kazuyuki） | 可（ライセンス文の同梱が条件） |
| VOICEVOX 音声モデル（`0.vvm`） | VOICEVOX 音声モデル 利用規約 | **可**（「アプリケーションに組み込んで再配布することができます」） |
| 春日部つむぎ 音声ライブラリ | 同規約の音声ライブラリ節 / <https://zunko.jp/con_ongen_kiyaku.html> | 生成音声は「VOICEVOX:春日部つむぎ」のクレジットで商用・非商用可 |
| VOICEVOX ONNX Runtime（`voicevox_onnxruntime.dll`） | VOICEVOX ONNX Runtime 利用規約 | **確認済み**（2026-09-13、`onnxruntime/TERMS.txt` を実際に読んで確認。原文は docs/licenses.md §7） |
| Open JTalk 辞書 | 修正 BSD（Copyright (c) 2009, Nara Institute of Science and Technology, Japan） | 可（著作権表示・条件文・免責の同梱が条件） |
| 春日部つむぎ公式立ち絵素材 v2.0 | 独自規約（`readme.txt`） | **二次配布禁止**。同梱しない（確定、B 案、§5.4） |
| 立ち絵の表情差分 PNG（§5.7 で生成） | 同上（原本の加工物） | **同梱しない**。ユーザーが自分の環境で生成する（docs/licenses.md §3.1） |
| psd-tools / Pillow（§5.7 の生成スクリプトの依存） | MIT / MIT-CMU | アプリには同梱しない（開発・生成時のみ使用。docs/licenses.md §15） |

**共通**: VOICEVOX を利用したことがわかるクレジット表記が必須（<https://voicevox.hiroshiba.jp/term/>）。
