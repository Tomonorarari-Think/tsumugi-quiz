#Requires -Version 5.1
<#
.SYNOPSIS
    voicevox_core 0.17.0 の公式ダウンローダーを取得し、ONNX Runtime / 音声モデル / Open JTalk 辞書 /
    C API を External/voicevox_core/ へダウンロードする。

.DESCRIPTION
    1. download-windows-x64.exe を voicevox_core の指定タグのリリースから External/voicevox_core/ へ取得する
       （gh CLI が使えればそれを、なければ Invoke-WebRequest を使う）
    2. ダウンローダーを実行して c-api / onnxruntime / models / dict を取得する
    3. 期待されるファイルの存在をチェックする

    ダウンローダーは「VOICEVOX 音声モデル 利用規約」「VOICEVOX ONNX Runtime 利用規約」への
    同意を対話で求める。voicevox_core 0.17.0 のダウンローダーには非対話オプション
    （--yes / --accept-terms 相当）が存在しないため、必ず PowerShell のコンソールで
    対話的に実行すること。規約本文がページャで表示されたら、読んでから q で閉じ、
    [y,n,r] : のプロンプトに y を入力する。

    根拠:
      - https://github.com/VOICEVOX/voicevox_core/blob/0.17.0/docs/guide/user/downloader.md
      - https://github.com/VOICEVOX/voicevox_core/blob/0.17.0/crates/downloader/src/main.rs
        （struct Args のオプション定義と ensure_confirmation の対話ロジックを確認）

.PARAMETER Version
    voicevox_core のリリースタグ。既定 0.17.0。--c-api-version にも同じ値を渡す。

.PARAMETER ModelsPattern
    取得する VVM のファイル名パターン。既定 '0.vvm'（春日部つむぎ ノーマルを含む、約 58MB）。
    '*' にすると全 VVM（1.5GB 超）を取得するので注意。

.PARAMETER OutputDir
    ダウンロード先。既定 <repo>/External/voicevox_core

.PARAMETER SkipDownloaderFetch
    download-windows-x64.exe の取得を省略し、既にある実行ファイルを使う。

.PARAMETER DryRun
    実行するコマンドを表示するだけで、ダウンロードも実行もしない。

.PARAMETER AcceptTerms
    利用者が「VOICEVOX 音声モデル 利用規約」「VOICEVOX ONNX Runtime 利用規約」を
    事前に読んで同意済みであることを明示したうえで、標準入力から y を渡して非対話実行する
    （実測 2026-09-13、`printf 'y\ny\n' | download-windows-x64.exe ...` 相当。exit 0 で完走することを確認済み）。
    既定（このスイッチを指定しない場合）は従来どおり対話実行のままとする。
    **規約を読まずにこのスイッチを使わないこと**（同意の趣旨に反するため）。

.EXAMPLE
    .\scripts\fetch-voicevox.ps1 -DryRun
    実行されるコマンドを確認する。

.EXAMPLE
    .\scripts\fetch-voicevox.ps1
    実際に取得する（規約同意の対話プロンプトが出る）。

.EXAMPLE
    .\scripts\fetch-voicevox.ps1 -AcceptTerms
    規約を読んで同意済みであることを明示したうえで、非対話で取得する。
#>
[CmdletBinding()]
param(
    [string] $Version       = '0.17.0',
    [string] $ModelsPattern = '0.vvm',
    [string] $OutputDir,
    [switch] $SkipDownloaderFetch,
    [switch] $DryRun,
    [switch] $AcceptTerms
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 定数
# ---------------------------------------------------------------------------
$Repo           = 'VOICEVOX/voicevox_core'
$DownloaderName = 'download-windows-x64.exe'
$Targets        = @('c-api', 'onnxruntime', 'models', 'dict')   # additional-libraries は CPU 版では不要
$Device         = 'cpu'

# ---------------------------------------------------------------------------
# ヘルパ
# ---------------------------------------------------------------------------
function Write-Step  { param([string] $Message) Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok    { param([string] $Message) Write-Host "  OK   $Message" -ForegroundColor Green }
function Write-Miss  { param([string] $Message) Write-Host "  MISS $Message" -ForegroundColor Red }
function Write-Note  { param([string] $Message) Write-Host "  $Message" -ForegroundColor DarkGray }

function Write-Command {
    param([string] $Exe, [string[]] $Arguments)
    $quoted = $Arguments | ForEach-Object { if ($_ -match '[\s]') { '"' + $_ + '"' } else { $_ } }
    Write-Host "  $Exe $($quoted -join ' ')" -ForegroundColor Yellow
}

function Test-CommandExists {
    param([string] $Name)
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

# ---------------------------------------------------------------------------
# パス解決
# ---------------------------------------------------------------------------
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot 'External\voicevox_core'
}
$DownloaderPath = Join-Path $OutputDir $DownloaderName

Write-Host ''
Write-Host '=======================================================' -ForegroundColor White
Write-Host ' voicevox_core assets fetcher' -ForegroundColor White
Write-Host '=======================================================' -ForegroundColor White
Write-Note "repo root      : $RepoRoot"
Write-Note "output dir     : $OutputDir"
Write-Note "version (tag)  : $Version"
Write-Note "models pattern : $ModelsPattern"
Write-Note "targets        : $($Targets -join ', ')"
if ($DryRun) { Write-Host '  *** DRY RUN: コマンドを表示するだけで実行しません ***' -ForegroundColor Magenta }
Write-Host ''

# ---------------------------------------------------------------------------
# 1) 出力ディレクトリ
# ---------------------------------------------------------------------------
Write-Step '出力ディレクトリを準備'
if ($DryRun) {
    Write-Command 'New-Item' @('-ItemType', 'Directory', '-Force', '-Path', $OutputDir)
} elseif (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    Write-Ok "作成: $OutputDir"
} else {
    Write-Ok "既存: $OutputDir"
}

# ---------------------------------------------------------------------------
# 2) ダウンローダーの取得
# ---------------------------------------------------------------------------
Write-Step "ダウンローダー ($DownloaderName) を取得"

if ($SkipDownloaderFetch) {
    Write-Note '-SkipDownloaderFetch が指定されたので取得をスキップします'
} elseif ((-not $DryRun) -and (Test-Path -LiteralPath $DownloaderPath)) {
    Write-Ok "既に存在します: $DownloaderPath"
    Write-Note '再取得したい場合は削除してから実行してください'
} else {
    $useGh = Test-CommandExists 'gh'
    if ($useGh) {
        $ghArgs = @('release', 'download', $Version, '-R', $Repo, '-p', $DownloaderName, '-D', $OutputDir)
        if ($DryRun) {
            Write-Command 'gh' $ghArgs
        } else {
            Write-Note "gh release download で取得します"
            & gh @ghArgs
            if ($LASTEXITCODE -ne 0) { throw "gh release download が失敗しました (exit $LASTEXITCODE)" }
        }
    } else {
        $url = "https://github.com/$Repo/releases/download/$Version/$DownloaderName"
        if ($DryRun) {
            Write-Command 'Invoke-WebRequest' @('-Uri', $url, '-OutFile', $DownloaderPath)
        } else {
            Write-Note 'gh が見つからないので Invoke-WebRequest で取得します'
            Invoke-WebRequest -Uri $url -OutFile $DownloaderPath -UseBasicParsing
        }
    }
    if (-not $DryRun) { Write-Ok "取得: $DownloaderPath" }
}

# ---------------------------------------------------------------------------
# 3) GitHub 認証トークン（レートリミット回避）
# ---------------------------------------------------------------------------
Write-Step 'GitHub 認証トークンを設定（レートリミット回避）'
# 公式ドキュメントの推奨: GH_TOKEN=$(gh auth token) download …
# 両方設定されている場合は GH_TOKEN が優先される。
if ($env:GH_TOKEN -or $env:GITHUB_TOKEN) {
    Write-Ok '環境変数 GH_TOKEN / GITHUB_TOKEN が既に設定されています'
} elseif (Test-CommandExists 'gh') {
    if ($DryRun) {
        Write-Command '$env:GH_TOKEN =' @('gh', 'auth', 'token')
    } else {
        try {
            $token = (& gh auth token) 2>$null
            if ($LASTEXITCODE -eq 0 -and $token) {
                $env:GH_TOKEN = $token.Trim()
                Write-Ok 'gh auth token から GH_TOKEN を設定しました'
            } else {
                Write-Note 'gh auth token を取得できませんでした（未認証？）。トークンなしで続行します'
            }
        } catch {
            Write-Note "gh auth token の実行に失敗しました: $($_.Exception.Message)"
        }
    }
} else {
    Write-Note 'gh が無いのでトークンなしで実行します（レートリミットに注意）'
}

# ---------------------------------------------------------------------------
# 4) ダウンローダーの実行
# ---------------------------------------------------------------------------
Write-Step 'ダウンローダーを実行'

$dlArgs = @(
    '--output',         $OutputDir
    '--only'
) + $Targets + @(
    '--devices',        $Device
    '--models-pattern', $ModelsPattern
    '--c-api-version',  $Version
)

Write-Host ''
if ($AcceptTerms) {
    Write-Host '  --- 利用規約の同意について（-AcceptTerms 指定：非対話実行） -----' -ForegroundColor Magenta
    Write-Host '  -AcceptTerms が指定されたため、次の規約に同意済みとして標準入力から y を渡します。' -ForegroundColor Magenta
    Write-Host '    ・VOICEVOX 音声モデル 利用規約' -ForegroundColor Magenta
    Write-Host '    ・VOICEVOX ONNX Runtime 利用規約' -ForegroundColor Magenta
    Write-Host '  このスイッチは、利用者が事前に規約本文を読んで同意済みであることを' -ForegroundColor Magenta
    Write-Host '  前提とします。読まずに指定しないでください。' -ForegroundColor Magenta
    Write-Host '  ----------------------------------------------------------------' -ForegroundColor Magenta
} else {
    Write-Host '  --- 利用規約の同意について -------------------------------------' -ForegroundColor Magenta
    Write-Host '  models / onnxruntime のダウンロードには、次の規約への同意が必要です。' -ForegroundColor Magenta
    Write-Host '    ・VOICEVOX 音声モデル 利用規約' -ForegroundColor Magenta
    Write-Host '    ・VOICEVOX ONNX Runtime 利用規約' -ForegroundColor Magenta
    Write-Host '  規約本文がページャで表示されます。上下キー/スペースでスクロールし、' -ForegroundColor Magenta
    Write-Host '  読み終えたら q で閉じ、[y,n,r] : のプロンプトに y を入力してください。' -ForegroundColor Magenta
    Write-Host '  （0.17.0 のダウンローダーに --yes 相当の非対話オプションはありません。' -ForegroundColor Magenta
    Write-Host '   -AcceptTerms を指定すると、標準入力から同意を渡して非対話実行できます）' -ForegroundColor Magenta
    Write-Host '  ----------------------------------------------------------------' -ForegroundColor Magenta
}
Write-Host ''

if ($DryRun) {
    Write-Command $DownloaderPath $dlArgs
} else {
    if (-not (Test-Path -LiteralPath $DownloaderPath)) {
        throw "ダウンローダーが見つかりません: $DownloaderPath"
    }
    if ($AcceptTerms) {
        # 規約を読んで同意済みであることを利用者が明示した場合のみ、
        # models / onnxruntime の 2 つの同意プロンプトへ標準入力から 'y' を流し込み非対話実行する。
        # 実測(2026-09-13): 'y','y' | & $DownloaderPath ... で exit 0 で完走することを確認済み。
        'y', 'y' | & $DownloaderPath @dlArgs
    } else {
        & $DownloaderPath @dlArgs
    }
    if ($LASTEXITCODE -ne 0) {
        throw "ダウンローダーが失敗しました (exit $LASTEXITCODE)"
    }
    Write-Ok 'ダウンロード完了'
}

# ---------------------------------------------------------------------------
# 5) 取得結果の検証
# ---------------------------------------------------------------------------
Write-Step '取得したファイルを検証'

$checks = @(
    @{
        Name  = 'voicevox_core.dll (c-api)'
        Kind  = 'File'
        Glob  = Join-Path $OutputDir 'c_api\lib\voicevox_core.dll'
    },
    @{
        # ダウンローダーが展開する DLL はバージョン付きファイル名になる場合がある。
        # 例: voicevox_onnxruntime-1.17.3.dll
        Name  = 'voicevox_onnxruntime*.dll (onnxruntime)'
        Kind  = 'Glob'
        Glob  = Join-Path $OutputDir 'onnxruntime\lib\voicevox_onnxruntime*.dll'
    },
    @{
        Name  = '音声モデル models\vvms\*.vvm'
        Kind  = 'Glob'
        Glob  = Join-Path $OutputDir 'models\vvms\*.vvm'
    },
    @{
        Name  = 'Open JTalk 辞書 dict\open_jtalk_dic_utf_8-1.11\sys.dic'
        Kind  = 'File'
        Glob  = Join-Path $OutputDir 'dict\open_jtalk_dic_utf_8-1.11\sys.dic'
    },
    @{
        Name  = '音声モデル利用規約 models\TERMS.txt'
        Kind  = 'File'
        Glob  = Join-Path $OutputDir 'models\TERMS.txt'
        Soft  = $true     # 無くても致命的ではない
    }
)

if ($DryRun) {
    Write-Note '検証対象（DryRun のため確認のみ表示）:'
    foreach ($c in $checks) { Write-Note "  - $($c.Name)  ->  $($c.Glob)" }
    Write-Host ''
    Write-Host 'DRY RUN 終了。実際に取得するには -DryRun を外して実行してください。' -ForegroundColor Magenta
    return
}

$failed = @()
foreach ($c in $checks) {
    $found = @(Get-ChildItem -Path $c.Glob -ErrorAction SilentlyContinue)
    if ($found.Count -gt 0) {
        foreach ($f in $found) {
            Write-Ok "$($c.Name)  ($([math]::Round($f.Length / 1MB, 2)) MB)  $($f.FullName)"
        }
    } else {
        Write-Miss "$($c.Name)  期待パス: $($c.Glob)"
        if (-not ($c.ContainsKey('Soft') -and $c.Soft)) { $failed += $c.Name }
    }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host '検証に失敗しました。次のファイルが見つかりません:' -ForegroundColor Red
    foreach ($f in $failed) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'ヒント:' -ForegroundColor Yellow
    Write-Host '  - 規約プロンプトで n を入力していないか確認してください' -ForegroundColor Yellow
    Write-Host '  - GitHub のレートリミットに当たった場合は GH_TOKEN を設定して再実行してください' -ForegroundColor Yellow
    Write-Host "  - 手動実行: $DownloaderPath --help" -ForegroundColor Yellow
    exit 1
}

Write-Host '全ての必須ファイルを確認しました。' -ForegroundColor Green
Write-Host ''
Write-Host '次の手順:' -ForegroundColor White
Write-Host '  .\scripts\setup-external.ps1    # External -> Assets へコピー' -ForegroundColor White
Write-Host ''
