# scripts/generate-tsumugi-expressions.ps1
# 春日部つむぎ公式立ち絵 v2.0 の PSD から、表情差分 PNG（待機/読み上げ中/正解/不正解など場面ごとの 9 枚、#212）を
# ユーザー自身のローカル環境で生成する（issue #86）。
#
# 権利上の前提（docs/licenses.md §3 / External/README.md §5）:
#   - 立ち絵素材（zip・PSD・PNG 原本）も、本スクリプトが生成した表情差分 PNG も、
#     リポジトリ・配布物には一切含めない（「二次配布、自作発言」は規約で明確に禁止）。
#     出力先は既定で AppPaths.DataRoot（Application.persistentDataPath 相当）配下の tsumugi/。
#   - 本スクリプトが行う加工は「PSD にもとから入っている表情レイヤーの表示切り替え」
#     「バストアップ範囲への切り出し（#191）」「等比縮小」だけ。描き足し・色変更はしない
#     （規約 5 項「春日部つむぎと分からない…改変は禁止」）。
#   - 「服を脱がせた状態」にならないことを、書き出し直前に PSD の実状態で検証する
#     （scripts/generate_tsumugi_expressions.py の safety 検証。満たさない場合は 1 枚も出力しない）。
#     設定ファイルでは無効化できない（ホワイトリスト + 組み込みの安全セット、PR #135 レビュー H1）。
#   - 出力先の検証（Assets/ 配下・リポジトリ内の拒否）は本ラッパーと Python 本体の両方にある
#     （レビュー H2。Python を直接実行しても効く）。リポジトリの判定は出力先の祖先を辿って
#     ProjectSettings/ProjectSettings.asset か .git を探す方式なので、worktree から本体ツリーの
#     Assets/ を指定した場合も拒否する（再レビュー M1）。
#
# 前提:
#   - Python 3.10 以降と、psd-tools（MIT）/ Pillow（MIT-CMU）。未導入なら -InstallDeps を付けて実行する。
#   - 立ち絵 zip が External/tsumugi/ に配置済みで、scripts/setup-external.ps1 で展開済みであること
#     （本スクリプトはダウンロードを行わない。入手手順は External/README.md §5）。
#
# 使い方:
#   pwsh ./scripts/generate-tsumugi-expressions.ps1
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -DryRun
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -InstallDeps
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -TsumugiRoot E:\Claude\tsumugi-quiz\External\tsumugi
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -DataRoot C:\Users\<you>\AppData\LocalLow\Tomonorarari-Think\TsumugiQuiz
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -MaxHeight 0        # 切り出したままの原寸（バストアップは 1385x1797）
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -Crop full          # 切り出さない（従来の全身、#191 以前と同じ構図）
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -Crop 0.2,0.0,0.9,0.5   # 範囲を比率（左,上,右,下）で指定
#   pwsh ./scripts/generate-tsumugi-expressions.ps1 -Only correct -Only wrong
#
# 終了コード（安全確認で拒否した場合は 4 = Python 側の EXIT_SAFETY と同じ）を見るときは、
# 必ず `pwsh -File ./scripts/generate-tsumugi-expressions.ps1 ...` の形で実行すること。
# `pwsh -Command "& ./scripts/generate-tsumugi-expressions.ps1 ..."` だと本スクリプトの exit が
# 握り潰されて 1 になる（PR #135 限定確認 LOW-3、実測）。

param(
    # 立ち絵 zip / 展開結果の置き場所（既定: この worktree の External/tsumugi）
    [string]$TsumugiRoot,
    # PSD を直接指定する（既定: $TsumugiRoot 配下を再帰検索）
    [string]$PsdPath,
    # レイヤー対応表 JSON（既定: External/tsumugi/expressions.json → 無ければ docs/tsumugi-expressions.sample.json）
    [string]$ConfigPath,
    # 出力先の親（AppPaths.DataRoot 相当）。既定は Resolve-AppDataRoot の解決結果
    [string]$DataRoot,
    # 出力先ディレクトリを直接指定する（既定: <データルート>/tsumugi）
    [string]$OutputDir,
    # 出力 PNG の最大高さ（切り出し後に等比縮小。0 で切り出したままの原寸）。
    # 既定 1280 の根拠は generate_tsumugi_expressions.py の DEFAULT_MAX_HEIGHT（#190 / #191）
    [int]$MaxHeight = 1280,
    # 切り出し範囲（#191）。bustup（既定、頭から腰の上まで）/ full（全身）/ 左,上,右,下（0.0〜1.0 の比率）。
    # 値の検証は Python 側（parse_crop）で行う。PR #202 レビュー L1: `& ./...ps1 -Crop 0.2,0.0,0.9,0.5` では
    # PowerShell が 4 要素の配列として渡し、`pwsh -File ... -Crop 0.2,0.0,0.9,0.5` では 1 つの文字列として渡すため、
    # 配列で受けて `-join ','` で連結し、どちらの呼び方でも同じ値にする（下の $cropValue）
    [string[]]$Crop = @("bustup"),
    # 書き出す表情の key（未指定なら設定ファイルの全件）
    [string[]]$Only,
    # python 実行ファイル（既定: python）
    [string]$Python = "python",
    # psd-tools / Pillow を pip install する
    [switch]$InstallDeps,
    [switch]$DryRun
)

. "$PSScriptRoot/common.ps1"

$ErrorActionPreference = "Stop"

$projectRoot = Get-ProjectRoot
$pythonScript = Join-Path $PSScriptRoot "generate_tsumugi_expressions.py"

if ([string]::IsNullOrWhiteSpace($TsumugiRoot)) {
    $TsumugiRoot = Join-Path $projectRoot "External\tsumugi"
}

function Exit-WithMessage {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [int]$Code = 1
    )

    Write-Host $Message -ForegroundColor Red
    exit $Code
}

# setup-external.ps1 と同じ考え方: 素材・生成物を Assets/ 配下に置かせない（ビルドに同梱されるため）。
function Assert-PathOutsideAssets {
    param(
        [Parameter(Mandatory = $true)][string]$ParamName,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    $assetsDir = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "Assets"))
    try {
        $resolved = [System.IO.Path]::GetFullPath($PathValue)
    } catch {
        Exit-WithMessage "-$ParamName のパスが不正です: $PathValue"
    }

    $assetsPrefix = $assetsDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($assetsPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $resolved.Equals($assetsDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        Exit-WithMessage "-$ParamName に Assets/ 配下は指定できません: $resolved"
    }
}

function Test-SameOrUnder {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $pathFull = [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    if ($pathFull.Equals($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar
    return $pathFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

# PR #135 再レビュー M1: 出力先から上へ辿ってリポジトリ（Unity プロジェクト）のルートを探す。
# スクリプト自身のツリー（$projectRoot）だけを基準にすると、worktree から本体ツリーの Assets/ を
# 指定したときに素通りしてしまうため。.git は worktree ではファイルなので Test-Path で判定する
# （Python 側 generate_tsumugi_expressions.py の find_repository_root と同じ規則）。
function Find-RepositoryRoot {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    $current = [System.IO.Path]::GetFullPath($PathValue)
    while (-not [string]::IsNullOrEmpty($current)) {
        if ((Test-Path -LiteralPath (Join-Path $current "ProjectSettings\ProjectSettings.asset")) -or
            (Test-Path -LiteralPath (Join-Path $current ".git"))) {
            return $current
        }

        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

# 生成物（加工した立ち絵）は git 管理下に入れてはいけない。出力先はリポジトリの外か、
# git 管理外の External/ 配下だけを許す（うっかりコミットを構造的に防ぐ）。
# 判定の基準にするルートは (1) 出力先から辿って見つかったリポジトリ、(2) スクリプト自身のツリー。
function Assert-OutputDirAllowed {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    try {
        $resolved = [System.IO.Path]::GetFullPath($PathValue)
    } catch {
        Exit-WithMessage "-OutputDir のパスが不正です: $PathValue"
    }

    $roots = @()
    $detected = Find-RepositoryRoot -PathValue $resolved
    if (-not [string]::IsNullOrEmpty($detected)) { $roots += $detected }

    $scriptRoot = [System.IO.Path]::GetFullPath($projectRoot)
    if ($roots -notcontains $scriptRoot) { $roots += $scriptRoot }

    foreach ($root in $roots) {
        if (Test-SameOrUnder -Path $resolved -Parent (Join-Path $root "Assets")) {
            # Python 側（generate_tsumugi_expressions.py）の EXIT_SAFETY と終了コードを揃える。
            Exit-WithMessage -Code 4 -Message @"
出力先に Assets/ 配下は指定できません: $resolved
検出したリポジトリ: $root
Assets/ 配下はビルド成果物に同梱されるため、立ち絵の加工物を置けません（docs/licenses.md §3.1、External/README.md §5.4）。
"@
        }

        if ((Test-SameOrUnder -Path $resolved -Parent $root) -and
            -not (Test-SameOrUnder -Path $resolved -Parent (Join-Path $root "External"))) {
            Exit-WithMessage -Code 4 -Message @"
出力先がリポジトリ内（External/ 以外）です: $resolved
検出したリポジトリ: $root
立ち絵の加工物は二次配布禁止（docs/licenses.md §3.1）のため、git 管理下に置けません。
-DataRoot / -OutputDir にデータルート（Application.persistentDataPath 相当）か External/ 配下を指定してください。
ホームやデータルート自体が git 管理下の場合は、-OutputDir で管理外のパスを指定してください。
"@
        }
    }
}

# ---- PSD の解決 --------------------------------------------------------------

function Resolve-PsdPath {
    if (-not [string]::IsNullOrWhiteSpace($PsdPath)) {
        if (-not (Test-Path -LiteralPath $PsdPath -PathType Leaf)) {
            Exit-WithMessage "-PsdPath が見つかりません: $PsdPath"
        }
        return (Resolve-Path -LiteralPath $PsdPath).Path
    }

    if (-not (Test-Path -LiteralPath $TsumugiRoot)) {
        return $null
    }

    # setup-external.ps1 の展開先（External/tsumugi/extracted/<zip 内フォルダ>/）を優先して探す。
    $candidates = @(
        Get-ChildItem -LiteralPath $TsumugiRoot -Recurse -Filter "*.psd" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch "v1\.1\.1" } |
            Sort-Object -Property @{ Expression = { $_.Name -match "v2\.0" } } -Descending
    )

    if ($candidates.Count -eq 0) {
        return $null
    }

    if ($candidates.Count -gt 1) {
        Write-Host "PSD が複数見つかりました。最初の 1 件を使います: $($candidates[0].FullName)" -ForegroundColor Yellow
        foreach ($candidate in $candidates) { Write-Host "  - $($candidate.FullName)" -ForegroundColor DarkGray }
    }

    return $candidates[0].FullName
}

function Resolve-ConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
            Exit-WithMessage "-ConfigPath が見つかりません: $ConfigPath"
        }
        return (Resolve-Path -LiteralPath $ConfigPath).Path
    }

    # ユーザーが自分で表情の組み合わせを差し替えたい場合は External/tsumugi/expressions.json を置く
    # （git 管理外）。無ければリポジトリ同梱のサンプルを使う。
    $userConfig = Join-Path $TsumugiRoot "expressions.json"
    if (Test-Path -LiteralPath $userConfig -PathType Leaf) {
        return (Resolve-Path -LiteralPath $userConfig).Path
    }

    $sample = Join-Path $projectRoot "docs\tsumugi-expressions.sample.json"
    if (-not (Test-Path -LiteralPath $sample -PathType Leaf)) {
        Exit-WithMessage "既定の設定ファイルが見つかりません: $sample"
    }

    return (Resolve-Path -LiteralPath $sample).Path
}

function Resolve-OutputDir {
    if (-not [string]::IsNullOrWhiteSpace($OutputDir)) {
        return $OutputDir
    }

    $dataRootInfo = Resolve-AppDataRoot -ExplicitDataRoot $DataRoot
    if ($null -eq $dataRootInfo) {
        Exit-WithMessage @"
データルート（AppPaths.DataRoot 相当）を特定できませんでした。
-DataRoot <パス> か -OutputDir <パス> を明示してください
（通常は %USERPROFILE%\AppData\LocalLow\<companyName>\<productName>）。
"@
    }

    if ($dataRootInfo.Source -eq "Environment") {
        Write-Host "データルートは環境変数 TSUMUGI_DATA_ROOT から解決しました: $($dataRootInfo.Path)" -ForegroundColor Yellow
        Write-Host "これはテスト用データルートの可能性があります。実機で使うなら -DataRoot を明示してください。" -ForegroundColor Yellow
    }

    return (Join-Path $dataRootInfo.Path "tsumugi")
}

# ---- Python の確認 -----------------------------------------------------------

function Assert-PythonAvailable {
    # レビュー M1: $ErrorActionPreference = "Stop" のもとで未導入の実行ファイルを直接呼ぶと
    # CommandNotFoundException が terminating error になり、案内を出す前にスクリプトが落ちる。
    # 先に Get-Command で存在確認する（scripts/fetch-voicevox.ps1 の Test-CommandExists と同じ作法）。
    if ($null -eq (Get-Command $Python -ErrorAction SilentlyContinue)) {
        Exit-WithMessage @"
Python が見つかりません（-Python '$Python'）。
https://www.python.org/downloads/windows/ から 3.10 以降を入れるか、-Python <実行ファイル> を指定してください。
（Microsoft Store 版の 'python' スタブが反応する環境では、実体のパスを -Python で明示してください）
"@
    }

    $version = & $Python --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Exit-WithMessage @"
Python の実行に失敗しました（-Python '$Python'、終了コード $LASTEXITCODE）: $version
"@
    }

    Write-Host "Python   : $version" -ForegroundColor DarkGray
}

function Install-PythonDependencies {
    Write-Host "psd-tools（MIT）/ Pillow（MIT-CMU）を導入します（docs/licenses.md §15）。" -ForegroundColor Cyan
    Write-Host "システム全体の Python を汚したくない場合は、先に仮想環境を作ってそちらを指定してください:" -ForegroundColor DarkGray
    Write-Host "  python -m venv .venv" -ForegroundColor DarkGray
    Write-Host "  pwsh ./scripts/generate-tsumugi-expressions.ps1 -Python .\.venv\Scripts\python.exe -InstallDeps" -ForegroundColor DarkGray

    # レビュー M2: --upgrade は無関係な既存パッケージまで更新してしまうため付けない
    # （必要なのは「入っていなければ入れる」だけ。最低バージョンは指定子で担保する）。
    # #219: 指定は配布 zip の表情生成ツールと共通の requirements ファイル 1 か所にまとめた。
    $requirementsPath = Join-Path $PSScriptRoot "tsumugi-expressions-requirements.txt"
    if (-not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
        Exit-WithMessage "依存ライブラリの一覧が見つかりません: $requirementsPath"
    }
    & $Python -m pip install -r $requirementsPath
    if ($LASTEXITCODE -ne 0) {
        Exit-WithMessage "pip install に失敗しました（終了コード $LASTEXITCODE）。"
    }
}

# ---- 実行 --------------------------------------------------------------------

Write-Host "===== 立ち絵の表情差分を生成します（issue #86） =====" -ForegroundColor Cyan

$cropValue = (@($Crop) | ForEach-Object { "$_".Trim() }) -join ","

Assert-PathOutsideAssets -ParamName "TsumugiRoot" -PathValue $TsumugiRoot

if ($MaxHeight -lt 0) {
    Exit-WithMessage "-MaxHeight は 0 以上である必要があります（実際: $MaxHeight）。"
}

if (-not (Test-Path -LiteralPath $pythonScript -PathType Leaf)) {
    Exit-WithMessage "生成スクリプトが見つかりません: $pythonScript"
}

Assert-PythonAvailable
if ($InstallDeps) {
    Install-PythonDependencies
}

$resolvedPsd = Resolve-PsdPath
if ($null -eq $resolvedPsd) {
    Write-Host @"
立ち絵 PSD が見つかりません: $TsumugiRoot 配下に *.psd がありません。

本スクリプトはダウンロードを行いません。次の手順で用意してください（External/README.md §5）。
  1. 公式（BOOTH）から「春日部つむぎ公式立ち絵素材 v2.0」の zip を入手し、External/tsumugi/ に置く
  2. pwsh ./scripts/setup-external.ps1   （zip を External/tsumugi/extracted/ に展開する）
  3. 本スクリプトを再実行する

未生成のままでもゲームは動作します（表情差分が無ければ待機の絵にフォールバックし、
待機の絵も無ければ立ち絵を表示しないまま進行します）。
"@ -ForegroundColor Yellow
    exit 0
}

$resolvedConfig = Resolve-ConfigPath
$resolvedOutputDir = Resolve-OutputDir

Assert-OutputDirAllowed -PathValue $resolvedOutputDir

Write-Host "PSD      : $resolvedPsd"
Write-Host "設定     : $resolvedConfig"
Write-Host "出力先   : $resolvedOutputDir"
Write-Host "切り出し : $cropValue"
Write-Host "最大高さ : $(if ($MaxHeight -eq 0) { '原寸' } else { "$MaxHeight px" })"
Write-Host ""

$arguments = @(
    $pythonScript,
    "--psd", $resolvedPsd,
    "--config", $resolvedConfig,
    "--out-dir", $resolvedOutputDir,
    "--max-height", "$MaxHeight",
    "--crop", $cropValue
)

foreach ($key in @($Only)) {
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        $arguments += @("--only", $key)
    }
}

if ($DryRun) {
    $arguments += "--dry-run"
}

# 日本語のログが Windows の既定コンソール（cp932）で化けないようにする。
$previousIoEncoding = $env:PYTHONIOENCODING
$env:PYTHONIOENCODING = "utf-8"
try {
    & $Python @arguments
    $exitCode = $LASTEXITCODE
} finally {
    $env:PYTHONIOENCODING = $previousIoEncoding
}

if ($exitCode -ne 0) {
    Write-Host "表情差分の生成に失敗しました（終了コード $exitCode）。" -ForegroundColor Red
    exit $exitCode
}

Write-Host ""
Write-Host "生成した PNG は git 管理外のユーザーデータです。リポジトリ・配布物に含めないでください。" -ForegroundColor DarkGray
exit 0
