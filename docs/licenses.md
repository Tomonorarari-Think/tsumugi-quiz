# 権利表記・ライセンス方針

## 目的
本プロジェクトが利用する VOICEVOX・春日部つむぎ関連素材・OSS ライブラリについて、根拠となる利用規約・ライセンスの URL、指定されたクレジット表記フォーマット、配布時に同梱すべきファイルを一覧化する。アプリ内クレジット画面・配布パッケージの `THIRD-PARTY-NOTICES.txt` はここに書かれた文言をそのまま使う。

## 関連ドキュメント
- [requirements.md](./requirements.md) — NFR-05（権利表記の厳密さ）・NFR-06（素材の git 管理外）
- [dev-workflow.md](./dev-workflow.md) — 配布パッケージ作成手順
- [tasks/setup-brief.md](./tasks/setup-brief.md) — §0 確定事項、K11（UPnP ライブラリ候補）

## 凡例
- 「確認日」は WebFetch 等で実際にアクセスして内容を確認した日付
- 本書やアプリ同梱の `Resources/Terms/*.txt` などに転記した第三者の規約・ライセンスの原文は、**いずれも各「確認日」「取得日」時点の写しである。規約は予告なく変更されうるため、最新版は各節の根拠 URL（公式ページ）を参照すること**
- 「取得できず（要手動確認）」は自動取得が失敗またはページが動的レンダリングで内容を取得できなかったもの。**この文言がある項目は、配布前に人間が目視で最新の規約を確認すること**

---

## 1. VOICEVOX（音声合成ソフトウェア本体の利用規約）

| 項目 | 内容 |
|---|---|
| 対象 | VOICEVOX ソフトウェアで合成した音声の利用 |
| ライセンス種別 | VOICEVOX 独自の利用規約（OSS ライセンスではない） |
| 根拠 URL | https://voicevox.hiroshiba.jp/term/ （確認日 2026-09-12、WebFetch で内容確認済み） |
| 規約が指定する表記フォーマット | 規約本文には「VOICEVOX を利用したことがわかるクレジット表記が必要です」とのみ記載され、統一フォーマットの指定はソフトウェア全体の規約には無い。個別の音声ライブラリ（話者）ごとに具体的なフォーマットが定められる（§2 参照） |
| 規約要点（WebFetch で確認した文面） | 「商用・非商用問わず利用することができます」/「作成された音声を利用する際は、各音声ライブラリの規約に従ってください」/「作成された音声の利用を他者に許諾する際は、当該他者に対し本許諾内容の 2 及び 3 の遵守を義務付けてください」/ 禁止事項として「本ソフトウェアの全てまたは一部を無断で再配布すること」「逆コンパイル・リバースエンジニアリング及びこれらの方法を公開すること」「製作者または第三者に不利益をもたらす行為」「公序良俗に反する行為」/免責:「本ソフトウェアにより生じた損害・不利益について、製作者は一切の責任を負いません」 |
| アプリのクレジット画面に載せる文言（確定版） | `VOICEVOX:春日部つむぎ`（§2 の話者別クレジットに包含されるため、ソフトウェア名単独の表記は不要。§2 の文言で足りる） |
| 同梱すべきファイル | なし（本規約はソフトウェア利用条件であり、再配布物ではない。規約 URL をクレジット画面・THIRD-PARTY-NOTICES.txt に記載） |

---

## 2. 春日部つむぎ（音声キャラクターの利用規約 / クレジット表記）

| 項目 | 内容 |
|---|---|
| 対象 | 春日部つむぎの音声（VOICEVOX 話者）を使って合成した音声の利用 |
| ライセンス種別 | キャラクター別利用規約（春日部つむぎ公式サイトに掲載。voicevox_vvm README / VOICEVOX 話者ページにも同旨の要約が掲載） |
| 根拠 URL（一次情報・確定、原文転記済み） | **https://tsumugi-official.studio.site/rule2** （2026-09-13、オーナー提供のスクリーンショットより統括が転記。ページは JavaScript 描画のため自動取得不可）。同運営の関連ページ https://tsumugi-official.studio.site/rule も同様の規約を掲載（§3 参照）。二次情報として voicevox_vvm README（`External/voicevox_core/models/README.txt` / `models/TERMS.txt`）にも要約クレジット文言の記載あり（§6 参照） |
| 「VOICEVOX「春日部つむぎ」利用規約」原文（全 7 項目、原文どおり転記） | `1. VOICEVOX「春日部つむぎ」利用規約とは、ヒホ氏制作の音声合成ソフトウェア「VOICEVOX」で作成した春日部つむぎの音声に対して適応されます。`<br>`2. VOICEVOXで合成した春日部つむぎの音声は商用・非商用問わず利用することが出来ます。`<br>`3. 利用の際には動画内、概要欄など任意の場所にクレジット表記をしてください。表記例： VOICEVOX:春日部つむぎ`<br>`4. 音声を利用する場合は、VOICEVOXの利用規約も必ずお守りください。`<br>`5. VOICEVOX「春日部つむぎ」を利用した作品においていかなる損害が発生しても、春日部つむぎ運営はこれに対して一切責任を負いません。`<br>`6. 以下の行為について禁止します。`<br>`・誹謗中傷を内容に含むもの。`<br>`・第三者に不快感を与えるもの。`<br>`・第三者の権利等を侵害するもの。`<br>`・特定の思想・団体に関係するもの。`<br>`・二次配布、自作発言`<br>`・その他、春日部つむぎ運営が不適切と判断する行為に使用すること。`<br>`7. 春日部つむぎ運営が必要と判断した場合には、通知することなくいつでも本規約を変更することができるものとします。` |
| 公式 Q&A（同ページ、原文どおり転記） | `Q: テレビ番組、ラジオ、ゲームの音声、自作のアプリケーションなどに利用してもいい？`<br>`A: 問題ありません。`<br>`クレジットは番組の公式サイト、音声の末尾、ゲームのエンディングなど、ユーザーが探して見つけられる場所に記載をお願いいたします。`<br>`尚、利用規約範囲内の利用の場合個別で利用許諾はお送りしていません。ご了承ください。` |
| 本アプリでの判断ポイント（確定） | 本アプリは無償・非商用の自作アプリケーションであり、公式 Q&A が「自作のアプリケーションなど」への利用を明示的に許容しているため規約範囲内。個別の利用許諾申請は不要（Q&A に「利用規約範囲内の利用の場合個別で利用許諾はお送りしていません」と明記）。守るべき義務は (a) クレジット「VOICEVOX:春日部つむぎ」を、ユーザーが探して見つけられる場所（本アプリではクレジット画面）に表示する（規約 3・Q&A） (b) VOICEVOX 本体の利用規約（§1）も併せて遵守する（規約 4） |
| 規約が指定する表記フォーマット（確定） | `VOICEVOX:春日部つむぎ`（規約 3 の表記例どおり） |
| アプリのクレジット画面に載せる文言（確定版） | `VOICEVOX:春日部つむぎ` |
| 同梱すべきファイル | なし（規約 URL をクレジット画面・THIRD-PARTY-NOTICES.txt に記載） |

---

## 3. 春日部つむぎ立ち絵（公式イラスト素材。公式の立ち絵 zip に同梱の readme.txt の規約を含む）

| 項目 | 内容 |
|---|---|
| 対象 | `External/tsumugi/春日部つむぎ立ち絵_公式_v2.0.zip` 内の立ち絵 PNG・PSD（Assets には展開せず、素材から生成したスプライトのみを使う。git 管理外） |
| ライセンス種別 | 独自の二次利用規約。**現行の一次情報は公式サイトの「春日部つむぎ公式立ち絵、Live2D」利用規約ページ**（下記原文）。zip 同梱の `readme.txt` も、同規約 4 項により引き続き確認・遵守が義務付けられている（両方を満たす必要がある） |
| 根拠（一次情報・確定、原文転記済み） | **https://tsumugi-official.studio.site/rule2** （2026-09-13、オーナー提供のスクリーンショットより統括が転記。ページは JavaScript 描画のため WebFetch 等での自動取得は不可）。加えて zip 内 `春日部つむぎ立ち絵_公式_v2.0/readme.txt`（UTF-8、BOM なし、CRLF。External/README.md の実測と整合）を Python `zipfile` で実際に展開して全文確認済み（確認日 2026-09-12） |
| 「春日部つむぎ公式立ち絵、Live2D」利用規約 原文（全 8 項目、原文どおり転記） | `1. 「春日部つむぎ公式立ち絵、Live2D」利用規約とは、BOOTHで配布されている春日部つむぎの公式立ち絵、Live2Dに対して適応されます。`<br>`2. 「春日部つむぎ公式立ち絵、Live2D」は映像制作を中心に様々な用途で利用いただけます。`<br>`3. 「春日部つむぎ公式立ち絵、Live2D」を商用利用する場合は映像、またそれに付随するサムネイルなどに限ります。ほか用途での商用利用は別途お問い合わせください。`<br>`4. 配布データのフォルダ内に入っているreadmeも確認の上ご利用ください。`<br>`5. 加筆、加工できます。ただし、春日部つむぎと分からない・春日部つむぎではないキャラクターへの改変は禁止します。`<br>`6. 「春日部つむぎ公式立ち絵、Live2D」を利用した作品においていかなる損害が発生しても、春日部つむぎ運営はこれに対して一切責任を負いません。`<br>`7. 以下の行為について禁止します。`<br>`・「春日部つむぎ公式立ち絵、Live2D」を印刷したグッズの販売、頒布。`<br>`・誹謗中傷を内容に含むもの。`<br>`・第三者に不快感を与えるもの。`<br>`・第三者の権利等を侵害するもの。`<br>`・特定の思想・団体に関係するもの。`<br>`・二次配布、自作発言`<br>`・その他、春日部つむぎ運営が不適切と判断する行為に使用すること。`<br>`8. 春日部つむぎ運営が必要と判断した場合には、通知することなくいつでも本規約を変更することができるものとします。` |
| 公式 Q&A（同ページ、原文どおり転記。§2 と同一 Q&A、立ち絵・音声共通） | `Q: テレビ番組、ラジオ、ゲームの音声、自作のアプリケーションなどに利用してもいい？`<br>`A: 問題ありません。`<br>`クレジットは番組の公式サイト、音声の末尾、ゲームのエンディングなど、ユーザーが探して見つけられる場所に記載をお願いいたします。`<br>`尚、利用規約範囲内の利用の場合個別で利用許諾はお送りしていません。ご了承ください。` |
| zip 同梱 readme.txt 原文（利用のルール、上記規約 4 項により引き続き遵守が必要） | 「服を脱がせた状態での利用は厳禁です。着せ替え差分作成時のみご利用ください。」/「加筆、加工できます。ただし、良識の範囲内で行ってください。」/「動画、サムネイルなどにお使いください。」/「この立ち絵を利用した動画は商用目的で利用することが出来ます。ほか用途での営利目的の利用は出来ません。」/「春日部つむぎを利用した作品においていかなる損害が発生しても、これに対して一切責任を負いません。」/「制作者が必要と判断した場合には、通知することなくいつでも本規約を変更することができるものとします。」 |
| zip 同梱 readme.txt 原文（禁止すること） | 「本素材を印刷したグッズの販売、頒布。」/「誹謗中傷を内容に含むもの。」/「第三者に不快感を与えるもの。」/「第三者の権利等を侵害するもの。」/「特定の思想・団体に関係するもの。」/「二次配布、自作発言」/「その他、春日部つくしが不適切と判断する行為に使用すること。」 |
| 版数の適用 | readme.txt: 「春日部つむぎ公式立ち絵素材は、最新のバージョンの規約がすべてのバージョンに適用されます。」（追記 2022/05/20）。公式サイト規約（rule2）8 項: 「春日部つむぎ運営が必要と判断した場合には、通知することなくいつでも本規約を変更することができるものとします。」 |
| **本アプリでの判断ポイント（確定）** | 本アプリは無償・非商用の自作アプリケーションであり、公式 Q&A が「自作のアプリケーションなど」への利用を明示的に許容しているため規約範囲内。商用利用ではないため、規約 3 項の「映像・サムネイル以外の商用利用は要問い合わせ」にも該当しない。守るべき義務: (a) クレジット「VOICEVOX:春日部つむぎ」を、ユーザーが探して見つけられる場所（本アプリではクレジット画面）に表示する（§2 の音声規約・Q&A に基づく。立ち絵自体には個別の表記フォーマット指定はないため、音声クレジットと合わせてキャラクター全体のクレジットとして扱う） (b) zip 同梱の readme.txt も併せて遵守する（規約 4 項により明示的に義務付けられている。服を脱がせた状態での利用厳禁、二次配布禁止など） (c) 加筆・加工は「春日部つむぎと分かる範囲」に留め、春日部つむぎではないキャラクターへの改変はしない（規約 5 項） (d) 立ち絵素材（zip・PSD・PNG 原本）自体を `Assets/` や配布 zip に含めない（二次配布禁止、規約 7 項・readme.txt 双方） (e) VOICEVOX 本体の利用規約（§1）も遵守する |
| アプリのクレジット画面に載せる文言（確定版） | `春日部つむぎ立ち絵 (C) 春日部つくし`。**注**: zip 同梱 readme.txt は作成者を「春日部つくし」と記すが、現行の公式サイト規約（rule2）は権利主体を「春日部つむぎ運営」と表記しており、名称に差異がある（個人クリエイターから運営体制への移行を示すものと推測されるが未確認）。クレジット文言は readme.txt の記載を踏襲しつつ、配布前に公式サイトでの最新表記を再確認すること |
| 同梱すべきファイル | **なし**。立ち絵素材の PSD/PNG 原本・zip も、そこから作成した表情差分 PNG（加工物）も、同梱・再配布しない（§3.1、issue #86 で確定）。配布 zip の `tools/tsumugi-expressions/`（#219）に入れるのは、利用者が自分で入手した zip から表情差分を作るためのツールのコードと設定（レイヤー名の対応表）だけで、素材の画像データは含まない（§3.1） |
| **実装方針（issue #24、統括判断 B 案、2026-09-14）** | アプリは実行時にユーザーが配置した画像（`AppPaths.DataRoot` 配下、`Application.persistentDataPath` 相当）を読む。未配置なら立ち絵を表示しない。原本は配布物（Assets 含む）に一切含めない。表情差分（4 状態分）の作成・配布可否は後続 issue #86 で扱う → **§3.1 で確定** |
| **UI テーマ配色の扱い（issue #132、2026-09-18）** | UI のテーマ配色（`Assets/TsumugiQuiz/UI/Styles/theme.uss` の `--color-*` トークン）は、公式素材（立ち絵 PNG・ロゴ PNG）から `scripts/extract-palette.ps1` で抽出した**色の数値（HEX）のみ**に基づく。単一の色そのものは著作物ではなく、素材の画像データはリポジトリにも配布物にも含めない（抽出は `External/tsumugi/` 配下の git 管理外ファイルを読むだけ）。抽出結果と採用したトークンは `docs/architecture.md` §10 に記録している |

### 3.1 表情差分の作成方式（issue #86 で確定、2026-09-18）

待機 / 読み上げ中 / 正解 / 不正解の 4 状態それぞれの立ち絵（表情差分）を、
**ユーザー自身の環境で PSD から生成する**方式に確定した（B 案の延長）。
#212（2026-10-03）で場面ごとの表情を増やし、回答権の獲得（自分 / 他人）・誤答の瞬間・時間切れ・
回答できる人がいないを加えた 9 状態にした。作成方式（ユーザー自身の環境で生成し、配布しない）と
加工の範囲（表情 4 グループの表示切り替え・切り出し・等比縮小のみ）は変えていない。

| 項目 | 内容 |
|---|---|
| 生成物 | `tsumugi_idle.png` / `tsumugi_reading.png` / `tsumugi_correct.png` / `tsumugi_wrong.png`（#86）と、`tsumugi_buzz_self.png` / `tsumugi_buzz_other.png` / `tsumugi_wrong_moment.png` / `tsumugi_timeout.png` / `tsumugi_no_eligible.png`（#212）の 9 枚（`AppPaths.DataRoot/tsumugi/` 配下）。**#212 で表情を増やしたので、#86 の 4 枚を生成済みの利用者も生成し直すこと**（作り直すまでは増えた場面が既存の表情へフォールバックし、`tsumugi_wrong.png` は以前の顔（どんより）のままになる。External/README.md §5.7） |
| 生成する場所 | **ユーザーのローカル環境のみ**。生成スクリプト（`scripts/generate-tsumugi-expressions.ps1` / `scripts/generate_tsumugi_expressions.py`）とレイヤー対応表（`docs/tsumugi-expressions.sample.json`。レイヤー名だけで素材は含まない）はリポジトリに含めるが、**生成された PNG はリポジトリにも配布物にも一切含めない**。出力先が `Assets/` 配下・リポジトリ内（`External/` 以外）だと、PowerShell ラッパーと Python 本体の**両方**が書き出し前に拒否する（PR #135 レビュー H2。ラッパーを経由せず `python scripts/generate_tsumugi_expressions.py` を直接実行しても効く）。「リポジトリ内」は出力先の祖先を辿って `ProjectSettings/ProjectSettings.asset` か `.git` を探して判定するので、スクリプトが置かれているツリー以外（本体ツリー・他の worktree・無関係な別リポジトリ）を指定した場合も拒否する（再レビュー M1） |
| 生成スクリプトの配布（#219、2026-10-03） | リポジトリを持たない利用者も 9 表情を作れるよう、配布 zip の `tools/tsumugi-expressions/` に生成スクリプトを同梱する。中身は自作のコード（`generate_tsumugi_expressions.py` はリポジトリと同じファイル、入口の `tsumugi_expressions_standalone.py`、同意の判定の `tsumugi_app_consent.py`、起動用の `make-expressions.bat` / `install-libraries.bat` / `make-expressions.ps1`）、レイヤー対応表（`expressions.json` = `docs/tsumugi-expressions.sample.json` の写しで、`$schemaNote` だけ zip 版の説明に差し替える）、依存の一覧（`requirements.txt`）、アプリが提示する規約の本文（`terms/*.txt` = `Assets/TsumugiQuiz/Resources/Terms/*.txt` の写し。同意の判定に使う）、`README.txt` だけで、**画像・PSD・zip は入れない**（`scripts/package-release.ps1` がファイル名の許可リストと同梱禁止物の検査で確かめる。表情差分の名前 `tsumugi_*.png` も同梱禁止物に加えた）。ツールは利用者が自分で入手した公式 zip から PSD だけを一時フォルダに取り出して使い、終わったら消す。出力先は `AppPaths.DataRoot` と同じ規則のデータルート（`<データルート>/tsumugi/`）で、アプリのフォルダ（zip を展開した場所）の中は拒否する（フォルダごと人に渡すと生成物も渡るため）。安全装置（`ALLOWED_EXPRESSION_GROUPS` の 4 グループへの限定、制服・私服の検証、リポジトリ内・`Assets/` の拒否）は生成の本体にあるので zip 版でもそのまま効く。**方針との整合**: 配布するのは素材ではなくツールのコードであり、生成は従来どおり利用者自身の環境で行い、生成物は配布しない（本節の B 案の延長、二次配布の禁止に触れない）。ツールのコードは自作で第三者のコードを含まず、依存の psd-tools（MIT）/ Pillow（MIT-CMU）は利用者が自分で PyPI から pip で入れる（配布物に含めない、§15）。レイヤー単位の利用の解釈に §12 項目 9 の制約が残る点は、ツールを配布しても変わらない。**NFR-08 との整合（PR #224 レビュー H1、2026-10-03 ユーザー決定の案 (a)）**: ツールは**アプリの同意の記録を必須とする**。データルートの `consent.json` に、アプリが立ち絵の表示に使う判定（`ConsentGate.HasUserConsented` = 同梱の全規約について termsId と本文の SHA-256 が一致する記録があること。撤回すると記録が 0 件になる）と同じ条件で有効な同意が無ければ、`--dry-run` でも立ち絵を読む前に終了コード 5 で止める（`consent.json` が壊れている・形が違うときも止める）。判定は `scripts/tsumugi_app_consent.py` に移植し、C# と同じテスト入力（`scripts/tests/fixtures/`）で Python と EditMode テスト（`ExpressionToolCompatibilityTests`）の両方から同じ結果になることを確かめている。利用者の手順は「アプリを起動して同意 → ツールを実行 → アプリを起動し直す」（docs/manual/setup.md 6.4）。**開発者用の `scripts/generate-tsumugi-expressions.ps1`（リポジトリ版）は同意の記録を確かめない**: リポジトリで作業する開発者は、External/README.md §5 の手順で素材を自分で入手し、本節と §3 の規約を確認したうえで扱う前提であり、出力先も `External/` やテスト用のデータルート（`-OutputDir` / `-DataRoot`）を使うことが多く、そこには `consent.json` が無いため。NFR-08 が対象にするアプリの利用者には、同意の確認がある zip 版のツールだけを案内する |
| 判断の根拠（配布しない） | 規約 7 項 / readme.txt 禁止事項の「二次配布、自作発言」。加工物（表情差分）であっても原本の表現をほぼそのまま含むため、これを配布物に同梱することは二次配布に当たりうる。**安全側に倒して同梱しない**（統括判断を仰がずに同梱する選択肢は採らない） |
| 判断の根拠（加工してよい） | 規約 5 項「加筆、加工できます。ただし、春日部つむぎと分からない・春日部つむぎではないキャラクターへの改変は禁止します。」/ readme.txt「加筆、加工できます。ただし、良識の範囲内で行ってください。」。本スクリプトが行う加工は **PSD にもとから入っている表情レイヤー（`!口` / `!目` / `!眉` / `!アクセサリー`）の表示切り替え、バストアップ範囲への切り出し（トリミング、#191 で追加）、等比縮小のみ**で、描き足し・色変更・変形（縦横比の変更）は行わない。切り出しの既定（`--crop bustup`）は頭頂から腰の上（ジャケットの裾）までで、顔・髪型・制服など春日部つむぎと分かる部分をすべて含むため、規約 5 項・readme.txt の範囲に収まる（`--crop full` で従来どおり全身のまま書き出すこともできる）。`--crop` に任意の範囲を指定した場合も、それはユーザー自身の環境での加工であり、規約 5 項（春日部つむぎと分からない・春日部つむぎではないキャラクターへの改変の禁止）と readme.txt（良識の範囲内）に沿う範囲で使うこと（生成物を配布しない点は既定と同じ）。切り出しは服装を変えないので、下の「服を脱がせた状態の禁止」の安全装置もそのまま効く。**ただしこの解釈には §12 項目 9 の制約がある**（レイヤー単位の利用への追加条件の有無を一次サイトで自動確認できていない） |
| 服を脱がせた状態の禁止（readme.txt） | **設定ファイルでは無効化できない安全装置**（PR #135 レビュー H1）として生成スクリプト本体に固定してある。(1) `layers` に書けるグループは `ALLOWED_EXPRESSION_GROUPS`（`!口` / `!目` / `!眉` / `!アクセサリー`）のホワイトリストに限る（`!体部分` / `制服` / `私服` は指定不可）。(2) `BUILTIN_FORBIDDEN_GROUPS`（`私服` が非表示）/ `BUILTIN_REQUIRED_VISIBLE_GROUPS`（`!体部分` / `制服` が表示）を書き出し直前に PSD の実状態で検証する。設定ファイルの `safety` は**追加**しかできず、組み込み分を削れない（`safety` を丸ごと省いても同じ検証が走る）。(3) 検証対象のグループが PSD に無い場合は「検証不能」として中止する。いずれも満たさなければ **1 枚も書き出さずに異常終了**（終了コード 4 = `EXIT_SAFETY`） |
| クレジット表記 | §3 と同じ（`春日部つむぎ立ち絵 (C) 春日部つくし` をクレジット画面に表示、`VOICEVOX:春日部つむぎ` と併記）。差分を作ったことによる追加の表記義務は規約上見当たらない |
| 未生成のとき | 状態専用の差分 →（誤答の瞬間・時間切れは不正解の差分、回答できる人がいないは時間切れ → 不正解の差分を挟んで）→ 待機の差分 → 従来の全身 PNG（`tsumugi_v2.png`）の順にフォールバックし、どれも無ければ立ち絵を表示しないまま進行する（`CharacterImagePaths.GetFileNameCandidates`、#212） |
| 切り出し範囲・大きさ（既定、#191 / #190） | PSD キャンバス（2037x4084）に対する比率 `0.22,0.01,0.90,0.45` = px で (448, 41)〜(1833, 1838)、1385x1797 を切り出し、高さ 1280px に等比縮小（987x1280）。範囲の根拠と `--max-height` の根拠は `scripts/generate_tsumugi_expressions.py` の `CROP_BUSTUP` / `DEFAULT_MAX_HEIGHT` のコメントと docs/architecture.md §10.2 |
| 使用するレイヤー（既定、#212 で更新） | 待機 = `*ω`（口）+ `*普通`（目）+ `*普通`（眉）/ 読み上げ中 = `*あ` + `*普通` + `*普通` / 回答権・自分 = `*わ` + `*見開く` + `*普通` + `！` / 回答権・他人 = `*お` + `*普通（横目）` + `*真面目` / 誤答の瞬間 = `*え` + `*瞳小` + `*困る` + `がーん` + `汗２` / 正解 = `*わあ！` + `*笑う` + `*普通` + `ほっぺ赤` / 不正解の確定 = `*綴じ　ギザギザ` + `*＞＜` + `*困る` + `汗１` / 時間切れ = `*綴じ　へ` + `*ジト目` + `*困る` + `汗１` + `どんより`（#86 の不正解の組み合わせ）/ 回答できる人がいない = `*う` + `*普通` + `*ん？` + `？`。いずれも `ホクロ`（既定表示）は維持。使うのは `!口` / `!目` / `!眉` / `!アクセサリー` の 4 グループだけで、`!体部分`・`制服`・`私服` には触れない（`*白目` / `*赤目` は表情として強すぎるため使っていない）。組み合わせはユーザーがプレビューで選んだ（2026-10-03） |
| 依存ライブラリ | Python の `psd-tools`（MIT）/ `Pillow`（MIT-CMU。Pillow の LICENSE 本文とインストール済みメタデータの `License-Expression` で確認、§15）。費用ゼロ・アカウント登録不要 |

> **注（実測、2026-09-18）**: zip 同梱の完成版 PNG `春日部つむぎ立ち絵_公式_v2.0.png` は **私服＋ウインク**の絵で、
> PSD の既定表示状態（**制服**）とは別の組み合わせになっている。本スクリプトは PSD の既定である制服で
> 表情差分を書き出すため、差分を生成した環境と、`tsumugi_v2.png` だけを置いた環境とでは服装が異なって見える。
> すべて生成すれば同一シリーズで揃うため実害は無いが、フォールバックが混在する状態
> （例: 待機だけ差分があり、他は `tsumugi_v2.png`）は避けたほうが見た目がよい。

---

## 4. 春日部つむぎロゴ（`tsumugi_logo.png`）

| 項目 | 内容 |
|---|---|
| 対象 | 同 zip 内 `春日部つむぎ立ち絵_公式_v2.0/tsumugi_logo.png` |
| ライセンス種別 | readme.txt に立ち絵素材と別建ての規約はなく、同じ zip 内に同梱されているため §3 の readme.txt の規約が適用されると解釈する（要手動確認: ロゴ専用の追加条件が別途あるかどうかは一次サイトで要確認） |
| 使用可否 | クレジット画面での小さなアイコン的表示程度であれば §3 と同様の判断ポイントが当てはまる。ロゴを商品化・グッズ化・アプリのメインアイコンとして単独で強く前面利用することは避け、あくまでクレジット表記に添える程度に留めるのが安全側 |
| 同梱すべきファイル | ロゴ原本は `External/` に留め、git 管理外。使う場合はクレジット画面用に加工した画像のみを `Assets/` に含める |

---

## 5. voicevox_core（音声合成コアライブラリ本体）

| 項目 | 内容 |
|---|---|
| 対象 | `External/voicevox_core/voicevox_core-windows-x64-0.17.0.zip` 内の `voicevox_core.dll` / `.lib` / `.h`（ビルド時に `Assets/Plugins/voicevox_core/x86_64/` へ配置スクリプトでコピー、git 管理外） |
| ライセンス種別 | MIT License |
| 根拠 URL | https://github.com/VOICEVOX/voicevox_core/blob/0.17.0/LICENSE （確認日 2026-09-12、WebFetch で全文確認済み）。zip 内 `LICENSE` ファイルの内容と一致することを確認済み |
| 著作権表示（原文どおり） | `Copyright (c) 2021 Hiroshiba Kazuyuki` |
| 補足（zip 内 README.txt、cp932 で再デコードして確認） | 「VOICEVOX CORE のソースコード及びビルド成果物のライセンスは MIT LICENSE です」「[Releases] にあるバージョン 0.16 未満のビルド済みのコアライブラリは別ライセンスなのでご注意ください」— 本プロジェクトが使う 0.17.0 は 0.16 以上のため、コアライブラリ本体は上記 MIT ライセンスがそのまま適用される（バージョン起因の別ライセンス問題は該当しない） |
| アプリのクレジット画面に載せる文言（確定版） | `VOICEVOX CORE (C) 2021 Hiroshiba Kazuyuki (MIT License)` |
| 同梱すべきファイル | zip 内 `LICENSE` の全文を `THIRD-PARTY-NOTICES.txt` に転記する |

---

## 6. 音声モデル（vvm ファイル）

| 項目 | 内容 |
|---|---|
| 対象 | 春日部つむぎの声を生成する音声モデル（`.vvm` ファイル。`Assets/StreamingAssets/voicevox_core/models/` に配置予定、git 管理外） |
| ライセンス種別 | VOICEVOX 音声モデル向けの独自利用規約（OSS ライセンスではない） |
| モデルバージョン | **0.16.4**（公式ダウンローダー実行ログ `Logs/voicevox-download.log` の `ダウンロードモデルタグ: 0.16.4` で確認。0.17.0 はプレリリースのため選ばれなかった。`TERMS.txt` / `README.txt` 自体にはバージョン番号の記載はない） |
| 根拠 URL | https://github.com/VOICEVOX/voicevox_vvm （リポジトリ説明、確認日 2026-09-12、WebFetch で確認）。原文は `External/voicevox_core/models/TERMS.txt`（377 行、2026-09-13 公式ダウンローダーで取得、git 管理外）で実物を確認済み |
| `TERMS.txt` 原文（冒頭の一般規約、原文どおり転記） | `# VOICEVOX 音声モデル 利用規約`<br>`## 許諾内容`<br>`1. 商用・非商用問わず利用することができます`<br>`2. アプリケーションに組み込んで再配布することができます`<br>`3. 作成された音声を利用する際は、各音声ライブラリの規約に従ってください`<br>`4. 作成された音声の利用を他者に許諾する際は、当該他者に対し本許諾内容の 3 及び 4 の遵守を義務付けてください`<br>`## 禁止事項`<br>`- 逆コンパイル・リバースエンジニアリング及びこれらの方法の公開すること`<br>`- 製作者または第三者に不利益をもたらす行為`<br>`- 公序良俗に反する行為`<br>`## 免責事項`<br>`本ソフトウェアにより生じた損害・不利益について、製作者は一切の責任を負いません。`<br>`## その他`<br>`ご利用の際は VOICEVOX を利用したことがわかるクレジット表記が必要です。` |
| **「配布 zip 同梱」と「初回起動時ダウンロード」のどちらが規約上安全か** | 上記の通り「アプリケーションに組み込んで再配布することができます」と明記されているため、**配布 zip に vvm ファイルを同梱する方式は規約上問題ない**。初回起動時ダウンロード方式も禁止されてはいないが、同梱の方が確実に規約の文言に合致し、実装も単純（ネットワーク不安定時の失敗を考慮しなくてよい）。よって配布 zip 同梱を推奨する（#34 で `scripts/package-release.ps1` により配布 zip 同梱に確定。dev-workflow.md §8 参照） |
| 個別キャラクターの追加制限 | vvm の一般規約に加えて、話者（春日部つむぎ）固有の利用規約（§2）が重畳して適用される。春日部つむぎ自体は商用・非商用とも利用可の話者のため、追加の制限は確認されていない |
| アプリのクレジット画面に載せる文言（確定版） | 音声モデル単体の個別クレジットは不要（§1・§2 のクレジットに包含される） |
| 同梱すべきファイル | vvm ファイル自体（バイナリ）は `External/` 由来のまま配布 zip に含める。規約書（TERMS.txt 相当）へのリンクを `THIRD-PARTY-NOTICES.txt` に記載する |

---

## 7. ONNX Runtime / VOICEVOX 版 ONNX Runtime ビルド

| 項目 | 内容 |
|---|---|
| 対象 | 音声合成の推論に使う ONNX Runtime（`voicevox_onnxruntime.dll`。`Assets/Plugins/voicevox_core/x86_64/` に配置予定、git 管理外） |
| バージョン | **1.17.3**（`External/voicevox_core/onnxruntime/VERSION_NUMBER` を実際に読んで確認。git コミット `a88d4e62f033fe34f7b6746871b7a2eadbe76acb`、`GIT_COMMIT_ID` より） |
| ライセンス種別 | MIT License（本家 / VOICEVOX ビルド版とも） |
| 根拠 URL（本家） | https://github.com/microsoft/onnxruntime/blob/main/LICENSE （確認日 2026-09-12、WebFetch で確認。著作権表示 `Copyright (c) Microsoft Corporation`） |
| 根拠 URL（VOICEVOX 版ビルダー） | https://github.com/VOICEVOX/onnxruntime-builder/blob/main/LICENSE （確認日 2026-09-12、WebFetch で確認。著作権表示 `Copyright (c) 2021 VOICEVOX`） |
| VOICEVOX ONNX Runtime 利用規約（確認済み） | 2026-09-13 に公式ダウンローダーを実行し、`External/voicevox_core/onnxruntime/TERMS.txt`（22 行、git 管理外）として実物を取得・全文確認済み。原文（原文どおり転記）:<br>`# VOICEVOX ONNX Runtime 利用規約`<br>`## 許諾内容`<br>`1. 商用・非商用問わず利用することができます`<br>`2. アプリケーションに組み込んで再配布することができます`<br>`3. 作成された音声を利用する際は、各音声ライブラリの規約に従ってください`<br>`4. 作成された音声の利用を他者に許諾する際は、当該他者に対し本許諾内容の 3 及び 4 の遵守を義務付けてください`<br>`## 禁止事項`<br>`- 逆コンパイル・リバースエンジニアリング及びこれらの方法の公開すること`<br>`- 製作者または第三者に不利益をもたらす行為`<br>`- 公序良俗に反する行為`<br>`## 免責事項`<br>`本ソフトウェアにより生じた損害・不利益について、製作者は一切の責任を負いません。`<br>`## その他`<br>`ご利用の際は VOICEVOX を利用したことがわかるクレジット表記が必要です。` |
| third-party-notices.html | ダウンローダーが `External/voicevox_core/onnxruntime/third-party-notices.html`（420,920 B、7,611 行、git 管理外）を同梱していることを確認済み。ONNX Runtime が依存する第三者コンポーネントの notices が含まれるため、**配布 zip の `THIRD-PARTY-NOTICES.txt` にこのファイルの内容（または同梱）を含めること** |
| アプリのクレジット画面に載せる文言（確定版） | `ONNX Runtime (C) Microsoft Corporation (MIT License)` / `voicevox_onnxruntime (C) 2021 VOICEVOX (MIT License)` |
| 同梱すべきファイル | 両 LICENSE の全文、VOICEVOX ONNX Runtime 利用規約の原文、および `third-party-notices.html` の内容を `THIRD-PARTY-NOTICES.txt` に転記・同梱する |

---

## 8. Open JTalk（形態素解析・読み推定エンジン、voicevox_core が内部で使用）

| 項目 | 内容 |
|---|---|
| 対象 | Open JTalk 本体（voicevox_core の依存として内部利用。単体の DLL 等をこのプロジェクトが直接配置するわけではないが、辞書は別途配置） |
| ライセンス種別 | Modified BSD License |
| 根拠 URL | http://open-jtalk.sourceforge.net/ （確認日 2026-09-12、WebFetch で確認。「This software is released under the Modified BSD license.」の記載を確認） |
| アプリのクレジット画面に載せる文言（確定版） | `Open JTalk (Modified BSD License)` |
| 同梱すべきファイル | 上記 URL、および §9 の辞書 COPYING を `THIRD-PARTY-NOTICES.txt` に記載 |

### 8-1. Open JTalk 辞書（`open_jtalk_dic_utf_8-1.11`）

| 項目 | 内容 |
|---|---|
| 対象 | `External/voicevox_core/open_jtalk_dic_utf_8-1.11.tar.gz`。展開先は `Assets/StreamingAssets/voicevox_core/open_jtalk_dic_utf_8-1.11/`（git 管理外、展開物を Assets に事前コミットしない） |
| ライセンス種別 | BSD 系（3 者の著作権表示が重畳） |
| 根拠 | tar.gz 内 `COPYING` を `tar -xzOf` で実際に展開して全文確認済み（確認日 2026-09-12） |
| 著作権表示（原文どおり、3 件） | `Copyright (c) 2009, Nara Institute of Science and Technology, Japan.` / `Copyright (c) 2011-2017, The UniDic Consortium` / `Copyright (c) 2008-2016 Nagoya Institute of Technology Department of Computer Science`（Open JTalk 本体開発元 HTS Working Group） |
| アプリのクレジット画面に載せる文言（確定版） | `open_jtalk_dic_utf_8 (C) Nara Institute of Science and Technology / The UniDic Consortium / Nagoya Institute of Technology (Modified BSD License)` |
| 同梱すべきファイル | `COPYING` の全文を `THIRD-PARTY-NOTICES.txt` に転記する |

---

## 9. Unity パッケージ（Unity Companion License）

以下はいずれも Unity 提供パッケージで、`Packages/manifest.json` に記載の実バージョンで URL を確認した（確認日 2026-09-12、WebFetch でページの存在とライセンス種別を確認）。いずれも **Unity Companion License**（Unity 製品と組み合わせて使う限り無償で使えるライセンス。根拠: https://unity.com/legal/licenses/unity-companion-license 、確認日 2026-09-12）。

| 対象パッケージ | バージョン | LICENSE URL |
|---|---|---|
| Netcode for GameObjects (`com.unity.netcode.gameobjects`) | 2.13.2 | https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/license/LICENSE.html |
| Unity Transport (`com.unity.transport`) | 6.6.0 | https://docs.unity3d.com/Packages/com.unity.transport@6.6/license/LICENSE.html |
| Newtonsoft Json for Unity (`com.unity.nuget.newtonsoft-json`) | 3.2.2 | https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/license/LICENSE.html |
| Input System (`com.unity.inputsystem`) | 1.20.0 | https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/license/LICENSE.html |
| Universal Render Pipeline (`com.unity.render-pipelines.universal`) | 17.6.0 | https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.6/license/LICENSE.html |
| Test Framework (`com.unity.test-framework`) | 1.8.0 | https://docs.unity3d.com/Packages/com.unity.test-framework@1.8/license/LICENSE.html |

- アプリのクレジット画面に載せる文言（確定版）: `Unity と各パッケージ (C) Unity Technologies (Unity Companion License)`
- 同梱すべきファイル: 上記 6 URL を `THIRD-PARTY-NOTICES.txt` に列挙する（Unity Companion License は条項が長いため全文転記ではなく URL 参照でよい。他社 OSS の MIT/BSD は全文転記する方針との差異に注意）

### 9.1 Unity Transport 6.6.0 の埋め込みと改変（issue #163、2026-09-30）

**Unity Transport（`com.unity.transport`）6.6.0 だけは、builtin のものをそのまま使わず、改変したものを `Packages/com.unity.transport/` に埋め込んで git 管理している**（Windows で、切断通知なしに消えたクライアントがいると、ホストの受信バッファが枯渇する不具合の修正。詳細は `Packages/com.unity.transport/TSUMUGI-PATCH.md`）。

| 項目 | 内容 |
|---|---|
| パッケージ | `com.unity.transport` 6.6.0（Unity 6000.6.0f1 同梱の builtin、`_fingerprint: 1839533e4eb391bf730cdbb7b64ed590a479e2d9`） |
| 埋め込み先 | `Packages/com.unity.transport/`（299 ファイル・約 2.0MB。`.dll` などのバイナリは含まない）。`Packages/packages-lock.json` の解決結果は `"source": "embedded"`（2026-09-30 実測） |
| 改変箇所 | `Runtime/UDPNetworkInterface.cs` の `ReceiveJob.Execute`、2 か所（受信を読み飛ばす `continue` の前に `ReceiveQueue.ReleaseBuffer(bufferIndex);` を追加）。1 か所目は Unity スタッフが正しいと認めた修正（https://discussions.unity.com/t/unity-transport-6-6-0-udp-host-stops-receiving-after-abrupt-client-disconnect-on-windows/1736023 、2026-09-09 回答）、2 か所目は同じパターンに対する当方の判断 |
| ライセンス | Unity Companion License（**GPL / LGPL / AGPL 系ではない**。本書 §12「ライセンス互換の判定基準」で「可」の区分） |

**`Packages/com.unity.transport/LICENSE.md`（原本のまま同梱）の全文**:

> Unity Transport copyright © 2023 Unity Technologies
>
> Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).
>
> Unless expressly provided otherwise, the Software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.

**Unity Companion License（v1.4、2024-10-29。https://unity.com/legal/licenses/unity-companion-license 、2026-09-30 に WebFetch で確認。全文は `Assets/TsumugiQuiz/Resources/Licenses/unity-companion-license.txt` に同梱済み）の該当条項と、本プロジェクトでの扱い**:

- **許諾範囲**: 「reproduce, prepare derivative works of, publicly display, publicly perform, and distribute the work」の許諾がある。ただし「in connection with the authoring and/or distribution of applications, software, or other content under a valid Unity content authoring and rendering engine software license」の場合に限る。本プロジェクトは、Unity（Personal）で作成・配布するアプリの一部として使うので、この範囲内。**改変（derivative works）も許諾されている**
- **表示義務**: 「This License, including the copyright notice associated with the Work, must be provided in all substantial portions of the Work and derivative works thereof.」
  - ソース（リポジトリ）: 埋め込みフォルダに原本の `LICENSE.md` をそのまま残しているので満たす
  - 配布物（ビルド）: UCL 全文（`unity-companion-license.txt`）と、Unity パッケージの一覧（`unity-packages-notices.txt`。Unity Transport 6.6.0 と LICENSE URL を記載）は、すでに Credits 画面（`CreditsLicenseCatalog`）から表示している（本書 §12.1）。今回、`unity-packages-notices.txt` の Unity Transport の行に「改変版を同梱している」旨の注記を加えた。**Resources/Terms（同意フロー用の規約）への追加は不要**。UCL は利用者に同意を求める規約ではなく、表示（notice）の義務だけなので、表示は Credits 側で満たす
- **権利の帰属**（§3.1〜3.3）: 「Unity will own all right, title, and interest to all intellectual property rights ... in the Work and derivative works of the Work」（§3.2）。改変した 2 行を含む派生物の権利は Unity に帰属する（当方は §3.3 により引き続き使える）。§3.1 は、ここで言う派生物に「games, apps, or content that you create with the Work」は含まれず「You keep all right, title, and interest in your own content」と定めているので、本アプリ本体のコード・コンテンツには及ばない
- **第三者の通知**（§5 後段）: Work に Unity の「third-party notices」ファイルが付いていれば、その条件に従う。本パッケージ（`Packages/com.unity.transport/`）には該当ファイルが無い（2026-09-30 確認）
- 条文の引用は、同梱の `unity-companion-license.txt`（v1.4）と突き合わせて確認した

**埋め込みを外す条件**: エンジン側に同じ修正が入り、ユーザーがエディタの更新を承認したとき（手順は `TSUMUGI-PATCH.md` の末尾。フォローアップ issue で追跡する）。

---

### 9.2 Unity テンプレート由来のファイル（com.unity.template.universal-2d）

本プロジェクトは、Unity 6.6.0f1 に同梱の Universal 2D テンプレートから非公開の開発リポジトリの初期化時に作った。テンプレート由来のファイルは Unity Companion License の対象で、リポジトリの MIT License（§17）の対象外である。

| 項目 | 内容 |
|---|---|
| 出所（一次情報） | Unity エディタ同梱のテンプレート `<Unity エディタ>\Data\Resources\PackageManager\ProjectTemplates\com.unity.template.2d-cross-platform-2d-7.0.0.tgz`（tgz を `tar -tzf` で一覧して確認）。ファイル名は `com.unity.template.2d-cross-platform-2d-7.0.0` だが、中の `package/package.json` は `name: com.unity.template.universal-2d`、`displayName: Universal 2D`、`version: 7.0.0` である |
| テンプレートの著作権表示（`package/LICENSE.md` の冒頭、抜粋） | `com.unity.template.universal-2d copyright © 2021 Unity Technologies` / `Licensed under the Unity Companion License for Unity-dependent projects (see https://unity3d.com/legal/licenses/unity_companion_license).`（続く免責の段落は省略） |
| ライセンス | Unity Companion License（全文は `Assets/TsumugiQuiz/Resources/Licenses/unity-companion-license.txt`） |
| 対象（ディレクトリ・ファイル単位） | `Assets/Settings/` 全体、`ProjectSettings/` 全体、`Packages/manifest.json`、`Packages/packages-lock.json`。tgz の `package/ProjectData~/` に同名の初期ファイル（`Assets/Settings/` の 7 ファイル、`ProjectSettings/` の各 `.asset`、`Packages/manifest.json`、`Packages/packages-lock.json`）があり、本リポジトリのものはその初期内容とそれに加えた変更である。`ProjectSettings/` のうち `ProjectAuditorSettings.asset`・`ProjectVersion.txt`・`SceneTemplateSettings.json` はテンプレートの tgz に無いが、Unity の設定ファイルとして同じ扱いにまとめる |
| 対象外（MIT 側） | Unity のパッケージが生成した `Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss` と `Assets/DefaultNetworkPrefabs.asset` は、テンプレートの `ProjectData~/` に無いため MIT 側に含める（案 a）。`.tss` は Unity 内蔵の既定テーマを参照する 1 行の `@import url("unity-theme://default");` だけで、テーマ本体は含まない |

## 10. UPnP ライブラリ（Mono.Nat 採用確定）

K11（setup-brief.md、2026-09-13 改訂）により **Mono.Nat 3.0.4 を採用として確定**した（`netstandard2.1` ビルドあり、UPnP + NAT-PMP 両対応。詳細は docs/network-nat.md §1.1）。

| 候補 | 採否 | ライセンス種別 | 根拠 URL |
|---|---|---|---|
| Mono.Nat | **採用** | MIT License | https://github.com/alanmcgovern/Mono.Nat/blob/master/LICENSE.md （確認日 2026-09-12、GitHub API でライセンス種別 MIT を確認、WebFetch で著作権表示を確認: `Copyright (C) 2006 Alan McGovern` / `Copyright (C) 2007 Ben Motmans` / `Copyright (C) 2013 Nicholas Terry`） |
| Open.NAT | 不採用（アーカイブ済み） | MIT License | https://github.com/lontivero/Open.NAT/blob/master/LICENSE （確認日 2026-09-12、GitHub API でライセンス種別 MIT を確認。リポジトリが `archived=true` のため今後の修正が入らず、かつ netstandard2.1 ビルドを持たないため不採用） |

- アプリのクレジット画面に載せる文言（確定版）: `Mono.Nat (C) 2006 Alan McGovern, 2007 Ben Motmans, 2013 Nicholas Terry (MIT License)`
- 同梱すべきファイル: `Mono.Nat` の `LICENSE.md` 全文を `THIRD-PARTY-NOTICES.txt` に転記する。`Assets/Plugins/Mono.Nat/` に DLL を置く場合も `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md` として同じライセンスファイルを隣に同梱する（docs/network-nat.md §1.1〜§1.2）
- Open.NAT の行は参考として残す（採否判断の根拠を追跡できるようにするため削除しない）

### 10.1 同梱した DLL の出典（#3 で実施）

| 項目 | 値 |
|---|---|
| 取得元 | <https://www.nuget.org/packages/Mono.Nat/3.0.4>（実 URL: `https://www.nuget.org/api/v2/package/Mono.Nat/3.0.4`、取得日 2026-09-13） |
| バージョン | 3.0.4（nuspec の `<version>3.0.4</version>`、`repository commit=48ef888146d15fa6f93e5f41dbbe55929dcdec54`） |
| nupkg の SHA-256 | `35d3aab94d8d68ab42bf4a75f9e63a6b08ce070c337fdd82c002780e0cf6fb83` |
| 取り出したファイル | `lib/netstandard2.1/Mono.Nat.dll`（68,608 バイト） |
| **DLL の SHA-256** | `b48c6a8c89d9b283e1ea8b0b725c9c430594f97f408c1df4851f206a6467f549` |
| アセンブリ識別子 | `Mono.Nat, Version=3.0.0.0, Culture=neutral, PublicKeyToken=6c9468a3c21bc6d1` |
| 配置先 | `Assets/Plugins/Mono.Nat/Mono.Nat.dll` |
| 同梱ライセンス | `Assets/Plugins/Mono.Nat/LICENSE.md`（nupkg 同梱の原本をコピー。本リポジトリの `.gitattributes` により改行のみ LF に正規化。本文は無改変）と `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md`（同一内容） |
| nuspec の `license` | `type="file"` / `LICENSE.md`（= 同梱した全文が正式なライセンス表記） |

- Plugin Inspector の設定は **Any Platform = 有効**（= Editor とビルド対象の Windows Standalone の両方で使える）。
  docs/network-nat.md §1.2 では「Standalone / Windows / x86_64 のみ」としていたが、
  Editor を除外すると Editor 側のアセンブリが `Mono.Nat` を参照できずコンパイルできない
  （EditMode / PlayMode テストが走らない）ため Any Platform とした。
  ビルドターゲットが Windows Standalone のみなので、実質的な同梱先は変わらない。
- `Auto Reference` は **無効**（`isExplicitlyReferenced: 1`）にし、`TsumugiQuiz.Network` /
  `TsumugiQuiz.Tests.*` の asmdef の `precompiledReferences` で明示参照する
  （Core など他の層から誤って参照されないようにするため）。

---

## 11. Unity 本体（Unity Personal での配布に必要な表記）

| 項目 | 内容 |
|---|---|
| ライセンス | Unity Personal エディションのライセンス条件（Unity 利用規約 https://unity.com/legal/terms-of-service 。本節の主な一次ソースは Unity Editor Software Terms https://unity.com/legal/editor-terms-of-service/software 、下記「追加確認（issue #36）」参照） |
| 確認できたこと | Unity 6 のプレイヤー設定ドキュメント（https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsSplashScreen.html 、確認日 2026-09-12、WebFetch で確認）には「Splash Image 設定でロゴの表示/非表示・独自ロゴの追加・スプラッシュ画面自体の無効化が可能」という一般的な設定方法が記載されている |
| 旧記載（#36 で更新。矛盾を避けるため見出しのみ変更し、本文はそのまま残す） | Unity Personal ライセンス固有の「スプラッシュ表示義務」の正式な条項文言は Terms of Service 側の一般ページからは確認できなかった。Unity 6 で Personal エディションでも Unity ロゴの非表示が可能になったという情報があるが、これは検索結果の要約であり一次ドキュメントで文言を確認できていない。**配布前に Unity Hub / Project Settings 上のライセンス種別表示、および最新の Unity 利用規約で Personal エディションのスプラッシュ画面義務を再確認すること**。→ 下段の「追加確認（issue #36）」を参照 |
| 方針（暫定） | 安全側として、Unity のスプラッシュ画面（Unity ロゴ）はデフォルト設定のまま無効化せずに残す。将来 Pro 等にアップグレードしない限り、義務がある前提で運用する |
| **実測値（issue #34、2026-09-14）** | `ProjectSettings/ProjectSettings.asset` を実際に確認。`m_ShowUnitySplashScreen: 1`（スプラッシュ画面自体は有効）、`m_ShowUnitySplashLogo: 1`（Unity ロゴを表示）、`m_SplashScreenLogos: []`（追加のカスタムロゴは未設定）。デフォルトから変更していないことを確認済み。本 issue では上記方針に従い、この設定を変更していない |
| **追加確認（issue #36、2026-09-17。状態: 一部確認・引き続き要手動確認）** | 確認できたのは、Unity Editor Software Terms（https://unity.com/legal/editor-terms-of-service/software 、確認日 2026-09-17、WebFetch で全文確認）という**個別の1文書についてのみ**である。同ページ冒頭に 2026 年 6 月 30 日最終更新と明記されており、変更点として同ページに記載されているのは主に用語定義（"Platform" / "Designated User" / "User Modification"）の整理と、非対応プラットフォーム向け・Enterprise ティアに関する追記であり、splash screen 条項の新設・削除についての記載はない（推測ではなく、WebFetch で得られた更新履歴の要約に基づく）。この文書の本文には **"splash screen" という語自体が出現しない**ことを確認したが、Unity の規約群は本文書以外にも複数（Terms of Service 本体、Unity Personal 固有の条件等）に分かれている可能性があり、**本確認はあくまで当該1文書の範囲に留まる。「Unity Personal にスプラッシュ画面表示義務が無い」ことを確定したものではなく、他の文書に同義の条項がないかは引き続き要手動確認である**。Unity 6 のプレイヤー設定マニュアル（https://docs.unity3d.com/6000.6/Documentation/Manual/class-PlayerSettingsSplashScreen.html 、確認日 2026-09-17）にも "Disable this option to remove the Unity logo from the splash screen." と、無償プランに限定しない一般的な設定手順として記載されているのみで、ライセンス種別による制限の記載はない |
| **Section 2.12「Proprietary Notices and Attribution」該当部分（抜粋。全文ではない）** | 原文の該当部分（抜粋）。入れ子の引用符 “ ” と ’ は Markdown 表示上の可読性のため ' に置き換えている。それ以外は原文ママ: `"You agree to retain the copyright, trademark, and other proprietary notices and disclaimers of Unity as they appear in the Offerings or documentation. If a Project you develop using Unity Software includes credits and attributions, you must display the following notice (replacing xxxx with the current year): '[Project name] was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies' 'Copyright © 2005-xxxx Unity Technologies. All rights reserved.' No other license or right is granted under these Software Terms in the trademarks, service marks, trade names, and logos associated with Unity, Unity's Offerings, or other intellectual property, including the Unity Software (the 'Unity Trademarks'). All use of the Unity Trademarks must comply with Unity's Trademark Guidelines."` — 出典は上記 Unity Editor Software Terms（Section 2.12）。**レビュー H-1（issue #100、2026-09-17 訂正）**: 原文中の "Unity's Trademark Guidelines" のハイパーリンク先は、curl で取得したページ HTML の Next.js RSC ペイロード（`self.__next_f.push` チャンク）内の当該テキストに対応する `markDefs` の `href` を実際に確認したところ **https://unity.com/legal/branding-trademarks**（ページタイトル "Guidelines for Using Unity Trademarks"、本文冒頭に "v. 4.2" と明記。以下「Trademark Guidelines」）であった。旧版の本ファイルはこれを誤って https://unity.com/legal/trademarks（後述の「Unity Trademark List」＝商標一覧ページ。Trademark Guidelines 本文中で "the Trademarks listed here ('Unity Trademark List')" として個別にリンクされている別ページ）としていたため訂正する。すなわちこの条項は「プロジェクトにクレジット・attribution 表示がある場合は定型文を表示しなければならない」という義務であり、スプラッシュ画面義務ではなく **クレジット画面での attribution 表記義務**である。本アプリは `CreditsView` を実装済み（クレジット・attribution を表示する構成）のため、この条項の対象になると解釈するのが安全側 |
| **未解決点・フォローアップ issue** | ~~上記 Section 2.12 の定型文は、現時点の `CreditsLicenseCatalog.cs` / `CreditsView` には含まれていない~~ → **issue #100（2026-09-17、レビュー指摘を反映して 2026-09-17 に再修正）で解消済み**。`CreditsLicenseCatalog.BuildUnityAttributionCreditLine()` が `Application.productName` と `DateTime.Now.Year`（レビュー L-1: 配布ビルドはユーザーのローカル PC で動くため UTC ではなくローカル時刻を使う）を差し込んで、Section 2.12 の定型文と下記 Trademark Notice and Attribution Statement を生成し、「3. OSS」節の `unity-packages` エントリのクレジット行冒頭に表示する（§12.1 参照）。`Resources/Licenses/unity-packages-notices.txt` にも同文言を追加済み（静的ファイルのため年は本ファイル更新時点の値。`scripts/gen-third-party-notices.ps1` が THIRD-PARTY-NOTICES.txt への連結時に現在年へ自動置換する。§12.1 参照） |
| **追加確認（issue #100、2026-09-17。初回レビューの指摘を受けて同日中に再確認・訂正）** | Section 2.12（上記抜粋）を WebFetch で再確認したが、2026-09-17 時点で記載内容・最終更新日（2026年6月30日）に変化はなかった。**Trademark Guidelines（https://unity.com/legal/branding-trademarks 、v.4.2）** を curl で取得した HTML から Next.js RSC ペイロードを抽出し、本文全文（約200ブロック）を確認した（確認日 2026-09-17）。要点: (1) **"Permitted uses: Wordmarks"** — ロゴ画像ではなく「Unity」という語（Wordmark）を published materials で使う場合、a) 主題に関連していること、b) 自社の商標より目立たせないこと、c) 後述の Trademark Notice and Attribution Statement を提示すること、の3条件を満たせば許諾は不要。(2) **"Permitted uses: Logos/brand assets"** — ロゴ画像を使う場合は別の条件（現行・未改変のロゴを使う、視認性を損なわない、単なる装飾に使わない、プロモーションの主役にしない等）があり、一覧に載っていないロゴの使用や個別の状況は `trademarks@unity3d.com` へのメールで**事前許諾**を得る必要がある。(3) 上記 (1) の Wordmark 条項が要求する **"TRADEMARK NOTICE AND ATTRIBUTION STATEMENT"** の定型（原文）: `"[This website/these materials/(product/service or company name)] [is/are] not sponsored by or affiliated with Unity Technologies or its affiliates. [Unity Trademark(s)] [is a/are] trademark[s] or registered trademark[s] of Unity Technologies or its affiliates in the U.S. and elsewhere."`。本アプリはロゴ画像を一切使わずテキスト（Wordmark）のみで対応する方針のため、Logo 側の事前許諾・サイズ規定は適用対象外と判断し、Wordmark 条項の3条件（クレジット画面という主題との関連性・自社ロゴ等より目立たせない配置・Notice の提示）を満たす実装とした。**初回レビュー時の記載（"テキスト表記のみの場合とロゴを使う場合を区別する規定は見当たらなかった"）は、Trademark Guidelines 本文ではなく別ページの Trademark List（商標一覧）のみを確認して書いた誤りであり、本項で訂正する** |
| **実装（issue #100、2026-09-17）** | `CreditsLicenseCatalog.BuildUnityAttributionCreditLine()` が、Section 2.12 の定型文（"[productName] was made with Unity®. ..." / "Copyright © 2005-[現在年] Unity Technologies. All rights reserved."）に続けて、上記 Trademark Notice and Attribution Statement を `productName` を当てはめた形（"[productName] is not sponsored by or affiliated with Unity Technologies or its affiliates. Unity is a trademark or registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere."）で生成し、`unity-packages` エントリのクレジット行に表示する。出典リンクとして Unity Editor Software Terms（Section 2.12）・Unity's Trademark Guidelines（branding-trademarks）・Unity Trademark List（trademarks）の3件を掲載する |

---

## 12. 方針節

### クレジット画面の構成（順序）
1. キャラクター（春日部つむぎ本体・立ち絵・ロゴ）
2. 音声（VOICEVOX / voicevox_core / vvm / ONNX Runtime / Open JTalk・辞書）
3. OSS（Unity パッケージ群、UPnP ライブラリ）

### 配布物への同梱
- 配布 zip には `THIRD-PARTY-NOTICES.txt` を同梱し、本ファイルの「同梱すべきファイル」列にある内容（LICENSE 全文または URL 参照）をすべて含める
- 新しい OSS・素材を追加する PR は、本ファイル（`docs/licenses.md`）と `THIRD-PARTY-NOTICES.txt` への追記を必須とする（reviewer エージェントのレビュー観点にも含まれる）

### 12.1 CreditsView の実装（issue #33、2026-09-13 追加）

`Assets/TsumugiQuiz/UI/Views/credits-view.uxml` + `Assets/TsumugiQuiz/Scripts/UI/Views/CreditsView.cs`
（`TsumugiQuiz.UI.Views` 名前空間）として実装した。`ViewNames.Credits` は本 issue でプレースホルダから
実 View に差し替わった（`DefaultViewControllerRegistrations` / `UiToolkitBootstrap` の `_viewDefinitions`
を更新し、`-executeMethod TsumugiQuiz.Editor.Setup.UiToolkitBootstrap.RunAndExit` で Main シーンへ反映済み）。

画面構成（上から下、ScrollView 内。§12 冒頭の順序どおり）:
1. **1. キャラクター** — 春日部つむぎ（音声・立ち絵）のクレジット文言、VOICEVOX/春日部つむぎ利用規約ページへのリンクボタン
2. **2. 音声** — VOICEVOX CORE / ONNX Runtime / voicevox_onnxruntime / ONNX Runtime 同梱コンポーネント通知 / Open JTalk・辞書。各項目にクレジット行 + 出典リンク + ライセンス全文の `Foldout`（既定は折りたたみ、初回展開時まで本文を生成しない遅延生成。展開後は内側に `max-height` 付きの `ScrollView` を持つ）
3. **3. OSS** — Unity パッケージ群（URL 参照）/ Unity Companion License 全文 / Mono.Nat / Noto Sans JP。各項目も同様にクレジット行 + 出典リンク + ライセンス全文の `Foldout`
4. **同意状況** — `ConsentStore.LoadRecords()`（issue #37）を用いて規約ごとの同意日時・ハッシュ先頭8桁・アプリバージョンを表示。文言は `TermsView` の同意済み表示と揃えている。「利用規約の確認・撤回」ボタン1つのみを置き、`ViewNames.Terms` へ遷移させる。**同意の撤回はこの画面では行わず、architecture.md の設計どおり `TermsView` の責務のままとする**（レビュー H-1。当初案では Credits 画面上で即座に撤回する実装だったが、統括判断によりレビュー後に修正した）
5. **アプリ情報** — `Application.version` と、ビルド番号を「バージョン: {Application.version}（ビルド番号 N）」の形で表示する（issue #204。N は `LocalBuildIdentity.DisplayNumber`。ビルドの違う相手を拒否したときの「バージョンが異なります（ホスト: x / あなた: y）。」の番号と同じもので、Editor では 0。docs/network.md §2.3「バージョンとビルドの一致」）

ONNX Runtime の third-party-notices（約400KB）のように画面に全文を載せると実用的でないほど長い項目は、
Foldout に要約文だけを表示し、「全文は THIRD-PARTY-NOTICES.txt を参照」と案内する
（`CreditsLicenseCatalog.Entry.ScreenSummary`。全文は同梱ファイル側で担保する。レビュー H-3）。

各項目のメタデータ（表示名・確定クレジット文言・出典 URL・Resources 上のパス）は
`Assets/TsumugiQuiz/Scripts/UI/Credits/CreditsLicenseCatalog.cs`（`TsumugiQuiz.UI.Credits` 名前空間）
に集約した（`TermsCatalog` と同じ設計）。§3（立ち絵クレジット表記）・§4（ロゴ専用条件）・§11（Unity
Personal のスプラッシュ画面義務）の未確認事項は、該当エントリの直上に TODO コメントとして明示している。

**issue #100（2026-09-17。レビュー指摘を受けて同日中に修正）**: §11 で追加確認した Unity Editor
Software Terms Section 2.12 の帰属定型文（"[Project name] was made with Unity®. ..." /
"Copyright © 2005-xxxx Unity Technologies. All rights reserved."）と、Unity's Trademark
Guidelines（https://unity.com/legal/branding-trademarks 、v.4.2）の
"TRADEMARK NOTICE AND ATTRIBUTION STATEMENT"（"[product name] is not sponsored by or
affiliated with Unity Technologies or its affiliates. [Unity Trademark(s)] is a trademark or
registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere." の
定型に productName を当てはめたもの）を、`CreditsLicenseCatalog.BuildUnityAttributionCreditLine()`
（`Application.productName` と `DateTime.Now.Year`（レビュー L-1: ローカル時刻を使用）を実行時に
差し込む internal メソッド）で生成し、「3. OSS」節の `unity-packages` エントリのクレジット行の
先頭に追加した（既存の「Unity と各パッケージ (C) Unity Technologies (Unity Companion License)」の
前に空行区切りで表示）。出典リンクとして Unity Editor Software Terms（Section 2.12）・
Unity's Trademark Guidelines（branding-trademarks）・Unity Trademark List（trademarks）の
3件を追加している（レビュー H-1: 当初 "Unity Trademark Guidelines" として記載していた URL が
実際には商標一覧ページ（Trademark List）であったため、正しい Guidelines の URL を追加し、
商標一覧は別リンクとして残した）。ロゴ画像は使用せずテキスト（Wordmark）のみで対応した。
Wordmark を published materials で使う場合の3条件（主題との関連性・自社商標より目立たせない・
Trademark Notice and Attribution Statement の提示）を満たす実装であり、Logo 側の事前許諾・
サイズ規定は適用対象外である（§11 追加確認欄に詳細）。
`Resources/Licenses/unity-packages-notices.txt` にも同じ定型文を静的テキストとして追加したが、
こちらは実行時計算ではなく年が固定値（追加時点の 2026 年）のため、`scripts/gen-third-party-notices.ps1`
が配布物 THIRD-PARTY-NOTICES.txt への連結時に現在年へ自動置換する（レビュー M-3。元ファイルは
編集しない）。`scripts/package-release.ps1` は生成後の THIRD-PARTY-NOTICES.txt を検査し、
記載の年が生成時点の現在年と一致しなければビルドを失敗させる（同レビュー M-3）。
開発者向けの運用手順（年更新のタイミング等）は notices ファイル自体には書かず、
本ドキュメントと dev-workflow.md §8.3 にのみ記載する（レビュー M-4）。

同梱ライセンス全文（`Assets/TsumugiQuiz/Resources/Licenses/*.txt`。各ファイル先頭に出典 URL・取得日を明記）。
**下表の順序は `scripts/gen-third-party-notices.ps1` の `$licenseFileOrder`（`THIRD-PARTY-NOTICES.txt` に
実際に連結される順序）と一致させてある。この配列が正であり、本表はそれに追従する（レビュー M-6）**:

| ファイル | 内容 | 出典 |
|---|---|---|
| `tsumugi-character-credit.txt` | 春日部つむぎ（音声・立ち絵）の確定クレジット文言 | docs/licenses.md §2〜§4 |
| `voicevox-core-license.txt` | voicevox_core MIT LICENSE 全文 | `External/voicevox_core/c_api/LICENSE`（§5） |
| `onnxruntime-license.txt` | ONNX Runtime（Microsoft）MIT LICENSE 全文 | third-party-notices.html 内 ort-license と同一（§7） |
| `voicevox-onnxruntime-license.txt` | voicevox_onnxruntime（onnxruntime-builder）MIT LICENSE 全文 | https://github.com/VOICEVOX/onnxruntime-builder/blob/main/LICENSE （2026-09-13 取得） |
| `onnxruntime-third-party-notices.txt` | ONNX Runtime 同梱の第三者コンポーネント通知（全文、抜粋ではない。画面には要約のみ表示） | `External/voicevox_core/onnxruntime/third-party-notices.html` を lxml で構造解析してテキスト化（§7） |
| `open-jtalk-dict-copying.txt` | Open JTalk + 辞書の COPYING 全文（NAIST/UniDic/NIT の3者表記） | `External/voicevox_core/dict/open_jtalk_dic_utf_8-1.11/COPYING`（§8・§8-1） |
| `unity-packages-notices.txt` | Unity 6パッケージの一覧・バージョン・LICENSE URL（Unity Companion License 適用。全文は次の行のファイル参照）に加え、Unity Editor Software Terms Section 2.12 の帰属定型文（Made with Unity）と、Unity's Trademark Guidelines（branding-trademarks）の Trademark Notice and Attribution Statement（issue #100） | docs/licenses.md §9・§11 |
| `unity-companion-license.txt` | Unity Companion License v1.4（2024-10-29）全文 | https://unity.com/legal/licenses/unity-companion-license （2026-09-13 取得。Next.js ページの RSC ペイロードから本文 JSON を抽出） |
| `mono-nat-license.txt` | Mono.Nat MIT LICENSE.md 全文 | `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md`（nupkg 同梱の原本、#3 でリポジトリに取り込み済み。§10・§10.1） |
| `noto-sans-jp-ofl.txt` | Noto Sans JP の SIL OFL 1.1 全文（著作権表示・商標表示をヘッダに明記） | `Assets/TsumugiQuiz/UI/Fonts/LICENSE-OFL-NotoSansJP.txt` と同一（§13） |

**Resources/Licenses が配布物 notices（`THIRD-PARTY-NOTICES.txt`）生成の単一ソースである**（レビュー M-3）。
Mono.Nat については、`Assets/Plugins/Mono.Nat/LICENSE.md` と `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md`
（#3 で追加）はいずれも DLL に隣接させるためのコピーであり、内容を更新する場合は
`Resources/Licenses/mono-nat-license.txt` との内容一致を保つこと。

配布 zip 同梱用の `THIRD-PARTY-NOTICES.txt` は `scripts/gen-third-party-notices.ps1` が
`Resources/Licenses/*.txt`（`CreditsLicenseCatalog.Entries` と対応する明示的な順序配列。
キャラクター→音声→OSS）と `Resources/Terms/*.txt`（`TermsCatalog.Entries` と対応する順序、利用規約4件）
を連結して生成する（既定の出力先 `Builds/THIRD-PARTY-NOTICES.txt`。`-DryRun` で書き込まずに連結順序だけ
確認できる。docs/dev-workflow.md §8 から参照。実際の配布 zip への同梱は #34 で実施済み）。

### ライセンス互換の判定基準
- **可**: MIT / Apache-2.0 / BSD 系（Modified BSD 含む）/ Unity Companion License
- **不可**: GPL / AGPL / LGPL などコピーレフト系（配布時にソース開示義務が生じ、個人の友人向け配布であっても対応コストが見合わないため採用しない）
- **不可**: 利用に費用が発生するもの、法人アカウント登録必須のもの（NFR-01 運用コストゼロに反する）
- 上記に当てはまらない特殊ライセンス（VOICEVOX・立ち絵のような独自利用規約）は、本ファイルに個別に根拠 URL と表記文言を書き、reviewer のレビューを経て採用可否を判断する

### アプリ内同意の仕組み（2026-09-13 追加、ユーザー指示: 第三者の権利を誠実かつ厳密に守る）

第三者（VOICEVOX / 春日部つむぎ / 立ち絵制作者）が定める利用規約の遵守を、アプリ内クレジット画面での「表示のみ」に留めず、**利用者本人が規約を読み、同意した事実を記録・後から確認できる仕組み**として実装する。requirements.md FR-71〜FR-76・NFR-08、architecture.md の `TermsView` / `ConsentStore` を参照。

- **規約テキストの同梱**: 本ファイルに一次情報として転記した規約のうち、次を `Assets/TsumugiQuiz/Resources/Terms/*.txt` としてアプリに同梱する。各ファイルの先頭に「出典 URL」「取得日」を明記してから本文を続ける
  - `voicevox-models-terms.txt` — VOICEVOX 音声モデル 利用規約（出典: https://github.com/VOICEVOX/voicevox_vvm 、取得日 2026-09-13、§6 の原文と同一）
  - `voicevox-onnxruntime-terms.txt` — VOICEVOX ONNX Runtime 利用規約（出典: `External/voicevox_core/onnxruntime/TERMS.txt`、取得日 2026-09-13、§7 の原文と同一）
  - `tsumugi-voice-credit.txt` — **本文転記済み**。「VOICEVOX「春日部つむぎ」利用規約」全 7 項目・公式 Q&A（出典: https://tsumugi-official.studio.site/rule2 、2026-09-13 オーナー提供のスクリーンショットより転記。JS 描画のため自動取得不可。§2 の原文と同一。確定クレジット文言 `VOICEVOX:春日部つむぎ` を含む）
  - `tsumugi-illustration-terms.txt` — **本文転記済み**。「春日部つむぎ公式立ち絵、Live2D」利用規約 全 8 項目・公式 Q&A、および zip 同梱 `readme.txt` の要点（出典: https://tsumugi-official.studio.site/rule2 、2026-09-13 オーナー提供のスクリーンショットより転記。JS 描画のため自動取得不可。§3 の原文と同一）
- **同意フロー（FR-71〜FR-73）**: 初回起動時（または再同意が必要な時）に `TermsView` が上記 4 ファイルを本文表示または URL リンクで提示し、「同意します」チェック + 同意ボタンで確定する。同意時に、同意日時・各規約テキストの SHA-256 ハッシュ・アプリバージョンを `Application.persistentDataPath/consent.json` に記録する
- **未同意時の制限（FR-74）**: 同意記録が無い状態では、TTS（VOICEVOX 音声モデル・春日部つむぎ音声）と立ち絵表示を利用する機能に進めない
- **事後確認・撤回（FR-75）**: 設定画面・クレジット画面から `consent.json` の内容（同意日時・同意した規約バージョン）を確認でき、規約本文の再表示・同意の撤回ができる。**撤回はゲーム進行中でも有効**で、読み上げは次の問題から合成・再生されなくなる（鳴っている 1 問だけは完了まで鳴る。合成済みでまだ鳴っていない音声は再生を始めない。issue #127、docs/tts.md §6.6）。立ち絵も次の出題から非表示になる（issue #139、docs/tts.md §8.4）
- **撤回したときにゲーム進行そのものを止めるかどうか（確定、2026-09-18、ユーザー承認）**（issue #127 レビュー M-4）: 「撤回＝読み上げ（TTS）と立ち絵の利用を止めるだけで、クイズの進行（問題文の表示・早押し・採点）は継続する」で確定した。FR-74 の「TTS・立ち絵を利用する機能へ進めない」は、同意記録が無い状態で当該機能に入ろうとする時点のゲート（初回同意前、および FR-76 による再同意要求時）を指す表現であり、**ゲーム進行中の撤回に適用した場合は「TTS・立ち絵という機能の利用」だけを止める意味と解釈する**（進行中のクイズそのものから退出させる、の意ではない）。この解釈は FR-75 の欄外注記・docs/tts.md §6.6 と表現をそろえてある（ユーザーが承認したのは挙動そのもの〔撤回＝TTS・立ち絵の利用停止のみ、クイズの進行は継続〕である。第三者の権利に直結する解釈のため、統括判断だけで決めず 2026-09-18 にユーザー本人へ確認して確定した。上記の FR-74 の読み方は、この確定に合わせた統括の整理）
- **再同意（FR-76）**: 同梱の規約テキストを更新して SHA-256 が変化した場合、既存の同意記録は無効化され、次回起動時に再同意を求める
- **この仕組みの根拠**: VOICEVOX 音声モデル 利用規約（§6 原文）の許諾内容 4「作成された音声の利用を他者に許諾する際は、当該他者に対し本許諾内容の 3 及び 4 の遵守を義務付けてください」は、アプリ製作者（本プロジェクト）が音声モデルを組み込んだアプリを配布する際、**利用者（アプリ使用者）にも規約 3・4 の遵守を義務付けることを要求している**。同一構造の条項は VOICEVOX ONNX Runtime 利用規約（§7 原文）にもある。本節の同意フローは、この「他者への遵守義務付け」を実効的に満たすための実装であり、単なる任意表示のクレジット画面（§12 冒頭）だけでは満たせない義務を担う

### 未確定・要手動確認の一覧（2026-09-17 時点）
1. ~~春日部つむぎのキャラクター規約ページ（§2）本文・クレジットフォーマットの一次ソースでの目視確認~~ → 2026-09-13 に voicevox_vvm README で暫定確認、同日中にオーナー提供のスクリーンショット（https://tsumugi-official.studio.site/rule2 ）から「VOICEVOX「春日部つむぎ」利用規約」全 7 項目・公式 Q&A の原文を転記し確定（解消済み）
2. ~~春日部つむぎ公式サイト（§3）の立ち絵規約本文の一次ソースでの目視確認~~ → 2026-09-13 にオーナー提供のスクリーンショット（https://tsumugi-official.studio.site/rule2 ）から「春日部つむぎ公式立ち絵、Live2D」利用規約 全 8 項目・公式 Q&A の原文を転記し確定（解消済み）
3. 春日部つむぎロゴ（§4）専用の追加条件の有無（**未解消、issue #36 で再挑戦済み**。2026-09-17 に
   https://tsumugi-official.studio.site/rule2 と https://tsumugi-official.studio.site/rule の 2 ページに
   WebFetch でアクセスを試みたが、いずれも JavaScript 描画のためページタイトルのみしか取得できず、
   ロゴ専用条件の有無を自動確認できなかった（既存の記載どおり）。オーナー提供のスクリーンショット等、
   人が実際にページを開いて確認する手段がない限り自動では解消できない。**引き続き要手動確認**）
4. Unity Personal のスプラッシュ画面義務の正式条項（§11）（**状態: 一部確認・引き続き要手動確認**、
   issue #36 で追加確認。2026-09-17 に Unity Editor Software Terms という個別の1文書
   （https://unity.com/legal/editor-terms-of-service/software 、2026 年 6 月 30 日最終更新と明記）を
   WebFetch で全文確認したところ、"splash screen" という語は本文中に存在しなかった。ただし
   **これは当該1文書についての確認に留まり、Unity の規約群は他にも複数文書に分かれているため、
   「スプラッシュ画面表示の義務が存在しない」ことを確定したものではない**（レビュー M-1）。
   一方で同文書 Section 2.12 に「プロジェクトにクレジット・attribution 表示がある場合は所定の
   attribution 文言を表示する義務」があることが判明し、本アプリの `CreditsView` はこれに該当しうる
   （§11 追加確認欄・該当部分抜粋欄に詳細）。**この attribution 文言をクレジット画面に追記する対応は
   issue #100（2026-09-17）で実装済み**（`CreditsLicenseCatalog` の `unity-packages` エントリ・
   `Resources/Licenses/unity-packages-notices.txt`。§11・§12.1 参照）。ただし
   **Unity Personal のスプラッシュ画面表示義務そのものの正式条項の有無は、issue #100 の範囲外
   （issue #100 の作業内容にも明記のとおり本 issue では扱わない）のため未解消のまま残る**。
   2026-09-14 実測の `ProjectSettings.asset`（デフォルトのままスプラッシュ画面を
   無効化していない）は安全側の運用として引き続き有効）
5. ~~UPnP ライブラリ（§10）は Mono.Nat / Open.NAT のどちらを採用するか未定~~ → 2026-09-13 に Mono.Nat 3.0.4 を採用として確定（解消済み）
6. ~~ONNX Runtime（§7）: VOICEVOX ONNX Runtime 利用規約はダウンローダー実行時にのみ表示されるため未確認~~ → 2026-09-13 に公式ダウンローダーを実行し `onnxruntime/TERMS.txt` の原文を確認・転記して確定（解消済み）
7. Tailscale（§14）を使った接続手順の実機検証（issue #36、レビュー L-4）: README・docs/network-nat.md §1.5 の手順は Tailscale 公式ドキュメント（ダウンロードページ・quickstart ガイド・料金ページ）の記載に基づいて作成したが、**実機 2 台以上を使った実際の接続検証（インストール〜ログイン〜Tailscale IP での参加コード生成〜他デバイスからの接続）は本作業環境（複数物理デバイス無し）では実施できていない。要手動確認**（docs/network-nat.md §1.5 の脚注に同内容を記載済み）
8. 立ち絵の**加工物（表情差分 PNG）**を配布物に同梱してよいか（§3.1、issue #86）。規約 5 項は「加筆、加工できます」と加工自体を明示的に許容する一方、7 項・readme.txt は「二次配布」を禁止しており、**加工物の配布がどちらに当たるかは規約本文からは読み取れない**。公式 Q&A も「自作のアプリケーションなどに利用してもいい？→ 問題ありません」と利用可否のみを述べ、素材（加工物を含む）を同梱配布してよいかには触れていない。運営への個別問い合わせは行っていない（規約は「利用規約範囲内の利用の場合個別で利用許諾はお送りしていません」とする）。**本プロジェクトは安全側に倒し、原本・加工物とも一切配布せず、ユーザーが自分の環境で生成する方式（§3.1）を採る**ため、この判断が確定しなくても実装は成立する。将来、配布物への同梱を検討する場合は運営への確認が必須（**要手動確認**）
9. 春日部つむぎ公式立ち絵 PSD の**各レイヤーを個別に利用する**こと自体への追加条件の有無（§3.1）。規約・readme.txt にレイヤー単位の利用に関する個別条項は無く、「加筆、加工できます（良識の範囲内）」「服を脱がせた状態での利用は厳禁」の 2 条件のみが該当すると解釈した。この解釈を超える条件（例: 表情差分の作成そのものへの制限）が公式サイトの他ページにある可能性は自動確認できていない（一次サイトは JS 描画のため WebFetch 不可。項目 3 と同じ制約。**要手動確認**）

---

## 13. Noto Sans JP（UI 日本語フォント、`Assets/TsumugiQuiz/UI/Fonts/` 同梱）

| 項目 | 内容 |
|---|---|
| 対象 | UI Toolkit の共通テーマ（`theme.uss` の `-unity-font-definition`）で使用する日本語フォント本体（`NotoSansJP-Regular.otf`、Regular ウェイトのみ同梱。他ウェイトは同梱しない） |
| ライセンス種別 | SIL Open Font License, Version 1.1（OFL 1.1） |
| 根拠 URL | https://github.com/notofonts/noto-cjk （リリース `Sans2.004`、アセット `16_NotoSansJP.zip`。2026-09-13、`gh release download` で取得・確認）。同一フォントは https://fonts.google.com/noto/specimen/Noto+Sans+JP からも配布されている |
| 著作権表示（フォントファイルの `name` テーブルから抽出、原文どおり） | `© 2014-2021 Adobe (http://www.adobe.com/).`（nameID 0 = Copyright）。`Noto is a trademark of Google Inc.`（nameID 7 = Trademark） |
| 取得したファイルのハッシュ（SHA-256） | zip: `2bbdd2c20f30670b39ca735c96d75f1fdabdb348103e43b820cf17701fd22b18`（`16_NotoSansJP.zip`）<br>font: `dff723ba59d57d136764a04b9b2d03205544f7cd785a711442d6d2d085ac5073`（`NotoSansJP-Regular.otf`） |
| OFL 1.1 の要点 | 商用・非商用問わず利用・改変・再配布が可能。フォント単体を有償販売することは禁止（アプリに組み込んで配布するのは可）。フォントの派生物は OFL を継承する必要がある。予約フォント名（Reserved Font Name）の使用制限あり |
| 同梱すべきファイル | OFL 1.1 ライセンス全文を `Assets/TsumugiQuiz/UI/Fonts/LICENSE-OFL-NotoSansJP.txt` として同梱済み。配布 zip の `THIRD-PARTY-NOTICES.txt` にも同ライセンス全文と上記著作権表示を転記する |
| アプリのクレジット画面に載せる文言（確定版） | `Noto Sans JP — © 2014-2021 Adobe (SIL Open Font License 1.1)` |
| 実装メモ | Unity の `FontAsset`（Dynamic 生成モード。日本語グリフを事前ベイクせず、実行時に SDF アトラスへ追加する）として `Assets/TsumugiQuiz/UI/Fonts/notosansjp-regular-sdf.asset` を生成し、`theme.uss` の `:root` から `-unity-font-definition: url(...)` で参照する。生成は `Assets/TsumugiQuiz/Scripts/Editor/Setup/UiToolkitBootstrap.cs` が行う |

---

## 14. Tailscale（README §「接続できないとき」で案内する外部ツール、アプリには同梱しない）

| 項目 | 内容 |
|---|---|
| 対象 | README・[docs/network-nat.md](./network-nat.md) §1.5 で、CGNAT 環境等で直接接続できない場合の代替手段として案内する外部アプリケーション（ユーザー自身が任意でインストールする。本プロジェクトのビルド・配布 zip には一切含めない） |
| 料金プラン（Personal） | 無料（"$0 Free forever"。最大 6 ユーザー、ユーザーデバイス数は無制限。根拠 URL: https://tailscale.com/pricing 、確認日 2026-09-17、WebFetch で確認） |
| クライアントのライセンス | tailscale/tailscale（オープンソースのクライアント本体）は BSD 3-Clause License。著作権表示: `Copyright (c) 2020 Tailscale Inc & contributors.`（根拠 URL: https://github.com/tailscale/tailscale/blob/main/LICENSE 、確認日 2026-09-17、WebFetch で確認） |
| サービス本体について | コーディネーションサーバー等の Tailscale サービス本体（SaaS 部分）は OSS ではなく、Tailscale Inc. が独自の利用規約・プライバシーポリシーの下で運営する商用サービスである（クライアントのソースコードが公開されていることと、サービス全体が OSS であることは別）。無償の Personal プランを利用する場合もアカウント登録（Google 等の既存アカウントでのログイン）が必要 |
| 本プロジェクトでの扱い | アプリはユーザーの Tailscale IP（`100.x.x.x`）を「グローバル IP を手入力する」欄に受け付けるのみで、Tailscale のインストール・実行・アカウント登録はユーザー自身が行う。アプリのビルド・配布物に Tailscale のバイナリ・ライブラリは一切含まれないため、`THIRD-PARTY-NOTICES.txt` への追記は不要（同梱物ではないため） |

---

## 15. psd-tools / Pillow（立ち絵の表情差分・サンプル問題の画像を生成するスクリプトの依存、配布物には同梱しない）

| 項目 | 内容 |
|---|---|
| 対象 | `scripts/generate_tsumugi_expressions.py`（issue #86。配布 zip の `tools/tsumugi-expressions/` にも同じファイルを同梱、#219）と `scripts/gen-sample-image.py`（issue #220、サンプル問題の画像の生成。Pillow のみ。導入は `pip install Pillow`）が使う Python パッケージ。**開発者がサンプル問題の画像を生成するときと、開発者・利用者が自分の環境で立ち絵の表情差分を生成するときだけ使う**。Unity プロジェクト・ビルド成果物・配布 zip には一切含まれない（C# からは呼ばない。zip に入れるのは依存の一覧 `requirements.txt` だけ） |
| psd-tools | MIT License。原文冒頭: `Copyright (c) 2019 Kota Yamaguchi`（根拠 URL: https://github.com/psd-tools/psd-tools/blob/main/LICENSE 、確認日 2026-09-18。`gh api repos/psd-tools/psd-tools/license` の `spdx_id` = `MIT`、および `contents/LICENSE` の本文を実際に取得して確認）。実測で使用したバージョン: **1.19.0**（`python -c "import psd_tools; print(psd_tools.__version__)"`、2026-09-18） |
| Pillow | MIT-CMU License（Pillow 独自の MIT 系ライセンス。HPND 由来。原文の著作権表示は `Copyright © 1997-2011 by Secret Labs AB` / `Copyright © 1995-2011 by Fredrik Lundh and contributors`（PIL）/ `Copyright © 2010 by Jeffrey 'Alex' Clark and contributors`（Pillow））（根拠 URL: https://github.com/python-pillow/Pillow/blob/main/LICENSE 、確認日 2026-09-18、WebFetch で本文確認。`importlib.metadata` の `License-Expression` も `MIT-CMU`）。実測で使用したバージョン: **12.3.0** |
| ライセンス互換 | どちらも MIT 系で「可」（§12「ライセンス互換の判定基準」）。費用ゼロ・アカウント登録不要 |
| 同梱すべきファイル | **なし**（配布物に含まれないため）。`THIRD-PARTY-NOTICES.txt` への記載も不要。導入方法は、開発者は `pwsh ./scripts/generate-tsumugi-expressions.ps1 -InstallDeps`、配布 zip の利用者は `tools/tsumugi-expressions/install-libraries.bat`（どちらも内部で `python -m pip install -r <requirements>`。指定は `scripts/tsumugi-expressions-requirements.txt` の 1 か所で、zip にはその写しを `requirements.txt` として入れる） |
| 利用者が pip で入れるもの（#219） | 利用者が自分の環境に PyPI から入れるもので、本プロジェクトは再配布しない。psd-tools の依存として attrs（MIT）・numpy（`BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0`）・typing_extensions（PSF-2.0）も入る（いずれも開発機にインストール済みのメタデータの `License-Expression` で確認、2026-10-03。attrs 26.1.0 / numpy 2.5.3 / typing_extensions 4.16.0）。**ただし numpy の Windows 用 wheel は GCC ランタイムを含む**: インストール済みの `numpy-2.5.3.dist-info/licenses/LICENSE.txt`（一次情報、2026-10-03 確認）に、同梱の `numpy.libs\libscipy_openblas*.dll` について「Name: GCC runtime library / Description: statically linked to files compiled with gcc / License: GPL-3.0-or-later WITH GCC-exception-3.1」と、GCC Runtime Library Exception 3.1 の全文が載っている（同じ DLL の OpenBLAS / LAPACK 本体は BSD-3-Clause 系）。GCC Runtime Library Exception は、GCC でコンパイルした GPL 以外のプログラムがこのランタイムを組み込んで使うことを許す例外で、この DLL はその対象である。本プロジェクトは numpy を再配布せず（配布 zip には入れない。利用者が自分で PyPI から pip で入れる）、ツールのコードも numpy にリンク・同梱しない（利用者の環境で import するだけ）ため、CLAUDE.md の「GPL / AGPL / LGPL 系ライブラリの導入禁止」（プロジェクトに取り込む・配布物に含めるもの）には当たらないと判断した（この扱いは 2026-10-03 にユーザー承認済み）。費用ゼロ・アカウント登録不要。バージョンは指定子（`psd-tools>=1.19` / `Pillow>=10`）の範囲で利用者の pip が選ぶ。2026-10-03 に、依存の入っていない新しい仮想環境（Python 3.14.3）を `install-libraries.bat -Python <仮想環境の python>` で使った実測では psd-tools 1.23.0 / Pillow 12.3.0 / numpy 2.5.3 / attrs 26.1.0 / typing_extensions 4.16.0 が入り、生成した 9 枚は psd-tools 1.19.0（開発機）で作ったものとバイト単位で一致した |

---

## 16. サンプル問題の画像（`Resources/Questions/sample-image.bytes`、issue #220）

| 項目 | 内容 |
|---|---|
| 対象 | 同梱サンプル問題 q3「この画像に写っている動物は？」の画像（猫のイラスト、800x600 の PNG） |
| 出所 | `scripts/gen-sample-image.py` が Pillow の図形描画だけで描いた自作のイラスト。本プロジェクトのために作成したもの。外部の素材や画像生成モデルの出力は使わず、スクリプト内の図形描画だけで描いている。Pillow 12.3.0 で生成 |
| 配布物への同梱 | 同梱する（アプリが初回起動時に `images/sample.png` として書き出す）。第三者の権利表記は不要。生成に使う Pillow は開発用で配布物に含まれない（§15） |

---

## 17. 本リポジトリ自体のライセンス（MIT License、2026-10-07 決定）

| 項目 | 内容 |
|---|---|
| ライセンス | MIT License。著作権者の表記は `Copyright (c) 2026 Tomonorarari-Think`。全文はリポジトリ直下の `LICENSE`（MIT の本文のみ。GitHub に MIT と判定させるため、注記は入れない） |
| 適用範囲の注記 | リポジトリ直下の `NOTICE.md` に英語・日本語で記載する。README の「ライセンス」節から `LICENSE` と `NOTICE.md` の両方にリンクする。配布 zip にも `LICENSE` と `NOTICE.md` を入れる（`scripts/package-release.ps1`） |
| MIT の対象（自作部分） | 作者が書いたコード、文書、効果音（`Assets/TsumugiQuiz/Audio/SE/`）、サンプル画像（§16）、問題データ（`Assets/TsumugiQuiz/Resources/Questions/sample-questions.json`）、`Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss`・`Assets/DefaultNetworkPrefabs.asset`（§9.2） |
| MIT の対象外（それぞれのライセンス・規約に従う） | `Packages/com.unity.transport/`（Unity Companion License。改変部分を含む。§9.1。同フォルダの `LICENSE.md`。フォルダ全体を UCL の扱いとし、自作の説明文書 `TSUMUGI-PATCH.md` も含める）/ Unity テンプレート由来のファイル（§9.2。`Assets/Settings/`、`ProjectSettings/`、`Packages/manifest.json`、`Packages/packages-lock.json`）/ `Assets/Plugins/Mono.Nat/`（MIT、Alan McGovern ほか。§10）とその写し `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md` / `Assets/TsumugiQuiz/UI/Fonts/`（SIL OFL 1.1。§13）/ `Assets/TsumugiQuiz/Resources/Licenses/`・`Assets/TsumugiQuiz/Resources/Terms/`・本書に転記した第三者のライセンス・規約の文章 / VOICEVOX・voicevox_core・春日部つむぎの音声・立ち絵（§1〜§6。リポジトリには含まれず、利用者が各規約に従って入手する） |
