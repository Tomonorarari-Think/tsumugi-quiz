# Issue 作成計画

作成: 2026-09-13 / 担当: worker（Sonnet）
統括の指示（会話内プロンプト）に基づき、GitHub milestone/label/issue を作成するための計画。
「仮番号」は作成前の一時識別子。GitHub 上のリポジトリは issue 0 件のクリーンな状態から作成するため、
**作成順 = issue番号順（#1〜#36）** になる前提で仮番号 In をそのまま実番号 #n として使用した
（依存関係は常に「先に作成済みの issue」のみを指すため、作成時点で実番号が確定している）。

## 前提となる統括判断の反映
- 参加コードは 12 文字（50bit ペイロード + 10bit チェック `V mod 1021`、Base32 12 文字、表示 `XXXX-XXXX-XXXX`、全 32 記号のみ使用）。
  `docs/network-joincode.md` は 2026-09-13 の改訂でこの 12 文字版に更新済み（旧 11 文字案は同書 §1.8 に記録）。
- UPnP は Mono.Nat 3.0.4（MIT）。
- 早押しは `NetworkManager.LocalTime.Time` を送信、T0 相対比較、集計窓 150ms、同着は暗号論的乱数。
- 音声モデルは配布 zip 同梱を第一候補（規約確認済み、tts.md §10.1/§10.4）。
- 設定キーは room-settings.md + network.md §11 + tts.md §12 の提案分すべてを対象にする。

## マイルストーン

| # | タイトル | 概要 |
|---|---|---|
| M1 | ロビー接続 | ホスト開始・参加コード・UPnP・ロビー表示 |
| M2 | 縦切り MVP（1 問遊べる） | 1 問を出題→早押し→回答→判定→結果まで通す（読み上げなし・自由入力のみ） |
| M3 | 全出題形式＋判定 | 選択式・画像付き・正規化判定・得点・お手つき・複数問進行・結果画面 |
| M4 | TTS＋立ち絵 | voicevox_core 組み込み・キャッシュ・同期再生・立ち絵状態表示 |
| M5 | ルーム設定＋問題エディタ | 全設定項目・プリセット・問題フォルダ読込・アプリ内エディタ |
| M6 | 権利表記・配布パッケージ | クレジット画面・THIRD-PARTY-NOTICES・SE 生成・配布 zip・README 仕上げ |

## ラベル
`area:network` `area:tts` `area:ui` `area:questions` `area:room` `area:core` `area:build` `area:docs`
`size:S` `size:M` `size:L` `blocked`

## Issue 一覧（作成順 = 実番号）

| # | タイトル | Milestone | 依存 | 担当 | サイズ | 受け入れ条件（要約） |
|---|---|---|---|---|---|---|
| 1 | [M1] Core: 参加コード エンコード/デコード/正規化 | M1 | なし | implementer | M | `JoinCodeCodec`（12文字版、`V mod 1021`）が往復一致、正規化、チェック検出の EditMode テストを通過 |
| 2 | [M1] Network: NetworkManager/UnityTransport のセットアップ | M1 | なし | implementer | M | Boot シーンで Host/Client 起動、ポート既定 7777+リトライ、ConnectionApproval 骨組み、protocolVersion 検証 |
| 3 | [M1] Network: Mono.Nat 導入と UPnP/NAT-PMP ポートマッピング + グローバル IP 取得 | M1 | #2 | implementer | L | Mono.Nat DLL 配置・ライセンス同梱、CreatePortMapAsync 成功、ipify フォールバック、CGNAT 検出 |
| 4 | [M1] UI: UI Toolkit 基盤（UIDocument・ViewRouter・Title View・共通 USS） | M1 | なし | worker | M | Main シーンに UIDocument 1 枚、ViewRouter で Title 表示、共通 USS テーマ適用 |
| 5 | [M1] UI: HostSetup View | M1 | #1, #3, #4 | worker | M | ホスト開始操作、参加コード（インターネット/LAN）表示・コピー、UPnP 状態・手動開放案内表示 |
| 6 | [M1] UI: Join View | M1 | #1, #2, #4 | worker | S | コード貼付・正規化プレビュー・エラー表示・接続進捗表示 |
| 7 | [M1] Network+UI: ロビー（プレイヤー一覧・役割・切断処理） | M1 | #2, #5, #6 | implementer | L | NetworkList でプレイヤー一覧同期、司会/プレイヤー役割、切断時のグレー表示・再接続 |
| 8 | [M1] Docs/Scripts: マルチプロセス手動検証手順とコマンドライン引数 | M1 | #7 | worker | S | `-tq-host`/`-tq-join`/`-tq-port`/`-tq-name` パース実装、手動検証手順を dev-workflow.md に追記（L-9: 実装は `-tq-role`/`-tq-code` ではなく `-tq-host`/`-tq-join` を採用。docs/network.md §10.3・issue #8 コメント参照） |
| 9 | [M2] Questions: JSON スキーマ読み込み | M2 | なし | worker | M | `QuestionRepository` が sample-questions.json を読み込み、バリデーション・EditMode テスト通過 |
| 10 | [M2] Core: AnswerNormalizer | M2 | なし | worker | S | question-data.md §5 の正規化10例をテストケース化し全通過 |
| 11 | [M2] Core: BuzzArbiter | M2 | なし | implementer | M | network.md §6.3/§6.4 の擬似コードを実装、境界値・同着抽選のテスト通過 |
| 12 | [M2] Network: GameSession ステートマシン（1問・freeTextのみ） | M2 | #2, #9, #11 | implementer | L | `QuizPhase` 遷移が network.md §6.6 に一致、フェーズは NetworkVariable |
| 13 | [M2] Network: 問題配信（正解を含まないDTO、テキストのみ） | M2 | #9, #12 | implementer | M | `QuestionDistributor` が answers/correctIndex を含まないDTOのみ配信 |
| 14 | [M2] UI: Game View 最小版 | M2 | #4, #12, #13, #10 | worker | M | 問題文表示、早押しボタン/Space、自由入力、結果表示の最小 UI |
| 15 | [M2] 統合: 1問プレイの縦切り確認 | M2 | #8, #14 | worker | S | 2プロセス手動検証手順を実施・記録、`scripts/verify.ps1` 通過 |
| 16 | [M3] Questions/Network: 画像分割送信 | M3 | #13 | implementer | L | MaxPayloadSize 32768、16KBチャンク、Ack・再送、先読み動作 |
| 17 | [M3] UI: 選択式の表示とシャッフル | M3 | #14 | worker | M | 選択肢表示シャッフル、元 index への変換、判定は元 index |
| 18 | [M3] Core/Room: 得点計算・お手つきペナルティ | M3 | #11, #12 | implementer | M | score.* 既定値の加減点、次問休み/減点切替、誤答後再開放 |
| 19 | [M3] Network: 複数問進行・出題フィルタ・タイムアウト制御 | M3 | #12, #18 | implementer | L | 複数問連続進行、フィルタ・出題数・シャッフル反映、各制限時間タイムアウト |
| 20 | [M3] UI: 結果画面・司会専用モード操作 | M3 | #19, #4 | worker | M | 順位表示、司会「次へ/一時停止/強制正解・不正解」操作 UI |
| 21 | [M4] Tts: voicevox_core P/Invoke バインディング | M4 | なし | implementer | L | tts-native-api.md の呼び出し順を実装、External 有無で PlayMode テスト分岐 |
| 22 | [M4] Tts: TtsService（スタイル解決・合成・キャッシュ） | M4 | #21 | implementer | M | ResolveStyleId フォールバック順、SHA-256 キャッシュキー、LRU 追い出し |
| 23 | [M4] Tts+Network: 同期再生 | M4 | #22, #13 | implementer | M | Ready→playAtServerTime→PlayScheduled、tts.enabled/speed 反映 |
| 24 | [M4] UI: 立ち絵 CharacterView | M4 | #23, #4 | worker | M | 待機/読み上げ中/正解/不正解のスプライト切替、ITtsPlaybackObserver 購読 |
| 25 | [M4] Tts: 欠落時のフォールバックUI | M4 | #21, #22 | worker | S | Diagnose() の結果に応じた案内画面、読み上げ無効化してもゲーム続行 |
| 26 | [M5] Room: RoomSettings/RoomPreset | M5 | #18, #19 | worker | M | 全キーの型・既定値・範囲・バリデーション、組み込み3プリセット、保存/読込 |
| 27 | [M5] Network: RoomSettings 同期・ロック | M5 | #26, #12 | implementer | S | ゲーム開始時に確定・ロック、NetworkVariable で読み取り専用同期 |
| 28 | [M5] UI: Settings View | M5 | #26, #4 | worker | M | 全設定項目の UI、プリセット選択・保存・警告表示 |
| 29 | [M5] Questions: 問題フォルダ監視/再読込 | M5 | #9 | worker | S | 「フォルダを開く」「再読込」ボタン、読み込みエラー一覧表示 |
| 30 | [M5] UI: 問題エディタ（セット一覧・問題一覧） | M5 | #9, #4 | worker | M | セット一覧・問題一覧表示、新規/削除/複製、並び替え |
| 31 | [M5] UI: 問題エディタ（編集フォーム・画像選択・バリデーション） | M5 | #30 | worker | M | freeText/choice切替フォーム、画像簡易選択UI、保存前バリデーション表示 |
| 32 | [M5] UI: 問題エディタ（保存・読み上げプレビュー） | M5 | #31, #22 | worker | S | JSON保存（半角スペース2個整形）、TTS試し読みボタン |
| 33 | [M6] UI: クレジット/ライセンス画面 | M6 | #4 | worker | S | licenses.md §12 の順序・確定文言を表示 |
| 34 | [M6] Build: THIRD-PARTY-NOTICES・配布zip・スプラッシュ | M6 | #33 | worker | M | 生成スクリプト、配布zipにモデル同梱、Unityスプラッシュ設定 |
| 35 | [M6] Audio: SE生成スクリプトとSE再生 | M6 | #4 | worker | S | `scripts/gen-se.py` で6種のSE生成・コミット、ゲーム内再生 |
| 36 | [M6] Docs: README仕上げ・規約要確認項目の消化 | M6 | #34 | worker | S | スクリーンショット・Tailscale手順検証、licenses.md未確定事項の消化 |

## 判断に迷った点（後述の「報告」にも記載）
- 参加コードの新チェック方式（`V mod 1021`）は 2026-09-13 に docs/network-joincode.md へ反映済み（テストベクタも
  Python で再計算済み）。issue #1 は同書 §1.7 のテストベクタ・§1.5 の擬似コードを参照する。
- RoomSettings の正式な集約（#26/#27）は M5 に置いたが、M2〜M3 の GameSession 等は先に最小限の設定値（既定値埋め込み）で
  動作させ、M5 でその参照先を RoomSettings に差し替える設計とした（依存関係を M1〜M3 で RoomSettings 待ちにしないため）。
- 問題エディタは 3 分割（#30/#31/#32）とした。

## 追加 issue（2026-09-13）
- #37 [M4] UI/Core: 利用規約の同意画面と同意記録（ConsentStore）— 依存 #4、#33 と規約テキスト共有、worker、size M。ユーザー要望（アプリ内で外部規約を提示し「同意します」を記録、後から本人が確認可能）
