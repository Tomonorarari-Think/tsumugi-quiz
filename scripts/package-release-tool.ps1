# scripts/package-release-tool.ps1
# scripts/package-release.ps1 から dot-source する、立ち絵の表情生成ツール（tools/tsumugi-expressions/、#219）の
# 同梱・検査と、その自己診断。Get-ForbiddenStagingItems / Get-ProjectRoot は本体・common.ps1 の関数を使う。

# ---- 立ち絵の表情生成ツール（tools/tsumugi-expressions/、issue #219） -------------------------
# 配布 zip だけを受け取った人が、自分で入手した公式の立ち絵 zip から 9 表情を作れるようにする。
# 同梱するのはコードと設定だけ（画像・PSD は入れない。docs/licenses.md §3.1・§15）。
# 生成の本体（generate_tsumugi_expressions.py）と設定（docs/tsumugi-expressions.sample.json）は
# リポジトリ版と同じファイルをコピーする（ロジックを二重に持たない）。
#   Kind = Copy      : バイト列をそのままコピー
#   Kind = CrlfText  : UTF-8 のテキストとして読み、改行を CRLF にして BOM なしで書く（.bat はメモ帳・cmd.exe 向け）
#   Kind = BomScript : 改行を CRLF にし、UTF-8（BOM 付き）で書く（Windows PowerShell 5.1 が日本語を読み違えないため）
#   Kind = ExpressionConfig : JSON の `$schemaNote`（開発者向けの説明）だけを zip 版の説明に差し替える（#219 L5）。
#                      ほかの値（レイヤー名・安全設定）は変えないことを、書き出したあとに読み直して確かめる
# 加えて、アプリの同意の判定（#219 H1）に使う規約の本文 Assets/TsumugiQuiz/Resources/Terms/*.txt を
# terms/ にそのままコピーする（アプリのビルドに入る TextAsset と同じファイル。Get-ExpressionToolFileList）。
$script:ExpressionToolDestinationRelativeDir = "tools\tsumugi-expressions"
$script:ExpressionToolFiles = @(
    [pscustomobject]@{ Source = "scripts\tsumugi-expressions-tool\make-expressions.bat"; Name = "make-expressions.bat"; Kind = "CrlfText" },
    [pscustomobject]@{ Source = "scripts\tsumugi-expressions-tool\install-libraries.bat"; Name = "install-libraries.bat"; Kind = "CrlfText" },
    [pscustomobject]@{ Source = "scripts\tsumugi-expressions-tool\make-expressions.ps1"; Name = "make-expressions.ps1"; Kind = "BomScript" },
    [pscustomobject]@{ Source = "scripts\tsumugi-expressions-tool\README.txt"; Name = "README.txt"; Kind = "CrlfText" },
    [pscustomobject]@{ Source = "scripts\tsumugi_expressions_standalone.py"; Name = "tsumugi_expressions_standalone.py"; Kind = "Copy" },
    [pscustomobject]@{ Source = "scripts\generate_tsumugi_expressions.py"; Name = "generate_tsumugi_expressions.py"; Kind = "Copy" },
    [pscustomobject]@{ Source = "scripts\tsumugi-expressions-requirements.txt"; Name = "requirements.txt"; Kind = "Copy" },
    [pscustomobject]@{ Source = "scripts\tsumugi_app_consent.py"; Name = "tsumugi_app_consent.py"; Kind = "Copy" },
    [pscustomobject]@{ Source = "docs\tsumugi-expressions.sample.json"; Name = "expressions.json"; Kind = "ExpressionConfig" }
)
$script:ExpressionToolTermsSourceRelativeDir = "Assets\TsumugiQuiz\Resources\Terms"
$script:ExpressionToolTermsDirName = "terms"
# zip 版の expressions.json の `$schemaNote`。JSON の文字列にそのまま入れるので、引用符と円記号（バックスラッシュ）は使わない。
$script:ExpressionToolSchemaNote = "どの表情にどのレイヤーを使うかの設定（つむぎクイズの表情生成ツールが読む）。素材の画像は含まない（PSD のレイヤーの名前だけ）。組み合わせを変えたいときは、このファイルをコピーして編集し、make-expressions.ps1 の -ConfigPath で指定する。layers に書けるのは表情のグループ（!口 / !目 / !眉 / !アクセサリー）だけで、服装・体のグループは指定できない（指定すると 1 枚も書き出さずに止まる）。"

# 同梱するファイルの一覧（固定のファイル + 規約の本文）。Export と検査の両方が使う。
function Get-ExpressionToolFileList {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $termsSourceDir = Join-Path $ProjectRoot $script:ExpressionToolTermsSourceRelativeDir
    $termsFiles = @(Get-ChildItem -LiteralPath $termsSourceDir -Filter "*.txt" -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($termsFiles.Count -eq 0) {
        throw "規約の本文（*.txt）が見つかりません: $termsSourceDir"
    }
    $terms = @($termsFiles | ForEach-Object {
        [pscustomobject]@{
            Source = Join-Path $script:ExpressionToolTermsSourceRelativeDir $_.Name
            Name   = Join-Path $script:ExpressionToolTermsDirName $_.Name
            Kind   = "Copy"
        }
    })
    return @($script:ExpressionToolFiles) + $terms
}

# ビルドに入っている規約の本文と、ツールにコピーする規約の本文（Resources/Terms/*.txt）が同じかを確かめ、
# ビルドに見つからない規約のファイル名を返す（PR #224 H1）。ツールは terms/ の本文のハッシュで同意を判定するので、
# ビルドより後に規約を直してビルドし直さずに zip を作ると、アプリとツールで「今の規約」が食い違う。
# Unity は TextAsset の中身をそのままのバイト列で *.assets に入れる（2026-10-03 のビルドで 4 件とも実測）ので、
# 各ファイルのバイト列が TsumugiQuiz_Data/*.assets のどれかに含まれるかで見る。
function Get-TermsMissingFromBuild {
    param(
        [Parameter(Mandatory = $true)][string]$DataDir,
        [Parameter(Mandatory = $true)][string]$TermsSourceDir
    )

    # Latin1 はバイトと文字が 1 対 1 なので、バイト列の部分一致を文字列の検索で調べられる。
    $latin1 = [System.Text.Encoding]::Latin1
    $assetsText = (@(Get-ChildItem -LiteralPath $DataDir -Filter "*.assets" -File) |
        ForEach-Object { $latin1.GetString([System.IO.File]::ReadAllBytes($_.FullName)) }) -join "`0"
    $missing = @()
    foreach ($termsFile in @(Get-ChildItem -LiteralPath $TermsSourceDir -Filter "*.txt" -File)) {
        $needle = $latin1.GetString([System.IO.File]::ReadAllBytes($termsFile.FullName))
        if ($assetsText.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
            $missing += $termsFile.Name
        }
    }
    return @($missing)
}

# docs/tsumugi-expressions.sample.json の `$schemaNote` だけを差し替えた JSON を返す（#219 L5）。
# 差し替えたあとに読み直し、`$schemaNote` 以外が元と同じであることを確かめる（違えば例外）。
function Convert-ExpressionConfigForRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Json
    )

    if ($script:ExpressionToolSchemaNote.Contains('"') -or $script:ExpressionToolSchemaNote.Contains([string][char]92)) {
        throw "ExpressionToolSchemaNote に引用符か円記号が入っています。"
    }
    # "$schemaNote": "<JSON の文字列（エスケープを含む）>"
    $pattern = '"\$schemaNote"\s*:\s*"(?:[^"\\]|\\.)*"'
    if ([regex]::Matches($Json, $pattern).Count -ne 1) {
        throw "expressions.json の `$schemaNote が 1 つではありません。"
    }
    $replacement = '"$schemaNote": "' + $script:ExpressionToolSchemaNote + '"'
    $replaced = [regex]::Replace($Json, $pattern, { param($m) $replacement })

    $before = $Json | ConvertFrom-Json | Select-Object -Property * -ExcludeProperty '$schemaNote' | ConvertTo-Json -Depth 32 -Compress
    $after = $replaced | ConvertFrom-Json | Select-Object -Property * -ExcludeProperty '$schemaNote' | ConvertTo-Json -Depth 32 -Compress
    if ($before -ne $after) {
        throw "expressions.json の `$schemaNote 以外が変わってしまいました。"
    }
    return $replaced
}

# ツールのファイルを $DestinationDir に書き出す。コピー元が無ければ例外（呼び出し側の try/catch で非 0 終了）。
function Export-ExpressionTool {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    $written = @()
    New-Item -ItemType Directory -Force -Path (Join-Path $DestinationDir $script:ExpressionToolTermsDirName) | Out-Null
    foreach ($file in (Get-ExpressionToolFileList -ProjectRoot $ProjectRoot)) {
        $sourcePath = Join-Path $ProjectRoot $file.Source
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "表情生成ツールのファイルが見つかりません: $sourcePath"
        }
        $destinationPath = Join-Path $DestinationDir $file.Name
        switch ($file.Kind) {
            "Copy" { Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force }
            "ExpressionConfig" {
                $json = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
                [System.IO.File]::WriteAllText($destinationPath, (Convert-ExpressionConfigForRelease -Json $json), $utf8NoBom)
            }
            default {
                # ReadAllText は先頭の BOM を取り除くので、書くときの BOM の有無はエンコーディングだけで決まる。
                $text = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
                $text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
                $encoding = if ($file.Kind -eq "BomScript") { $utf8Bom } else { $utf8NoBom }
                [System.IO.File]::WriteAllText($destinationPath, $text, $encoding)
            }
        }
        $written += $destinationPath
    }

    return @($written)
}

# 書き出したツールのフォルダを検査し、問題の一覧を返す（空なら問題なし）。
# - 決めたファイル以外（画像・PSD・生成物など）が入っていないこと（ファイル名の許可リスト）
# - 決めたファイルがすべてあること
# - .ps1 が UTF-8（BOM 付き）、.bat が ASCII だけで CRLF であること
function Get-ExpressionToolProblems {
    param(
        [Parameter(Mandatory = $true)][string]$ToolDir,
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $problems = @()
    if (-not (Test-Path -LiteralPath $ToolDir -PathType Container)) {
        return @("表情生成ツールのフォルダがありません: $ToolDir")
    }

    $expectedNames = @(Get-ExpressionToolFileList -ProjectRoot $ProjectRoot | ForEach-Object { $_.Name })
    foreach ($item in @(Get-ChildItem -LiteralPath $ToolDir -Recurse -Force)) {
        $relative = $item.FullName.Substring($ToolDir.TrimEnd('\').Length + 1)
        $isTermsDir = $item.PSIsContainer -and $relative -eq $script:ExpressionToolTermsDirName
        if (-not $isTermsDir -and ($item.PSIsContainer -or $expectedNames -notcontains $relative)) {
            $problems += "決めたファイル以外が入っています（コードと設定だけを入れる）: $relative"
        }
    }
    foreach ($name in $expectedNames) {
        $path = Join-Path $ToolDir $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $problems += "ファイルがありません: $name"
            continue
        }
        $bytes = [System.IO.File]::ReadAllBytes($path)
        if ($name -like "*.ps1" -and -not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
            $problems += "$name が UTF-8（BOM 付き）ではありません（Windows PowerShell 5.1 が日本語を読み違えます）"
        }
        if ($name -like "*.bat") {
            if (@($bytes | Where-Object { $_ -gt 0x7F }).Count -gt 0) {
                $problems += "$name に ASCII 以外の文字があります（cmd.exe はコンソールのコードページで読みます）"
            }
            $text = [System.Text.Encoding]::ASCII.GetString($bytes)
            if ($text -match "(?<!`r)`n") {
                $problems += "$name の改行が CRLF ではありません"
            }
        }
    }

    return @($problems)
}

# 表情生成ツール（#219）の自己診断。実際のリポジトリのファイルを一時フォルダに書き出し、
# (1) 決めたファイルがそろい問題が無いこと、(2) 同梱禁止物検査に引っかからないこと、
# (3) 画像・PSD を紛れ込ませると両方の検査が検出すること、を確かめる。
function Invoke-ExpressionToolSelfTest {
    param(
        [Parameter(Mandatory = $true)][string]$TempRoot
    )

    $stagingRoot = Join-Path $TempRoot "tool-staging"
    $toolDir = Join-Path $stagingRoot $script:ExpressionToolDestinationRelativeDir
    try {
        [void](Export-ExpressionTool -ProjectRoot (Get-ProjectRoot) -DestinationDir $toolDir)
    } catch {
        Write-Host "自己診断に失敗しました: 表情生成ツールを書き出せませんでした: $($_.Exception.Message)" -ForegroundColor Red
        return 1
    }

    $problems = @()
    $initialProblems = @(Get-ExpressionToolProblems -ToolDir $toolDir -ProjectRoot (Get-ProjectRoot))
    if ($initialProblems.Count -gt 0) {
        $problems += $initialProblems | ForEach-Object { "書き出した直後の検査: $_" }
    }
    $initialForbidden = Get-ForbiddenStagingItems -StagingDirPath $stagingRoot
    if ($initialForbidden.Files.Count -gt 0 -or $initialForbidden.Dirs.Count -gt 0) {
        $problems += "ツールのファイルが同梱禁止物として検出されました: " + (($initialForbidden.Files + $initialForbidden.Dirs | ForEach-Object { $_.Name }) -join ", ")
    }
    $releaseConfig = [System.IO.File]::ReadAllText((Join-Path $toolDir "expressions.json"), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($releaseConfig.'$schemaNote' -ne $script:ExpressionToolSchemaNote) {
        $problems += "expressions.json の `$schemaNote が zip 版の説明に差し替わっていません"
    }
    $sourceTerms = @(Get-ChildItem -LiteralPath (Join-Path (Get-ProjectRoot) $script:ExpressionToolTermsSourceRelativeDir) -Filter "*.txt" -File)
    foreach ($termsFile in $sourceTerms) {
        $copied = Join-Path (Join-Path $toolDir $script:ExpressionToolTermsDirName) $termsFile.Name
        if (-not (Test-Path -LiteralPath $copied) -or
            (Get-FileHash -LiteralPath $copied).Hash -ne (Get-FileHash -LiteralPath $termsFile.FullName).Hash) {
            $problems += "規約の本文 $($termsFile.Name) がそのままコピーされていません"
        }
    }
    $written = @(Get-ChildItem -LiteralPath $toolDir -File -Recurse | ForEach-Object { $_.FullName.Substring($toolDir.Length + 1) } | Sort-Object)
    Write-Host ("  書き出したファイル: {0}" -f ($written -join ", ")) -ForegroundColor DarkGray

    # 画像・PSD を紛れ込ませる（中身はダミー）。許可リストの検査と同梱禁止物の検査の両方で検出されること。
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllBytes((Join-Path $toolDir "tsumugi_idle.png"), [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
    [System.IO.File]::WriteAllText((Join-Path $toolDir "source.psd"), "dummy", $utf8NoBom)
    $injectedProblems = @(Get-ExpressionToolProblems -ToolDir $toolDir -ProjectRoot (Get-ProjectRoot))
    foreach ($name in @("tsumugi_idle.png", "source.psd")) {
        if (@($injectedProblems | Where-Object { $_ -match [regex]::Escape($name) }).Count -eq 0) {
            $problems += "ツールのフォルダに紛れ込ませた $name を検出できませんでした"
        }
    }
    $injectedForbidden = Get-ForbiddenStagingItems -StagingDirPath $stagingRoot
    foreach ($name in @("tsumugi_idle.png", "source.psd")) {
        if (@($injectedForbidden.Files | Where-Object { $_.Name -eq $name }).Count -eq 0) {
            $problems += "同梱禁止物の検査が tools 配下の $name を検出できませんでした"
        }
    }

    # ビルドの規約との照合（Get-TermsMissingFromBuild）。1 件だけを含む偽の .assets で、含まない規約を見つけること。
    $fakeData = Join-Path $TempRoot "fake-build-data"
    New-Item -ItemType Directory -Force -Path $fakeData | Out-Null
    $termsSource = Join-Path (Get-ProjectRoot) $script:ExpressionToolTermsSourceRelativeDir
    $firstTerms = $sourceTerms | Sort-Object Name | Select-Object -First 1
    $fakeBytes = [byte[]](0x00, 0x01) + [System.IO.File]::ReadAllBytes($firstTerms.FullName) + [byte[]](0xFF)
    [System.IO.File]::WriteAllBytes((Join-Path $fakeData "resources.assets"), $fakeBytes)
    $missingFromFake = @(Get-TermsMissingFromBuild -DataDir $fakeData -TermsSourceDir $termsSource)
    $expectedMissing = @($sourceTerms | Where-Object { $_.Name -ne $firstTerms.Name } | ForEach-Object { $_.Name } | Sort-Object)
    if ((@($missingFromFake | Sort-Object) -join ",") -ne ($expectedMissing -join ",")) {
        $problems += "ビルドの規約との照合が想定と違います（見つからない: $($missingFromFake -join ', ') / 期待: $($expectedMissing -join ', ')）"
    }

    if ($problems.Count -gt 0) {
        Write-Host "自己診断に失敗しました（表情生成ツールの同梱）:" -ForegroundColor Red
        foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
        return 1
    }

    Write-Host "OK: 表情生成ツール（$($written.Count) ファイル、コードと設定だけ）を書き出せること、画像・PSD を紛れ込ませると検出すること、ビルドの規約との照合を確認しました。" -ForegroundColor Green
    return 0
}
