# scripts/tests/build-provenance.tests.ps1
# scripts/common.ps1 の Test-BuildProvenance に対する回帰テスト（issue #131 レビュー M-1/M-2/L-1）。
#
# 実行:
#   pwsh ./scripts/tests/build-provenance.tests.ps1
#
# Unity 不要・数秒で完了する。一時ディレクトリ・使い捨ての git リポジトリ（1コミットのみ）を
# 使って検証するだけで、実際の Builds/ 配下やこのリポジトリ自体には一切触れない。

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../common.ps1"

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
    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("tq-build-provenance-test-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

# issue #142 項目 2: 実行者のグローバル git 設定（commit.gpgsign=true で GPG 署名が要求される、
# core.hooksPath で本リポジトリの pre-push フック等が誤って適用される、等）に依存しないよう、
# ここで使う git 呼び出しはすべて -c で隔離した設定を明示する。
$script:IsolatedGitConfigArgs = @(
    "-c", "commit.gpgsign=false",
    "-c", "tag.gpgsign=false",
    "-c", "core.hooksPath=",
    "-c", "user.name=Test",
    "-c", "user.email=test@example.com"
)

# issue #142 レビュー M-3: git がエラー終了した場合、呼び出し元は理由が分からないまま
# 空/欠損の出力を受け取ることになる。$LASTEXITCODE を確認し、失敗時は git の出力（stderr 含む）
# を添えて throw する。
#
# issue #142 レビュー L-3: -c での上書きは指定したキーのみに限られるため、実行者のグローバル
# git 設定にある未知のキー（例: init.defaultBranch、credential.helper 等）の影響が残りうる。
# GIT_CONFIG_NOSYSTEM でシステム設定を、GIT_CONFIG_GLOBAL で実在しないダミーパスを指すことで
# ユーザーのグローバル設定ファイルそのものを読ませないようにする（git 2.32+。それより古い git では
# 単に無視され、-c による明示上書きは従来通り効くため実害はない）。
function Invoke-IsolatedGit {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$GitArgs
    )

    $previousNoSystem = $env:GIT_CONFIG_NOSYSTEM
    $previousGlobal = $env:GIT_CONFIG_GLOBAL
    try {
        $env:GIT_CONFIG_NOSYSTEM = "1"
        $env:GIT_CONFIG_GLOBAL = Join-Path ([System.IO.Path]::GetTempPath()) "tq-build-provenance-test-dummy-gitconfig-does-not-exist"

        $output = & git -C $Path @script:IsolatedGitConfigArgs @GitArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "git $($GitArgs -join ' ') が失敗しました (exit=$LASTEXITCODE): $($output -join "`n")"
        }
        return $output
    } finally {
        if ($null -eq $previousNoSystem) {
            Remove-Item Env:GIT_CONFIG_NOSYSTEM -ErrorAction SilentlyContinue
        } else {
            $env:GIT_CONFIG_NOSYSTEM = $previousNoSystem
        }
        if ($null -eq $previousGlobal) {
            Remove-Item Env:GIT_CONFIG_GLOBAL -ErrorAction SilentlyContinue
        } else {
            $env:GIT_CONFIG_GLOBAL = $previousGlobal
        }
    }
}

# テスト用の使い捨て git リポジトリを 1 コミットだけ作って初期化する。
# issue #142 レビュー L-3: --template= を明示し、実行者の git テンプレートディレクトリ
# （コミットフック等が仕込まれている可能性がある）を使わせない。
function New-TempGitRepo {
    param([Parameter(Mandatory = $true)][string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-IsolatedGit -Path $Path -GitArgs @("init", "--quiet", "--template=") | Out-Null
    Set-Content -Path (Join-Path $Path "README.txt") -Value "test" -Encoding UTF8
    Invoke-IsolatedGit -Path $Path -GitArgs @("add", ".") | Out-Null
    Invoke-IsolatedGit -Path $Path -GitArgs @("commit", "-m", "init", "--quiet") | Out-Null
}

# issue #142 レビュー L-4: リポジトリの現在の HEAD コミットハッシュを取得する。テスト内で
# 直接 `git rev-parse` を呼ばず、隔離済みの Invoke-IsolatedGit 経由に統一する。
function Get-TempRepoHeadCommit {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Invoke-IsolatedGit -Path $Path -GitArgs @("rev-parse", "HEAD")).Trim()
}

function Write-BuildInfo {
    param(
        [Parameter(Mandatory = $true)][string]$BuildDir,
        [Parameter(Mandatory = $true)][pscustomobject]$BuildInfo
    )
    ($BuildInfo | ConvertTo-Json) | Set-Content -Path (Join-Path $BuildDir ".build-info.json") -Encoding UTF8
}

# ---- (無い) .build-info.json が存在しない ----------------------------------------------

Test-Case -Name "(無い) .build-info.json 不在は既定で Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "NotFound") { throw "Reason が NotFound ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case -Name "(無い) -AllowForeignBuild 指定時は Allowed=true" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo -AllowForeignBuild
        if (-not $result.Allowed) { throw "Allowed が false のままです" }
        if ($result.Warnings.Count -eq 0) { throw "警告が積まれていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- (壊れている) JSON が壊れている ------------------------------------------------------

Test-Case -Name "(壊れている) 壊れた JSON は既定で Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        Set-Content -Path (Join-Path $buildDir ".build-info.json") -Value "{ not valid json" -Encoding UTF8
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "ParseError") { throw "Reason が ParseError ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- (MissingCommitInfo) .build-info.json の gitCommit が記録されていない ---------------
# issue #142 項目 1（レビュー L-5）: .build-info.json 自体は読めるが gitCommit が null
# （記録側で git 取得に失敗した等）の場合、Test-BuildProvenance の
# `-not $result.BuildInfo.gitCommit -or -not $currentCommit` 分岐で Reason=MissingCommitInfo
# になることを検証する。

Test-Case -Name "(MissingCommitInfo) gitCommit が null だと既定で Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $null
            gitBranch      = "other-branch"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "MissingCommitInfo") { throw "Reason が MissingCommitInfo ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case -Name "(MissingCommitInfo) -AllowForeignBuild 指定時は Allowed=true" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $null
            gitBranch      = "other-branch"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo -AllowForeignBuild
        if (-not $result.Allowed) { throw "Allowed が false のままです" }
        if ($result.Warnings.Count -eq 0) { throw "警告が積まれていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- (不一致) --------------------------------------------------------------------------

Test-Case -Name "(不一致) gitCommit が異なると既定で Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = "0000000000000000000000000000000000000000"
            gitBranch      = "other-branch"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "Mismatch") { throw "Reason が Mismatch ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# M-2: 同じ gitCommit でも sourceWorktree が異なる（develop から切ったばかりの別 worktree 等）
# 場合は不一致として扱う。
Test-Case -Name "(不一致) 同じ gitCommit でも sourceWorktree が異なると Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $currentCommit = Get-TempRepoHeadCommit -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $currentCommit
            gitBranch      = "main"
            sourceWorktree = (Join-Path ([System.IO.Path]::GetTempPath()) "tq-other-worktree")
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "Mismatch") { throw "Reason が Mismatch ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-Case -Name "(不一致) -AllowForeignBuild 指定時は Allowed=true" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = "0000000000000000000000000000000000000000"
            gitBranch      = "other-branch"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo -AllowForeignBuild
        if (-not $result.Allowed) { throw "Allowed が false のままです" }
        if ($result.Warnings.Count -eq 0) { throw "警告が積まれていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- (一致) ----------------------------------------------------------------------------

Test-Case -Name "(一致) gitCommit・sourceWorktree が一致すると Allowed=true" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $currentCommit = Get-TempRepoHeadCommit -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $currentCommit
            gitBranch      = "main"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if (-not $result.Allowed) { throw "Allowed が false になっています" }
        if ($result.Warnings.Count -ne 0) { throw "一致しているのに警告が積まれています" }
        if (-not $result.Matches) { throw "Matches が true になっていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# issue #142 項目 3: TrimEnd + -ieq だけでは吸収できない表記差異（末尾区切り・大文字小文字・
# ".." を含む相対的な表記）でも GetFullPath による正規化後に一致すること。
Test-Case -Name "(一致) 末尾区切り・大文字小文字・..を含む表記でも Allowed=true" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $currentCommit = Get-TempRepoHeadCommit -Path $repo

        $repoLeaf = Split-Path -Path $repo -Leaf
        $repoParent = Split-Path -Path $repo -Parent
        $weirdSource = (Join-Path $repoParent ("dummy" + [System.IO.Path]::DirectorySeparatorChar + ".." + [System.IO.Path]::DirectorySeparatorChar + $repoLeaf))
        # 末尾区切りを付加し、大文字小文字を反転させた表記でも一致することを確認する。
        $weirdSource = $weirdSource.ToUpper() + "\"

        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $currentCommit
            gitBranch      = "main"
            sourceWorktree = $weirdSource
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if (-not $result.Allowed) { throw "Allowed が false になっています" }
        if (-not $result.Matches) { throw "Matches が true になっていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# issue #142 レビュー M-2: sourceWorktree の正規化は「緩める」方向の変更なので、相対値
# （IsPathRooted が $false。例: "."）はそのまま比較され、常に絶対パスの ProjectRoot とは
# 一致しない＝不一致として拒否されること。誤って相対値を GetFullPath 側のカレントディレクトリ
# 基準で解決して偶然一致させてしまわないことを確認する。
Test-Case -Name "(不一致) sourceWorktree が相対値（.）だと Allowed=false" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $currentCommit = Get-TempRepoHeadCommit -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $currentCommit
            gitBranch      = "main"
            sourceWorktree = "."
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $false
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if ($result.Allowed) { throw "Allowed が true になっています" }
        if ($result.Reason -ne "Mismatch") { throw "Reason が Mismatch ではありません: $($result.Reason)" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- (dirty) ----------------------------------------------------------------------------

Test-Case -Name "(dirty) 一致でも dirty=true なら警告が積まれる" -Assertion {
    $repo = New-TempTestDir
    $buildDir = New-TempTestDir
    try {
        New-TempGitRepo -Path $repo
        $currentCommit = Get-TempRepoHeadCommit -Path $repo
        Write-BuildInfo -BuildDir $buildDir -BuildInfo ([pscustomobject]@{
            gitCommit      = $currentCommit
            gitBranch      = "main"
            sourceWorktree = $repo
            builtAtUtc     = (Get-Date).ToString("o")
            dirty          = $true
        })
        $result = Test-BuildProvenance -BuildDir $buildDir -ProjectRoot $repo
        if (-not $result.Allowed) { throw "Allowed が false になっています" }
        if (-not $result.Dirty) { throw "Dirty が true になっていません" }
        if ($result.Warnings.Count -eq 0) { throw "dirty=true なのに警告が積まれていません" }
    } finally {
        Remove-Item -Path $repo, $buildDir -Recurse -Force -ErrorAction SilentlyContinue
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

Write-Host "すべての build-provenance テストに成功しました。" -ForegroundColor Green
exit 0
