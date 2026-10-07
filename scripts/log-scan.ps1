# scripts/log-scan.ps1
# Unity バッチモードのログからエラーを判定するロジック本体（関数のみ）。
# scripts/common.ps1 から dot-source して使う。単体では実行しない。
#
# 判定ルールの詳細・根拠は docs/dev-workflow.md「9. ログ判定ルール（scripts/log-scan.ps1）」を参照。
# 回帰テスト: scripts/tests/log-scan.tests.ps1

# 既知の無害な行。ここには「実際に誤検知が確認されたもの」のみを狭いパターンで追加すること。
# パターンを追加・変更する場合は、必ず log-scan.tests.ps1 にケースを足して意図を明記する。
$script:LogScanIgnorePatterns = @(
    # ライセンストークン未取得の警告（オフライン/未アクティベート環境で常に出る。実害なし）。
    '^\[Licensing::Module\] Error: Access token is unavailable'
)

# スタックトレース行・メソッドシグネチャ行を判定する。
# これらは "error"/"exception" という単語を含み得るが、Unity が実際に報告する
# エラー行ではないため、エラー候補から除外する対象。
#
# 実測した具体例（scripts/tests/log-scan.tests.ps1 にも同じ行を収録）:
#   UnityEngine.TestTools.EnumerableTestMethodCommand/<ExecuteEnumerableAndRecordExceptions>d__4:MoveNext ()
#   TsumugiQuiz...PortMappingServiceTests/<MapAsync_WhenDeviceThrowsUnexpectedException_ReturnsFailed>d__11:MoveNext () (at Assets/....cs:220)
#     at TsumugiQuiz.Tests.Shared.Tts.FakeTtsSynthesisEngine.SynthesizeAsync (...) [0x000c5] in ....cs:64
#   SomeClass:Handler (System.Exception ex)   … 型引数に例外型を持つメソッドシグネチャ
function Test-LogScanIsStackTraceLine {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line
    )

    # .NET 形式のスタックトレース行（"  at Namespace.Type.Method (...) [0x...] in file:line"）
    if ($Line -match '^\s*at\s') { return $true }

    # Unity 独自形式のスタックトレース行（"Namespace.Type:Method (...) (at file:line)"）
    if ($Line -match '\(at\s[^)]+:\d+\)') { return $true }

    # IL オフセット付きの .NET スタックトレース断片（"[0x0001] in file:line"）
    if ($Line -match '\[0x[0-9a-fA-F]+\]\s+in\s') { return $true }

    # "Namespace.Type:Method (...)" / "Namespace.Type.Method (...)" 形式のメソッドシグネチャ行
    # （引数に例外型名を含んでいても、シグネチャそのものはエラーではない）
    if ($Line -match '^[A-Za-z_][A-Za-z0-9_.<>`/+]*[:.][A-Za-z_<][A-Za-z0-9_<>`]*\s*\(') { return $true }

    return $false
}

# Unity が実際に報告するエラー行かどうかを判定する。
# 行頭一致のパターンは「行頭」であることを厳守する（緩めると誤検知の再発につながる）。
function Test-LogScanIsErrorLine {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line
    )

    # C# コンパイルエラー（例: "Assets\Foo.cs(9,38): error CS1002: ; expected"）
    if ($Line -match 'error CS\d+') { return $true }

    # コンパイル失敗の確定行（Unity が出力）
    if ($Line -match '^Scripts have compiler errors\.') { return $true }

    # "Error:" / "ERROR" で始まる行
    if ($Line -match '^Error:') { return $true }
    if ($Line -match '^ERROR\b') { return $true }

    # 例外型名 + コロン、行頭一致（例: "NullReferenceException: ..." "System.InvalidOperationException: ..."）
    # LogAssert.Expect で想定済みのログは "[タグ] メッセージ: 例外型: 詳細" のように行頭がタグから始まるため、
    # この行頭一致には引っかからない（docs/dev-workflow.md 参照）。
    if ($Line -match '^[A-Za-z_][A-Za-z0-9_.]*Exception:\s') { return $true }

    if ($Line -match '^Failed to\b') { return $true }

    # 明示的に名指しされている例外・例外的状態
    if ($Line -match 'NullReferenceException') { return $true }
    if ($Line -match 'UnityException') { return $true }

    # バッチモードの致命的中断
    if ($Line -match '^Aborting batchmode\b') { return $true }

    # #156: TtsEngineFactoryGuardSetUp（EditMode の engineFactory 省略時フォールバック検知ガード、
    # Assets/TsumugiQuiz/Tests/EditMode/TtsEngineFactoryGuardSetUp.cs）が発火したことを示す行。
    # 発火元テストが LogAssert.ignoreFailingMessages = true を設定していても、この行自体は
    # ログファイルに残るため、NUnit 側の Assert.Fail（OneTimeTearDown）と二重の検知手段にする。
    if ($Line -match '^\[TtsEngineFactoryGuardSetUp\]') { return $true }

    # テストランナーの失敗を示す行。
    # 実測では Assert.Fail / 未処理例外によるテスト失敗はこのログファイルに一切出力されず
    # （結果は test-results\*.xml のみに記録される。docs/dev-workflow.md 参照）、
    # 以下は将来の Unity バージョンやテストランナーの出力差異に備えた保険的パターン。
    if ($Line -match '^\s*Test Failed\b') { return $true }
    if ($Line -match '^\s*\d+\)\s+\S.*:\S') { return $true }

    return $false
}

# ログファイルからエラー候補行を抽出する。
# 戻り値は [pscustomobject]@{ LineNumber; Line } の配列（0 件でも配列を保証する）。
function Get-LogScanErrorMatches {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [string[]]$IgnorePatterns = $script:LogScanIgnorePatterns
    )

    if (-not (Test-Path $LogPath)) {
        return $null
    }

    $lines = @(Get-Content -LiteralPath $LogPath)
    $result = @()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if (Test-LogScanIsStackTraceLine -Line $line) { continue }
        if (-not (Test-LogScanIsErrorLine -Line $line)) { continue }

        $ignored = $false
        foreach ($pattern in $IgnorePatterns) {
            if ($line -match $pattern) { $ignored = $true; break }
        }
        if ($ignored) { continue }

        $result += [pscustomobject]@{ LineNumber = ($i + 1); Line = $line }
    }

    return @($result)
}
