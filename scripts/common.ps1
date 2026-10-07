# common.ps1
# scripts/*.ps1 から dot-source して使う共通ヘルパー。
# 使い方: . "$PSScriptRoot/common.ps1"

$script:UnityExePath = "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
$script:ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# ログのエラー判定ロジック本体は scripts/log-scan.ps1 に切り出している（#61）。
. "$PSScriptRoot/log-scan.ps1"

# scripts/build.ps1 -OutputDir 使用時の直列化ロジックは scripts/build-output-lock.ps1 に切り出している（#131）。
. "$PSScriptRoot/build-output-lock.ps1"

# 相対パスも絶対パスも受け取り、存在しないパスでも（作成前でも）フルパスへ正規化する。
# Resolve-Path は対象が存在しないと失敗するため、-OutputDir / -BuildDir のように
# 事前に New-Item していないパスを扱う箇所で使う。
function Resolve-FullPathAllowMissing {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path (Get-Location).Path $Path
    }

    return [System.IO.Path]::GetFullPath($Path)
}

# issue #131 レビュー H-1: 固定パスビルドの出所記録（.build-info.json）・照合に使う、
# 指定リポジトリの現在の HEAD コミット・ブランチを取得する。
# git コマンドが無い/失敗した場合（例: git 未インストール環境）は $null を返す
# （呼び出し側が「取得できない」場合の扱いを決める。例外は投げない）。
function Get-GitHeadInfo {
    param(
        [string]$RepoPath = (Get-ProjectRoot)
    )

    try {
        $commit = (& git -C $RepoPath rev-parse HEAD 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
            return $null
        }
        $branch = (& git -C $RepoPath rev-parse --abbrev-ref HEAD 2>$null)
        return [pscustomobject]@{
            Commit = $commit.Trim()
            Branch = if ([string]::IsNullOrWhiteSpace($branch)) { $null } else { $branch.Trim() }
        }
    } catch {
        return $null
    }
}

# issue #131 レビュー M-2: 指定リポジトリの作業ツリーに未コミットの変更があるかどうかを返す。
# git コマンドが無い/失敗した場合は「わからない」を「dirty ではない」として $false を返す
# （dirty 判定はあくまで警告用の補助情報であり、誤って過剰に警告を出すより誤って警告を出さない
# 方を安全側とみなす）。
# 【issue #142 項目 5】この $false は「クリーンと確認できた」場合と区別が付かない
# （git 失敗時も dirty=false として記録される）。呼び出し側（scripts/run-multi.ps1 等）で
# 判定不能を区別する必要が生じた場合は改めて対応する。
function Test-GitWorkingTreeDirty {
    param(
        [string]$RepoPath = (Get-ProjectRoot)
    )

    try {
        $porcelain = & git -C $RepoPath status --porcelain 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $false
        }
        return -not [string]::IsNullOrWhiteSpace(($porcelain -join "`n"))
    } catch {
        return $false
    }
}

# issue #131 レビュー H-1/M-1/M-2/L-1: 固定パスのビルド出所記録（.build-info.json）を検証し、
# 呼び出し元（scripts/run-multi.ps1 -BuildDir）がそのまま使ってよいかどうかを判定する。
# 単体テスト（scripts/tests/build-provenance.tests.ps1）から直接呼べるよう、throw はせず
# 判定結果を返すだけにする（呼び出し側が Allowed を見て throw するかどうかを決める）。
#
# 戻り値のプロパティ:
#   BuildInfoPath : 検証対象の .build-info.json のパス
#   BuildInfo     : 読み取れた場合の内容（読めなければ $null）
#   Verifiable    : gitCommit（記録側・実行側の両方）が揃っており、照合できたかどうか
#   Matches       : gitCommit と sourceWorktree（大文字小文字無視・末尾の \ / を無視・
#                   絶対パスであれば ".." 等を含む表記も GetFullPath で正規化して吸収。
#                   相対値はそのままでは絶対パスの自分の worktree と一致しえないため拒否扱いになる。
#                   issue #142 レビュー M-2/L-1/L-2）の両方が
#                   自分の worktree と一致するかどうか（Verifiable が $false のときは常に $false）
#   Dirty         : 記録側ビルド時に未コミットの変更があったかどうか（.build-info.json の dirty）
#   Reason        : 検証不能・不一致の理由（"NotFound" / "ParseError" / "MissingCommitInfo" /
#                   "Mismatch" / $null（一致）のいずれか）
#   Allowed       : 呼び出し元がこのビルドをそのまま使ってよいかどうかの最終判定
#                   （一致 / または -AllowForeignBuild 指定時は常に $true）
#   Warnings      : 呼び出し側が表示すべき警告文の配列（Allowed = $true でも dirty や
#                   -AllowForeignBuild 由来の警告が入ることがある）
#
# 【レビュー M-1 対応】「検証できない」（.build-info.json が無い・壊れている・gitCommit が
# 記録側/実行側のどちらかで取得できない）場合を、不一致（Mismatch）と同じく Allowed = $false
# として扱う（-AllowForeignBuild 指定時のみ警告のうえ Allowed = $true にする）。以前の実装は
# これらのケースで暗黙的に「素通り」させていた。
#
# 【レビュー M-2 対応】gitCommit の一致だけでなく sourceWorktree の一致も要求する（同じ HEAD の
# 別 worktree、例えば develop から切ったばかりの feature ブランチで日常的に起こる状況を
# 誤って「同一」と判定しないため）。加えて記録側ビルド時に未コミットの変更があった場合は
# Dirty = $true とし、Matches = $true でも Warnings に「コミット照合では同一性を保証できない」
# 旨を積む。
#
# issue #142 レビュー M-2: sourceWorktree の正規化は「表記の揺れを吸収して一致させやすくする」
# 方向の変更であり、誤って緩めすぎる（本来別の場所を指す相対値を偶然一致させてしまう）と
# 出所照合の安全側の判定が崩れる。そのため GetFullPath による正規化は「既に絶対パスである」
# 値にのみ適用し、相対値（IsPathRooted が $false）はそのまま比較して確実に不一致＝拒否とする。
function Get-NormalizedWorktreePath {
    param(
        [AllowEmptyString()][string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    $normalized = $Path
    if ([System.IO.Path]::IsPathRooted($normalized)) {
        try {
            # issue #142 レビュー L-2: TrimEnd は GetFullPath の後に適用する
            # （GetFullPath が末尾区切りの有無で結果を変えないことに依存しないため）。
            $normalized = [System.IO.Path]::GetFullPath($normalized)
        } catch {
            # 不正なパス文字列等で GetFullPath が失敗した場合は元の値のまま返す
            # （比較で不一致になりうるが、例外で処理全体を止めない）。
        }
    }

    return $normalized.TrimEnd('\', '/')
}

function Test-BuildProvenance {
    param(
        [Parameter(Mandatory = $true)][string]$BuildDir,
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [switch]$AllowForeignBuild
    )

    $buildInfoPath = Join-Path $BuildDir ".build-info.json"
    $result = [pscustomobject]@{
        BuildInfoPath = $buildInfoPath
        BuildInfo     = $null
        Verifiable    = $false
        Matches       = $false
        Dirty         = $false
        Reason        = $null
        Allowed       = $false
        Warnings      = @()
    }

    if (-not (Test-Path -LiteralPath $buildInfoPath)) {
        $result.Reason = "NotFound"
    } else {
        try {
            $result.BuildInfo = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json
        } catch {
            $result.Reason = "ParseError"
        }

        if ($result.BuildInfo) {
            $currentGitInfo = Get-GitHeadInfo -RepoPath $ProjectRoot
            $currentCommit = if ($currentGitInfo) { $currentGitInfo.Commit } else { $null }

            if (-not $result.BuildInfo.gitCommit -or -not $currentCommit) {
                $result.Reason = "MissingCommitInfo"
            } else {
                $result.Verifiable = $true

                # issue #142 項目 3 / レビュー M-2・L-1・L-2: TrimEnd + -ieq だけでは相対パス表記・
                # 大文字小文字混在のドライブ文字・".." を含む表記等の差異を吸収できないため、
                # Get-NormalizedWorktreePath で両辺を同じ規則（絶対パスのみ GetFullPath で正規化、
                # 相対値はそのまま＝不一致扱い）で正規化する。
                $normalizedSource = Get-NormalizedWorktreePath -Path ([string]$result.BuildInfo.sourceWorktree)
                $normalizedProjectRoot = Get-NormalizedWorktreePath -Path $ProjectRoot

                $commitMatches = ($result.BuildInfo.gitCommit -eq $currentCommit)
                $worktreeMatches = $normalizedSource -and ($normalizedSource -ieq $normalizedProjectRoot)
                $result.Matches = $commitMatches -and $worktreeMatches
                $result.Dirty = [bool]$result.BuildInfo.dirty

                if (-not $result.Matches) {
                    $result.Reason = "Mismatch"
                }
            }
        }
    }

    if ($result.Verifiable -and $result.Matches) {
        $result.Allowed = $true
        if ($result.Dirty) {
            $result.Warnings += "固定パスのビルドは未コミットの変更を含む状態で作られています（dirty=true）。コミット照合では同一性を保証できません。念のため -BuildFirst で再ビルドすることを推奨します。"
        }
        return $result
    }

    if ($AllowForeignBuild) {
        $result.Allowed = $true
        $result.Warnings += switch ($result.Reason) {
            "NotFound" { "$buildInfoPath が見つからないため出所を検証できませんが、そのまま使用します。" }
            "ParseError" { "$buildInfoPath を解析できないため出所を検証できませんが、そのまま使用します。" }
            "MissingCommitInfo" { "gitCommit を取得できないため出所を検証できませんが、そのまま使用します。" }
            "Mismatch" { "固定パスのビルドは別 worktree/コミットのものですが、そのまま使用します（gitCommit=$($result.BuildInfo.gitCommit)、sourceWorktree=$($result.BuildInfo.sourceWorktree)）。" }
            default { "出所を検証できませんが、そのまま使用します。" }
        }
    }

    return $result
}

function Get-UnityExePath {
    if (-not (Test-Path $script:UnityExePath)) {
        throw "Unity Editor が見つかりません: $script:UnityExePath"
    }
    return $script:UnityExePath
}

function Get-ProjectRoot {
    return $script:ProjectRoot
}

# AppPaths.DataRoot（TsumugiQuiz.Core.AppPaths、#71）相当のパスを解決する。
# issue #8 レビュー M-5: scripts/setup-external.ps1（issue #24 / #77）にあった同名関数を、
# scripts/run-multi.ps1 とも共有できるようこちらへ統合した。setup-external.ps1 側はこの共通版を使う。
#
# 優先順位は AppPaths 自身と合わせる:
#   1) -ExplicitDataRoot（呼び出し側の明示引数。例: setup-external.ps1 -DataRoot）
#   2) 環境変数 TSUMUGI_DATA_ROOT（scripts/verify.ps1 がテスト実行のワークツリー分離用に設定する。
#      テスト用データルートの可能性があり、アプリ実行時には参照されない場合がある）
#   3) ProjectSettings.asset の companyName/productName から Application.persistentDataPath 相当を
#      組み立てる（Windows: %USERPROFILE%\AppData\LocalLow\<companyName>\<productName>）
# いずれも得られない場合は $null を返す（呼び出し側は案内メッセージのみ表示する）。
# 戻り値は { Path; Source }（Source は "Explicit" / "Environment" / "ProjectSettings"）。
function Resolve-AppDataRoot {
    param(
        [string]$ExplicitDataRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitDataRoot)) {
        return [pscustomobject]@{ Path = $ExplicitDataRoot; Source = "Explicit" }
    }

    if (-not [string]::IsNullOrWhiteSpace($env:TSUMUGI_DATA_ROOT)) {
        return [pscustomobject]@{ Path = $env:TSUMUGI_DATA_ROOT; Source = "Environment" }
    }

    $settingsPath = Join-Path (Get-ProjectRoot) "ProjectSettings\ProjectSettings.asset"
    if (-not (Test-Path $settingsPath)) {
        return $null
    }

    $content = Get-Content $settingsPath -Raw
    $companyName = $null
    $productName = $null
    if ($content -match "(?m)^\s*companyName:\s*(.+)\s*$") { $companyName = $Matches[1].Trim() }
    if ($content -match "(?m)^\s*productName:\s*(.+)\s*$") { $productName = $Matches[1].Trim() }

    if ([string]::IsNullOrWhiteSpace($companyName) -or [string]::IsNullOrWhiteSpace($productName)) {
        return $null
    }

    return [pscustomobject]@{
        Path   = (Join-Path $env:USERPROFILE "AppData\LocalLow\$companyName\$productName")
        Source = "ProjectSettings"
    }
}

# Library/ 配下の UnityLockfile が残っていると、既に Unity プロセスが動作中と判定され
# バッチモード起動が待機・失敗することがあるため、事前に検出して警告する。
function Test-UnityLockfileHeld {
    param(
        [string]$ProjectPath = (Get-ProjectRoot)
    )

    $lockfilePath = Join-Path $ProjectPath "Library\UnityLockfile"
    if (-not (Test-Path $lockfilePath)) {
        return $false
    }

    # UnityLockfile はロック取得中のプロセスがある間は他プロセスから排他ロックされている。
    # 読み取り専用でオープンを試み、失敗したら「保持中」とみなす。
    try {
        $stream = [System.IO.File]::Open($lockfilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

# System.Diagnostics.ProcessStartInfo.ArgumentList を使って Unity をバッチモードで起動する。
# Start-Process -ArgumentList は引数中のスペース・引用符の扱いが不安定なため使わない。
function Invoke-UnityBatch {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int]$TimeoutSeconds = 1800,
        # 起動する Unity プロセスにだけ渡す追加の環境変数（#71。TSUMUGI_DATA_ROOT の worktree 分離用）。
        # プロセス自身（pwsh）の環境は変更しない。
        [hashtable]$EnvironmentVariables = @{}
    )

    if (Test-UnityLockfileHeld) {
        Write-Warning "Library/UnityLockfile が存在し、他の Unity プロセスが起動中の可能性があります。Unity Editor / Hub を終了してから再実行してください。"
        return 1
    }

    $unityExe = Get-UnityExePath

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $unityExe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($arg in $Arguments) {
        $startInfo.ArgumentList.Add($arg)
    }

    foreach ($key in $EnvironmentVariables.Keys) {
        $startInfo.Environment[$key] = $EnvironmentVariables[$key]
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Warning "Unity プロセスがタイムアウトしました (${TimeoutSeconds}秒)。強制終了します。"
        try { $process.Kill() } catch {}
        return 1
    }

    return $process.ExitCode
}

function Test-LogHasErrors {
    param(
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    if (-not (Test-Path $LogPath)) {
        Write-Warning "ログファイルが見つかりません: $LogPath"
        return $true
    }

    # 判定ロジック本体は scripts/log-scan.ps1（Get-LogScanErrorMatches）。
    # 対象・除外ルールの詳細は docs/dev-workflow.md を参照。
    $realMatches = @(Get-LogScanErrorMatches -LogPath $LogPath)

    if ($realMatches.Count -gt 0) {
        Write-Host "----- ログ内のエラー候補 -----" -ForegroundColor Yellow
        foreach ($m in $realMatches) {
            Write-Host ("{0}: {1}" -f $m.LineNumber, $m.Line)
        }
        return $true
    }

    return $false
}
