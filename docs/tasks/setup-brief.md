# セットアップ共通ブリーフ（統括 → 全サブエージェント）

作成: 2026-09-12 / 統括: Fable
このファイルは docs/ 配下の各設計ドキュメントが互いに矛盾しないように、統括が先に決めた共通事項をまとめたもの。
各エージェントはこのブリーフと、ユーザーからの確定事項（下記「確定事項」）に従うこと。
「仮決め」と書いた項目は統括が仮に決めたもので、最終報告でユーザーに提示する。変更したい場合は理由を添えて報告に書く（勝手に変えない）。

## 0. 確定事項（ユーザー指定・変更不可）
- Unity 6000.6.0f1（Unity 6.6）、2D、ビルドターゲット Windows Standalone のみ
- UI は UI Toolkit のみ（uGUI は使わない。テンプレート由来の com.unity.ugui パッケージは依存のため残すが、コードから参照しない）
- GitHub Actions は使わない。検証はローカルスクリプト（scripts/*.ps1）で代替
- BGM なし。SE は自作（生成スクリプトまたは簡易合成）
- ネットワーク: Netcode for GameObjects（NGO）+ Unity Transport、Host モード、直接接続（外部アカウント不要、運用コストゼロ）
  - UPnP 自動ポートマッピング → グローバル IP + ポートを短い参加コードにエンコード → 非対応時は手動ポート開放案内 + README に Tailscale 手順
  - 早押し判定はサーバー時刻基準（受信順ではなく、クライアントが押下したネットワーク時刻をサーバーが比較）
- TTS: voicevox_core をネイティブ同梱、各クライアントがローカル合成、話者は春日部つむぎ、再生開始はホスト時刻基準で同期、合成結果はキャッシュ
  - voicevox_core / ONNX Runtime / Open JTalk 辞書 / vvm は git 管理外、External/voicevox_core/ 配下
  - 音声モデルの「配布 zip 同梱」「初回起動時ダウンロード」の両案を docs に併記し、規約確認後に選ぶ
  → **改訂（2026-09-13）**: vvm の TERMS.txt（https://github.com/VOICEVOX/voicevox_vvm ）で「アプリケーションに組み込んで再配布することができます」を確認したため、案 (a) 配布 zip 同梱を第一候補とする。両案は引き続き docs/tts.md §10 に併記する（最終選定は dev-workflow.md の配布パッケージ作成手順で確定）
- 立ち絵: 春日部つむぎ公式立ち絵（External/tsumugi/、git 管理外）。状態は最低限「待機 / 読み上げ中 / 正解 / 不正解」
- 権利表記: アプリ内クレジット画面。docs/licenses.md に根拠 URL と表記文言
- 問題データ: JSON。形式は freeText / choice、画像はどちらとも組み合わせ可。自動判定はひらがな/カタカナ/全角半角を正規化して完全一致、複数正解可、あいまい一致なし
- 問題追加は (1) 所定フォルダに JSON を置く (2) アプリ内エディタ。問題と画像はホストが持ち、クライアントへ配信
- ルーム設定はユーザー指定の項目すべて（requirements.md 参照）。プリセット保存・読込

## 1. 環境の実測値（統括が確認済み）
- Unity CLI: `unity` 1.0.0-beta.8（`%LOCALAPPDATA%\Unity\bin\unity.exe`）
- Editor: `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`、ライセンス Unity Personal
- テンプレート: com.unity.template.universal-2d 7.0.0（URP 17.6.0、Input System 1.20.0、Test Framework 1.8.0）
- git 2.52 / gh 認証済み（アカウント Tomonorarari-Think）
- GitHub: https://github.com/Tomonorarari-Think/tsumugi-quiz（private、デフォルトブランチ develop、マージ後ブランチ自動削除 ON）
- External/ に配置済み: voicevox_core-windows-x64-0.17.0.zip（C API 本体のみ: voicevox_core.dll / .lib / .h）、open_jtalk_dic_utf_8-1.11.tar.gz、春日部つむぎ立ち絵_公式_v2.0.zip（readme.txt、png、psd、logo）
- voicevox_core 0.17.0 リリース資産に `download-windows-x64.exe`（公式ダウンローダー）あり。**まだ実行していない**（ユーザー承認待ち）

## 2. 仮決め（統括が決定。報告で提示する）

**2026-09-13 ユーザー承認済み（K1〜K24 と改訂）**。以下の仮決め項目・改訂内容はすべてユーザーが確認し承認した。

**2026-09-18 追加承認**: K5 の改訂（`TsumugiQuiz.Tests.Shared` を追加した asmdef 計 10 本構成、#67）、選択式（choice）の判定方式（K25、#17）、開発機ルーターの UPnP 無応答時の手動ポート開放 + Tailscale 案内運用（K26、#36）の 3 件をユーザーが確定とした。あわせて、クライアントの「ルーム設定を見る」ボタン表示可否（K27）を統括判断で確定とした。

| # | 項目 | 仮決め内容 |
|---|------|-----------|
| K1 | テンプレート | Universal 2D（URP）。2D Built-in ではなく URP を採用（Unity 6 の標準 2D テンプレートのため） |
| K2 | テンプレート付属物 | Assets/Welcome（IET チュートリアル）と com.unity.learn.iet-framework は削除。com.unity.ugui / visualscripting / timeline はテンプレート依存のため残す |
| K3 | 会社名/製品名 | ProjectSettings: Company = `Tomonorarari-Think`、Product = `TsumugiQuiz`、exe = `TsumugiQuiz.exe` |
| K4 | シーン構成 | `Boot.unity`（NetworkManager、常駐サービス、DontDestroyOnLoad）→ `Main.unity`（UIDocument 1 枚。画面は UXML の View を ViewRouter で切替: Title / HostSetup / Join / Lobby / Game / QuestionEditor / Settings / Credits） |
| K5 | フォルダ・asmdef | `Assets/TsumugiQuiz/` 配下に `Scripts/{Core,Questions,Room,Network,Tts,UI,Editor}`、`UI/{Views,Styles,Templates}`、`Scenes`、`Prefabs`、`Audio/SE`、`Settings`、`Tests/{Shared,EditMode,PlayMode}`。asmdef は `TsumugiQuiz.Core`（純 C#、Unity API 非依存を目標）、`TsumugiQuiz.Questions`、`TsumugiQuiz.Room`、`TsumugiQuiz.Network`、`TsumugiQuiz.Tts`、`TsumugiQuiz.UI`、`TsumugiQuiz.Editor`、`TsumugiQuiz.Tests.Shared`、`TsumugiQuiz.Tests.EditMode`、`TsumugiQuiz.Tests.PlayMode` の計 10 本。名前空間はフォルダと一致（`TsumugiQuiz.Network` など）。依存方向は Core ← Questions/Room ← Network/Tts ← UI の一方向のみ（K6）<br>→ **確定（2026-09-18 ユーザー承認）**: `TsumugiQuiz.Tests.Shared` は EditMode/PlayMode 共通のテスト用フェイク（`FakeTtsSynthesisEngine` / `TestWavFactory` / `FakeNatDevice` 等）を集約する asmdef（`UNITY_INCLUDE_TESTS` 限定、ランタイム・配布物には含まれない）。2026-09-14 の統括仮決めどおり asmdef 計 10 本で確定した。詳細は docs/architecture.md §3、#67 |
| K6 | 依存方向 | Core ← Questions/Room ← Network/Tts ← UI。UI は Network/Tts/Room に依存してよいが逆は禁止。Core は他に依存しない |
| K7 | JSON ライブラリ | `com.unity.nuget.newtonsoft-json`（Unity 提供、MIT）。JsonUtility は使わない（null/リスト/ポリモーフィズムが弱いため） |
| K8 | 問題フォルダ | `%USERPROFILE%\Documents\TsumugiQuiz\Questions\`（JSON）と `...\Questions\images\`（画像）。persistentDataPath（AppData\LocalLow）は見つけにくいので Documents を採用。アプリ内に「フォルダを開く」ボタン。キャッシュ（TTS wav）は `Application.persistentDataPath/TtsCache/` |
| K9 | 参加コード | 内容 = version(2bit)=0 + IPv4(32bit) + port(16bit) = 50bit → Crockford Base32 10 文字 + チェック文字 1（mod 37）= 11 文字。表示は `XXXXX-XXXXX-X`。ホスト画面には「インターネット用（グローバル IP）」と「LAN 用（プライベート IP）」の 2 種を表示。IPv6 は対象外（version ビットで将来拡張）<br>→ **改訂（2026-09-13）: 12 文字方式。理由: 記号混入を避け口頭伝達を容易にするため**。50bit ペイロード + 10bit チェック（`V mod 1021`）= 60bit = Crockford Base32 **12 文字**（記号なし、全文字が 32 記号のいずれか）。表示は `XXXX-XXXX-XXXX`。詳細は docs/network-joincode.md §1 |
| K10 | グローバル IP の取得 | 1) UPnP GetExternalIPAddress 2) 失敗時は HTTPS の IP 確認サービス（例 https://api.ipify.org、無料・アカウント不要）3) 失敗時は手動入力。CGNAT（取得した IP が 100.64.0.0/10 等）を検出したら「直接接続不可、Tailscale 案内」を表示 |
| K11 | UPnP ライブラリ | 候補は Mono.Nat（MIT、netstandard2.0、https://github.com/alanmcgovern/Mono.Nat）と Open.NAT（MIT、https://github.com/lontivero/Open.NAT）。network.md 担当が Unity（.NET Standard 2.1）での可用性を調べて推奨を決める。DLL を Assets/Plugins に置く場合もライセンスファイルを同梱<br>→ **改訂（2026-09-13）**: **Mono.Nat 3.0.4** を推奨として確定（MIT、`netstandard2.1` ビルドあり、UPnP + NAT-PMP 両対応）。Open.NAT は netstandard2.1 ビルドを持たず、リポジトリもアーカイブ済み（read-only）のため不採用。詳細は docs/network-nat.md §1.1 |
| K12 | 早押し受付 | 既定は「読み上げ開始と同時に受付開始」（読み上げ中に押せる）。ルーム設定 `buzz.allowDuringReading` で「読み上げ完了後のみ」に切替可。読み上げ OFF 時は問題文表示と同時に受付開始 |
| K13 | 早押し判定の集計窓 | サーバーは最初の押下を受信してから `buzz.collectWindowMs`（既定 150ms）待ち、その間に届いた押下のうち「ネットワーク時刻が最も早いもの」を採用。押下時刻は「受付開始時刻（サーバー時刻）からの経過時間」として扱い、受付開始前・受信時刻より後のタイムスタンプは不正として補正/棄却<br>→ **改訂（2026-09-13）**: 同着（差 < 1ms）は暗号論的乱数（`RandomNumberGenerator`）で抽選する。タイムスタンプの検証を明確化: `T0` 未満は `T0` に丸めて受理、`serverNow`（受信時のサーバー時刻）超過は `serverNow` に丸めて受理、`serverNow + 1.0` 秒超は棄却（`TooFarInFuture`）。押下時刻はクライアントの `NetworkManager.Singleton.LocalTime.Time` を送り、サーバーは受付開始時刻 `T0` からの相対経過 `dt = reportedTime - T0` で比較する。詳細は docs/network.md §6.2〜§6.4 |
| K14 | 問題配信 | 答え（answers / correctIndex）はクライアントに送らない。判定はサーバー。クライアントには text / readingText / choices / 画像のみ、出題直前に配信（次問を先読み）。画像は 16KB 分割のカスタムメッセージ（またはそれに準ずる方式）で送る。画像は PNG/JPG、1 枚 2MB 上限（エディタで警告）<br>→ **改訂（2026-09-13）**: 画像 16KB チャンク分割のため、`UnityTransport.MaxPayloadSize` を既定の 6144 から **32768** に引き上げる（既定 6144 では UTP のフラグメンテーションステージに 16KB のチャンクが乗らないため）。詳細は docs/network.md §8.3<br>→ **追加改訂（2026-09-13、統括判断）**: `network.maxPayloadSizeBytes` / `network.imageChunkBytes` は設定項目にしない。Transport の設定は接続確立前にホスト・クライアント双方で一致している必要があり、NGO/UTP には接続後の実行時同期の仕組みがないため、`TsumugiQuiz.Network.NetworkConstants.MaxPayloadSizeBytes = 32768` / `NetworkConstants.ImageChunkBytes = 16384` の定数として固定する。`docs/room-settings.md` §2・docs/network.md §8.3/§11 から該当キーを削除・修正済み |
| K15 | TTS 再生同期 | 各クライアントは問題受信後に合成（またはキャッシュ）し `Ready` をサーバーへ通知。サーバーは全員 Ready（またはタイムアウト `tts.readyTimeoutMs` 既定 3000ms）で `playAtServerTime` を配信。クライアントは `AudioSource.PlayScheduled` 相当でそのネットワーク時刻に再生開始。ホストは合成済み wav の長さを全員に共有し「読み上げ完了時刻」も同じ時刻基準で決める |
| K16 | TTS キャッシュ | キー = SHA-256(readingText + styleId + speed)。wav をディスクに保存。話者スタイルは「ノーマル」を既定（style id はモデルのメタ情報から実行時に解決し、固定値をハードコードしない）<br>→ **改訂（2026-09-13）**: キャッシュキーは `SHA-256(readingText, styleName, speakerName, speed, coreVersion, modelsVersion を NUL 区切りで連結)`。`styleId` ではなく `speakerName` + `styleName`（名前）を使うのは、`styleId` が VVM のバージョンで変わりうるため。`coreVersion` / `modelsVersion` を含めるのは、voicevox_core 更新時に同じ入力でも波形が変わりうるため。詳細は docs/tts.md §7.1 |
| K17 | SE | `scripts/gen-se.py`（Python + numpy、標準的な波形合成）で wav を生成し `Assets/TsumugiQuiz/Audio/SE/` にコミット。種類: 早押し、正解、不正解、タイムアップ、開始、参加 |
| K18 | ホストの司会専用モード | 司会専用時、ホストはプレイヤー一覧に含めず、早押し・回答 UI を出さない。代わりに「次へ」「一時停止」「強制正解/不正解」の司会操作 |
| K19 | 得点の既定値 | 正解 +10、誤答 0、お手つきペナルティは「次問休み」（減点に切替可、既定 -5） |
| K20 | 制限時間の既定値 | 読み上げ完了後の早押し受付 10 秒、早押し後の回答入力 15 秒、選択式の回答 20 秒 |
| K21 | 入力 | Input System パッケージ（テンプレート付属）。早押しキーは Space（設定で変更可） |
| K22 | ブランチ運用 | git flow: main（リリース）/ develop（統合、デフォルト）/ feature/<issue番号>-<説明> / release/* / hotfix/*。PR は develop 向け、squash マージ。マージ後ローカル feature を削除 |
| K23 | ローカル検証 | `scripts/verify.ps1`（EditMode + PlayMode テスト実行、結果 XML 保存、ログの error grep）、`scripts/build.ps1`（Windows Standalone ビルド → `Builds/Windows/`）。CI の代替として PR 前に必ず実行 |
| K24 | ネイティブ配置 | ビルド時: `voicevox_core.dll` と `voicevox_onnxruntime.dll` は `Assets/Plugins/voicevox_core/x86_64/`（git 管理外、External から配置スクリプトでコピー）。Open JTalk 辞書・vvm・規約ファイルは `Assets/StreamingAssets/voicevox_core/{dict/open_jtalk_dic_utf_8-1.11, models/vvms/*.vvm, models/TERMS.txt, models/README.txt, onnxruntime/TERMS.txt, c_api/LICENSE}`（git 管理外）。配置スクリプト `scripts/setup-external.ps1` が External → Assets へコピーする<br>→ **改訂（2026-09-13）: External の構成（`dict/` / `models/vvms/`）と揃える**。当初は `Assets/StreamingAssets/voicevox_core/{open_jtalk_dic_utf_8-1.11, models}` と平坦にする想定だったが、(1) External との差分確認が容易、(2) 配布時に同梱が必要な規約・ライセンス（`docs/licenses.md`）を同じ木に並べられる、ため。`TsumugiQuiz.Tts.VoicevoxPaths` はどちらの配置でも辞書・vvm を見つける。詳細は docs/tts.md §5.5 |
| K25 | 選択式（choice）の判定方式 | **確定（2026-09-18 ユーザー承認、#17）**: 早押しなし・全員が制限時間内（`answer.choiceTimeLimitSec`、既定 20 秒）に選択・時間切れで一斉判定。不正解を選んだクライアントは `score.penaltyType`（既定 `skipNext` = 次問休み）を freeText の誤答と同じ設定で適用する。選択肢の表示順は `TsumugiQuiz.UI.Views.Game.ChoiceShuffle` がクライアントごとの固定シード（問題インデックスと `clientId` を混合）でシャッフルし、判定は常に元 `correctIndex` で行うため表示順は正誤判定に影響しない。詳細は docs/network.md §6.6/§8、docs/room-settings.md「選択式」節 |
| K26 | NAT 越え失敗時の案内 | **確定（2026-09-18 ユーザー承認、#36）**: 開発機のルーターが UPnP/NAT-PMP に無応答（実測、docs/network-nat.md §3.3）だったため、自動ポートマッピングに失敗した場合は README に (1) 手動ポート開放（UDP 7777 番をホスト PC の IP へポートフォワーディング）の手順、(2) それでも接続できない場合や CGNAT 環境向けに Tailscale（無料 VPN、要アカウントログイン・費用なし）の導入手順を掲載する運用とする。アプリ側は「グローバル IP を手入力する」欄を提供し、取得した Tailscale IP（100.x.x.x 帯）をそのまま入力すれば参加コードを生成できる。詳細は README.md「接続できないとき」、docs/network-nat.md §1.5 |
| K27 | クライアントの「ルーム設定を見る」ボタン | **確定（2026-09-18 統括判断）**: 出す。ロビーからクライアントも Settings View を開けるが、ルーム設定タブは入力・「適用」ごと無効化し閲覧専用にする（#28 PR #92 の実装どおり）。詳細は docs/room-settings.md §7.3 |

## 3. ドキュメントの共通体裁
- 日本語。見出しは `#` から。先頭に「目的」「関連ドキュメント」の節
- 仮決め項目を書くときは末尾に `（仮決め K9）` のように番号を付ける
- 根拠 URL は本文中に書く（バージョン・API 名は必ず出典を確認する）
- 1 ファイル 800 行以内。長くなる場合は分割

## 4. サブエージェントへの共通ルール
- git commit / push はしない（統括が行う）
- `External/` や `Library/` などをコミット対象にしない
- Unity を起動する Bash は前景・`timeout: 600000`
- 完了報告に「作成ファイル一覧」「実測結果」「未確定事項」を含める
