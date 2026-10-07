# scripts/tests/build-output-lock.tests.ps1
# scripts/build-output-lock.ps1 のロック取得・解放ロジックに対する回帰テスト（issue #131 レビュー M-6）。
#
# 実行:
#   pwsh ./scripts/tests/build-output-lock.tests.ps1
#
# Unity 不要・数秒で完了する。一時ディレクトリに .build.lock を作って検証するだけで、
# 実際の Builds/ 配下やプロジェクトの他のファイルには一切触れない。

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../build-output-lock.ps1"

$script:failures = @()
$script:passCount = 0
$script:totalCount = 0

function Test-Case {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Assertion
    )

    $script:totalCount++
    try {
        & $Assertion
        $script:passCount++
        Write-Host "  [OK]   $Name" -ForegroundColor Green
    } catch {
        $script:failures += "$Name => $_"
        Write-Host "  [NG]   $Name : $_" -ForegroundColor Red
    }
}

function New-TempTestDir {
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("tq-build-lock-test-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

# (a) ロック無し → 取得できる。
Test-Case -Name "(a) ロック無しで取得できる" -Assertion {
    $dir = New-TempTestDir
    try {
        $lockPath = Enter-BuildOutputLock -OutputDir $dir
        if (-not (Test-Path -LiteralPath $lockPath)) {
            throw "ロックファイルが作成されていません: $lockPath"
        }
        $data = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
        if ([int]$data.pid -ne $PID) {
            throw "記録された pid ($($data.pid)) が自分の PID ($PID) と一致しません"
        }
    } finally {
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (b) 存在しない PID を記録した古いロック → 無視して取得できる。
Test-Case -Name "(b) 存在しない PID の古いロックは無視される" -Assertion {
    $dir = New-TempTestDir
    try {
        $lockPath = Join-Path $dir ".build.lock"
        ([pscustomobject]@{ pid = 999999; startedAtUtc = (Get-Date).ToString("o") } | ConvertTo-Json) |
            Set-Content -LiteralPath $lockPath -Encoding UTF8
        $result = Enter-BuildOutputLock -OutputDir $dir
        if (-not (Test-Path -LiteralPath $result)) {
            throw "ロック取得に失敗しました"
        }
    } finally {
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (c) 生存している pwsh プロセスの PID を記録したロック → 例外。
Test-Case -Name "(c) 生存中の pwsh PID を記録したロックは例外になる" -Assertion {
    $dir = New-TempTestDir
    $bgProcess = $null
    try {
        $pwshPath = (Get-Process -Id $PID).Path
        $bgProcess = Start-Process -FilePath $pwshPath -ArgumentList "-NoProfile", "-Command", "Start-Sleep -Seconds 20" -PassThru
        Start-Sleep -Milliseconds 500

        $lockPath = Join-Path $dir ".build.lock"
        ([pscustomobject]@{ pid = $bgProcess.Id; startedAtUtc = (Get-Date).ToString("o") } | ConvertTo-Json) |
            Set-Content -LiteralPath $lockPath -Encoding UTF8

        $threw = $false
        try {
            Enter-BuildOutputLock -OutputDir $dir | Out-Null
        } catch {
            $threw = $true
        }
        if (-not $threw) {
            throw "例外が送出されませんでした"
        }
    } finally {
        if ($bgProcess -and -not $bgProcess.HasExited) {
            Stop-Process -Id $bgProcess.Id -Force -ErrorAction SilentlyContinue
        }
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (d) JSON が壊れている → 古いロックとして無視される。
Test-Case -Name "(d) 壊れた JSON は古いロックとして無視される" -Assertion {
    $dir = New-TempTestDir
    try {
        $lockPath = Join-Path $dir ".build.lock"
        Set-Content -LiteralPath $lockPath -Value "{ this is not valid json" -Encoding UTF8
        $result = Enter-BuildOutputLock -OutputDir $dir
        if (-not (Test-Path -LiteralPath $result)) {
            throw "ロック取得に失敗しました"
        }
    } finally {
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (e) Exit-BuildOutputLock は自分の pid のロックを削除する。
Test-Case -Name "(e) Exit-BuildOutputLock は自分の PID のロックを削除する" -Assertion {
    $dir = New-TempTestDir
    try {
        $lockPath = Enter-BuildOutputLock -OutputDir $dir
        Exit-BuildOutputLock -LockPath $lockPath
        if (Test-Path -LiteralPath $lockPath) {
            throw "ロックファイルが削除されていません: $lockPath"
        }
    } finally {
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (e') レビュー L-3 の回帰確認: Exit-BuildOutputLock は他 PID のロックを削除しない。
Test-Case -Name "(e') Exit-BuildOutputLock は他 PID のロックを削除しない" -Assertion {
    $dir = New-TempTestDir
    try {
        $lockPath = Join-Path $dir ".build.lock"
        ([pscustomobject]@{ pid = 999998; startedAtUtc = (Get-Date).ToString("o") } | ConvertTo-Json) |
            Set-Content -LiteralPath $lockPath -Encoding UTF8
        Exit-BuildOutputLock -LockPath $lockPath
        if (-not (Test-Path -LiteralPath $lockPath)) {
            throw "他 PID のロックファイルが削除されてしまいました: $lockPath"
        }
    } finally {
        Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# (f) レビュー L-2: 存在しない exe パスを指定すると空配列を返す。
Test-Case -Name "(f) Get-ProcessesRunningExe は存在しないパスで空配列を返す" -Assertion {
    $nonExistentPath = Join-Path ([System.IO.Path]::GetTempPath()) ("tq-nonexistent-" + [Guid]::NewGuid().ToString("N") + ".exe")
    $result = @(Get-ProcessesRunningExe -ExePath $nonExistentPath)
    if ($result.Count -ne 0) {
        throw "空配列ではありませんでした（$($result.Count) 件）"
    }
}

# (g) レビュー L-2: 自分自身（このテストを実行している pwsh.exe）の exe パスを指定すると、
# 自分の PID が結果に含まれる（"TsumugiQuiz.exe" 決め打ちを解消したことの回帰確認、M-3）。
Test-Case -Name "(g) Get-ProcessesRunningExe は自分の pwsh.exe を指定すると自分の PID を含む" -Assertion {
    $selfExePath = (Get-Process -Id $PID).Path
    $result = @(Get-ProcessesRunningExe -ExePath $selfExePath)
    $selfEntry = $result | Where-Object { $_.ProcessId -eq $PID }
    if (-not $selfEntry) {
        throw "自分の PID ($PID) が結果に含まれていません（$($result.Count) 件中）"
    }
}

Write-Host ""
Write-Host ("結果: {0}/{1} 件成功" -f $script:passCount, $script:totalCount) -ForegroundColor Cyan

if ($script:failures.Count -gt 0) {
    Write-Host "----- 失敗したケース -----" -ForegroundColor Red
    foreach ($f in $script:failures) {
        Write-Host $f -ForegroundColor Red
    }
    exit 1
}

Write-Host "すべての build-output-lock テストに成功しました。" -ForegroundColor Green
exit 0
