# scripts/gen-third-party-notices.ps1
# Assets/TsumugiQuiz/Resources/Licenses/*.txt と Assets/TsumugiQuiz/Resources/Terms/*.txt を連結し、
# 配布 zip 直下に同梱する THIRD-PARTY-NOTICES.txt を生成する（docs/licenses.md §12・
# docs/dev-workflow.md §8、issue #33。連結順序はレビュー M-2 により明示的な配列で固定する）。
#
# issue #34 の scripts/package-release.ps1 から呼び出され、実際の配布 zip に同梱される
# THIRD-PARTY-NOTICES.txt を生成する（単体でも実行できる）。
#
# 使い方:
#   pwsh ./scripts/gen-third-party-notices.ps1                       # Builds/THIRD-PARTY-NOTICES.txt に生成
#   pwsh ./scripts/gen-third-party-notices.ps1 -OutputPath out.txt    # 出力先を指定
#   pwsh ./scripts/gen-third-party-notices.ps1 -DryRun                # 書き込まず、連結順序とサイズだけ表示

param(
    [string]$OutputPath,
    [switch]$DryRun
)

. "$PSScriptRoot/common.ps1"

$ErrorActionPreference = "Stop"

$projectRoot = Get-ProjectRoot
$licensesDir = Join-Path $projectRoot "Assets\TsumugiQuiz\Resources\Licenses"
$termsDir = Join-Path $projectRoot "Assets\TsumugiQuiz\Resources\Terms"

if (-not $OutputPath) {
    $buildsDir = Join-Path $projectRoot "Builds"
    New-Item -ItemType Directory -Force -Path $buildsDir | Out-Null
    $OutputPath = Join-Path $buildsDir "THIRD-PARTY-NOTICES.txt"
}

# docs/licenses.md §12 の順序（1.キャラクター 2.音声 3.OSS）に合わせた明示的な連結順序。
# Assets/TsumugiQuiz/Scripts/UI/Credits/CreditsLicenseCatalog.cs の Entries と対応させること。
# アルファベット順の Get-ChildItem ソートに依存すると、新しいファイルを追加した際に
# 意図しない順序で THIRD-PARTY-NOTICES.txt が生成されてしまうため、順序を固定する。
$licenseFileOrder = @(
    # 1. キャラクター
    "tsumugi-character-credit"
    # 2. 音声
    "voicevox-core-license"
    "onnxruntime-license"
    "voicevox-onnxruntime-license"
    "onnxruntime-third-party-notices"
    "open-jtalk-dict-copying"
    # 3. OSS
    "unity-packages-notices"
    "unity-companion-license"
    "mono-nat-license"
    "noto-sans-jp-ofl"
)

# Assets/TsumugiQuiz/Scripts/UI/Consent/TermsCatalog.cs の Entries と対応させた順序。
$termsFileOrder = @(
    "voicevox-models-terms"
    "voicevox-onnxruntime-terms"
    "tsumugi-voice-credit"
    "tsumugi-illustration-terms"
)

function Resolve-OrderedTextFiles {
    param(
        [string]$Directory,
        [string[]]$Order
    )

    if (-not (Test-Path $Directory)) {
        throw "ディレクトリが見つかりません: $Directory"
    }

    $allFiles = @(Get-ChildItem -Path $Directory -Filter "*.txt" -File)
    $byBaseName = @{}
    foreach ($file in $allFiles) {
        $byBaseName[$file.BaseName] = $file
    }

    $ordered = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($baseName in $Order) {
        if (-not $byBaseName.ContainsKey($baseName)) {
            throw "順序リストに含まれるファイルが見つかりません: $Directory\$baseName.txt"
        }
        $ordered.Add($byBaseName[$baseName])
        $byBaseName.Remove($baseName)
    }

    # 順序リストに書き忘れた新規ファイルがあれば、見落とさないよう末尾に追加した上で警告する。
    foreach ($leftover in $byBaseName.Values) {
        Write-Warning "順序リストに含まれていないファイルを末尾に追加しました: $($leftover.Name)（gen-third-party-notices.ps1 の順序配列に追記してください）"
        $ordered.Add($leftover)
    }

    return @($ordered)
}

$licenseFiles = Resolve-OrderedTextFiles -Directory $licensesDir -Order $licenseFileOrder
$termsFiles = Resolve-OrderedTextFiles -Directory $termsDir -Order $termsFileOrder

if ($DryRun) {
    Write-Host "===== DryRun: 連結順序（書き込みは行いません） =====" -ForegroundColor Cyan
    Write-Host "Resources/Licenses:"
    $licenseFiles | ForEach-Object { Write-Host ("  - {0}" -f $_.Name) }
    Write-Host "Resources/Terms:"
    $termsFiles | ForEach-Object { Write-Host ("  - {0}" -f $_.Name) }
    Write-Host ("出力予定先: {0}" -f $OutputPath)
    exit 0
}

$sections = New-Object System.Collections.Generic.List[string]

$header = @"
THIRD-PARTY NOTICES
====================

このファイルは、つむぎクイズ（tsumugi-quiz）が利用する第三者ソフトウェア・素材・
利用規約の一覧です。各項目のクレジット表記・ライセンス全文・利用規約全文は、
docs/licenses.md の記載に基づき、以下に同梱の Resources/Licenses/*.txt および
Resources/Terms/*.txt の内容をそのまま転記したものです。連結順序は docs/licenses.md §12
（1.キャラクター 2.音声 3.OSS）に対応しています。

このファイルは scripts/gen-third-party-notices.ps1 により自動生成されます。
手動で編集しないでください（元ファイルを編集してから再生成すること）。
"@

$sections.Add($header)

$sections.Add("`n`n====================`n同梱ライセンス（Resources/Licenses/）`n====================")
foreach ($file in $licenseFiles) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8

    # issue #100 レビュー M-3: Unity Editor Software Terms Section 2.12 の帰属定型文
    # 「Copyright © 2005-xxxx Unity Technologies.」の年は、元ファイル（unity-packages-notices.txt）を
    # 編集せず、連結（=配布物生成）のタイミングで現在年に置換する。他ファイルにはこのパターンの
    # 文字列は存在しないため、全ファイル一律に置換しても副作用はない。
    $content = $content -replace '(?<=Copyright © 2005-)\d{4}(?= Unity Technologies\. All rights reserved\.)', (Get-Date).Year

    $sections.Add("`n--------------------`n[$($file.BaseName)]`n--------------------`n$content")
}

$sections.Add("`n`n====================`n同梱利用規約（Resources/Terms/）`n====================")
foreach ($file in $termsFiles) {
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $sections.Add("`n--------------------`n[$($file.BaseName)]`n--------------------`n$content")
}

$combined = [string]::Join("`n", $sections)

# Windows のメモ帳等でも素直に開けるよう、改行を CRLF に統一する（レビュー L1）。
# 連結元の Resources/Licenses|Terms/*.txt は git 管理下で LF 統一のため、そのままだと
# THIRD-PARTY-NOTICES.txt 全体が LF のままになる。既存の CRLF を巻き込んで二重変換しないよう、
# 先に LF へ正規化してから CRLF へ変換する。
$combined = ($combined -replace "`r`n", "`n") -replace "`n", "`r`n"

Set-Content -Path $OutputPath -Value $combined -Encoding UTF8 -NoNewline

$sizeBytes = (Get-Item $OutputPath).Length
Write-Host "THIRD-PARTY-NOTICES.txt を生成しました: $OutputPath ($sizeBytes bytes)" -ForegroundColor Green
Write-Host ("  Resources/Licenses: {0} files" -f $licenseFiles.Count)
Write-Host ("  Resources/Terms:    {0} files" -f $termsFiles.Count)

# 呼び出し元（scripts/package-release.ps1 等）が $LASTEXITCODE で成否判定できるよう、
# 正常終了時は明示的に 0 を返す（issue #34）。
exit 0
