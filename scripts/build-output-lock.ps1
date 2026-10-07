# scripts/build-output-lock.ps1
# scripts/build.ps1 -OutputDir 使用時の直列化・実行中プロセス検出ロジック（issue #131）。
# 複数 worktree が同じ固定パス（例: E:\Claude\tsumugi-quiz\Builds\verify\Windows）へ同時に
# ビルドすると出力が競合するため、<OutputDir>/.build.lock による簡易的な排他と、
# 実機確認プロセス（TsumugiQuiz.exe 等、出力先の exe と同名のプロセス）が対象 OutputDir の
# exe を実行中かどうかの検出を行う。
#
# 【既知の制約】ロック取得自体に TOCTOU の隙がある（Test-Path での存在確認と Set-Content での
# 書き込みの間に別プロセスが割り込む可能性を排除できない）。手動でのローカル検証用途を想定した
# 安全網であり、CI 等の高頻度・自動的な同時実行までは保証しない。

$script:BuildLockFileName = ".build.lock"

# 指定した exe パスを ExecutablePath として実行中のプロセスを列挙する（Get-CimInstance Win32_Process）。
# レビュー M-3: WMI の Name フィルタは "TsumugiQuiz.exe" 決め打ちではなく、$ExePath のファイル名を
# 使う（将来 exe 名が変わっても追従できるようにするため）。
# プロセスは止めない。呼び出し側が警告・エラー表示に使うだけ。
function Get-ProcessesRunningExe {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath
    )

    if (-not (Test-Path -LiteralPath $ExePath)) {
        return @()
    }

    $resolvedExePath = (Resolve-Path -LiteralPath $ExePath).Path
    $exeName = Split-Path -Path $resolvedExePath -Leaf
    # WQL の文字列リテラル内でシングルクォートをエスケープする。
    $escapedExeName = $exeName -replace "'", "''"
    $candidates = Get-CimInstance -ClassName Win32_Process -Filter "Name = '$escapedExeName'" -ErrorAction SilentlyContinue
    return @($candidates | Where-Object {
        $_.ExecutablePath -and ($_.ExecutablePath -ieq $resolvedExePath)
    })
}

# <OutputDir>/.build.lock を確保する。
# - ロックファイルが存在し、記録された pid のプロセスがまだ生きていれば、他 worktree がこの
#   出力先へビルド中とみなして例外を投げる（呼び出し側でエラー終了させる）。
# - 記録された pid のプロセスが既に終了している（または pid が別プロセスに再利用されて
#   pwsh/powershell ではなくなっている）場合は「古いロック」とみなして無視し、上書きする。
# - ロックファイルが壊れている（JSON として読めない）場合も「古いロック」として無視する。
# レビュー M-2: ロックディレクトリの作成・ロックファイルの書き込みに失敗した場合は
# -ErrorAction Stop で例外化し、ロックを取得できていないのに処理が進んでしまう事故を防ぐ。
function Enter-BuildOutputLock {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDir
    )

    New-Item -ItemType Directory -Force -Path $OutputDir -ErrorAction Stop | Out-Null
    $lockPath = Join-Path $OutputDir $script:BuildLockFileName

    if (Test-Path -LiteralPath $lockPath) {
        $ownerStillAlive = $false
        $ownerPid = $null
        try {
            $lockData = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
            $ownerPid = [int]$lockData.pid
            $ownerProcess = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
            if ($ownerProcess -and @("pwsh", "powershell") -contains $ownerProcess.ProcessName) {
                $ownerStillAlive = $true
            }
        } catch {
            Write-Warning "ロックファイルを解析できませんでした。壊れたロックとみなして無視します: $lockPath ($_)"
        }

        if ($ownerStillAlive) {
            throw "他の worktree がこの出力先 ($OutputDir) でビルド中の可能性があります（ロック所有 PID $ownerPid、$lockPath）。完了を待ってから再実行してください。"
        } elseif ($ownerPid) {
            Write-Warning "古いロックファイルを検出しました（所有 PID $ownerPid は存在しないか別プロセスに再利用されています）。無視して上書きします: $lockPath"
        }
    }

    # レビュー L-2: キーは camelCase（.build-info.json と統一）。
    $lockData = [pscustomobject]@{
        pid          = $PID
        startedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    ($lockData | ConvertTo-Json) | Set-Content -LiteralPath $lockPath -Encoding UTF8 -ErrorAction Stop

    return $lockPath
}

# レビュー L-3: ロックファイルに記録された pid が自分の PID と一致するときだけ削除する。
# 一致しない場合（例: 自分の Enter-BuildOutputLock がタイムアウト等で失敗し、別プロセスが
# 新しくロックを取得し直した後にこちらの finally が走った等）は、他プロセスのロックを
# 誤って消してしまわないよう削除をスキップし警告する。
function Exit-BuildOutputLock {
    param(
        [string]$LockPath
    )

    if (-not $LockPath -or -not (Test-Path -LiteralPath $LockPath)) {
        return
    }

    try {
        $lockData = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
        if ([int]$lockData.pid -eq $PID) {
            Remove-Item -LiteralPath $LockPath -Force -ErrorAction SilentlyContinue
        } else {
            Write-Warning "ロックファイルの所有 PID ($($lockData.pid)) が自分の PID ($PID) と一致しないため削除しません: $LockPath"
        }
    } catch {
        Write-Warning "ロックファイルの内容を確認できなかったため削除しません: $LockPath ($_)"
    }
}
