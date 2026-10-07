# make-expressions.ps1
# 配布 zip の tools\tsumugi-expressions\ に同梱する、立ち絵の表情生成ツールの起動スクリプト（issue #219）。
# 利用者は make-expressions.bat / install-libraries.bat から呼ぶ（ダブルクリック・ドラッグ＆ドロップ）。
#
# 役割は「Python を見つける」「依存ライブラリを入れる」「tsumugi_expressions_standalone.py を呼ぶ」だけ。
# 立ち絵の取り出し・データルートの決定・安全装置は Python 側（standalone → generate_tsumugi_expressions.py）にある。
#
# Windows PowerShell 5.1（Windows に最初から入っている）でも動くように書く（pwsh 7 だけの構文を使わない）。
# 5.1 は BOM の無い UTF-8 のスクリプトを ANSI（日本語環境では CP932）として読むため、このファイルは
# UTF-8（BOM 付き）で保存する（scripts/package-release.ps1 が同梱時に BOM を確かめる）。
#
# 使い方（PowerShell から直接呼ぶ場合）:
#   powershell -ExecutionPolicy Bypass -File .\make-expressions.ps1 -InstallDepsOnly
#   powershell -ExecutionPolicy Bypass -File .\make-expressions.ps1 "C:\Users\<you>\Downloads\春日部つむぎ立ち絵_公式_v2.0.zip"
#   powershell -ExecutionPolicy Bypass -File .\make-expressions.ps1 <zip> -DataRoot D:\work\data-root -DryRun
#
# 終了コード: Python 側の終了コードをそのまま返す（4 = 安全確認で中止、5 = アプリでの同意が無い）。Python が見つからない・
# ライブラリの導入に失敗したときは 1 または 2。

[CmdletBinding()]
param(
    # 立ち絵の zip / 展開したフォルダ / PSD（ドラッグ＆ドロップで渡される）。省くとダウンロード フォルダを探す
    [Parameter(Position = 0)][string]$Source,
    # データルート（既定はアプリと同じ規則。AppPaths.DataRoot）
    [string]$DataRoot,
    # 出力先フォルダを直接指定する（既定は <データルート>\tsumugi）
    [string]$OutputDir,
    # 表情の設定ファイル（既定は隣の expressions.json）
    [string]$ConfigPath,
    # 使う Python（既定は py → python の順に探す）
    [string]$Python,
    [string[]]$Only,
    [string]$Crop,
    [int]$MaxHeight = -1,
    # psd-tools / Pillow を入れてから表情を作る
    [switch]$InstallDeps,
    # psd-tools / Pillow を入れるだけで終わる（install-libraries.bat）
    [switch]$InstallDepsOnly,
    [switch]$DryRun,
    # ドラッグ＆ドロップで 2 つ以上渡されたときの受け皿（エラーにする）
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$ExtraArguments
)

# 外部コマンドの標準エラーを例外にしない（5.1 は Stop だと NativeCommandError で止まる）。終了コードで判定する。
$ErrorActionPreference = "Continue"

$script:ExitDependency = 2
$script:MinimumPythonVersionText = "3.10"
$script:VersionProbeCode = "import sys; sys.exit(0 if sys.version_info >= (3, 10) else 3)"
$script:DependencyProbeCode = "import psd_tools, PIL"
$script:VersionTooOldExitCode = 3

# ---- Python の検出（テストから dot-source して呼ぶ。scripts/tests/tsumugi-expressions-tool.tests.ps1） ----

# Microsoft Store のエイリアス（%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe など）のパスかどうか。
# Python が入っていない Windows でも、このエイリアスだけはあることが多い（実行すると Store を開くか、
# 終了コード 9009 で終わる）。ただし Python install manager や Store 版の Python も同じ場所に
# 本物のエイリアスを置くので、パスだけでは「使えない」と決めず、実際に起動して確かめる（Find-PythonCommand）。
function Test-StoreAliasPath {
    param([AllowEmptyString()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ($Path -match '(?i)\\Microsoft\\WindowsApps\\')
}

# 1 つの引数を、Windows の標準の解釈（CommandLineToArgvW / MSVC ランタイム）で元に戻る形に引用する。
# 空白・引用符を含むパス（例: C:\Users\山田 太郎\Downloads\...zip、末尾が \ のフォルダ）を壊さないため。
function ConvertTo-CommandLineArgument {
    param([AllowEmptyString()][string]$Value)

    if ($Value -eq "") { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq '\') {
            $backslashes++
            continue
        }
        if ($ch -eq '"') {
            # 引用符の直前の \ は 2 倍にし、引用符自体も \ でエスケープする。
            [void]$builder.Append('\' * ($backslashes * 2 + 1)).Append('"')
        } else {
            [void]$builder.Append('\' * $backslashes).Append($ch)
        }
        $backslashes = 0
    }
    # 閉じる引用符の直前の \ は 2 倍にする（"C:\dir\" の \" を引用符のエスケープと読ませない）。
    [void]$builder.Append('\' * ($backslashes * 2)).Append('"')
    return $builder.ToString()
}

# 外部コマンドを、このコンソールにそのまま出力させて実行し、終了コードを返す。
# `& コマンド` を関数の中で使うと出力が関数の戻り値として取り込まれ、画面に出ない（しかも UTF-8 の
# 日本語がコンソールのコードページで化ける）ため、Start-Process で標準入出力を引き継がせる。
function Invoke-NativeInConsole {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @()
    )

    $argumentLine = (@($Arguments) | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " "
    $startArgs = @{ FilePath = $FilePath; NoNewWindow = $true; Wait = $true; PassThru = $true; ErrorAction = "Stop" }
    if ($argumentLine -ne "") { $startArgs["ArgumentList"] = $argumentLine }
    $process = Start-Process @startArgs
    if ($null -eq $process.ExitCode) { return -1 }
    return [int]$process.ExitCode
}

# 候補を実際に起動して、終了コードを返す（0 = 3.10 以降、3 = 古い、それ以外 = 起動できない）。
# 出力を取り込まずにコンソールへそのまま出す（Python install manager は、ランタイムが無いと最初の起動で
# 自動インストールを始め、確認を求めることがあるため。docs.python.org の Windows の章）。
function Invoke-PythonVersionProbe {
    param([Parameter(Mandatory = $true)][string]$Command)

    return (Invoke-NativeInConsole -FilePath $Command -Arguments @("-c", $script:VersionProbeCode))
}

# 候補（-Python の指定 → py → python）を順に起動して、3.10 以降の Python を探す。
# $GetCommand / $Probe は差し替え可能（テスト用）。戻り値は { Command; Found; Diagnostics }。
function Find-PythonCommand {
    param(
        [string]$Explicit,
        [scriptblock]$GetCommand = { param($name) Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 },
        [scriptblock]$Probe = { param($command) Invoke-PythonVersionProbe -Command $command }
    )

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { $candidates += $Explicit } else { $candidates += @("py", "python") }

    $diagnostics = @()
    foreach ($name in $candidates) {
        $info = & $GetCommand $name
        if ($null -eq $info) {
            $diagnostics += [pscustomobject]@{ Name = $name; Path = $null; ExitCode = $null; StoreAlias = $false }
            continue
        }

        $path = [string]$info.Source
        $exitCode = $null
        try {
            $exitCode = & $Probe $path
        } catch {
            $exitCode = -1
        }
        $exitCode = [int]($exitCode | Select-Object -Last 1)
        $diagnostics += [pscustomobject]@{ Name = $name; Path = $path; ExitCode = $exitCode; StoreAlias = (Test-StoreAliasPath $path) }
        if ($exitCode -eq 0) {
            return [pscustomobject]@{ Command = $path; Found = $true; Diagnostics = $diagnostics }
        }
    }

    return [pscustomobject]@{ Command = $null; Found = $false; Diagnostics = $diagnostics }
}

# Python が見つからなかったときの案内（利用者向けの日本語）。
function Get-PythonMissingMessage {
    param([object[]]$Diagnostics)

    $lines = @("Python $($script:MinimumPythonVersionText) 以降が見つかりません。")
    $tooOld = @($Diagnostics | Where-Object { $_.ExitCode -eq $script:VersionTooOldExitCode })
    $aliasOnly = @($Diagnostics | Where-Object { $_.StoreAlias -and $_.ExitCode -ne 0 })

    if ($tooOld.Count -gt 0) {
        $lines += "見つかった Python が古すぎます（$($tooOld[0].Path)）。新しい Python を入れてください。"
    } elseif ($aliasOnly.Count -gt 0) {
        $lines += "「$($aliasOnly[0].Name)」は Microsoft Store を開くためのエイリアスだけで、Python 本体は入っていないようです"
        $lines += "（$($aliasOnly[0].Path)、終了コード $($aliasOnly[0].ExitCode)）。"
    }

    $lines += ""
    $lines += "次の手順で Python を入れてから、もう一度実行してください（くわしくは manual\setup.html の 6.4）。"
    $lines += "  1. https://www.python.org/downloads/ から Windows 用の Python をダウンロードしてインストールする"
    $lines += "  2. インストールが終わったら、この画面を閉じて install-libraries.bat をもう一度ダブルクリックする"
    return ($lines -join [Environment]::NewLine)
}

# ---- 本体 ----------------------------------------------------------------------

function Write-Failure {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

function Invoke-Tool {
    $toolDir = $PSScriptRoot
    $standalone = Join-Path $toolDir "tsumugi_expressions_standalone.py"
    $requirements = Join-Path $toolDir "requirements.txt"

    Write-Host "===== 立ち絵の表情を作ります（つむぎクイズ） =====" -ForegroundColor Cyan

    if (@($ExtraArguments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        Write-Failure "立ち絵の zip は 1 つだけ渡してください（余分な引数: $($ExtraArguments -join ' ')）。"
        return 1
    }

    foreach ($required in @($standalone, $requirements)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            Write-Failure "ツールのファイルが足りません: $required`nzip をもう一度展開し直してください。"
            return 1
        }
    }

    $found = Find-PythonCommand -Explicit $Python
    if (-not $found.Found) {
        Write-Failure (Get-PythonMissingMessage -Diagnostics $found.Diagnostics)
        return 1
    }
    $pythonExe = $found.Command
    $versionText = (& $pythonExe -c "import sys; print('%d.%d.%d' % sys.version_info[:3])") | Select-Object -Last 1
    Write-Host "Python   : $versionText（$pythonExe）" -ForegroundColor DarkGray

    # 余計な __pycache__ をツールのフォルダに作らない。日本語の表示は UTF-8 で出す。
    $env:PYTHONDONTWRITEBYTECODE = "1"
    $env:PYTHONIOENCODING = "utf-8"

    if ($InstallDeps -or $InstallDepsOnly) {
        Write-Host "画像を作るのに使うライブラリ（psd-tools・Pillow）をインターネットから入れます。数分かかることがあります。" -ForegroundColor Cyan
        $pipExitCode = Invoke-NativeInConsole -FilePath $pythonExe -Arguments @("-m", "pip", "install", "-r", $requirements)
        if ($pipExitCode -ne 0) {
            Write-Failure "ライブラリを入れられませんでした（終了コード $pipExitCode）。インターネットにつながっているか確かめてください。"
            return $script:ExitDependency
        }
        Write-Host "ライブラリを入れました。" -ForegroundColor Green
        if ($InstallDepsOnly) {
            Write-Host "次に、立ち絵の zip を make-expressions.bat の上にドラッグ＆ドロップしてください。" -ForegroundColor Cyan
            return 0
        }
    }

    & $pythonExe -c $script:DependencyProbeCode 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Failure "画像を作るのに使うライブラリ（psd-tools・Pillow）が入っていません。先に install-libraries.bat をダブルクリックしてください。"
        return $script:ExitDependency
    }

    $arguments = @($standalone)
    if (-not [string]::IsNullOrWhiteSpace($Source)) { $arguments += $Source }
    if (-not [string]::IsNullOrWhiteSpace($DataRoot)) { $arguments += @("--data-root", $DataRoot) }
    if (-not [string]::IsNullOrWhiteSpace($OutputDir)) { $arguments += @("--out-dir", $OutputDir) }
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) { $arguments += @("--config", $ConfigPath) }
    if (-not [string]::IsNullOrWhiteSpace($Crop)) { $arguments += @("--crop", $Crop) }
    if ($MaxHeight -ge 0) { $arguments += @("--max-height", "$MaxHeight") }
    foreach ($key in @($Only)) {
        if (-not [string]::IsNullOrWhiteSpace($key)) { $arguments += @("--only", $key) }
    }
    if ($DryRun) { $arguments += "--dry-run" }

    $exitCode = Invoke-NativeInConsole -FilePath $pythonExe -Arguments $arguments
    if ($exitCode -eq 0 -and $DryRun) {
        Write-Host "確認だけ行いました（-DryRun のため、画像は書き出していません）。" -ForegroundColor Green
    } elseif ($exitCode -eq 0) {
        Write-Host "表情の画像を作り終えました。アプリを起動し直すと表示されます。" -ForegroundColor Green
    } else {
        Write-Failure "表情を作れませんでした（終了コード $exitCode）。上に出ているエラーの内容を確かめてください。"
    }
    return $exitCode
}

# dot-source（テスト）のときは本体を実行しない。
if ($MyInvocation.InvocationName -ne ".") {
    $code = Invoke-Tool
    exit ([int]($code | Select-Object -Last 1))
}
