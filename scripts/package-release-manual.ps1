# scripts/package-release-manual.ps1
# scripts/package-release.ps1 から dot-source する、利用者向け手順書（docs/manual/*.md）の HTML 化と
# その自己診断（PR #218）。ファイルを 800 行以下に保つため本体から分けた（#219。処理は変えていない）。

# ---- 利用者向け手順書（docs/manual/*.md）の HTML 化 -----------------------------------
# 配布 zip だけを受け取った利用者も手順書を読めるようにする（リポジトリは private のため
# GitHub のリンクは使えない）。変換は pwsh 6.1 以降に組み込みの ConvertFrom-Markdown を使い、
# 新しい外部ツール・ライブラリは追加しない。
#
# zip 内の配置: manual/<名前>.html と manual/images/*（docs/manual/ と同じ相対構造）。
# Markdown 側は images/xxx.png と <名前>.md の相対リンクで書く（GitHub 上でもそのまま表示できる）。
# 変換時に <名前>.md へのリンクだけを <名前>.html へ書き換える。
#
# ConvertFrom-Markdown（Markdig）の見出しの自動 ID は ASCII 以外を落とす（日本語の見出しは
# section / section-1 … になる）ため、手順書の中で見出しへのページ内リンク（#…）は使わない。
# zip の外を指す相対リンク（../ や docs/ の他の文書）も zip では切れるので、検出したら失敗させる。

$script:ManualSourceRelativeDir = "docs\manual"
$script:ManualDestinationDirName = "manual"
$script:ManualImagesDirName = "images"

# 変換結果の HTML を包む雛形。{0} = タイトル、{1} = 本文。メモ帳以外のブラウザでも
# 文字化けしないよう、ファイルは BOM なし UTF-8 で書き、meta charset で明示する。
$script:ManualHtmlTemplate = @'
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{0}</title>
<style>
body {{ font-family: "Yu Gothic UI", "Meiryo", sans-serif; line-height: 1.7; max-width: 960px; margin: 0 auto; padding: 16px 24px 48px; color: #2c2320; background: #fffaf5; }}
h1 {{ border-bottom: 3px solid #e8a87c; padding-bottom: 4px; }}
h2 {{ border-bottom: 1px solid #e8c9b0; padding-bottom: 2px; margin-top: 2em; }}
table {{ border-collapse: collapse; margin: 8px 0; }}
th, td {{ border: 1px solid #d9c2b0; padding: 4px 8px; vertical-align: top; }}
th {{ background: #f6e4d6; }}
img {{ max-width: 100%; height: auto; border: 1px solid #d9c2b0; }}
code {{ background: #f3ebe4; padding: 0 3px; }}
pre {{ background: #f3ebe4; padding: 8px; overflow-x: auto; }}
pre code {{ padding: 0; }}
blockquote {{ border-left: 4px solid #e8a87c; margin-left: 0; padding-left: 12px; color: #5a4a42; }}
</style>
</head>
<body>
{1}
</body>
</html>
'@

# ConvertFrom-Markdown の出力（本文の HTML 断片）を手順書用に整える。
# - 相対リンクの <名前>.md を <名前>.html へ書き換える（https:// などの外部リンクは触らない）
function Convert-ManualHtmlLinks {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Html
    )

    return [regex]::Replace(
        $Html,
        'href="(?<name>[A-Za-z0-9_-]+)\.md(?<anchor>#[^"]*)?"',
        { param($m) 'href="' + $m.Groups['name'].Value + '.html' + $m.Groups['anchor'].Value + '"' })
}

# 変換後の HTML が参照する相対パス（画像・リンク）を集め、zip 内で切れるものを返す。
# 外部（scheme 付き・mailto）と、ページ内リンク（#…）は対象外。
function Get-ManualBrokenReferences {
    param(
        [Parameter(Mandatory = $true)][string]$HtmlPath,
        [Parameter(Mandatory = $true)][string]$ManualDir
    )

    $html = [System.IO.File]::ReadAllText($HtmlPath, [System.Text.Encoding]::UTF8)
    $broken = @()
    $references = [regex]::Matches($html, '(?:src|href)="(?<ref>[^"]+)"')
    foreach ($match in $references) {
        $ref = $match.Groups['ref'].Value
        # 絶対パス（ドライブ文字・file: スキーム・先頭が / や \ のもの）は、作った PC でしか開けないので失敗にする。
        # ドライブ文字（C:）は 1 文字のスキームとしても読めるため、外部リンクの判定より先に見る。
        $decodedRef = [System.Uri]::UnescapeDataString($ref)
        if ($decodedRef -match '^[A-Za-z]:' -or $decodedRef -match '^file:' -or
            $decodedRef.StartsWith('/') -or $decodedRef.StartsWith('\')) {
            $broken += "$ref（絶対パスです。zip 内の manual フォルダからの相対パスにしてください）"
            continue
        }
        if ($ref -match '^[A-Za-z][A-Za-z0-9+.-]*:' -or $ref.StartsWith('#')) {
            continue
        }

        $pathPart = ($ref -split '#', 2)[0]
        $decoded = [System.Uri]::UnescapeDataString($pathPart)
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $ManualDir $decoded))
        $manualFull = [System.IO.Path]::GetFullPath($ManualDir).TrimEnd('\') + '\'
        if (-not $fullPath.StartsWith($manualFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            $broken += "$ref（zip 内の manual フォルダの外を指しています）"
            continue
        }
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            $broken += "$ref（ファイルがありません）"
        }
    }

    return @($broken)
}

# docs/manual/*.md を HTML に変換し、画像と一緒に $DestinationDir へ書き出す。
# 失敗（変換不能・参照切れ）は例外で知らせる（呼び出し側の try/catch で非 0 終了になる）。
function Export-ManualHtml {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Get-Command ConvertFrom-Markdown -ErrorAction SilentlyContinue)) {
        throw "ConvertFrom-Markdown が使えません。PowerShell 7（pwsh）で実行してください（現在: $($PSVersionTable.PSVersion)）。"
    }
    if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
        throw "手順書のフォルダが見つかりません: $SourceDir"
    }

    $markdownFiles = @(Get-ChildItem -LiteralPath $SourceDir -Filter "*.md" -File | Sort-Object Name)
    if ($markdownFiles.Count -eq 0) {
        throw "手順書（*.md）が 1 件もありません: $SourceDir"
    }

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null

    $sourceImages = Join-Path $SourceDir $script:ManualImagesDirName
    if (Test-Path -LiteralPath $sourceImages -PathType Container) {
        $destinationImages = Join-Path $DestinationDir $script:ManualImagesDirName
        New-Item -ItemType Directory -Force -Path $destinationImages | Out-Null
        Get-ChildItem -LiteralPath $sourceImages -File | Where-Object { $_.Extension -in @(".png", ".jpg", ".jpeg") } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destinationImages $_.Name) -Force }
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $written = @()
    foreach ($file in $markdownFiles) {
        $markdown = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        $body = (ConvertFrom-Markdown -InputObject $markdown).Html
        $body = Convert-ManualHtmlLinks -Html $body

        $title = $file.BaseName
        $titleMatch = [regex]::Match($markdown, '(?m)^#\s+(?<title>.+?)\s*$')
        if ($titleMatch.Success) {
            $title = [System.Net.WebUtility]::HtmlEncode($titleMatch.Groups['title'].Value)
        }

        $html = [string]::Format($script:ManualHtmlTemplate, $title, $body)
        $html = ($html -replace "`r`n", "`n") -replace "`n", "`r`n"
        $htmlPath = Join-Path $DestinationDir ($file.BaseName + ".html")
        [System.IO.File]::WriteAllText($htmlPath, $html, $utf8NoBom)
        $written += $htmlPath
    }

    $allBroken = @()
    foreach ($htmlPath in $written) {
        foreach ($b in (Get-ManualBrokenReferences -HtmlPath $htmlPath -ManualDir $DestinationDir)) {
            $allBroken += "$(Split-Path $htmlPath -Leaf): $b"
        }
    }
    if ($allBroken.Count -gt 0) {
        throw ("手順書の HTML に、zip 内で切れる参照があります:`n  - " + ($allBroken -join "`n  - "))
    }

    return @($written)
}

# 手順書の HTML 化（Export-ManualHtml）の自己診断。日本語・表・相対リンク・相対パスの画像を含む
# 小さな Markdown を変換し、文字コード・リンクの書き換え・画像のコピー・zip の外を指す参照の検出を確かめる。
function Invoke-ManualHtmlSelfTest {
    param(
        [Parameter(Mandatory = $true)][string]$TempRoot
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $sourceDir = Join-Path $TempRoot "manual-src"
    $imagesDir = Join-Path $sourceDir $script:ManualImagesDirName
    New-Item -ItemType Directory -Force -Path $imagesDir | Out-Null
    # PNG の署名だけのダミー（中身の妥当性は見ない。コピーされることだけを確かめる）。
    [System.IO.File]::WriteAllBytes((Join-Path $imagesDir "sample.png"), [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
    [System.IO.File]::WriteAllText((Join-Path $sourceDir "first.md"),
        "# 手順書テスト`n`n日本語の本文。[もう一方](second.md) と [外部](https://example.com/a.md)。`n`n| 列 | 値 |`n|---|---|`n| あ | い |`n`n![画像](images/sample.png)`n",
        $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $sourceDir "second.md"), "# 二つ目`n`n[戻る](first.md)`n", $utf8NoBom)

    $destinationDir = Join-Path $TempRoot "manual-out"
    try {
        [void](Export-ManualHtml -SourceDir $sourceDir -DestinationDir $destinationDir)
    } catch {
        Write-Host "自己診断に失敗しました: 手順書の HTML 化が失敗しました: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }

    $firstHtmlPath = Join-Path $destinationDir "first.html"
    $bytes = [System.IO.File]::ReadAllBytes($firstHtmlPath)
    $html = [System.IO.File]::ReadAllText($firstHtmlPath, [System.Text.Encoding]::UTF8)
    $problems = @()
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $problems += "HTML に BOM が付いています（BOM なし UTF-8 の想定）"
    }
    if ($html -notmatch '<meta charset="utf-8">') { $problems += "meta charset がありません" }
    if ($html -notmatch '日本語の本文') { $problems += "日本語の本文が UTF-8 で書かれていません" }
    if ($html -notmatch '<title>手順書テスト</title>') { $problems += "タイトルが最初の見出しになっていません" }
    if ($html -notmatch 'href="second\.html"') { $problems += "second.md へのリンクが second.html に書き換わっていません" }
    if ($html -notmatch 'href="https://example\.com/a\.md"') { $problems += "外部リンクまで書き換えています" }
    if ($html -notmatch '<img src="images/sample\.png"') { $problems += "画像の相対パスが変わっています" }
    if ($html -notmatch '<table>') { $problems += "表が変換されていません" }
    if (-not (Test-Path -LiteralPath (Join-Path $destinationDir "images\sample.png"))) { $problems += "画像がコピーされていません" }

    # zip 内で切れる参照は、どれも HTML 化を失敗させること。1 件ずつ別の Markdown を足して確かめる。
    $badCases = @(
        @{ Label = "manual フォルダの外を指すリンク（../）"; Body = "[外](../outside.md)"; Expect = '\.\./outside\.md' },
        @{ Label = "ドライブ文字から始まる絶対パスの画像"; Body = "![絶対](C:/Users/someone/shot.png)"; Expect = 'C:/Users/someone/shot\.png' },
        @{ Label = "存在しない画像"; Body = "![無い](images/missing.png)"; Expect = 'images/missing\.png' }
    )
    $caseIndex = 0
    foreach ($case in $badCases) {
        $caseIndex++
        $badPath = Join-Path $sourceDir "bad.md"
        [System.IO.File]::WriteAllText($badPath, "# 検出テスト`n`n$($case.Body)`n", $utf8NoBom)
        $detected = $false
        try {
            [void](Export-ManualHtml -SourceDir $sourceDir -DestinationDir (Join-Path $TempRoot "manual-bad-$caseIndex"))
        } catch {
            $detected = $_.Exception.Message -match $case.Expect
        }
        Remove-Item -LiteralPath $badPath -Force
        if (-not $detected) { $problems += "$($case.Label)を検出できませんでした" }
    }

    if ($problems.Count -gt 0) {
        Write-Host "自己診断に失敗しました（手順書の HTML 化）:" -ForegroundColor Red
        foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
        return 1
    }

    Write-Host "OK: 手順書の HTML 化（UTF-8・相対リンクの書き換え・画像のコピー・zip の外を指す参照・絶対パス・存在しない画像の検出）を確認しました。" -ForegroundColor Green
    return 0
}
