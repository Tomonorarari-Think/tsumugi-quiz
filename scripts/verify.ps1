# scripts/verify.ps1
# Unity バッチモードで EditMode / PlayMode テストを実行し、結果 XML を Logs/test-results/ に保存する。
# CI の代替としてローカルで PR 前に実行すること（docs/tasks/setup-brief.md K23）。
#
# 使い方:
#   pwsh ./scripts/verify.ps1                  # EditMode + PlayMode (既定。Network カテゴリは除外)
#   pwsh ./scripts/verify.ps1 -Platform EditMode
#   pwsh ./scripts/verify.ps1 -Platform PlayMode
#   pwsh ./scripts/verify.ps1 -IncludeNetwork  # 実ルーター / 実インターネットに出るテストも実行
#
# -IncludeNetwork を付けない既定では `-testCategory "!Network"` を渡し、
# [Category("Network")] を付けたテスト（UPnP 探索・IP 確認サービス・実 NIC 列挙）を除外する。
# これらは実行環境のネットワーク構成に依存し、オフラインでは数秒待たされるため。
#
# -perfTestResults <path> を渡し、com.unity.collections の推移依存で入っている
# com.unity.test-framework.performance（6.6.0、builtin。本プロジェクトは Unity.PerformanceTesting を
# 直接参照していない）の結果保存先をプロジェクト内（Logs/test-results/、worktree ごとに分離済み）に
# 固定する。このオプションを渡さない場合、同パッケージの既定コールバック
# （Editor/PerformanceTestRunSaver.cs、ソースで確認）が Application.persistentDataPath 配下の
# 固定パス（TestResults.xml / PerformanceTestResults.json）を読み書きし、これは会社名・製品名だけで
# 決まるため全 worktree で同一になる。複数 worktree・複数 Editor で同時に verify を走らせると
# Sharing violation（IOException）になり、ログ判定が誤って失敗扱いする（#175）。
# -perfTestResults を渡すと com.unity.test-framework.performance の
# Editor/TestRunnerInitializer.cs（ソースで確認）が CmdLineResultsSavingCallbacks に切り替わり、
# テスト結果を in-memory の ITestResultAdaptor から直接読み、指定パスへ書く（persistentDataPath を
# 一切経由しない）ため、この競合が根本的に解消される。
#
# 実行の最初に scripts/tests/log-scan.tests.ps1（ログ判定ロジックの回帰テスト、Unity 不要・数秒）、
# scripts/tests/build-output-lock.tests.ps1（issue #131 レビュー M-6、Unity 不要・数秒）、
# scripts/tests/build-provenance.tests.ps1（issue #131 レビュー L-1、Unity 不要・数秒）を走らせる。
# ここで失敗する場合、後続の Unity バッチテストのログ判定も信用できないため即座に中断する（#61）。
# 続けて scripts/tests/tsumugi-expressions-tool.tests.ps1（表情生成ツールの起動スクリプト、#219）と
# scripts/tests/test_*.py（立ち絵生成スクリプトの単体テスト、#191 / #219。python が無ければスキップ）を走らせる。
# 単体で確認したい場合は `pwsh ./scripts/tests/log-scan.tests.ps1` /
# `pwsh ./scripts/tests/build-output-lock.tests.ps1` /
# `pwsh ./scripts/tests/build-provenance.tests.ps1` を直接実行する。

param(
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Platform = "All",

    [switch]$IncludeNetwork
)

. "$PSScriptRoot/common.ps1"

Write-Host "===== ログ判定ロジックの回帰テスト =====" -ForegroundColor Cyan
& "$PSScriptRoot/tests/log-scan.tests.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ログ判定ロジックの回帰テストに失敗しました。scripts/log-scan.ps1 を確認してください。" -ForegroundColor Red
    exit 1
}
Write-Host ""

Write-Host "===== build-output-lock ロジックの回帰テスト =====" -ForegroundColor Cyan
& "$PSScriptRoot/tests/build-output-lock.tests.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "build-output-lock ロジックの回帰テストに失敗しました。scripts/build-output-lock.ps1 を確認してください。" -ForegroundColor Red
    exit 1
}
Write-Host ""

Write-Host "===== build-provenance ロジックの回帰テスト =====" -ForegroundColor Cyan
& "$PSScriptRoot/tests/build-provenance.tests.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "build-provenance ロジックの回帰テストに失敗しました。scripts/common.ps1 の Test-BuildProvenance を確認してください。" -ForegroundColor Red
    exit 1
}
Write-Host ""

# issue #219: 配布 zip 同梱の表情生成ツールの起動スクリプト（Python の検出・Store のエイリアスの見分け）。
# Python を実際には起動しない（Unity・Python 不要・1 秒未満）。
Write-Host "===== 表情生成ツールの起動スクリプトの回帰テスト =====" -ForegroundColor Cyan
& "$PSScriptRoot/tests/tsumugi-expressions-tool.tests.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "表情生成ツールの起動スクリプトのテストに失敗しました。scripts/tsumugi-expressions-tool/make-expressions.ps1 を確認してください。" -ForegroundColor Red
    exit 1
}
Write-Host ""

# issue #191: 立ち絵の表情差分を生成するスクリプト（scripts/generate_tsumugi_expressions.py）の切り出し・縮小の
# 単体テスト。素材は使わず合成画像だけで動く（Unity 不要・1 秒未満）。Python はアプリの実行・ビルドには不要な
# 開発用の依存なので、python が無い環境ではスキップする（Pillow が無い場合はテスト側が画像処理のケースだけスキップする）。
# PR #202 レビュー M1: Get-Command だけでは、Python 未導入の Windows にある Microsoft Store のエイリアス
# （%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe。実行すると終了コード 9009）を「ある」と誤判定し、
# verify 全体が失敗する。実際に `python -c "import sys"` を実行して成功した場合だけテストを走らせる。
Write-Host "===== 立ち絵生成スクリプト（Python）の単体テスト =====" -ForegroundColor Cyan
$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
$pythonProbeExitCode = $null
if ($null -ne $pythonCommand) {
    try {
        & python -c "import sys" *> $null
        $pythonProbeExitCode = $LASTEXITCODE
    } catch {
        $pythonProbeExitCode = -1
    }
}
if ($null -eq $pythonCommand) {
    Write-Host "python が見つからないためスキップします（任意のテスト）。" -ForegroundColor Yellow
} elseif ($pythonProbeExitCode -ne 0) {
    Write-Host ("python を実行できないためスキップします（任意のテスト。{0}、終了コード {1}。Microsoft Store のエイリアスの可能性があります）。" -f $pythonCommand.Source, $pythonProbeExitCode) -ForegroundColor Yellow
} else {
    $previousDontWriteBytecode = $env:PYTHONDONTWRITEBYTECODE
    $env:PYTHONDONTWRITEBYTECODE = "1"
    try {
        & python -m unittest discover -s (Join-Path $PSScriptRoot "tests") -p "test_*.py"
        $pythonTestExitCode = $LASTEXITCODE
    } finally {
        $env:PYTHONDONTWRITEBYTECODE = $previousDontWriteBytecode
    }
    if ($pythonTestExitCode -ne 0) {
        Write-Host "立ち絵生成スクリプトの単体テストに失敗しました。scripts/generate_tsumugi_expressions.py / scripts/tsumugi_expressions_standalone.py を確認してください。" -ForegroundColor Red
        exit 1
    }
}
Write-Host ""

$projectRoot = Get-ProjectRoot
$resultsDir = Join-Path $projectRoot "Logs\test-results"
$logsDir = Join-Path $projectRoot "Logs"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$platforms = if ($Platform -eq "All") { @("EditMode", "PlayMode") } else { @($Platform) }

$overallSuccess = $true
$summaries = @()

foreach ($testPlatform in $platforms) {
    $categoryNote = if ($IncludeNetwork) { "（Network カテゴリを含む）" } else { "（Network カテゴリを除外）" }
    Write-Host "===== $testPlatform テストを実行 $categoryNote =====" -ForegroundColor Cyan

    $resultsPath = Join-Path $resultsDir "$testPlatform-results.xml"
    $logPath = Join-Path $logsDir "verify-$testPlatform.log"
    # com.unity.test-framework.performance の結果保存先（#175）。$resultsPath とは別ファイル。
    $perfResultsPath = Join-Path $resultsDir "$testPlatform-perf-results.json"

    if (Test-Path $resultsPath) {
        Remove-Item $resultsPath -Force
    }
    if (Test-Path $perfResultsPath) {
        Remove-Item $perfResultsPath -Force
    }

    # このプロセス（worktree）専用のデータルート（#71）。consent.json・TtsCache 等の書き込み先を
    # 実行単位で分離し、他 worktree・Editor で開いているプロジェクトとの Sharing violation を防ぐ。
    # 前回実行の残骸を引きずらないよう、起動前に必ずクリアする。
    $dataRoot = Join-Path $logsDir "test-data\$testPlatform"
    if (Test-Path $dataRoot) {
        Remove-Item $dataRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

    # 同じく、このプロセス専用の Documents ルート（問題フォルダ・プリセットフォルダの親、#112）。
    # テストアセンブリ側でも DocumentsPaths.ConfigureDefault で隔離しているが、優先順位そのものを
    # 検証するテストが DocumentsPaths.Reset() を呼ぶ間も実ユーザーの Documents を指さないよう、
    # より強い優先順位である環境変数でも二重に隔離する（PR #118 レビュー M-3）。
    $documentsRoot = Join-Path $logsDir "test-documents\$testPlatform"
    if (Test-Path $documentsRoot) {
        Remove-Item $documentsRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $documentsRoot | Out-Null

    $arguments = @(
        "-batchmode"
        "-nographics"
        "-projectPath", $projectRoot
        "-runTests"
        "-testPlatform", $testPlatform
        "-testResults", $resultsPath
        "-perfTestResults", $perfResultsPath
        "-logFile", $logPath
    )

    if (-not $IncludeNetwork) {
        $arguments += "-testCategory"
        $arguments += "!Network"
    }

    $exitCode = Invoke-UnityBatch -Arguments $arguments -TimeoutSeconds 1800 -EnvironmentVariables @{
        TSUMUGI_DATA_ROOT      = $dataRoot
        TSUMUGI_DOCUMENTS_ROOT = $documentsRoot
    }

    $logHasErrors = Test-LogHasErrors -LogPath $logPath

    if (-not (Test-Path $resultsPath)) {
        Write-Host "テスト結果 XML が生成されませんでした: $resultsPath" -ForegroundColor Red
        $overallSuccess = $false
        $summaries += [pscustomobject]@{
            Platform = $testPlatform
            Total    = 0
            Passed   = 0
            Failed   = 0
            ExitCode = $exitCode
        }
        continue
    }

    [xml]$xml = Get-Content $resultsPath
    $root = $xml.'test-run'
    if ($null -eq $root) {
        $root = $xml.DocumentElement
    }

    $total = [int]$root.total
    $passed = [int]$root.passed
    $failed = [int]$root.failed

    $summaries += [pscustomobject]@{
        Platform = $testPlatform
        Total    = $total
        Passed   = $passed
        Failed   = $failed
        ExitCode = $exitCode
    }

    Write-Host ("{0}: total={1} passed={2} failed={3} (Unity exit code={4})" -f $testPlatform, $total, $passed, $failed, $exitCode)

    if ($exitCode -ne 0 -or $failed -gt 0 -or $logHasErrors) {
        $overallSuccess = $false
    }
}

Write-Host ""
Write-Host "===== 結果サマリ =====" -ForegroundColor Cyan
$summaries | Format-Table -AutoSize

if (-not $overallSuccess) {
    Write-Host "検証に失敗しました。" -ForegroundColor Red
    exit 1
}

Write-Host "すべてのテストに成功しました。" -ForegroundColor Green
exit 0
