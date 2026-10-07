# scripts/extract-palette.ps1
# 春日部つむぎの公式素材（ロゴ・立ち絵 PNG）から主要色（イメージカラー）を抽出する（issue #132）。
#
# 目的:
#   UI テーマ（Assets/TsumugiQuiz/UI/Styles/theme.uss）の --color-* トークンを
#   「素材由来のパレット」として決めるための根拠を、再現可能な形で得る。
#
# 方法:
#   1. PNG を System.Drawing で読み、透明・半透明ピクセル（既定 alpha < 200）を捨てる
#   2. RGB を 5bit/ch（32 階調）に量子化したヒストグラムを作る（32768 ビン）
#   3. ヒストグラムのビンを重み付きの点群とみなし、k-means（k-means++ 風の決定的な初期化）で
#      $ColorCount 色に集約する
#   4. 各クラスタの重心（HEX）と出現比率（%）を多い順に出力する
#
#   外部ライブラリは使わない（.NET の System.Drawing のみ。Windows 前提）。
#   ステップ 2 の量子化により、8M ピクセル級の立ち絵でも数秒で終わる。
#
# 素材の扱い（重要）:
#   春日部つむぎ公式立ち絵素材は二次配布禁止（External/README.md §5.3）。
#   本スクリプトは External/ 配下の素材を読むだけで、画像を Assets/ やリポジトリへコピーしない。
#   出力されるのは色の数値（HEX と比率）のみ。
#
# 使い方:
#   # 既定: External/tsumugi/extracted 配下の PNG をすべて処理する
#   pwsh ./scripts/extract-palette.ps1
#
#   # zip をまだ展開していない場合は -ExpandArchive を付ける（External/tsumugi/*.zip を展開する）
#   pwsh ./scripts/extract-palette.ps1 -ExpandArchive
#
#   # 個別の画像を指定し、結果をファイルにも書き出す
#   pwsh ./scripts/extract-palette.ps1 -Path External/tsumugi/extracted/.../tsumugi_logo.png -OutFile Logs/palette.md
#
# 出力は常に Markdown の表（PR 本文・docs へそのまま貼れる形式）。

[CmdletBinding()]
param(
    # 対象の画像ファイル（省略時は -InputRoot 配下の *.png をすべて処理する）
    [string[]]$Path,

    # 画像の探索起点（既定: External/tsumugi/extracted）
    [string]$InputRoot,

    # 指定すると External/tsumugi/*.zip を -InputRoot へ展開してから処理する
    [switch]$ExpandArchive,

    # 抽出する色数（k-means の k）
    [ValidateRange(2, 32)]
    [int]$ColorCount = 8,

    # ピクセルのサンプリング間隔（1 = 全ピクセル。2 なら縦横 2 ピクセルおき = 1/4 の画素を使う）
    [ValidateRange(1, 16)]
    [int]$SampleStride = 2,

    # この値未満のアルファのピクセルは背景とみなして捨てる
    [ValidateRange(0, 255)]
    [int]$MinAlpha = 200,

    # k-means の最大反復回数
    [ValidateRange(1, 200)]
    [int]$MaxIterations = 40,

    # 出力をファイルにも書き出す
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $InputRoot) {
    $InputRoot = Join-Path $repoRoot 'External/tsumugi/extracted'
}

# --------------------------------------------------------------------------------------
# 1 枚の画像から量子化ヒストグラムを作る
# --------------------------------------------------------------------------------------
function Get-QuantizedHistogram {
    param(
        [Parameter(Mandatory = $true)][string]$ImagePath,
        [Parameter(Mandatory = $true)][int]$Stride,
        [Parameter(Mandatory = $true)][int]$AlphaThreshold
    )

    $bitmap = [System.Drawing.Bitmap]::new($ImagePath)
    try {
        $rect = [System.Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
        $data = $bitmap.LockBits(
            $rect,
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            # Stride はボトムアップ DIB では負になりうるため、行オフセットの計算にも
            # 一貫して絶対値を使う（#132 レビュー L-3）。
            $rowStride = [Math]::Abs($data.Stride)
            $byteCount = $rowStride * $bitmap.Height
            $buffer = [byte[]]::new($byteCount)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $byteCount)

            # ビン番号 = (r5 << 10) | (g5 << 5) | b5
            $bins = [long[]]::new(32768)
            $total = 0L

            for ($y = 0; $y -lt $bitmap.Height; $y += $Stride) {
                $rowOffset = $y * $rowStride
                for ($x = 0; $x -lt $bitmap.Width; $x += $Stride) {
                    $i = $rowOffset + ($x * 4)
                    # Format32bppArgb はリトルエンディアンで B, G, R, A の順に並ぶ
                    if ($buffer[$i + 3] -lt $AlphaThreshold) { continue }
                    $b = $buffer[$i]
                    $g = $buffer[$i + 1]
                    $r = $buffer[$i + 2]
                    $bin = (([int]$r -shr 3) -shl 10) -bor (([int]$g -shr 3) -shl 5) -bor ([int]$b -shr 3)
                    $bins[$bin] = $bins[$bin] + 1
                    $total++
                }
            }

            return [pscustomobject]@{
                Bins       = $bins
                TotalCount = $total
                Width      = $bitmap.Width
                Height     = $bitmap.Height
            }
        }
        finally {
            $bitmap.UnlockBits($data)
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

# --------------------------------------------------------------------------------------
# ヒストグラム（重み付き点群）に対する k-means
# --------------------------------------------------------------------------------------
function Get-KMeansPalette {
    param(
        [Parameter(Mandatory = $true)][long[]]$Bins,
        [Parameter(Mandatory = $true)][int]$K,
        [Parameter(Mandatory = $true)][int]$Iterations
    )

    # 非ゼロのビンだけを点として取り出す（ビン中心 = 量子化値 + 4）
    $pointR = [System.Collections.Generic.List[double]]::new()
    $pointG = [System.Collections.Generic.List[double]]::new()
    $pointB = [System.Collections.Generic.List[double]]::new()
    $weight = [System.Collections.Generic.List[double]]::new()

    for ($bin = 0; $bin -lt $Bins.Length; $bin++) {
        $count = $Bins[$bin]
        if ($count -le 0) { continue }
        $pointR.Add(((($bin -shr 10) -band 0x1F) * 8) + 4)
        $pointG.Add(((($bin -shr 5) -band 0x1F) * 8) + 4)
        $pointB.Add((($bin -band 0x1F) * 8) + 4)
        $weight.Add([double]$count)
    }

    $pointCount = $pointR.Count
    if ($pointCount -eq 0) {
        throw '不透明なピクセルが 1 つもありません（MinAlpha を下げてください）。'
    }
    $effectiveK = [Math]::Min($K, $pointCount)

    # 初期化: 最も重い点を 1 つ目に取り、以降は「重み × 既存重心への最短距離^2」が最大の点を選ぶ
    # （k-means++ の決定的（乱数を使わない）版。実行ごとに結果が変わらないようにするため）
    $centroidR = [double[]]::new($effectiveK)
    $centroidG = [double[]]::new($effectiveK)
    $centroidB = [double[]]::new($effectiveK)

    $firstIndex = 0
    for ($i = 1; $i -lt $pointCount; $i++) {
        if ($weight[$i] -gt $weight[$firstIndex]) { $firstIndex = $i }
    }
    $centroidR[0] = $pointR[$firstIndex]
    $centroidG[0] = $pointG[$firstIndex]
    $centroidB[0] = $pointB[$firstIndex]

    $nearestSquared = [double[]]::new($pointCount)
    for ($i = 0; $i -lt $pointCount; $i++) { $nearestSquared[$i] = [double]::MaxValue }

    for ($c = 1; $c -lt $effectiveK; $c++) {
        $prev = $c - 1
        $bestIndex = -1
        $bestScore = -1.0
        for ($i = 0; $i -lt $pointCount; $i++) {
            $dr = $pointR[$i] - $centroidR[$prev]
            $dg = $pointG[$i] - $centroidG[$prev]
            $db = $pointB[$i] - $centroidB[$prev]
            $d2 = ($dr * $dr) + ($dg * $dg) + ($db * $db)
            if ($d2 -lt $nearestSquared[$i]) { $nearestSquared[$i] = $d2 }
            $score = $weight[$i] * $nearestSquared[$i]
            if ($score -gt $bestScore) {
                $bestScore = $score
                $bestIndex = $i
            }
        }
        $centroidR[$c] = $pointR[$bestIndex]
        $centroidG[$c] = $pointG[$bestIndex]
        $centroidB[$c] = $pointB[$bestIndex]
        # 選んだ点が次のラウンドで再選択されないよう距離を 0 にする
        $nearestSquared[$bestIndex] = 0
    }

    $assignment = [int[]]::new($pointCount)
    for ($iteration = 0; $iteration -lt $Iterations; $iteration++) {
        $changed = $false

        for ($i = 0; $i -lt $pointCount; $i++) {
            $best = 0
            $bestDistance = [double]::MaxValue
            for ($c = 0; $c -lt $effectiveK; $c++) {
                $dr = $pointR[$i] - $centroidR[$c]
                $dg = $pointG[$i] - $centroidG[$c]
                $db = $pointB[$i] - $centroidB[$c]
                $d2 = ($dr * $dr) + ($dg * $dg) + ($db * $db)
                if ($d2 -lt $bestDistance) {
                    $bestDistance = $d2
                    $best = $c
                }
            }
            if ($assignment[$i] -ne $best) {
                $assignment[$i] = $best
                $changed = $true
            }
        }

        $sumR = [double[]]::new($effectiveK)
        $sumG = [double[]]::new($effectiveK)
        $sumB = [double[]]::new($effectiveK)
        $sumW = [double[]]::new($effectiveK)
        for ($i = 0; $i -lt $pointCount; $i++) {
            $c = $assignment[$i]
            $w = $weight[$i]
            $sumR[$c] += $pointR[$i] * $w
            $sumG[$c] += $pointG[$i] * $w
            $sumB[$c] += $pointB[$i] * $w
            $sumW[$c] += $w
        }
        for ($c = 0; $c -lt $effectiveK; $c++) {
            if ($sumW[$c] -le 0) { continue }
            $centroidR[$c] = $sumR[$c] / $sumW[$c]
            $centroidG[$c] = $sumG[$c] / $sumW[$c]
            $centroidB[$c] = $sumB[$c] / $sumW[$c]
        }

        if (-not $changed) { break }
    }

    # クラスタごとの重み合計を集計して比率に直す
    $clusterWeight = [double[]]::new($effectiveK)
    $totalWeight = 0.0
    for ($i = 0; $i -lt $pointCount; $i++) {
        $clusterWeight[$assignment[$i]] += $weight[$i]
        $totalWeight += $weight[$i]
    }

    $results = for ($c = 0; $c -lt $effectiveK; $c++) {
        if ($clusterWeight[$c] -le 0) { continue }
        $r = [int][Math]::Round($centroidR[$c])
        $g = [int][Math]::Round($centroidG[$c])
        $b = [int][Math]::Round($centroidB[$c])
        $r = [Math]::Max(0, [Math]::Min(255, $r))
        $g = [Math]::Max(0, [Math]::Min(255, $g))
        $b = [Math]::Max(0, [Math]::Min(255, $b))
        [pscustomobject]@{
            Hex        = '#{0:x2}{1:x2}{2:x2}' -f $r, $g, $b
            R          = $r
            G          = $g
            B          = $b
            Ratio      = if ($totalWeight -gt 0) { $clusterWeight[$c] / $totalWeight } else { 0 }
            PixelCount = [long]$clusterWeight[$c]
            Luminance  = Get-RelativeLuminance -R $r -G $g -B $b
        }
    }

    return $results | Sort-Object -Property Ratio -Descending
}

# WCAG 2.1 の相対輝度（コントラスト比の計算に使う）。
# 同じ式は EditMode テスト
# Assets/TsumugiQuiz/Tests/EditMode/UI/ThemePaletteTests.cs の RelativeLuminance にもある
# （あちらが theme.uss のトークンを検証する本番のゲート、こちらは素材を調べる調査用）。
# 片方を直したらもう片方も合わせること（#132 レビュー L-9）。
function Get-RelativeLuminance {
    param(
        [Parameter(Mandatory = $true)][int]$R,
        [Parameter(Mandatory = $true)][int]$G,
        [Parameter(Mandatory = $true)][int]$B
    )

    $channels = @($R, $G, $B) | ForEach-Object {
        $v = $_ / 255.0
        if ($v -le 0.03928) { $v / 12.92 } else { [Math]::Pow((($v + 0.055) / 1.055), 2.4) }
    }
    return (0.2126 * $channels[0]) + (0.7152 * $channels[1]) + (0.0722 * $channels[2])
}

# --------------------------------------------------------------------------------------
# 入力の解決
# --------------------------------------------------------------------------------------
if ($ExpandArchive) {
    $zipDirectory = Join-Path $repoRoot 'External/tsumugi'
    $archives = @(Get-ChildItem -Path $zipDirectory -Filter '*.zip' -File -ErrorAction SilentlyContinue)
    if ($archives.Count -eq 0) {
        throw "展開対象の zip が見つかりません: $zipDirectory（External/README.md §5.1 を参照）"
    }
    foreach ($archive in $archives) {
        Write-Host "展開: $($archive.Name) -> $InputRoot"
        Expand-Archive -Path $archive.FullName -DestinationPath $InputRoot -Force
    }
}

if (-not $Path -or $Path.Count -eq 0) {
    if (-not (Test-Path -LiteralPath $InputRoot)) {
        throw "画像フォルダが見つかりません: $InputRoot（-ExpandArchive を付けて実行するか、External/README.md §5.1 の手順で展開してください）"
    }
    $Path = @(Get-ChildItem -Path $InputRoot -Filter '*.png' -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
}

if ($Path.Count -eq 0) {
    throw "対象の PNG が見つかりません: $InputRoot"
}

# --------------------------------------------------------------------------------------
# 実行
# --------------------------------------------------------------------------------------
$lines = [System.Collections.Generic.List[string]]::new()
function Add-Line {
    param([string]$Text = '')
    $lines.Add($Text)
}

Add-Line "# 春日部つむぎ素材のパレット抽出結果"
Add-Line ''
Add-Line "生成: scripts/extract-palette.ps1（ColorCount=$ColorCount, SampleStride=$SampleStride, MinAlpha=$MinAlpha）"
Add-Line ''

foreach ($imagePath in $Path) {
    if (-not (Test-Path -LiteralPath $imagePath)) {
        Write-Warning "見つかりません: $imagePath"
        continue
    }

    Write-Host "解析中: $imagePath"
    $histogram = Get-QuantizedHistogram -ImagePath $imagePath -Stride $SampleStride -AlphaThreshold $MinAlpha
    if ($histogram.TotalCount -eq 0) {
        Write-Warning "不透明ピクセルがありません: $imagePath"
        continue
    }
    $palette = Get-KMeansPalette -Bins $histogram.Bins -K $ColorCount -Iterations $MaxIterations

    $name = Split-Path -Leaf $imagePath
    Add-Line "## $name"
    Add-Line ''
    Add-Line ("サイズ {0}x{1} / サンプル画素 {2:N0}（alpha >= {3}）" -f $histogram.Width, $histogram.Height, $histogram.TotalCount, $MinAlpha)
    Add-Line ''
    Add-Line '| # | HEX | RGB | 比率 | 相対輝度 |'
    Add-Line '|---|---|---|---|---|'

    $index = 1
    foreach ($entry in $palette) {
        Add-Line ('| {0} | `{1}` | {2}, {3}, {4} | {5:P2} | {6:N4} |' -f `
                $index, $entry.Hex, $entry.R, $entry.G, $entry.B, $entry.Ratio, $entry.Luminance)
        $index++
    }
    Add-Line ''
}

$output = $lines -join [Environment]::NewLine

Write-Output $output

if ($OutFile) {
    $outDirectory = Split-Path -Parent $OutFile
    if ($outDirectory -and -not (Test-Path -LiteralPath $outDirectory)) {
        New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
    }
    Set-Content -LiteralPath $OutFile -Value $output -Encoding utf8
    Write-Host "書き出しました: $OutFile"
}
