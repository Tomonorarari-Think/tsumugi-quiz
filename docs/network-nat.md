# UPnP 自動ポート開放とグローバル IP の取得

## 目的

ホストが「外部から到達できるアドレスとポート」を自動で用意するための方式を定める。
UPnP / NAT-PMP ライブラリの選定（仮決め K11）、グローバル IP の取得（仮決め K10）、
CGNAT の検出、失敗時のフォールバックを扱う。

## 関連ドキュメント

- [docs/network.md](network.md) — 接続フロー全体
- [docs/network-joincode.md](network-joincode.md) — ここで得た IP / ポートを符号化する方式

---

## 1. UPnP による自動ポートマッピング（仮決め K11）

### 1.1 ライブラリ比較

| 項目 | **Mono.Nat** | Open.NAT |
|---|---|---|
| リポジトリ | <https://github.com/alanmcgovern/Mono.Nat> | <https://github.com/lontivero/Open.NAT> |
| ライセンス | MIT（`LICENSE.md`: "The MIT License", Alan McGovern / Ben Motmans / Nicholas Terry） | MIT |
| 最新リリース | `release-v3.0.4`（2022-10-19） | — |
| 最終コミット | 2022-10-19（`48ef8881`） | 2024-06-07（リポジトリ更新のみ） |
| **リポジトリ状態** | 通常（アクティブではないが読み取り専用ではない） | **アーカイブ済み（read-only）** |
| TargetFrameworks | `net6.0; netstandard2.1; netstandard2.0`（`Mono.Nat/Mono.Nat.csproj` で実測） | .NET 4.5 / netstandard1.x 系 |
| NuGet | `Mono.Nat` 3.0.4（依存パッケージなし） | `Open.NAT` |
| プロトコル | **UPnP + NAT-PMP 両対応**（NuGet の PackageDescription、`Mono.Nat/Pmp` と `Mono.Nat/Upnp` ディレクトリで確認） | UPnP + NAT-PMP |
| API | `Task` ベース（`Task<Mapping> CreatePortMapAsync(Mapping)` など） | `Task` ベース |

**推奨: Mono.Nat 3.0.4**。理由は 3 点。

1. **`netstandard2.1` ターゲットのビルドが存在する**。本プロジェクトの Api Compatibility Level は .NET Standard 2.1（`apiCompatibilityLevel: 6`）なので、そのまま Unity に載る。Open.NAT は netstandard2.1 ビルドを持たない。
2. **Open.NAT はアーカイブ済み**で、今後の修正が一切入らない（`gh api repos/lontivero/Open.NAT` の `archived=true` で実測）。
3. **NAT-PMP にも対応している**（仮決め K11 の加点項目）。UPnP を無効にしている家庭用ルーターでも NAT-PMP が有効なことがあり、成功率が上がる。

どちらも MIT なので、ライセンス全文を `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md` として同梱し、アプリ内クレジット画面から参照する。

### 1.2 Unity への導入方法

**採用: NuGet パッケージから `netstandard2.1` の DLL を取り出して `Assets/Plugins/Mono.Nat/` に置く。**

```
nuget パッケージ Mono.Nat.3.0.4.nupkg（実体は zip）
  └ lib/netstandard2.1/Mono.Nat.dll   ← これだけを取り出す
```

- 配置先: `Assets/Plugins/Mono.Nat/Mono.Nat.dll` と `Assets/TsumugiQuiz/Licenses/Mono.Nat-LICENSE.md`
- Plugin Inspector の Platform は **Any Platform**（#3 で確定）。Editor を除外すると Editor 側の
  アセンブリが `Mono.Nat` を参照できずコンパイルが通らない（EditMode / PlayMode テストが走らない）ため。
  ビルドターゲットは Windows Standalone のみなので、同梱先は実質的に変わらない（docs/licenses.md §10.1）。
  `Auto Reference` は無効にし、`TsumugiQuiz.Network` / `TsumugiQuiz.Tests.*` の asmdef で明示参照する。
- **Mono.Nat の DLL とライセンスはリポジトリにコミットしてよい**（MIT、再配布可、サイズも小さい）。External/ 配下の権利物とは扱いが異なる。
- ソースコードをコピーする案は採らない。`Mono.Nat` は `GitInfo` パッケージ参照やターゲット別 `#if NETSTANDARD2_0` 分岐を持ち、そのままでは Unity のコンパイルに乗らないため、保守コストが高い。

### 1.3 ポートマッピング手順

```csharp
using Mono.Nat;

// 1) 発見。UPnP と NAT-PMP を同時に探す。
NatUtility.DeviceFound += OnDeviceFound;
NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);
// … discoveryTimeoutMs（既定 5000ms）待つ。見つからなければ失敗扱い。
NatUtility.StopDiscovery();

// 2) マッピング作成（UDP、内部 7777 → 外部 7777）
async Task<MappingResult> MapAsync(INatDevice device, int port, CancellationToken ct)
{
    var mapping = new Mapping(Protocol.Udp, port, port, LifetimeSeconds, "TsumugiQuiz");
    var created = await device.CreatePortMapAsync(mapping);   // 失敗時は MappingException
    var external = await device.GetExternalIPAsync();          // §2 の 1 段目
    return new MappingResult(created.PublicPort, external);
}

// 3) 終了時に削除
await device.DeletePortMapAsync(mapping);
```

確認した API（`Mono.Nat/INatDevice.cs`、`Mono.Nat/NatUtility.cs`、`Mono.Nat/Mapping.cs` を実際に読んで確認）:

- `static event EventHandler<DeviceEventArgs> NatUtility.DeviceFound`
- `static void NatUtility.StartDiscovery(params NatProtocol[] devices)` / `static void NatUtility.StopDiscovery()`
- `Task<Mapping> INatDevice.CreatePortMapAsync(Mapping mapping)`
- `Task<Mapping> INatDevice.DeletePortMapAsync(Mapping mapping)`
- `Task<Mapping[]> INatDevice.GetAllMappingsAsync()`
- `Task<IPAddress> INatDevice.GetExternalIPAsync()`
- `Task<Mapping> INatDevice.GetSpecificMappingAsync(Protocol protocol, int publicPort)`
- `Mapping(Protocol protocol, int privatePort, int publicPort)` / `Mapping(Protocol protocol, int privatePort, int publicPort, int lifetime, string description)`

実装上の注意:

- **プロトコルは UDP**。Unity Transport は UDP を使う。TCP のマッピングを作っても意味がない。
- `CreatePortMapAsync` が返す `Mapping.PublicPort` は、要求した外部ポートが埋まっていた場合にルーターが別の値を割り当てることがある。**返り値の `PublicPort` を参加コードに使う**こと（要求値を使ってはいけない）。
- `lifetime` は 0（無期限）を指定できるが、無期限を拒むルーターがある。**3600 秒を指定し、`upnp.renewIntervalMs`（既定 1,800,000ms = 30 分）ごとに再作成する**。
- 発見・マッピングはメインスレッドをブロックしない。`async`／`UniTask` 相当で行い、UI には「ポート開放中…」を出す。

### 1.4 終了時のクリーンアップ

`Application.quitting` と `OnApplicationQuit`、およびホスト停止時に `DeletePortMapAsync` を呼ぶ。
ただし **強制終了ではフックが走らない**ため、マッピングが残ることを前提にする。

- 起動時に `GetAllMappingsAsync()` を呼び、`Description == "TsumugiQuiz"` の古いマッピングを削除してから作り直す。
- 削除に失敗してもホスト開始自体は続行する（既存マッピングが同じポートを指していれば実害はない）。

### 1.5 失敗時のフォールバック（手動ポート開放案内画面）

UPnP / NAT-PMP のいずれでもマッピングできなかった場合、`HostSetup` 画面に次を表示する。

```
┌────────────────────────────────────────────────┐
│  自動ポート開放に失敗しました                   │
│                                                │
│  ルーターの設定画面で、次のポートを開放してください │
│    プロトコル : UDP                            │
│    外部ポート : 7777                           │
│    内部ポート : 7777                           │
│    宛先 IP   : 192.168.1.23  ← このPCのLAN IP  │
│                                                │
│  [ LAN IP をコピー ]  [ 再試行 ]               │
│                                                │
│  ▸ ルーターの設定方法がわからない場合           │
│     → Tailscale を使う手順（README を参照）     │
│                                                │
│  グローバル IP を手入力する                     │
│    [                    ]  [ コードを作る ]     │
└────────────────────────────────────────────────┘
```

表示すべき情報は「プロトコル（UDP）／外部ポート／内部ポート／このPCのLAN IP」の 4 点。
この 4 点があれば、どのメーカーのルーターでも設定項目に対応づけられる。

**Tailscale 手順の要点**（README に記載し、この画面からは参照のみ）:

1. ホストと参加者全員が Tailscale（無料の Personal プラン）をインストールし、同じ Tailnet に参加する（招待リンクを配る）。
2. ホストは `tailscale ip -4` で 100.x.y.z の Tailscale IP を確認する。
3. その IP をアプリの「グローバル IP を手入力する」欄に入れて参加コードを生成し、参加者に配る。
4. UPnP もポート開放も不要。Tailscale が NAT を越える。

> 注: Tailscale IP は 100.64.0.0/10（CGNAT 帯）に属する。§1.6 の CGNAT 検出は、
> **UPnP / IP 確認サービスから得た IP** に対して行い、手入力された IP には適用しない（適用すると Tailscale 手順が使えなくなる）。

> 注（issue #36、2026-09-17）: README の Tailscale 手順は、[Tailscale 公式ダウンロードページ](https://tailscale.com/download)・[インストールガイド](https://tailscale.com/kb/1017/install)・[料金ページ](https://tailscale.com/pricing) の記載内容に基づいて作成した（いずれも WebFetch で確認済み）。**実機 2 台以上を使った実際の接続検証（インストール〜ログイン〜Tailscale IP での参加コード生成〜他デバイスからの接続）は本作業環境（Unity バッチ実行専用、複数物理デバイス無し）では実施できないため未検証であり、要手動確認である。** 配布前、または実際に CGNAT 環境のユーザーから問い合わせがあった際に、開発者の手元で 2 台以上の実機を用いて手順どおりに接続できることを確認すること。

### 1.6 CGNAT の検出（仮決め K10）

取得したグローバル IP が次のいずれかの範囲なら、**そのアドレスは NAT の外から到達できない**ため、直接接続は成立しない。

| 範囲 | 意味 |
|---|---|
| `100.64.0.0/10` | CGNAT（RFC 6598 Shared Address Space） |
| `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | プライベート（RFC 1918） |
| `127.0.0.0/8` | ループバック |
| `169.254.0.0/16` | リンクローカル |
| `0.0.0.0/8`, `240.0.0.0/4` | 予約 |

さらに **UPnP の `GetExternalIPAsync()` の結果と、HTTPS の IP 確認サービスの結果が一致しない**場合も、二重 NAT（ルーターの上流にさらに NAT がある）を疑う。この場合は警告を出し、HTTPS 側の値を採用する（外から見える真のアドレスはそちら）。

検出時の UI:

```
このインターネット回線は CGNAT（大規模 NAT）のため、
外部から直接接続できません。

・同じ LAN 内の人は「LAN 用コード」で参加できます
・インターネット越しに遊ぶには Tailscale をお使いください（README 参照）
```

---

## 2. グローバル IP の取得（仮決め K10）

3 段階で試し、最初に成功した値を使う。

| 段 | 方法 | 失敗条件 |
|---|---|---|
| 1 | `INatDevice.GetExternalIPAsync()`（UPnP / NAT-PMP） | デバイス未発見、例外、プライベート IP が返る |
| 2 | HTTPS の IP 確認サービス `https://api.ipify.org`（プレーンテキストで IPv4 を返す） | タイムアウト（5 秒）、HTTP ステータス 200 以外、IPv4 としてパースできない |
| 3 | ユーザーによる手入力 | — |

**ipify の利用条件**: 公式サイト <https://www.ipify.org/> に "You can use it without limit (even if you're doing millions of requests per minute" と明記されており、無料・回数無制限・アカウント不要。商用利用を禁じる記述はない。ログも取らない（"No visitor information is ever logged. Period."）。エンドポイントは `https://api.ipify.org`（IPv4）、`https://api6.ipify.org`（IPv6）、`https://api64.ipify.org`（両対応）。本プロジェクトは IPv4 のみなので `api.ipify.org` を使う。

実装上の検証（外部データを信用しない）:

- レスポンスは **64 バイトを超えたら破棄**する。
- `IPAddress.TryParse` + `AddressFamily == InterNetwork` を確認する。
- §1.6 の範囲チェックに掛ける。
- 段 2 が失敗しても例外を投げない。段 3 に落とす。
- サービスの単一障害点を避けるため、URL はルーム設定 `network.ipLookupUrls`（文字列配列）で差し替えられるようにし、既定値に `https://api.ipify.org` を入れる。

---

## 3. 実装（#3 で追加）

### 3.1 クラス構成

| 型 | asmdef | 役割 |
|---|---|---|
| `TsumugiQuiz.Core.Network.IpRangeClassifier` / `IpAddressCategory` | Core（純 C#） | IPv4 の厳密な解析と §1.6 の帯域分類 |
| `TsumugiQuiz.Core.Network.ManualPortMappingGuide` | Core（純 C#） | §1.5 の手動ポート開放案内（プロトコル / 外部ポート / 内部ポート / LAN IP）のデータ |
| `TsumugiQuiz.Network.Nat.NatOptions` | Network | `upnp.*` と `network.ipLookupUrls`（アプリ設定）の既定値・範囲・検証 |
| `TsumugiQuiz.Network.Nat.INatDiscovery` / `MonoNatDiscovery` | Network | NAT デバイス探索（Mono.Nat の `NatUtility` を包む） |
| `TsumugiQuiz.Network.Nat.INatDevice` / `MonoNatDevice` | Network | デバイス操作（Mono.Nat の `INatDevice` を包む） |
| `TsumugiQuiz.Network.Nat.PortMappingService` | Network | 探索 → 古いマッピング削除 → 作成 → 更新 → 削除の一連の手順 |
| `TsumugiQuiz.Network.Nat.PublicIpResolver` / `IIpLookupClient` / `UnityWebRequestIpLookupClient` | Network | §2 の 3 段階のグローバル IP 取得 |
| `TsumugiQuiz.Network.Nat.LanIpResolver` | Network | LAN IPv4 の列挙と選択 |
| `TsumugiQuiz.Network.Nat.HostConnectivityService` / `HostAddressInfo` | Network | 上記の統合。#5 の HostSetup 画面はこの `HostAddressInfo` だけを見る |

Mono.Nat の型（`NatUtility` / `INatDevice` / `Mapping` / `MappingException` / `ErrorCode`）は
`MonoNatDiscovery` と `MonoNatDevice` の内側に閉じ込め、他のクラス・テストには出さない。
これによりテストは `INatDiscovery` / `INatDevice` / `IIpLookupClient` の差し替えだけで書ける。

### 3.2 実装上の注意（実装時に判明した点）

**スレッド**

- `NatUtility.DeviceFound` は **Mono.Nat のワーカースレッドで発火する**。`MonoNatDiscovery` では
  `TaskCompletionSource`（`TaskCreationOptions.RunContinuationsAsynchronously` 付き）に詰め替えるだけにし、
  イベントハンドラ内で Unity API を触らない。
- `NatUtility` は **静的（プロセス全体で 1 つ）**。同時に 2 つの探索が走らないよう
  `MonoNatDiscovery` 内の `SemaphoreSlim` で直列化し、探索終了時に必ず `StopDiscovery()` を呼ぶ。
- `PortMappingService` / `MonoNatDevice` の `await` はすべて `ConfigureAwait(false)` にして
  呼び出し元スレッドに依存しないようにした。これにより `Application.quitting` から
  `PortMappingService.ReleaseBlocking()`（同期待ち）を呼んでもデッドロックしない。
  逆に `UnityWebRequest` はメインスレッド専用なので、`UnityWebRequestIpLookupClient` は
  生成時にメインスレッドの `SynchronizationContext` を捕まえ、別スレッドから呼ばれた場合は `Post` してから送信する。
- `PublicIpResolver` / `HostConnectivityService` は `ConfigureAwait` を使わない（= 継続がメインスレッドに戻る）。
  **メインスレッドから呼ぶこと**が前提。

**タイムアウト**

- Mono.Nat の非同期 API は `CancellationToken` を受け付けない。`MonoNatDevice` では
  `Task.WhenAny` + `Task.Delay` で「待つのをやめる」形にし、1 操作あたり 10 秒の上限を入れた
  （応答しないルーターで固まらないための保険。下位のリクエスト自体は裏で完走する）。
- 探索の「見つからなかった」は、経過時間が `upnp.discoveryTimeoutMs` に達していれば
  `PortMappingStatus.Timeout`、それより早く終わっていれば `DeviceNotFound` として区別する。
- `UnityWebRequest.timeout` は **秒単位**。`0` は無制限になるため、ミリ秒指定を切り上げて最低 1 秒にする。

**マッピングの寿命と更新**

- 要求した内部ポートは `PortMappingService` が保持し、**更新も削除も必ずその値で行う**。
  ルーターが返す `Mapping.PrivatePort` は信用しない（要求と違う値を返す実装があり、
  それを使うと更新のたびに別ポートのマッピングが増え、削除も効かなくなる）。
  外部ポート（`PublicPort`）は逆に必ず応答側を採用する。
- **古いマッピングの掛除（§1.4）は `MapAsync` の初回だけ**行う。
  更新（`RenewAsync`）で掛除すると、自分が今使っているマッピングを消し、
  作り直すまでの間に新規接続が受けられなくなる。更新は単なる上書き作成とする。
- `upnp.renewIntervalMs` は読み込み時に **`upnp.mappingLifetimeSec` の半分以下へ丸める**
  （lifetime `0` = 無期限のときは丸めない）。更新間隔が lifetime を超えていると、
  更新が走る前にルーター側で失効し、ホストが気づかないまま接続不能になるため。
- 更新で **外部ポートが変わると配布済みの参加コードが無効になる**。
  `PortMappingService.MappingChanged`（ワーカースレッドで発火）を
  `HostConnectivityService` が購読し、メインスレッドへ移してから `Current` を差し替え、
  `AddressChanged` / `JoinCodeInvalidated`（+ `IsJoinCodeOutdated`）で #5 の画面に知らせる。

**値型の安全性**

- `HostAddressInfo` / `PortMappingResult` / `PublicIpResult` / `NatPortMapping` /
  `ManualPortMappingGuide` はいずれも構造体で、`default(T)` で生成されうる。
  文字列はプロパティ側で `?? string.Empty` に畳み、未解決の状態でも画面が
  NullReferenceException にならないようにしている（EditMode テストで固定）。
- 手入力のグローバル IP は **グローバル帯と CGNAT 帯だけ**受け付ける。
  プライベート・ループバック・リンクローカル・予約帯はインターネット用コードにならないので無視する。
  CGNAT を許すのは Tailscale IP（100.x）の手順を塞がないため（§1.5 の注記）。

**テストの分け方**

実ネットワークに出るテスト（UPnP 探索、IP 確認サービス、実 NIC 列挙）には
`[Category("Network")]` を付け、`scripts/verify.ps1` の既定（`-testCategory "!Network"`）では除外する。
実行は `pwsh ./scripts/verify.ps1 -IncludeNetwork`。これらのテストでも
「探索はタイムアウト + 余裕の範囲内で必ず終わる」「作成できたら削除まで必ず行う」
「取得できたアドレスは IPv4 の正準形」といった **環境に依存しない不変条件は Assert する**。

**その他**

- `upnp.renewIntervalMs` の下限を 10 秒とした（設定ファイルに 0 が入ってもルーターへ連続リクエストしないため）。
- `network.ipLookupUrls` は **https の絶対 URL のみ**受け付ける（設定ファイル経由で平文 HTTP を差し込まれないため）。
  1 件も残らなければ既定値に戻す。
- §1.6 の二重 NAT 判定を行うため、`PublicIpResolver` は **段 1 が成功しても段 2 を実行する**
  （§2 の表は「最初に成功した値を使う」だが、比較には両方必要）。値が食い違う場合は警告して段 2 を採用する。
- CGNAT 帯のアドレスは「取得できなかった」ではなく「取得できたが到達できない」として扱い、
  `PublicIpResult.IsCarrierGradeNat` で案内する。**手入力されたアドレスには CGNAT 判定を適用しない**
  （§1.5 の注記のとおり、Tailscale IP を塞がないため）。
- IPv4 の解析は `IPAddress.TryParse` に任せず、Core 側で「ドット区切り 4 組・各組 1〜3 桁・先頭 0 禁止」の
  正準形だけを受け付ける実装にした（`IPAddress.Parse` は実行環境によって `10.1` 形式や先頭 0 の
  8 進数解釈を許すことがあり、帯域判定の前段としては危険）。
- LAN IP は **UDP ソケットを外部アドレスへ `Connect` して `LocalEndPoint` を読む**方法を第 1 候補にした
  （UDP の `Connect` はパケットを送らない）。複数 NIC（有線 + 無線 + Hyper-V / WSL 等の仮想）がある PC でも
  既定ルートに紐づくアドレスが得られる。取得できない場合は `NetworkInterface` の列挙にフォールバックする。

### 3.3 実測結果（2026-09-13、開発機 Windows 11）

`pwsh ./scripts/verify.ps1`（EditMode 485 件 / PlayMode 23 件、いずれも全件 Pass）で
`[Category("Network")]` の PlayMode テストを実行した結果。

| 項目 | 実測値 |
|---|---|
| NAT デバイス探索（UPnP + NAT-PMP、タイムアウト 5000ms） | **未検出**（5027ms 経過 → `PortMappingStatus.Timeout`） |
| 手動 SSDP 検証（`M-SEARCH` / `InternetGatewayDevice:1` を 239.255.255.250:1900 へ送信） | 応答 0 件 |
| 手動 NAT-PMP 検証（既定ゲートウェイの 5351 番ポート（アドレスは省略）へ external address request） | 応答なし（タイムアウト） |
| `https://api.ipify.org` からのグローバル IP | 取得成功 / 所要 236ms / 分類 `Public` / CGNAT でない |
| 同 IP を `https://checkip.amazonaws.com` で照合 | **一致**（同一アドレス） |
| LAN IP（`LanIpResolver.Resolve()`） | 開発機の実測値（`192.168.x.x` のプライベートアドレス。値は省略）。既定ルート経由で判定。列挙結果も同一の 1 件のみ |

**探索が未検出だった原因**: Mono.Nat を経由しない素の SSDP / NAT-PMP プローブでも応答が 0 件だったため、
実装ではなく **この回線のルーターが UPnP / NAT-PMP に応答しない**（機能が無効か非対応）ことによる。
したがって「マッピング作成 → ルーター管理画面で確認 → 削除」の実測は本環境では取れていない。
UPnP を有効にできるルーターで `pwsh ./scripts/verify.ps1 -Platform PlayMode` を再実行すると、
`NatRealNetworkTests` が作成・一覧読み返し・削除の各段をログに出す（テストは成功・失敗どちらでも Pass）。

**IP 確認サービスの選定に関する実測**: デュアルスタック回線では `https://ifconfig.me/ip` や
`https://icanhazip.com` が **IPv6 アドレスを返した**（先頭部分は省略）。IPv4 専用エンドポイントである
`https://api.ipify.org` を既定にしている §2 の判断はこの点でも妥当で、実装側も
`AddressFamily == InterNetwork` の検証で IPv6 応答を弾く。
