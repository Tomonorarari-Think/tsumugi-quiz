# scripts/build.ps1
# Unity バッチモードで Windows Standalone (x64) ビルドを実行する。
# 実体は Assets/TsumugiQuiz/Scripts/Editor/Build/BuildCommand.cs (TsumugiQuiz.Editor.BuildCommand.BuildWindows)。
# 出力先: 既定 Builds/Windows/TsumugiQuiz.exe（-OutputDir で変更可、issue #131）。
# レビュー L-7: 出力先は常に -buildOutputDir で Unity 側へ絶対パスとして渡すため、
# 実行時の CWD（カレントディレクトリ）に依存しない（従来は Unity 側が
# Directory.GetCurrentDirectory() を基準に解決していた）。
#
# 使い方:
#   pwsh ./scripts/build.ps1              # Mono (既定)
#   pwsh ./scripts/build.ps1 -IL2CPP      # IL2CPP に切替
#   pwsh ./scripts/build.ps1 -OutputDir E:\Claude\tsumugi-quiz\Builds\verify\Windows
#                                          # 固定パスへ出力（実機確認用。docs/dev-workflow.md §3.3.1）
#
# -OutputDir 使用時のみ、次の直列化・安全確認を行う（既定の出力先 Builds/Windows では行わない。
# 各 worktree 専用のパスのため worktree 間の競合が起こらないため）:
#   - 出力先の TsumugiQuiz.exe を実行中のプロセスがあれば、上書きせずエラー終了する
#     （プロセスは止めない。実機確認中のビルドを壊さないため）
#   - <OutputDir>/.build.lock による簡易ロックで、複数 worktree からの同時ビルドを直列化する
#     （scripts/build-output-lock.ps1、issue #131）
#   - レビュー H-1: ビルド成功後に <OutputDir>/.build-info.json（gitCommit / gitBranch /
#     sourceWorktree / builtAtUtc）を書き出す。固定パスは複数 worktree の共有資源であり、
#     出所の記録が無いと他 worktree のビルドを自分の実機証跡と誤認しうるため
#     （scripts/run-multi.ps1 -BuildDir が起動時にこの内容を検証する）

param(
    [switch]$IL2CPP,
    [string]$OutputDir
)

. "$PSScriptRoot/common.ps1"

$projectRoot = Get-ProjectRoot
$logsDir = Join-Path $projectRoot "Logs"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

$logPath = Join-Path $logsDir "build.log"

$usingExplicitOutputDir = -not [string]::IsNullOrWhiteSpace($OutputDir)
$resolvedOutputDir = if ($usingExplicitOutputDir) {
    Resolve-FullPathAllowMissing -Path $OutputDir
} else {
    Join-Path $projectRoot "Builds\Windows"
}
$outputExe = Join-Path $resolvedOutputDir "TsumugiQuiz.exe"

$lockPath = $null
if ($usingExplicitOutputDir) {
    $runningProcesses = @(Get-ProcessesRunningExe -ExePath $outputExe)
    if ($runningProcesses.Count -gt 0) {
        $pidList = ($runningProcesses | ForEach-Object { $_.ProcessId }) -join ", "
        Write-Host "出力先 ($resolvedOutputDir) の TsumugiQuiz.exe が実行中のため、ビルドを中止します（PID: $pidList）。実機確認プロセスを終了してから再実行してください（本スクリプトはプロセスを終了しません）。" -ForegroundColor Red
        exit 1
    }

    try {
        $lockPath = Enter-BuildOutputLock -OutputDir $resolvedOutputDir
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
}

$arguments = @(
    "-batchmode"
    "-nographics"
    "-quit"
    "-projectPath", $projectRoot
    "-executeMethod", "TsumugiQuiz.Editor.BuildCommand.BuildWindows"
    "-logFile", $logPath
    "-buildOutputDir", $resolvedOutputDir
)

if ($IL2CPP) {
    $arguments += "-IL2CPP"
}

Write-Host "===== Windows Standalone ビルドを開始 (scriptingBackend=$(if ($IL2CPP) { 'IL2CPP' } else { 'Mono' }), OutputDir=$resolvedOutputDir) =====" -ForegroundColor Cyan

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $exitCode = Invoke-UnityBatch -Arguments $arguments -TimeoutSeconds 3600
} finally {
    Exit-BuildOutputLock -LockPath $lockPath
}
$stopwatch.Stop()

$logHasErrors = Test-LogHasErrors -LogPath $logPath

$elapsed = $stopwatch.Elapsed
Write-Host ("所要時間: {0:hh\:mm\:ss}" -f $elapsed)

if ($exitCode -ne 0) {
    Write-Host "ビルドに失敗しました (Unity exit code=$exitCode)。$logPath を確認してください。" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $outputExe)) {
    Write-Host "ビルド成功と報告されましたが、出力ファイルが見つかりません: $outputExe" -ForegroundColor Red
    exit 1
}

# レビュー H-1: -OutputDir 使用時のみ出所記録を書き出す（固定パスは複数 worktree の共有資源のため）。
# レビュー M-2: 未コミットの変更がある状態でのビルドかどうか（dirty）も記録する
# （gitCommit が一致していても、未コミットの変更を含む場合は「同一のビルド」とは限らないため。
# scripts/run-multi.ps1 -BuildDir 側が警告を出す判断材料にする）。
if ($usingExplicitOutputDir) {
    $gitInfo = Get-GitHeadInfo -RepoPath $projectRoot
    $isDirty = Test-GitWorkingTreeDirty -RepoPath $projectRoot
    $buildInfo = [pscustomobject]@{
        gitCommit      = if ($gitInfo) { $gitInfo.Commit } else { $null }
        gitBranch      = if ($gitInfo) { $gitInfo.Branch } else { $null }
        sourceWorktree = $projectRoot
        builtAtUtc     = [DateTime]::UtcNow.ToString("o")
        dirty          = $isDirty
    }
    $buildInfoPath = Join-Path $resolvedOutputDir ".build-info.json"

    # レビュー M-4: 書き出しに失敗した場合、古い（前回実行の）.build-info.json を残したまま
    # ビルド成功として終了しない。中途半端に壊れたファイルが残っている可能性もあるため、
    # 失敗時は削除してから非0で終了する。
    try {
        ($buildInfo | ConvertTo-Json) | Set-Content -LiteralPath $buildInfoPath -Encoding UTF8 -ErrorAction Stop
    } catch {
        Write-Host "出所記録の書き出しに失敗しました: $buildInfoPath ($_)" -ForegroundColor Red
        Remove-Item -LiteralPath $buildInfoPath -Force -ErrorAction SilentlyContinue
        exit 1
    }

    if ($null -eq $gitInfo) {
        Write-Warning "git の HEAD 情報を取得できませんでした。$buildInfoPath の gitCommit/gitBranch は null になります。"
    } else {
        Write-Host "出所記録を書き出しました: $buildInfoPath (gitCommit=$($gitInfo.Commit), gitBranch=$($gitInfo.Branch), dirty=$isDirty)" -ForegroundColor DarkGray
    }
}

$sizeBytes = (Get-Item $outputExe).Length
$sizeMB = [math]::Round($sizeBytes / 1MB, 2)

Write-Host "ビルド成功: $outputExe ($sizeMB MB)" -ForegroundColor Green

if ($logHasErrors) {
    Write-Warning "ログにエラーらしき行がありました。詳細は上の出力および $logPath を確認してください。"
}

exit 0
