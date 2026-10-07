# TSUMUGI-PATCH: Unity Transport 6.6.0 の埋め込みと改変（issue #163）

このフォルダ（`Packages/com.unity.transport/`）は、Unity 6000.6.0f1 に同梱されている builtin パッケージ
`com.unity.transport` 6.6.0（`_fingerprint: 1839533e4eb391bf730cdbb7b64ed590a479e2d9`）を
`Library/PackageCache/com.unity.transport@1839533e4eb3` からそのままコピーし、下記の 2 か所だけを改変した
**埋め込みパッケージ（embedded package）**である。改変以外のファイルは原本と同一。

注: `Library/PackageCache` からコピーしたため、`package.json` には Package Manager がキャッシュ展開時に付ける
`"_fingerprint"` 行が含まれている（Unity 本体同梱の原本との差はこの 1 行と下記 2 か所、および本ファイルのみ。
PR #171 のレビューで `diff -r` により確認済み）。`Documentation~` の PNG 7 件と `Samples~` の `.unity` 6 件も
原本どおり残している（DLL などのバイナリは含まない）。

Unity のエンジン側で同じ修正が入ったら、この埋め込みを削除して builtin に戻す（手順は末尾）。

## なぜ埋め込んだか（不具合）

- 症状（issue #163、2026-09-30 実機）: 同じ PC 上のクライアントを強制終了すると、ホストが以後の新規接続にまったく応答しなくなり、
  接続中の他クライアントも `[ClosedByRemote]` で切断される。これが再接続（#69）を阻んでいた
- 原因: `Runtime/UDPNetworkInterface.cs` の `ReceiveJob.Execute` が、失敗した受信完了（`CompletionStatus.Failed`）を
  `continue` で読み飛ばす際に、`ScheduleAllReceives` で確保した受信バッファを `ReleaseBuffer` していない。
  Windows では、閉じたポートへ送った UDP に対する ICMP port unreachable が次の受信の失敗（WSAECONNRESET）として返るため、
  ホストが消えた相手へ送るたびに受信バッファが 1 つずつ失われ、受信キュー（NGO の `MaxPacketQueueSize`、本プロジェクトは 128）を
  使い切ると何も受信できなくなる。UTP 既存の回復処理（全件失敗時に `getsockname` でソケット異常を確認して作り直す）は、
  ソケット自体は正常なので働かない
- 一次情報:
  - Unity Discussions「Unity Transport 6.6.0: UDP host stops receiving after abrupt client disconnect on Windows」
    https://discussions.unity.com/t/unity-transport-6-6-0-udp-host-stops-receiving-after-abrupt-client-disconnect-on-windows/1736023
    （2026-09-08 投稿。**2026-09-09 に Unity スタッフ（Simon Lemay）が不具合と認め、`continue` の前に
    `ReceiveQueue.ReleaseBuffer(bufferIndex);` を入れる修正が正しいと回答**。「次のパッケージバージョンで修正」とのこと）
  - UTP 6.5.0 以降は builtin パッケージで、修正はエンジンのリリースノートに載る。Unity 6000.6.3f1（2026-09-24）の
    リリースノート https://unity.com/releases/editor/whats-new/6000.6.3f1 には該当修正なし（2026-09-30 確認）
- 再現テスト: `Assets/TsumugiQuiz/Tests/PlayMode/Network/HostReceiveAfterAbruptDropTests.cs`（`[Category("Network")]`）。
  改変前は「切断通知なしに消えたクライアントの後で新規クライアントが接続できる」が失敗し（`Failed to connect to server.`）、
  改変後は成功する（2026-09-30 実測）

## 改変箇所（`Runtime/UDPNetworkInterface.cs`、`ReceiveJob.Execute`）

どちらも `// TSUMUGI-PATCH #163` のコメントで印を付けてある。

1. **Unity が認めた修正**: 受信完了が `Failed` のとき、`continue` の前でバッファを返す
   ```csharp
   if (results[i].status == Binding.Baselib_RegisteredNetwork_CompletionStatus.Failed)
   {
       failedCount++;
       ReceiveQueue.ReleaseBuffer(bufferIndex); // TSUMUGI-PATCH #163
       continue;
   }
   ```
2. **同じパターンへの当方の判断による修正**（Discussions のスレッドには含まれない）: 受信バイト数が 0 以下または容量超過で
   読み飛ばすときも、同じく確保したバッファを返さずに捨てていたため、`continue` の前でバッファを返す
   ```csharp
   if (receivedBytes <= 0 || receivedBytes > ReceiveQueue.PayloadCapacity)
   {
       ReceiveQueue.ReleaseBuffer(bufferIndex); // TSUMUGI-PATCH #163
       continue;
   }
   ```

それ以外（`package.json` のバージョン・依存、他のソース、`LICENSE.md` を含む）は一切変更していない。

## ライセンス

`LICENSE.md`（原本のまま同梱）のとおり Unity Companion License が適用される。改変・再配布は Unity 依存プロジェクトで
Unity のライセンスの下で行う限り許諾されている。詳細と表示義務の確認結果は `docs/licenses.md` §9.1 を参照。

## 埋め込みを外す手順（エンジン側で修正されたら）

1. Unity のリリースノート（または `com.unity.transport` の changelog）で、失敗した受信完了のバッファ解放
   （上記スレッドの不具合）が修正されたことを一次情報で確認する
2. Unity エディタをそのバージョンへ上げることについてユーザーの承認を得る（エディタのバージョンは確定事項。docs/requirements.md §5）
3. `Packages/com.unity.transport/` フォルダを丸ごと削除する（`Packages/manifest.json` の `"com.unity.transport"` の行はそのまま）
4. Unity でプロジェクトを開き直し（またはバッチ起動し）、`Packages/packages-lock.json` の `com.unity.transport` が
   `"source": "builtin"` に戻ったことを確認する
5. `pwsh ./scripts/verify.ps1 -IncludeNetwork -Platform PlayMode` で `HostReceiveAfterAbruptDropTests` が成功することを確認する
   （失敗するならエンジン側の修正が不十分なので、埋め込みを戻す）
6. docs/licenses.md §9.1 と `Assets/TsumugiQuiz/Resources/Licenses/unity-packages-notices.txt` の改変注記を削除する
