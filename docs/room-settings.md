# ルーム設定

## 目的
ホストが変更できるルーム設定の全項目・キー名・型・既定値・範囲・プリセットの仕様を定義し、`RoomSettings` / `RoomPreset`（Room asmdef）の実装とロビー UI の実装が同じ前提に立てるようにする。

## 関連ドキュメント
- [requirements.md](./requirements.md) — FR-60/FR-61（ルーム設定・プリセット）
- [architecture.md](./architecture.md) — `RoomSettings` / `RoomPreset`（Room asmdef）
- [question-data.md](./question-data.md) — 出題形式・タグ・画像の定義（フィルタ項目の対象）
- network.md — 設定のクライアントへの同期方式（NetworkVariable / 開始時一括送信、別担当作成）

---

## 0. 「ルーム設定」と「アプリ設定」の区別

- **ルーム設定**: ホストが決めて、ゲーム開始時に全クライアントへ読み取り専用として同期する値（§4 参照）。本書 §1 に列挙する項目はすべてルーム設定であり、プリセット（§3）の対象になる
- **アプリ設定**: 各 PC がローカルに持つ値。クライアントへの同期は行わない。保存先は `Application.persistentDataPath/app-settings.json`（仮決め: 本ドキュメント）
- network.md・tts.md からの追加提案キー（ルーム設定・アプリ設定の両方を含む）は §2 にまとめる

---

## 1. 設定項目一覧

キーは JSON のドット記法で表す（実装上のクラス構造とは対応させつつ、プリセット JSON のフラットなキーとして扱う）。
本節はほとんどがルーム設定だが、**プレイヤー**の節のみ例外的にアプリ設定を含む（§0 の区別・種別の列を参照）。

### プレイヤー（アプリ設定、issue #6 で追加）

| キー | 種別 | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|---|
| `player.name` | アプリ設定 | string | `""` | 1〜16 文字。制御文字・見えない文字・基底文字 1 つあたり 5 個以上の結合記号は不可（`TsumugiQuiz.Core.Network.PlayerNameValidator`、docs/network.md §9.2） | 参加コード入力画面（Join View）・ホスト開始時に使うプレイヤー名。前回入力値を次回起動時の初期値にする。**保存先は本書 §0 の `app-settings.json`**（#28 で `PlayerPrefs` から移行済み。§7.1 を参照。旧キー `TsumugiQuiz.PlayerName` からは初回読み込み時に一度きり移行する） |

### ホスト・ルーム

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `host.role` | string | `"player"` | `"player"` \| `"moderator"` | ホストがプレイヤーを兼ねるか、司会専任（司会専用モード、FR-80）か |
| `room.maxPlayers` | integer | `6` | `2`〜`12` | 最大参加人数（ホストが `moderator` の場合、ホストはこの人数に含まない） |

> **ホストを開始する前の保存先（#5、issue #155 で `app-settings.json` へ移行）**: `host.role` は
> HostSetup View（#5）の「司会専任」トグルの値として、`AppSettingsStore`（`app-settings.json` の
> `host.role` キー、値は本書と同じ `"player"` / `"moderator"`）に保存する。以前は `PlayerPrefs`
> （Windows ではレジストリ）へ暫定保存していたが、同一 PC の複数インスタンス・別データルート
> （`-tq-data-root` / `run-multi.ps1` の固定パス運用）で値が共有されてしまう不具合があったため、
> 他のアプリ設定と同じ保存先へ移した（issue #155、実測: docs/tasks/m2-verification.md「Run B」）。
> 既存の `PlayerPrefs` の値は初回読み込み時に一度だけ引き継ぎ、以後は参照しない
> （詳細は §7.1 を参照）。ホスト開始後の権威は `RoomSettings`（Room asmdef）とプリセット（§3）に
> 一本化されており、本項目はあくまで「次にホストを開始するときの初期値」として使う。
> `room.maxPlayers` は #5 の時点では未接続だったが、#7 でルーム設定と接続済み。

### 問題選択

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `questions.setIds` | string[] | `[]`（空 = 読み込み済み全セット対象） | - | 出題に使う問題セットの `setId` の配列 |
| `questions.typeFilter` | string | `"both"` | `"freeText"` \| `"choice"` \| `"both"` | 出題形式フィルタ |
| `questions.imageOnly` | boolean | `false` | - | true の場合、`imagePath` を持つ問題のみ出題する（**仮決め: 本ドキュメント**） |
| `questions.tagFilter` | string[] | `[]`（空 = フィルタなし） | - | 指定したタグのいずれかを持つ問題のみ出題する |
| `questions.count` | integer | `10` | `0`〜（フィルタ後の候補数が上限）、`0` = 全問 | 1ゲームあたりの出題数 |
| `questions.shuffleOrder` | boolean | `true` | - | 出題順をシャッフルするか（**仮決め: 本ドキュメント**。既定は毎回新鮮に感じられるよう true とした） |

> **実装（#19）**: 本節の 6 項目は `TsumugiQuiz.Room.QuestionSelectionSettings`（不変）として実装し、
> 適用は `TsumugiQuiz.Network.QuestionSelector` が行う。適用順は
> 「候補プールの平坦化 → `setIds` → `typeFilter` → `imageOnly` → `tagFilter` → `shuffleOrder` → `count` で切り詰め」。
> シャッフルは `TsumugiQuiz.Core.SeededRandom`（SplitMix64）による Fisher-Yates で、
> **同じシード・同じ候補なら必ず同じ並び**になる（`System.Random` を使わないのは実行環境で並びが変わりうるため）。
> `setIds` は問題セットの一覧を渡した場合にのみ効く（`GameSession.SetQuestionSets`。
> 供給元だけを渡した場合は警告ログを出して無視する）。プリセット JSON からの読み込みと
> 範囲外値のクランプは #26（`RoomSettings`）で行う。
>
> **重複 `id` は許容する**（#19）: 複数のセットに同じ `id` の問題が含まれていても、出題候補では別の問題として扱い
> 取り除かない（`setId` が違えば別の問題と見なすのが自然で、`id` の一意性はセット内でのみ保証される）。
> 同じ問題を 2 回出したくない場合は `questions.setIds` で対象セットを絞る。
>
> **選択式の進行（#17 で実装済み）**: `typeFilter` が `"both"` / `"choice"` の場合、選択式の問題も
> freeText と同様に出題候補へ含める（以前は #17 まで一律で除外していた）。選択式は早押しを介さず、
> `answer.choiceTimeLimitSec` の間に全員が選択でき、時間切れで一斉に判定する（確定: #17、2026-09-18 ユーザー承認、§6.6・
> `QuizPhase.ChoiceAnswering`）。詳細は network.md §1.3 / §6.6、question-data.md §6 を参照。

### 結果表示

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `result.autoAdvanceSec` | number | `5` | `0`〜`60`、`0` = 手動 | 1 問の結果表示から次の問題へ自動で進むまでの秒数。`0` の場合は自動で進まず、司会の「次へ」操作（#20）を待つ（#19 で追加。**既定 5 秒・`0` = 手動で確定**（統括判断 2026-09-13）。5 秒は「正解と得点を読み取れて、かつ間延びしない」長さとして採用した） |

> **実装（#19）**: `TsumugiQuiz.Room.SessionSettings.ResultAutoAdvanceSec` として持ち、
> サーバー（`GameSession`）がネットワーク tick ごとに「Result に入ってからの経過時間」を見て
> `NextQuestion()` を呼ぶ。全問終了していれば代わりに `Finished` へ進み最終得点を配信する。

### 表示（issue #194）

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `display.showScores` | boolean | `true` | - | Game 画面の参加者パネル（左列）に全員の得点を表示するか（既定は「表示する」でユーザー承認済み。キー名は統括判断 #194） |

> **見せ方の切り替えであり、秘匿ではない**。得点表（`GameSession` の `NetworkList<ScoreEntry>`、docs/network.md §1.2）は
> 設定に関わらず全クライアントへ同期されている。この設定は表示を変えるだけで、改造したクライアントなら得点を読める。
>
> 効く範囲（統括判断 #194）:
> - **Game 画面の参加者パネルだけ**に効く。**Result 画面の順位表には効かない**（全問終了後の最終得点は常に表示する）
> - **司会（`host.role = "moderator"`）には設定に関わらず得点を表示する**（司会は進行を把握する役のため）
> - 自分の得点（Game 画面のヘッダー）は従来どおり常に表示する
> - 押下順（早押しの集計窓での順位）・回答順（その問題で回答権を得た順番）と、未回答者（回答権の有無・選択式の回答済み）は
>   この設定の対象外で、常に表示する（ユーザー承認済み）
>
> 実装は `TsumugiQuiz.Room.RoomSettings.ShowScores`（`WithShowScores`）。`RoomSettingsPayload` でクライアントへ同期する。
> 組み込みプリセット（§3）はいずれも既定値（表示する）のまま。設定画面（ルーム設定タブ）の「表示」パネルにトグルがある。

### 選択式

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `choices.shuffleDisplay` | boolean | `true` | - | 選択肢の表示順をシャッフルするか。判定は常に元 `correctIndex` で行う（question-data.md §6）（**仮決め: 本ドキュメント**。#17 では `TsumugiQuiz.UI.Views.Game.ChoiceShuffle.DefaultShuffleDisplay` の定数として実装し、設定との接続は #26） |

### 制限時間（仮決め K20 の既定値）

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `buzz.timeLimitSec` | integer | `10` | `1`〜`60` | 読み上げ完了後、早押し受付が開いてから誰も押さない場合のタイムアウト秒数 |
| `answer.freeTextTimeLimitSec` | integer | `15` | `1`〜`60` | 早押し後、自由入力の回答を入力できる制限時間 |
| `answer.choiceTimeLimitSec` | integer | `20` | `1`〜`60` | 選択式の回答（早押しなしで全員が回答する形式）の制限時間。#17 で `QuizTimeLimits.ChoiceTimeLimitSec` として実装（確定: #17、2026-09-18 ユーザー承認、network.md §6.6） |

### 得点（仮決め K19 の既定値）

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `score.correctPoints` | integer | `10` | `0`〜`100` | 正解時の加点 |
| `score.incorrectPoints` | integer | `0` | `-100`〜`100` | 誤答時の得点変化（既定は変化なし） |
| `score.penaltyType` | string | `"skipNext"` | `"skipNext"` \| `"minusPoints"` \| `"none"` | お手つき時のペナルティ種別（次問休み / 減点 / ペナルティなし）。`"none"` は誤答しても `incorrectPoints` の変化だけで、次問休みにも減点にもしない（組み込みプリセット「のんびり」で使う）。選択式（`choice`）で不正解を選んだ場合も同じ設定を適用する（確定: #17、2026-09-18 ユーザー承認。統括判断（b）、network.md §6.6） |
| `score.penaltyMinusPoints` | integer | `-5` | `-100`〜`0` | `penaltyType` が `"minusPoints"` のときの減点幅 |

### 読み上げ

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `tts.enabled` | boolean | `true` | - | 読み上げ ON/OFF |
| `tts.speed` | number | `1.0` | `0.5`〜`2.0` | 読み上げ速度 |

### 問題文の表示（issue #144、**2026-09-30 ユーザー承認済み**）

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `question.revealMsPerChar` | integer | `80` | `0`〜`500`、`0` = 一括表示 | 自由入力（早押し）形式の問題文を**固定速度で文字送りする**ときの 1 文字あたりのミリ秒（requirements.md FR-43）。部屋として読み上げが無い場合（`tts.enabled = false`、または再生開始時刻が届かないまま早押し受付が開いた）に使う。読み上げを行う部屋では、自分の PC で音が鳴らない（同意撤回・読み上げ未準備）場合も含めてホストの読み上げ時間の按分が優先され、この値は使わない。読み上げを行う部屋で時間軸が取れない場合は `max(この値, 250)` を使う（下限 250ms/文字は 2026-09-30 ユーザー承認済み。docs/tts.md §6.8）。`0` にすると文字送りせず従来どおり全文を一括表示する |

> **既定 80ms/文字・範囲 0〜500 は 2026-09-30 ユーザー承認済み**。80ms/文字は issue #144 の提案値。
> 上限 500ms は「1 文字 0.5 秒より遅いと待たされる印象が強い」ことからの値で、範囲外の値は
> `RoomSettingsValidator` が警告付きでクランプする。組み込みプリセット（§3）はいずれも既定値のまま。
> 実装は `TsumugiQuiz.Room.RoomSettings.QuestionRevealMsPerChar`（既定値・範囲の出典は
> `TsumugiQuiz.Core.Reveal.QuestionRevealSchedule`）で、`RoomSettingsPayload` によりクライアントへ同期される。
> 設定画面（ルーム設定タブ）の「読み上げ」パネルに入力欄がある。
> キー名の接頭辞 `question.` は §2 のアプリ設定 `question.prefetchCount` と同じだが、こちらはルーム設定である。

### 早押し詳細

| キー | 型 | 既定値 | 範囲 | 説明 |
|---|---|---|---|---|
| `buzz.allowDuringReading` | boolean | `true` | - | 読み上げ中でも早押し可能か（仮決め K12。false の場合は読み上げ完了後のみ受付開始） |
| `buzz.collectWindowMs` | integer | `150` | `50`〜`500` | 最初の押下受信からの集計窓（ミリ秒、docs/network.md §6.3）。上級設定（既定値からの変更は非推奨のため UI 上は「詳細設定」に隠す） |
| `answer.singleAttemptOnly` | boolean | `true` | - | 早押し後の回答入力を1回のみに制限するか（**仮決め: 本ドキュメント**。誤入力の再送信は不可とする）。**`false` の意味（制限時間内なら誤答後に送信し直せる）も仮決めで、#18 では未実装**（現状は値に関わらず常に 1 回のみ）。本人へ「誤答なのでもう一度」を伝える通知 RPC が必要になるため、#26 でそれと合わせて実装する。**設定画面には表示しない（#221、機能を実装するまで）**。JSON・プリセットのキーは互換のため残し、読み込みは受け付け、設定画面から適用・保存しても値は保たれる |
| `buzz.reopenAfterWrongAnswer` | boolean | `true` | - | 誤答・お手つき発生後、残り時間があれば他プレイヤーに早押しを再開放するか（既定は「する」。setup-brief.md §0 の確定動作） |

---

## 2. network.md / tts.md からの追加提案キー（統括承認済み、2026-09-13）

network.md §11 と tts.md §12 で提案されたキーをすべて承認した。`tts.readyTimeoutMs` と `tts.leadTimeSec` は両ドキュメントに重複して登場するため 1 行にまとめる。

| キー | 種別 | 型 | 既定 | 範囲 | 説明 | 出典節 |
|---|---|---|---|---|---|---|
| `network.port` | アプリ設定 | integer | `7777` | `1024`〜`65535` | ホストの待ち受けポート | network.md §2.1 |
| ~~`network.tickRate`~~ | **設定項目から削除**（統括判断、2026-09-17） | integer | `30` 固定 | — | NGO の `NetworkConfig.TickRate`。**ユーザーからは変更できない**（下の備考を参照）。既存の `app-settings.json` に残っていた場合は `AppSettingsValidator` が警告を出して無視する | network.md §7.1 / §12.6 |
| `network.ipLookupUrls` | アプリ設定 | string[] | `["https://api.ipify.org"]` | https の絶対 URL のみ（それ以外は無視して既定値にフォールバック） | グローバル IP 確認サービスの URL（先頭から順に試す） | network.md §4 / network-nat.md §2 |
| `network.allowLateJoin` | ルーム設定 | boolean | `false` | — | ゲーム進行中の途中参加を許可するか（司会が実行中に切替可能）。#19 で `SessionSettings.AllowLateJoin` として保持し、`GameSession.ResyncClient(clientId)` で現在状態と現在問のデータを送り直せるようにした。接続そのものの可否判断はロビー（#7） | network.md §2.3 |
| `upnp.enabled` | アプリ設定 | boolean | `true` | — | UPnP / NAT-PMP による自動ポート開放を試みるか | network-nat.md §1 |
| `upnp.discoveryTimeoutMs` | アプリ設定 | integer | `5000` | `1000`〜`30000` | NAT デバイス探索のタイムアウト | network-nat.md §1.3 |
| `upnp.mappingLifetimeSec` | アプリ設定 | integer | `3600` | `0`〜`86400` | ポートマッピングの有効期間（`0` = 無期限） | network-nat.md §1.3 |
| `upnp.renewIntervalMs` | アプリ設定 | integer | `1800000` | `10000`〜`upnp.mappingLifetimeSec` × 1000 ÷ 2（lifetime が `0` = 無期限のときは上限なし） | マッピングの更新間隔（30 分）。更新前に lifetime が切れて接続不能にならないよう、読み込み時に lifetime の半分以下へ丸める | network-nat.md §1.3 |
| `question.prefetchCount` | アプリ設定 | integer | `1` | `0`〜`3` | 先読みする問題数 | network.md §8.1 |
| `tts.readyTimeoutMs` | ルーム設定 | integer | `3000` | `0`〜`15000` | 全クライアントの TTS Ready を待つ上限（仮決め K15） | network.md §7.3 / tts.md §6.1 |
| `tts.leadTimeSec` | ルーム設定 | number | `0.3` | `0.1`〜`2.0` | `playAtServerTime` を現在のサーバー時刻からどれだけ先に置くか | network.md §7.3 / tts.md §6.1 |
| `tts.speakerName` | アプリ設定 | string | `"春日部つむぎ"` | — | 話者名。実行時にメタ情報から解決する（仮決め K16） | tts.md §4.1 |
| `tts.styleName` | アプリ設定 | string | `"ノーマル"` | — | スタイル名。同上 | tts.md §4.1 |
| `tts.cacheMaxBytes` | アプリ設定 | integer | `209715200`（200MB） | `0`〜 | キャッシュ合計サイズの上限 | tts.md §7.3 |
| `tts.cacheMaxEntries` | アプリ設定 | integer | `5000` | `0`〜 | キャッシュのエントリ数上限 | tts.md §7.3 |
| `tts.assetPathOverride` | アプリ設定 | string | `""` | — | 辞書・モデルの探索パスを手動指定（開発時に External を直接指す用途） | tts.md §10.4 |
| `result.autoAdvanceSec` | ルーム設定 | number | `5` | `0`〜`60`、`0` = 手動 | 結果表示から次の問題へ自動で進むまでの秒数（§1「結果表示」。#19 で追加し統括承認済み） | network.md §6.6 |
| `character.enabled` | アプリ設定 | boolean | `true` | — | 立ち絵を表示するか（#24 の `CharacterView`）。各 PC のローカルな表示設定であり、クライアントへ同期する必要が無いためアプリ設定とした（統括判断、2026-09-14） | #24（`CharacterView`）。#28 で `GameView.Character.cs` に接続済み |

備考:
- 早押し判定の `tieEpsilonSec`（同着とみなす閾値 1ms）はユーザーが変える意味のない値なので設定項目にせず、定数にする（network.md §6.3）
- **`network.tickRate` は設定項目から外し、`30` 固定にする（統括判断、2026-09-17。PR #92 Phase 2）**: NGO 2.13.2 の `NetworkConfig.GetConfig()`（`Runtime/Configuration/NetworkConfig.cs`）は接続時の設定ハッシュに `TickRate` を含めており、そのハッシュは `ConnectionRequestMessage` の `CompareConfig` で照合される。`network.tickRate` はアプリ設定（各 PC ローカル）でクライアントへ同期されないため、ユーザーが変更できると**値の違う参加者が接続できなくなる**。実値は `Boot.unity` の `NetworkManager.NetworkConfig.TickRate = 30` のみが持ち、`AppSettings` からは `NetworkTickRate` プロパティごと削除した（`AppSettings.FixedNetworkTickRate` は警告メッセージ用の定数）
- **`network.maxPayloadSizeBytes` / `network.imageChunkBytes` は設定項目にしない（統括判断、2026-09-13）**: `UnityTransport.MaxPayloadSize` は Transport の設定であり、ホスト・クライアントが接続を確立する前に双方で値が一致している必要がある。NGO/UTP には接続後にこの値を動的に変更・同期する仕組みがないため、アプリ設定・ルーム設定として実行時に配っても意味がない。代わりに `TsumugiQuiz.Network.NetworkConstants.MaxPayloadSizeBytes = 32768` / `NetworkConstants.ImageChunkBytes = 16384` の定数として固定する（詳細: network.md §8.3・§11）
- `network.allowLateJoin` をルーム設定にしたのは、途中参加の可否がロビー内の全プレイヤーに関わる room ポリシーであるため。それ以外の `network.*` / `upnp.*` / `question.prefetchCount` は、ホスト機（またはクライアント機）のローカルな技術的挙動であり、クライアントへ同期する必要がないためアプリ設定とした
- 「ルーム設定」欄の行は §4 の同期・確定タイミングの対象に、「アプリ設定」欄の行は本書 §0 のアプリ設定ファイルの対象になる
- **`app-settings.json` は `schemaVersion` を持たない（#26 統括判断 L9）**。プリセット JSON（§3）と異なり、
  キー単位で読み取り・欠損時は既定値にフォールバックする実装（`TsumugiQuiz.Room.AppSettingsStore` /
  `AppSettingsValidator`）のため、ファイル全体のスキーマバージョンによる一括の互換性判定は今のところ不要。
  将来キーの意味が非互換に変わる場合にどう移行するかは #28 で検討する
- **`host.role`（§1 の表）は本表には載せていないが、issue #155 で `app-settings.json` にも同名のキーを持つ**。
  上表の「種別」列の分類（ルーム設定 / アプリ設定）はどちらのカテゴリの話も一元管理するためのものだが、
  `host.role` は「ゲーム進行中の権威はルーム設定（`RoomSettingsSync`）」「ホストを開始する前の初期値は
  `app-settings.json`（`AppSettings.HostRole`）」という 2 つの顔を持つ唯一の例外である（§7.1 参照）

---

## 3. プリセット

### 保存先・スキーマ

- 保存先: `%USERPROFILE%\Documents\TsumugiQuiz\Presets\<name>.json`
- スキーマ:

```json
{
  "schemaVersion": 1,
  "name": "プリセット名",
  "settings": {
    "host.role": "player",
    "room.maxPlayers": 6
  }
}
```

- `settings` は本書 §1 のキーのうち、既定値から変更したいものだけを含めばよい（未指定のキーは既定値が使われる）。ただし組み込みプリセットは全項目を明示的に持つ

### 組み込みプリセット

| プリセット名 | 特徴 | 主な設定値（既定値からの差分） |
|---|---|---|
| 標準 | 本書の既定値そのまま | 差分なし（全項目デフォルト） |
| 早押し重視 | 読み上げ完了後のみ受付、短い制限時間でテンポよく | `buzz.allowDuringReading=false`, `buzz.timeLimitSec=6`, `answer.freeTextTimeLimitSec=8`, `answer.choiceTimeLimitSec=10`, `score.penaltyType="minusPoints"`, `score.penaltyMinusPoints=-5`, `result.autoAdvanceSec=3` |
| のんびり | 長い制限時間、誤答ペナルティなし | `buzz.timeLimitSec=20`, `answer.freeTextTimeLimitSec=30`, `answer.choiceTimeLimitSec=40`, `score.incorrectPoints=0`, `score.penaltyType="none"`, `buzz.reopenAfterWrongAnswer=true`, `result.autoAdvanceSec=8` |

上記の具体的な数値配分は統括による暫定案（**仮決め: 本ドキュメント**）。ユーザーの実プレイ感覚に応じて調整可能。

### プリセットを選んでからクライアントへ届くまで（#27）

ロビーの設定 UI（#28）がプリセットや個別の項目を編集して `RoomSettings` を組み立てたら、
**ホストで・ゲーム開始前に** `TsumugiQuiz.Network.RoomSettingsSync.TrySetSettings(RoomSettings)` を呼ぶ。
これが唯一の書き込み口で、成功すると `NetworkVariable<RoomSettingsPayload>` 経由で全クライアントへ配られる。

| API | 呼べる条件 | 説明 |
|---|---|---|
| `RoomSettingsSync.TrySetSettings(RoomSettings)` | ホスト（サーバー）かつロック前 | ルーム設定を差し替える。クライアントから・ロック後は `false` を返して警告ログを出す |
| `RoomSettingsSync.Current` | 全ピア | いま有効な `RoomSettings`（null にならない） |
| `RoomSettingsSync.IsLocked` | 全ピア | ゲーム開始で確定済みか |
| `RoomSettingsSync.SettingsChanged` / `LockChanged` | 全ピア | 変更通知（UI の再描画用）。**スポーン時の初期状態では発火しない**ので、初期表示は `Current` / `IsLocked` を読む |
| `RoomSettingsSync.LastValidationWarnings` | 全ピア | 直近の検証で出た警告（クランプ・未知の列挙値） |
| `RoomSettingsSync.Unlock()` | ホスト、かつ進行中でない（`GameSession.ServerPhase` が `Lobby` / `Finished`） | ロック解除（ロビーへ戻ったとき、#20）。前回の進行設定も捨てる。**進行中に呼ぶと警告ログを出して `false`** |

`NetworkVariable` 本体は `internal` で、UI 層からは触れない（上表の API だけを使う）。

`RoomSettingsSync` は `GameSession` プレハブに載っており、`NetworkService.StartHost` と同時にスポーンされるため、
ロビー表示中から `NetworkService.FindActiveGameSession()?.SettingsSync` で取得できる。
詳細な仕様は [network.md](network.md) §12 を参照。

> **アプリ設定は対象外**: §0/§2 で「アプリ設定」に分類した行（`question.prefetchCount` / `upnp.*` /
> `network.port` など）は `RoomSettingsSync` では配らない。各 PC がローカルに持ち、
> `AppSettings` から直接適用する（`app-settings.json` の読み込み配線は #28）。

---

## 4. 設定の適用タイミング

- ロビー画面（`Lobby` View）ではホストがいつでも設定を変更できる
- 「ゲーム開始」操作の時点で設定値を確定（ロック）し、以後そのゲーム中は変更不可とする
- 確定した設定値はクライアントへ読み取り専用として同期する

### 実装（#27、`TsumugiQuiz.Network.RoomSettingsSync`）

同期方式は **NGO の `NetworkVariable<RoomSettingsPayload>`**（サーバー書き込み・クライアント読み取り専用）に決めた
（[network.md](network.md) §12）。ロビー中の変更が即座にクライアントへ反映され、
途中参加・再接続したクライアントにもスポーン時の同期で確定値が届くため。

- **確定点**: `GameSession.StartQuestion`（`StartSession` も 1 問目でここを通る）の全検証を通過した直後。
  サーバーが実際に使う進行設定（制限時間・得点・出題）をルーム設定へ書き戻してからロックする。
  書き戻す値も §5 の検証を通るため、**範囲内の値であれば**「クライアントが読む設定」と
  「サーバーが進行に使う設定」は一致する。範囲外の値を `GameSession.Configure` へ直接渡した場合は
  ルーム設定側（＝ クライアント表示）だけが丸められ、そのずれは警告ログに出る
  （`[GameSession] 進行設定がルーム設定の範囲外だったためクランプされました。…`、network.md §12.2）
- **ロック後**: `TrySetSettings` は拒否し、`[RoomSettingsSync] ゲーム開始で確定済みのため…` を警告ログに出す。
  ロビーへ戻る（#20 の `GameSession.ReturnToLobby`）と自動で解除され、あわせて前回の進行設定も捨てる
  （次の開始ではロビーのルーム設定が使われる）
- **検証**: ホストが `TrySetSettings` で渡す値も、クライアントが受信する値も、
  §5 のクランプ規則（`RoomSettingsValidator`）を通してから採用する。範囲外はクランプ、
  未定義の列挙値は既定値へ。ホストも同じ検証を通すので、ホストの `Current` と
  クライアントの `Current` はクランプ結果まで含めて一致する
- **同期しない項目**: `questions.setIds` / `questions.tagFilter`。
  `NetworkVariable` に載せる `INetworkSerializable` は `unmanaged` な構造体である必要があり、
  可変長の文字列配列を持てないため。どちらも「サーバーが出題列を組み立てるための入力」でしかなく、
  クライアントは使わないので、サーバー側の `RoomSettings` にのみ保持する（network.md §12.5）

---

## 5. バリデーション

- 各キーは本書 §1 の範囲外の値が指定された場合、境界値（下限/上限）にクランプし、ホスト側 UI に警告を表示する（統括判断、2026-09-14）。列挙型（`host.role` / `questions.typeFilter` / `score.penaltyType`）や型が不正な値は、境界値という概念が無いため既定値にクランプする
- `questions.setIds` に存在しない `setId` が含まれる場合はそのエントリを無視し警告する
- `questions.count` がフィルタ後の候補問題数を超える場合は候補数に丸める（0 は「全問」として扱うため丸め対象外）
- `score.penaltyMinusPoints` は `penaltyType` が `"minusPoints"` 以外（`"skipNext"` / `"none"`）のときは無視される（値は保持するが挙動に影響しない）
- **誤答時の得点変化は `score.incorrectPoints` ＋（`penaltyType` が `"minusPoints"` のときだけ）`score.penaltyMinusPoints` の合算**とする（#18 の実装。既定値では 0 + 0 = 0 で変化なし）。組み込みプリセット「早押し重視」が `incorrectPoints=0` のまま `penaltyType="minusPoints"` を指定していることから、両者は排他ではなく足し合わせる値として扱う
- **合計得点に下限（床）は設けない**（統括判断、#18）。減点が続けばマイナスの得点になりうる。0 で止めると「減点されたのに表示が変わらない」状態になり、ペナルティが機能していないように見えるため
- **得点とお手つきペナルティは席（ロビーの名簿エントリ）が持つ**（#84）。切断しても席が残っている間（docs/network.md §2.4 の 60 秒）は保持され、同じ名前 + 再接続トークン（#69）で復帰すると新しい `clientId` へ引き継がれる。保持期間切れ・トークン不一致・同名の別人の新規参加はいずれも別の席になるので引き継がない。引き継ぐもの / 引き継がないものの一覧は [docs/network.md](network.md) §2.4「再接続で引き継ぐもの / 引き継がないもの」

---

## 6. 実装（#26）

`TsumugiQuiz.Room`（`Assets/TsumugiQuiz/Scripts/Room/`）に以下を実装した。JSON 変換には
`Newtonsoft.Json`（`com.unity.nuget.newtonsoft-json`）を直接参照している。この DLL は asmdef を持たない
素のプリコンパイル済みプラグインであり、`TsumugiQuiz.Questions` と同様に asmdef の `references` へ
追加しなくても参照できるため、`RoomSettingsJson` のようなインターフェース経由の迂回は行っていない。

| クラス | ファイル | 役割 |
|---|---|---|
| `RoomSettings` | `RoomSettings.cs` | ルーム設定の全項目を持つ不変オブジェクト。`Session`（既存の `SessionSettings`）を内包し、それ以外の項目（`host.role` / `room.maxPlayers` / `choices.shuffleDisplay` / `buzz.allowDuringReading` / `tts.*`）を追加する。`answer.choiceTimeLimitSec` は #17（#79）で `QuizTimeLimits.ChoiceTimeLimitSec` として実装済みのため、`RoomSettings.ChoiceTimeLimitSec` は `Session.TimeLimits.ChoiceTimeLimitSec` に委譲する（値を二重に持たない） |
| `RoomSettingsInput` | `RoomSettingsInput.cs` | プリセット JSON から読み込む、検証前の生の値（すべて null 許容）。JSON キーは本書 §1/§2 のドット記法のまま |
| `RoomSettingsValidator` | `RoomSettingsValidator.cs` | `RoomSettingsInput` を検証し、範囲外の値をクランプして `RoomSettings` を組み立てる（§5）。`questions.setIds` の存在確認・`questions.count` の候補数丸めは `TsumugiQuiz.Network.QuestionSelector` が実行時に行うため対象外 |
| `RoomSettingsValidationResult` | `RoomSettingsValidationResult.cs` | 検証結果（`Settings` + `Warnings`） |
| `RoomPreset` | `RoomPreset.cs` | プリセット（`Name` / `SchemaVersion` / `Settings`）。組み込み3種は `RoomPreset.Standard` / `BuzzFocused` / `Relaxed`（`BuiltIns` で一覧取得） |
| `RoomPresetJson` | `RoomPresetJson.cs` | プリセット JSON（`{ schemaVersion, name, settings }`）の読み書き。壊れた JSON は例外を投げず、既定値ベースのプリセット＋警告を返す |
| `RoomPresetParseResult` | `RoomPresetParseResult.cs` | JSON 読み込み結果（`Found` / `Preset` / `Warnings`） |
| `RoomPresetStore` | `RoomPresetStore.cs` | ユーザー保存プリセットの一覧・保存・読込・削除（既定フォルダ: `%USERPROFILE%\Documents\TsumugiQuiz\Presets`） |
| `IRoomFileSystem` / `FileSystemRoomFileSystem` | `IRoomFileSystem.cs` / `FileSystemRoomFileSystem.cs` | `RoomPresetStore` / `AppSettingsStore` が使うファイル I/O の抽象と実装（テストではフェイクを注入） |
| `AppSettings` | `AppSettings.cs` | アプリ設定（§0/§2 の「アプリ設定」の行: `player.name` / `network.port` / `network.ipLookupUrls` / `upnp.*` / `question.prefetchCount` / `tts.speakerName` 等。`network.tickRate` は §2 の統括判断により削除済み）と、ホストを開始する前の `host.role`（issue #155、§7.1）の初期値を持つ。`Create` で検証・クランプする（`TsumugiQuiz.Tts.TtsSettings` と同じ方式） |
| `AppSettingsInput` / `AppSettingsLoadResult` | `AppSettingsInput.cs` / `AppSettingsLoadResult.cs` | `app-settings.json` の生の値 / 読み込み結果 |
| `AppSettingsStore` | `AppSettingsStore.cs` | `app-settings.json` の読み書き。既定の保存先は `TsumugiQuiz.Core.AppPaths.DataRoot`/`app-settings.json`（#71、`AppSettingsStore.GetDefaultFilePath()`）。`AppPaths` 自体は `Application.persistentDataPath` の値を受け取るだけで Unity API に依存しないため、`Room` 層（`Core` を参照）から直接使える。テスト・特殊用途向けに `filePath` を明示指定するコンストラクタも用意した |
| `AppSettingsValidator` / `AppSettingsValidationResult` | `AppSettingsValidator.cs` / `AppSettingsValidationResult.cs` | `AppSettingsInput` を検証し、`RoomSettingsValidationResult` と同じ構造（`Settings` + `Warnings`）でクランプ結果を返す（H1/H2）。`player.name` のみ `TsumugiQuiz.Core.Network.PlayerNameValidator.TryNormalize` で書式検証し、失敗時は空文字列 + 警告にする |
| `AppSettingsSaveResult` / `RoomPresetSaveResult` | `AppSettingsSaveResult.cs` / `RoomPresetSaveResult.cs` | `AppSettingsStore.Save` / `RoomPresetStore.Save` の結果（`Success` + `Warnings`）。ファイル I/O 例外はここに吸収し、呼び出し元へは投げない（H4） |
| `JsonInputReader` | `JsonInputReader.cs` | プリセット / アプリ設定 JSON を `JObject` からキー単位で読み取る内部ヘルパー。型が不正なキーはそのキーだけ既定値扱いにして警告を追加し（M10）、読み取らなかった未知キーも警告にする（M9） |
| `TsumugiQuiz.Core.SettingsDefaults` | `Assets/TsumugiQuiz/Scripts/Core/SettingsDefaults.cs` | `network.port` / `question.prefetchCount` / `upnp.*` / `network.ipLookupUrls` / `tts.speakerName` / `tts.styleName` / `tts.cacheMaxBytes` / `tts.cacheMaxEntries` の既定値を集約（M6）。`TsumugiQuiz.Network.Nat.NatOptions` / `TsumugiQuiz.Tts.TtsSettings` / `TsumugiQuiz.Tts.VoicevoxStyleResolver` / `TsumugiQuiz.Network.NetworkConstants` / `TsumugiQuiz.Network.QuestionDistributor` / `TsumugiQuiz.Room.AppSettings` がここを参照する |
| `QuestionTypeFilters` / `TsumugiQuiz.Core.PenaltyKinds` | `QuestionTypeFilters.cs` / `Assets/TsumugiQuiz/Scripts/Core/PenaltyKinds.cs` | `questions.typeFilter` / `score.penaltyType` の文字列キーと列挙型の相互変換（#27 で追加）。プリセット JSON（`RoomSettingsValidator`）とルーム設定の同期ペイロード（`TsumugiQuiz.Network.RoomSettingsPayload`）が同じキー定義を使うようにするため。`host.role` の `TsumugiQuiz.Core.Network.HostRoles` と同じ方針 |
| `TsumugiQuiz.Core.DocumentsPaths` | `Assets/TsumugiQuiz/Scripts/Core/DocumentsPaths.cs` | Documents フォルダ解決の共通ヘルパー（M12）。`Environment.GetFolderPath` が空文字列を返す異常系をここに集約し、`RoomPresetStore.GetDefaultFolderPath` と `TsumugiQuiz.Questions.QuestionRepository.GetDefaultQuestionsFolderPath` の両方が使う。issue #112 でテスト・検証用の差し替え口（`Configure` / `ConfigureDefault` / `-tq-documents-root` / 環境変数 `TSUMUGI_DOCUMENTS_ROOT`）を追加した（解決順は docs/question-data.md §4） |

テストは `Assets/TsumugiQuiz/Tests/EditMode/Room/`
（`RoomSettingsTests` / `RoomSettingsValidatorTests` / `RoomPresetTests` / `RoomPresetStoreTests` /
`AppSettingsTests` / `AppSettingsStoreTests` / `FileSystemRoomFileSystemTests` /
フェイク `InMemoryRoomFileSystem`）に置いた。

### 実装時の補足・確認事項（統括レビュー対応、2026-09-14）

- **`buzz.collectWindowMs` の範囲は `50〜500`**。docs/network.md §6.3 の
  「ルーム設定 `buzz.collectWindowMs` で 50〜500ms の範囲で変更できるようにする」に統一した
  （`RoomSettingsValidator.MinCollectWindowMs` / `MaxCollectWindowMs`、本書 §1 の表）。
  この既定値・範囲は `TsumugiQuiz.Core.QuizTimeLimits.MinCollectWindowMs` / `MaxCollectWindowMs` /
  `DefaultCollectWindowMs`（ミリ秒。JSON/UI 側の単位に合わせてミリ秒を一次情報にした、#26 統括判断 L2）
  から導出し、秒単位の `MinCollectWindowSec` 等は逆にミリ秒側から導出する。ミリ秒・秒の値を二重に
  持たないようにしている（M7/M8/L2）。`QuizTimeLimits` 自体のコンストラクタ検証（0 以上の有限値）は
  より緩いままにしている（早押し判定エンジンの汎用性のため。既存テストは 50ms/150ms のみ使用しており影響なし）
- **`host.role` はルーム設定として `RoomSettings` に一本化されている**（本書 §1「ホスト・ルーム」の節で種別欄が無い
  ＝ルーム設定）。ただし issue #155 で「ホストを開始する前」の初期値の保存先を `PlayerPrefs` から
  `AppSettings.HostRole`（`app-settings.json`）へ移したため、`AppSettings` にも同名のプロパティが存在する
  （§7.1 参照。権威はあくまで `RoomSettingsSync` 側で、`AppSettings.HostRole` は次にホストを開始するときの
  初期値でしかない）。`AppSettings` に新しいプロパティを追加すると
  `TsumugiQuiz.Tests.EditMode.Room.AppSettingsPropertyCanaryTests` が公開プロパティ集合の変化を検知して
  テストを失敗させる。これは「アプリ設定タブ（`SettingsView.AppTab.cs`）が UI に持たないキーを
  `PreserveKeysNotEditedHere` で保護し忘れると、保存のたびに既定値へ黙って戻ってしまう」回帰
  （`host.role` 追加時に実際に発生し、本 PR で修正した）を防ぐためのカナリアテストである
- **`character.enabled`（アプリ設定）を追加した**。§0/§2 のアプリ設定表を参照。#24 の `CharacterView` は
  #28 でこの値を参照する（`TODO(#28)`）
- **既定値の重複解消（M6）**: `TsumugiQuiz.Core.SettingsDefaults` に集約した値以外（Min/Max の範囲、
  `upnp.renewIntervalMs` のクランプ計算式）は各層に残したまま（統括判断の対象が既定値の重複のみのため）。
  `AppSettings`（Room 層のデータ）を実際に `TsumugiQuiz.Tts.TtsSettings` / `TsumugiQuiz.Network.Nat.NatOptions`
  に変換して適用するアダプタは、Tts / Network asmdef が Room を参照する配線と合わせて #28 で作る
  （現時点では Tts asmdef は Room を参照していない。Network asmdef は Room を参照済みなので、
  `network.*` / `upnp.*` 側のアダプタは #27/#28 のどちらでも実装可能）
- **ファイル I/O のアトミック化（H3）**: `FileSystemRoomFileSystem.WriteAllText` は一時ファイルに書き切ってから
  `File.Replace` / `File.Move` で置き換える（`TsumugiQuiz.UI.JsonConsentStorage.Save` と同じ方式）
- **I/O 例外の扱い（H4）**: `AppSettingsStore.Load/Save` と `RoomPresetStore.Load/Save/Delete/ListUserPresetNames`
  は `IOException` / `UnauthorizedAccessException` を捕捉し、例外を投げず「既定値 + 警告」（Load）
  または「`Success = false` + 警告」（Save、`AppSettingsSaveResult` / `RoomPresetSaveResult`）を返す
- **`schemaVersion` の扱い（H5）**: 読み込み時に `RoomPreset.CurrentSchemaVersion`（＝ 1）以外
  （欠落時は 0 扱い）の場合は、移行手段が無いため `settings` の中身を信用せず既定値へフォールバックし警告する。
  保存時は `RoomPreset.SchemaVersion` の値に関わらず、必ず `RoomPreset.CurrentSchemaVersion` を書き出す
- **未知キー・型不一致からの回復（M9/M10）**: プリセット / アプリ設定 JSON は `JsonInputReader` で
  キーごとに読み取る。型が期待と異なるキーはそのキーだけ既定値になり警告を追加する（他の正しいキーは失われない）。
  スキーマに無い未知キーも無視した旨を警告する
- **`HostRoles.TryParse`（M13）**: `host.role` の文字列判定ロジックを `TsumugiQuiz.Core.Network.HostRoles` に
  一本化した（`RoomSettingsValidator` は `TryParse` を呼ぶだけ）
- **予約デバイス名・長さ上限（L16）**: `RoomPresetStore` はプリセット名の保存時に Windows の予約デバイス名
  （`CON` / `PRN` / `AUX` / `NUL` / `COM0`-`9` / `LPT0`-`9`、大文字小文字を区別しない）と
  100 文字（`RoomPresetStore.MaxNameLength`）を超える名前を拒否する
- **`AppPaths` 未設定時の制約（L18）**: `AppSettingsStore.GetDefaultFilePath()`（＝ `AppPaths.Combine` 経由）は、
  `AppPaths.Configure` / 環境変数 `TSUMUGI_DATA_ROOT` / `AppPaths.ConfigureDefault` のいずれも
  設定されていない状態で呼ぶと `InvalidOperationException` を投げる（#71 の仕様どおり）。
  通常は Boot（`TsumugiQuiz.Network.AppPathsBootstrap`）が起動時に解決するため問題にならないが、
  Boot を経由しないテスト等では呼び出し側が先に `AppPaths.Configure` / `ConfigureDefault` を呼ぶこと
- `GameSession` / `LobbyState` / `TtsSyncCoordinator` への実際の配線（`Configure` / `StartSession` /
  `ConfigureRoom` への値渡し、`ReadingEnabled` / `Speed` / `ReadyTimeoutSec` / `LeadTimeSec` への接続）は
  **#27 で実装した**（`TsumugiQuiz.Network.RoomSettingsSync` / `RoomSettingsPayload` / `RoomSettingsApplier`、
  本書 §4 の「実装（#27）」と [network.md](network.md) §12）

### 2 回目のレビュー対応（統括判断、2026-09-14）

- **数値の範囲外・巨大整数からの回復（H1）**: `JsonInputReader.GetInt`/`GetLong`/`GetDouble` は、
  Newtonsoft が `long` の範囲外の整数を `System.Numerics.BigInteger` として保持するケースを明示的に検出し、
  `OverflowException`/`InvalidCastException` を投げずにそのキーだけ既定値+警告にする
  （`{"network.port": 99999999999}` のような入力でも例外を投げない）
- **`FileSystemRoomFileSystem.WriteAllText` の失敗時の後始末（M1）**: `File.Replace`/`File.Move` が失敗した場合、
  残った `.tmp` を削除してから元の例外を再スローする（削除自体の失敗は元の例外を隠さない）
- **`player.name` の正規化を `AppSettings.Create` 自体に持たせた（M2）**: `PlayerNameValidator.TryNormalize` を
  `Create` 内で直接呼ぶことで、`AppSettingsValidator` を経由しない直接呼び出し（`Create`/`WithPlayerName`）でも
  不正な値が素通りしない。`AppSettingsValidator` は警告メッセージの生成（差分比較）のみを担当する
- **軽微な修正**: 本書の記法統一（L1: XML doc の `<c>` タグではなく Markdown のバッククォートを使う）、
  ミリ秒/秒の定数導出方向の統一（L2、上記参照）、プリセット名の末尾ドット・空白の拒否（L4）、
  `RoomPresetStore.Delete` の戻り値の意味（ファイル無し/I/O失敗を区別しないこと）と
  `ArgumentException` を投げる条件の明記（L5）、I/O 例外の警告メッセージに例外の型名
  （`ex.GetType().Name`）を含めるようにした（L7）、`app-settings.json` は `schemaVersion` を
  持たない設計であることの明記（L9、上記の備考を参照）

### 3 回目のレビュー対応（develop の #79 取り込みに伴う整合、統括判断、2026-09-14）

develop に #79（選択式の表示・シャッフル、`QuizTimeLimits.ChoiceTimeLimitSec` と4引数コンストラクタを追加）が
入ったため、本 PR で `RoomSettings` 自身が持っていた `answer.choiceTimeLimitSec` の値と重複した。
以下のとおり統合した。

- `QuizTimeLimits.cs` のマージコンフリクト（本 PR のミリ秒定数群 と #79 の `DefaultChoiceTimeLimitSec`/
  4引数コンストラクタ）を両方残す形で解消
- `RoomSettingsValidator` は 4 引数版 `new QuizTimeLimits(buzz, answer, choice, collectWindow)` に
  `answer.choiceTimeLimitSec`（クランプ済み）を渡すよう変更
- `RoomSettings.ChoiceTimeLimitSec` は `Session.TimeLimits.ChoiceTimeLimitSec`（`QuizTimeLimits`）に
  委譲する読み取り専用プロパティにした（コンストラクタの `choiceTimeLimitSec` 引数は廃止）
- `RoomSettings.DefaultChoiceTimeLimitSec` は `QuizTimeLimits.DefaultChoiceTimeLimitSec` を参照する
  （既定値の出典を 1 か所にする）。`MinChoiceTimeLimitSec`/`MaxChoiceTimeLimitSec`（1〜60秒、docs §1 の範囲）は
  引き続き `RoomSettings` 側に残し、`RoomSettingsValidator` のクランプにのみ使う
  （`QuizTimeLimits` 自体は `> 0` の検証のみで、UI 向けの 1〜60 秒という範囲は持たない）
- `RoomSettings.WithChoiceTimeLimitSec` は `Session.TimeLimits` を新しい `QuizTimeLimits` で作り直す実装に変更。
  1〜60 秒という範囲の強制は行わない（`RoomSettingsValidator` の責務）
- `RoomPresetTests` / `RoomSettingsValidatorTests` に「`answer.choiceTimeLimitSec` が
  `TimeLimits.ChoiceTimeLimitSec` に反映される」ことを確認するアサートを追加（組み込みプリセット
  「早押し重視」=10秒・「のんびり」=40秒、クランプ後の値の両方）
- `FileSystemRoomFileSystem.WriteAllText` の `.tmp` への書き込み自体も try に含め、失敗時に `.tmp` を
  削除してから再スローするよう修正（従来は `Replace`/`Move` の失敗だけを対象にしていた）

---

## 7. Settings View（issue #28）

`Assets/TsumugiQuiz/Scripts/UI/Views/Settings/`（`TsumugiQuiz.UI.Views.Settings`）に、本書 §1〜§3 を
一画面で扱う `SettingsView` を実装した（`settings-view.uxml` + `SettingsView`、partial class 6 分割:
`SettingsView.cs` / `.RoomTab.cs` / `.RoomSync.cs` / `.PresetsTab.cs` / `.AppTab.cs` / `.TtsTab.cs`）。
Title 画面の「設定」ボタンと**ロビーの「ルーム設定」ボタン**（§7.3）から遷移する（`ViewNames.Settings`）。
`PlaceholderView` 自体は今後 View を追加するときの足場として残してあるが、
`ViewNames.PlaceholderViews` は空になり、どの View にも割り当てられていない（統括判断、2026-09-17）。

| ファイル | 役割 |
|---|---|
| `SettingsView.cs` | タブ切り替え（ルーム設定/プリセット/アプリ設定）・戻る・利用規約への遷移 |
| `SettingsView.RoomSync.cs` | `RoomSettingsSync`（#27）との唯一の結合点。「適用」からの `TrySetSettings` とロック・権限に応じた表示メッセージ（後述 §7.4） |
| `SettingsView.RoomTab.cs` | ルーム設定タブ。§1 のキー（`answer.singleAttemptOnly` を除く、#221）に対応する入力 UI（`buzz.timeLimitSec` 等の integer 項目は `IntegerField`）。`buzz.collectWindowMs` のみ「詳細設定」の `Foldout` に隠す |
| `SettingsView.PresetsTab.cs` | プリセットタブ。組み込み 3 種 + ユーザー保存分の選択・読込・保存・削除。保存前に現在のフィールド値を `RoomSettingsValidator` で検証し直す。削除は 2 回目のクリックで確定（誤操作防止） |
| `SettingsView.AppTab.cs` | アプリ設定タブ。`player.name` / `upnp.*` / `network.ipLookupUrls` / `tts.*` / `character.enabled` / `network.port` に加え、`question.prefetchCount` も「詳細設定」の `Foldout` で扱う（`network.tickRate` は §2 の統括判断により UI から削除した） |
| `SettingsView.TtsTab.cs` | 「音声合成」ボタン。#25 の `TtsStatusPanel` を `TitleView` と同じ方式（`Document.rootVisualElement` 直下へのオーバーレイ表示）で開閉する。「再試行」には保存済みアプリ設定から作った `ITtsSettingsProvider` を渡す |
| `RoomSettingsDropdownOptions` | `host.role` / `questions.typeFilter` / `score.penaltyType` の保存値⇔画面表示ラベル対応表（`DropdownField` 用） |
| `SettingsPresetCatalog` | 組み込み 3 種 + ユーザー保存プリセット名をまとめて一覧化する純ロジック（ファイル I/O なし） |
| `SettingsFieldBinder` | UXML 要素の取得とログ・欠落検知（`Require<T>`）を共通化するヘルパ |
| `TsumugiQuiz.Room.RoomSettingsDraft` | 「編集中の `RoomSettings`」の唯一の保持先（後述 §7.2。Room 層に置き、Settings View と `RoomSettingsSync` の両方が読む） |
| `AppSettingsAdapters` | `AppSettings → TtsSettings` / `AppSettings → NatOptions` の変換アダプタ（UI 層） |
| `TsumugiQuiz.Network.Nat.NatOptionsAppSettingsAdapter` | 同上の `NatOptions` 変換の実体（Network 層。`NetworkBootstrap` の既定ファクトリと UI 層のアダプタの両方がここに委譲する） |
| `NetworkBootstrap.AppSettings.cs` | 起動時（`Start`）に `app-settings.json` を読み、`upnp.*` / `network.ipLookupUrls` / `question.prefetchCount` をネットワーク層へ反映する（`ApplyAppSettings`） |
| `TsumugiQuiz.UI.Views.Game.GameView.Tts.cs` | `TtsSyncPlayer.SetSettingsProvider` の実配線（セッション取得時） |
| `TsumugiQuiz.UI.PlayerNamePreferences` | `player.name` の読み書き窓口（`JoinView` / `HostSetupView` 共通、後述 §7.1） |

### 7.1 player.name / network.port / host.role の保存先移行（PR #92 レビュー H2、issue #155）

従来 `PlayerPrefs` に暫定保存していた `player.name`（`JoinView`/`HostSetupView` 共有）、
`network.port`（`HostSetupView` 専用キー `HostSetup.Port`）、`host.role`（HostSetup View の
「司会専任」トグル）を、いずれも `AppSettingsStore`（`app-settings.json`）へ移行した。

- `player.name`: `TsumugiQuiz.UI.PlayerNamePreferences.Load/Save` が窓口。`Load()` は
  `AppSettings.PlayerName` が空で、かつ旧 `PlayerPrefs` キー（`TsumugiQuiz.PlayerName`）に値が
  残っている場合だけ、その値を一度きり `app-settings.json` へ書き写し、旧キーを削除する
- `network.port`: `HostSetupPreferences.LoadPort/SavePort` が窓口。同様に旧キー
  （`HostSetup.Port`）からの一度きりの移行を行う。ただし `0`（OS 自動選択）は
  `AppSettings.NetworkPort` の範囲（1024〜65535）で表現できないため、「次回以降の既定値」としては
  保存しない（その回だけ OS に選ばせる単発の指定として扱う）
- `host.role`（issue #155）: `HostSetupPreferences.LoadHostRole/SaveHostRole`（UI 層）と
  `TsumugiQuiz.Network.HostRolePreference.Load`（Network 層、`NetworkBootstrap` /
  `RoomSettingsSync` が読む）の両方が窓口。いずれも `AppSettings.HostRole`
  （`AppSettingsStore`、`host.role` キー）を読み書きする。旧 `PlayerPrefs`（キー `host.role`、
  `TsumugiQuiz.Core.Network.HostRoles.SettingsKey`）に値が残っている場合、`app-settings.json` 側が
  既定値（`player`）のときに限り一度だけ引き継ぎ、旧キーを削除する（移行ログを 1 行出す）。
  同一 PC の複数インスタンス・別データルートで値が共有されてしまう不具合（#155 の実測: 別セッションの
  `PlayerPrefs` が残っていたため意図せず司会専任で起動した）を修正するための移行であり、
  移行後は他のアプリ設定と同様に `-tq-data-root` によるデータルート分離の対象になる
- `AppSettingsStore` の既定コンストラクタが例外を投げた場合（`AppPaths` 未設定）は、いずれも
  空文字列・既定ポート・既定の `host.role`（`player`）にフォールバックし、例外を外へ投げない

### 7.2 「編集中の RoomSettings」の永続化（PR #92 レビュー H4）

`RoomSettingsDraft`（`TsumugiQuiz.Room`）が「編集中の `RoomSettings`」の唯一の保持先。
プロセス内キャッシュに加え、`app-settings.json` の `room.lastApplied`（`AppSettings.RoomLastApplied`、
文字列。`RoomPresetJson.Serialize` と同じ JSON 形式）に永続化するため、**Settings 画面を開き直しても
「標準」へ戻らず、前回編集・適用した内容から再開する**。

- 実装は `TsumugiQuiz.Room.RoomSettingsDraft`（再レビュー H-2 で `TsumugiQuiz.UI.Views.Settings` から移した）。
  Room 層は Unity API 非依存を目標にしているため、失敗はログではなく `out warnings` で呼び出し側へ返す
- 更新タイミング: 「適用」ボタン、プリセットの読込・保存（組み込み・ユーザー保存の両方）。
  **クライアントとして接続中は書き込まない**（ホストの設定で自分の下書きを潰さないため。§7.4 / M-1）
- 読み込み: 初回アクセス時のみ。`room.lastApplied` が空・壊れている場合は「標準」プリセットから始める
- 下書きは「この PC で次にホストを始めるときの初期値」。**ホスト開始時に
  `RoomSettingsSync.OnNetworkSpawn` がこの下書きを読み、`host.role` だけ HostSetup View の直前の
  トグル（`HostRolePreference`）を重ねる**（再レビュー H-2。
  `_pendingInitial ?? RoomSettingsDraft.Current.WithHostRole(HostRolePreference.Load())`）。
  実際に参加者へ配るのは §7.4 の「適用」
- **アプリ設定タブの「保存」では失われない**: `AppSettingsStore.Save` はファイル全体を書き直すため、
  アプリ設定タブが UI に持たないキー（現状 `room.lastApplied` と `host.role`。後者は issue #155 で追加）は
  保存直前に読み直して引き継ぐ（再レビュー H-1。`SettingsView.AppTab.cs` の `PreserveKeysNotEditedHere`）

### 7.3 ロビーからの導線（M9、#28 Phase 2）

`lobby-view.uxml` の操作行に `lobby-settings-button` を追加し、ロビーから Settings View へ移動できる
ようにした。戻り先は `ViewRouter` の履歴（= ロビー）。

- **ホスト**: ボタン表示は「ルーム設定」。ルーム設定・プリセット・アプリ設定のすべてを編集できる
- **クライアント**: ボタン表示は「ルーム設定を見る」（**確定（2026-09-18 統括判断）**: 出す。閲覧のみとし、
  入力と「適用」は無効化する。#28 PR #92 の実装どおり）。同じ Settings View を開くが、
  ルーム設定タブは**入力・「適用」ごと無効化**して閲覧のみにする（再レビュー M-1。
  `room-settings-section` を `SetEnabled(false)`）。
  アプリ設定（`player.name` / `tts.*` / `character.enabled` 等）とプリセットの編集は各 PC ローカルの
  話なのでクライアントでも意味がある。「非表示にする」案もあったが、クライアントも
  「いまのルームのルール（制限時間・得点・読み上げ）」を確認したいはずなので閲覧用に出すことにした
- ホストから切断された後（`HandleDisconnectedFromHost` / `HandleTransportFailed`）は、操作を
  「タイトルへ戻る」だけに絞る既存方針（統括判断 #7 Q1）に合わせてこのボタンも隠す

### 7.4 RoomSettingsSync との結合（#28 Phase 2）

結合点は「ルーム設定」タブの**「適用」ボタン 1 か所**だけで、実装は `SettingsView.RoomSync.cs`
（`PushToRoomSettingsSync`）に閉じている。

1. 入力値を `RoomSettingsValidator` で検証し、画面へ反映する（クランプ結果を戻す）
2. `RoomSettingsDraft`（§7.2）へ保存する（ロック中・未接続でも保存する。クライアント接続中のみスキップ）
3. `RoomSettingsSync` が見つかり、ホストで、かつ `IsLocked == false` のときだけ
   `TrySetSettings(...)` を呼び、`LastValidationWarnings` を画面の警告一覧へ足す

手順 2 は**クライアントとして接続中はスキップする**（M-1）。また画面を開いた時点で、クライアント・
ロック中はルーム設定タブ全体を無効化して「閲覧」であることを明示する。

状態メッセージ（`room-settings-status-label`）:

| 状況 | 表示 |
|---|---|
| 同期先あり・ホスト・未ロック | 設定を適用しました（参加者全員に反映されます）。 |
| クライアント | ルーム設定を変更できるのはホストだけです。（入力は無効化済み。下書きにも書かない） |
| ホスト・`IsLocked == true`（ゲーム進行中） | ゲーム進行中は変更できません。この PC 内には保存しました（ロビーへ戻ると次回から反映されます）。 |
| 同期先なし（ホスト未開始） | 設定を保存しました（まだホストを開始していないため、この PC 内にのみ保存されます）。 |

画面を開いたときの初期値は「稼働中の `RoomSettingsSync.Current`」→「`RoomSettingsDraft.Current`」の順。

**`host.role` の一本化**: `host.role` の権威は `RoomSettingsSync` に一本化し、Settings View からは
`HostRolePreference`（issue #155 で `app-settings.json` へ移行済み。§7.1 参照）にも
`LobbyState.Role` にも直接書き込まない（Phase 1 の
`ApplyHostRoleBridge` は削除した）。ロビーへの反映は `RoomSettingsSync.ReconcileHostRole` →
`RoomSettingsApplier.Apply` → `LobbyState.ConfigureRoom` が行い、`LobbyView` は `LobbyState.Role` を
購読しているので表示も追随する（docs/network.md §12.6）。

**`RoomSettingsSync.Unlock()` は呼ばない**: 進行中に呼ぶと拒否される API であり、ロック解除は
ロビーへ戻る操作（`GameSession.ReturnToLobby`）が行う（docs/network.md §12.2）。

**`tts.enabled` と実行時の `ReadingEnabled`**: Settings View は `tts.enabled` を**ルーム設定**として
`TrySetSettings` に渡すだけで、`TtsSyncCoordinator.SetReadingEnabled` は直接呼ばない
（実行時の読み上げ ON/OFF への反映は `RoomSettingsApplier` 経由のみ。docs/network.md §12.6）。

### アダプタの配線状況（PR #92 レビュー H5/M1 で全項目配線済み）

- **`AppSettings → NatOptions`**: 変換ロジックは `TsumugiQuiz.Network.Nat.NatOptionsAppSettingsAdapter`
  に一本化した（`TsumugiQuiz.UI.Views.Settings.AppSettingsAdapters.ToNatOptions` はここへ委譲するだけ）。
  `NetworkBootstrap.HostConnectivityFactory` の**既定値自体**がこの変換を使うため、Settings 画面を
  一度も開かなくても `app-settings.json` の内容が起動時から反映される。さらに `NetworkBootstrap.Start()`
  （`NetworkBootstrap.AppSettings.cs` の `ApplySavedAppSettings`）が起動時に `app-settings.json` を
  読み込んでファクトリを作り直す。`SettingsView.AppTab.cs` は保存成功時に
  `NetworkBootstrap.ApplyAppSettings(appSettings)`（内部で `RefreshHostConnectivityFactory` を呼ぶ）
  でファクトリを最新の設定で作り直す。ホストが実行中の間は、ポートマッピングを壊さないよう
  次回のホスト開始まで反映を遅らせる（ファクトリの差し替えのみ行い、稼働中の `HostConnectivityService`
  は破棄しない）
  - 起動時の読み込みを `Awake` ではなく `Start` で行うのは、`AppPaths.DataRoot` を設定する
    `AppPathsBootstrap` が Boot シーンの**別 GameObject** に載っており、GameObject 同士の `Awake`
    実行順が保証されないため（`Start` はすべての `Awake` の後に走る）
- **`AppSettings → TtsSettings`**（#138 で「都度読み」へ変更）: 変換は
  `TsumugiQuiz.UI.Views.Settings.AppSettingsTtsSettingsProvider`（`Load()` のたびに `app-settings.json` を
  読み直す `ITtsSettingsProvider`）に一本化し、生成は `TtsSettingsProviderFactory.BuildOrNull()` を通す。
  (1) アプリ起動時に `DefaultViewControllerRegistrations.ConfigureTtsConsentGate` が
  `TtsService.ConfigureDefaults(settingsProvider:)` へ登録する（初期化は始めない）。
  (2) `GameView`（`GameView.Tts.cs`）がセッション取得時に `TtsSyncPlayer.SetSettingsProvider` へ渡す。
  (3) `SettingsView.TtsTab.cs` が `TtsStatusPanel` の「再試行」から
  `TtsService.RetryInitializeAsync(settingsProvider:)` に渡す。
  (4) `SettingsView.AppTab.cs` の保存成功時に `TtsAppSettingsReloader.ReloadIfNeeded` が、
  **初期化済み・同意済み・内容に変化ありのときだけ** `RetryInitializeAsync` で初期化をやり直す
  （話者・スタイル・`tts.assetPathOverride` は合成エンジンの生成時に固定されるため、
  provider の差し替えだけでは反映できない）。詳細は docs/tts.md §6.5 を参照
- **`question.prefetchCount → QuestionDistributor.PrefetchCount`**（#28 Phase 2）:
  `NetworkBootstrap` が読み込んだアプリ設定を `CurrentAppSettings` に保持し、
  (1) `QuestionDistributor.OnNetworkSpawn`（サーバーのみ）がスポーン時にそこから読む、
  (2) Settings 画面での保存時は `NetworkBootstrap.ApplyAppSettings` が
  スポーン済みの `QuestionDistributor` へ直接書く。Boot を経由しない構成
  （PlayMode の単体テスト等）では `NetworkBootstrap.Instance` が null なので既定値（1）のまま

### 未確定事項・判断に迷った点

- `network.tickRate` は統括判断で設定項目から削除し、`30` 固定にした（§2 の備考。NGO の接続時ハッシュに
  含まれ、値が揃っていない参加者が接続できなくなるため）
- `tts.cacheMaxBytes`（`long`）の入力 UI は `TextField`（数値文字列、解析失敗時は警告を表示したうえで
  既定値にフォールバック）にした。UI Toolkit に `LongField` 相当の専用コントロールがあるかを実機検証して
  いないため、保守的に文字列入力にした
- `character.enabled` は `GameView.Character.cs`（`InitializeCharacterView`）から
  `AppSettingsStore().Load().Settings.CharacterEnabled` を読むよう配線した（false なら立ち絵を表示しない）。
  Settings View 本体の範囲を超えるが、#26 の `TODO(#28)` に対応するため実施した
