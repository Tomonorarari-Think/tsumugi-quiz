# scripts/setup-external.ps1
# External/voicevox_core/ と External/tsumugi/ に取得済みの資産を Assets/ 配下の所定の場所へ
# コピー・展開する（仮決め K24、立ち絵は issue #24）。
#
# コピー内容（voicevox_core）:
#   External/voicevox_core/c_api/lib/voicevox_core.dll              -> Assets/Plugins/voicevox_core/x86_64/
#   External/voicevox_core/onnxruntime/lib/voicevox_onnxruntime.dll -> Assets/Plugins/voicevox_core/x86_64/
#   External/voicevox_core/dict/open_jtalk_dic_*                    -> Assets/StreamingAssets/voicevox_core/dict/
#   External/voicevox_core/models/vvms/*.vvm                        -> Assets/StreamingAssets/voicevox_core/models/vvms/
#   External/voicevox_core/models/TERMS.txt, README.txt             -> Assets/StreamingAssets/voicevox_core/models/
#   External/voicevox_core/onnxruntime/TERMS.txt                    -> Assets/StreamingAssets/voicevox_core/onnxruntime/
#   External/voicevox_core/c_api/LICENSE                            -> Assets/StreamingAssets/voicevox_core/c_api/
#
# コピー内容（立ち絵、issue #24。voicevox_core とは独立した処理で、未配置でもスクリプト全体を失敗させない）:
#   External/tsumugi/*.zip（春日部つむぎ立ち絵_公式_v2.0.zip） -> (展開) External/tsumugi/extracted/
#   展開先の v2.0 PNG -> <データルート>/tsumugi/tsumugi_v2.png
#     （データルートは AppPaths.DataRoot 相当 = Application.persistentDataPath 相当。
#      Assets/ には一切コピーしない。レビュー H1: Assets/StreamingAssets/ も「Assets/」であり、
#      .gitignore していても原本を置く場所として不適切なため。v1.1.1 PNG・PSD・zip 自体・ロゴは対象外）
#
# コピー先はいずれも .gitignore 済み（本スクリプトが検証する）。voicevox_core のみ Assets/ 配下。
# DLL の Plugin Inspector 設定は Assets/TsumugiQuiz/Scripts/Editor/Tts/VoicevoxPluginPostprocessor.cs が
# インポート時に自動適用するので、.meta を git 管理する必要はない。
#
# 使い方:
#   pwsh ./scripts/setup-external.ps1
#   pwsh ./scripts/setup-external.ps1 -DryRun
#   pwsh ./scripts/setup-external.ps1 -ExternalRoot E:\Claude\tsumugi-quiz\External\voicevox_core   # worktree から本体の External を参照する
#   pwsh ./scripts/setup-external.ps1 -TsumugiRoot E:\Claude\tsumugi-quiz\External\tsumugi          # 同上（立ち絵）
#   pwsh ./scripts/setup-external.ps1 -SkipTsumugi                                                  # 立ち絵の配置だけ省略する
#   pwsh ./scripts/setup-external.ps1 -DataRoot C:\Users\<you>\AppData\LocalLow\Tomonorarari-Think\TsumugiQuiz
#       # 立ち絵のコピー先（データルート）を明示指定する。環境変数 TSUMUGI_DATA_ROOT より優先される
#       # （TSUMUGI_DATA_ROOT は scripts/verify.ps1 がテスト実行用に設定するものなので、
#       #   実機で実際にアプリが使うデータルートへ確実に配置したい場合はこちらを使うこと。レビュー M-c）

param(
    [string]$ExternalRoot,
    [string]$TsumugiRoot,
    [string]$DataRoot,
    [switch]$SkipTsumugi,
    [switch]$DryRun
)

. "$PSScriptRoot/common.ps1"

$ErrorActionPreference = "Stop"

$projectRoot = Get-ProjectRoot

if ([string]::IsNullOrWhiteSpace($ExternalRoot)) {
    $ExternalRoot = Join-Path $projectRoot "External\voicevox_core"
}

if ([string]::IsNullOrWhiteSpace($TsumugiRoot)) {
    $TsumugiRoot = Join-Path $projectRoot "External\tsumugi"
}

$destPluginsDir    = Join-Path $projectRoot "Assets\Plugins\voicevox_core\x86_64"
$destStreamingRoot = Join-Path $projectRoot "Assets\StreamingAssets\voicevox_core"

# 立ち絵（issue #24）。zip の展開先はこの worktree（$projectRoot）配下に固定する。
# -TsumugiRoot でメインツリー等、他所の External/tsumugi（zip の置き場所）を参照させることはできるが、
# 展開先は常に自分の worktree 内に置く（他ワークツリーを書き換えないため）。
# コピー先（レビュー H1）は Assets/ ではなく AppPaths.DataRoot（Application.persistentDataPath 相当）配下。
# 立ち絵原本を Assets/ に置くこと自体が二次配布のリスクになるため（.gitignore の有無に関わらず）。
$tsumugiExtractDir   = Join-Path $projectRoot "External\tsumugi\extracted"
$tsumugiDestFileName = "tsumugi_v2.png"

# ONNX Runtime の DLL 名。実測（2026-09-13、1.17.3）でバージョン接尾辞は付かないため固定名で扱う。
# C# 側（TsumugiQuiz.Tts.Native.VoicevoxNative.OnnxruntimeDllFileName）と一致させること。
$onnxruntimeDllName = "voicevox_onnxruntime.dll"

# コピー元が Assets/ 配下だと、コピー先と重なって自分自身を上書きしうる（voicevox_core）、
# または本来 Assets/ に置いてはいけない素材が紛れ込む（立ち絵、レビュー H1/L4）ため弾く。
function Assert-PathOutsideAssets {
    param(
        [Parameter(Mandatory = $true)][string]$ParamName,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    $assetsDir = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "Assets"))
    $resolved = $null
    try {
        $resolved = [System.IO.Path]::GetFullPath($PathValue)
    } catch {
        Write-Host "-$ParamName のパスが不正です: $PathValue" -ForegroundColor Red
        exit 1
    }

    $assetsPrefix = $assetsDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($assetsPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $resolved.Equals($assetsDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "-$ParamName に Assets/ 配下は指定できません: $resolved" -ForegroundColor Red
        exit 1
    }
}

# AppPaths.DataRoot（TsumugiQuiz.Core.AppPaths、#71）相当のパスを解決する Resolve-AppDataRoot は
# scripts/common.ps1 に統合した（issue #8 レビュー M-5。scripts/run-multi.ps1 とも共有するため）。
# 優先順位・戻り値の形（{ Path; Source }）は common.ps1 のコメントを参照。

# ---- .gitignore の確認（権利上コミットしてはいけない配置先） ----------------

function Assert-Gitignored {
    $gitignorePath = Join-Path $projectRoot ".gitignore"
    if (-not (Test-Path $gitignorePath)) {
        throw ".gitignore が見つかりません: $gitignorePath"
    }

    $content = Get-Content $gitignorePath -Raw
    $required = @(
        "/Assets/StreamingAssets/voicevox_core/",
        "/Assets/Plugins/voicevox_core/"
    )

    $missing = @($required | Where-Object { $content -notmatch [regex]::Escape($_) })
    if ($missing.Count -gt 0) {
        Write-Host "コピー先が .gitignore で除外されていません。権利上コミットできないため中止します:" -ForegroundColor Red
        foreach ($pattern in $missing) { Write-Host "  - $pattern" }
        exit 1
    }

    Write-Host ".gitignore の除外設定を確認しました（コピー先は git 管理外）。" -ForegroundColor DarkGray
}

# ---- コピー計画の組み立て ---------------------------------------------------

function New-CopyItem {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [ValidateSet("File", "Directory")][string]$Kind = "File"
    )

    return [pscustomobject]@{
        Source         = $Source
        DestinationDir = $DestinationDir
        Kind           = $Kind
    }
}

function Get-CopyPlan {
    $plan = @()
    $problems = @()

    # --- ネイティブ DLL ---
    $coreDll = Join-Path $ExternalRoot "c_api\lib\voicevox_core.dll"
    if (Test-Path $coreDll) {
        $plan += New-CopyItem -Source $coreDll -DestinationDir $destPluginsDir
    } else {
        $problems += "voicevox_core.dll が見つかりません: $coreDll"
    }

    # ONNX Runtime の DLL はバージョン接尾辞なしの固定名（実測 2026-09-13、1.17.3）。
    # C# 側も同じ固定名で探すので、ワイルドカードで別名を拾わない。
    $ortDll = Join-Path $ExternalRoot "onnxruntime\lib\$onnxruntimeDllName"
    if (Test-Path $ortDll) {
        $plan += New-CopyItem -Source $ortDll -DestinationDir $destPluginsDir
    } else {
        $problems += "$onnxruntimeDllName が見つかりません: $ortDll"
    }

    # --- Open JTalk 辞書 ---
    $dictParent = Join-Path $ExternalRoot "dict"
    $dictDirs = @()
    if (Test-Path $dictParent) {
        $dictDirs = @(Get-ChildItem -Path $dictParent -Directory -Filter "open_jtalk_dic*")
    }
    if ($dictDirs.Count -gt 0) {
        foreach ($dir in $dictDirs) {
            $plan += New-CopyItem -Source $dir.FullName -DestinationDir (Join-Path $destStreamingRoot "dict") -Kind Directory
        }
    } else {
        $problems += "Open JTalk 辞書（open_jtalk_dic*）が見つかりません: $dictParent"
    }

    # --- 音声モデル ---
    $vvmDir = Join-Path $ExternalRoot "models\vvms"
    $vvms = @()
    if (Test-Path $vvmDir) {
        $vvms = @(Get-ChildItem -Path $vvmDir -Filter "*.vvm" -File)
    }
    if ($vvms.Count -gt 0) {
        foreach ($vvm in $vvms) {
            $plan += New-CopyItem -Source $vvm.FullName -DestinationDir (Join-Path $destStreamingRoot "models\vvms")
        }
    } else {
        $problems += "音声モデル（*.vvm）が見つかりません: $vvmDir"
    }

    # --- 規約・ライセンス（配布時に同梱する。docs/licenses.md 参照） ---
    $legal = @(
        @{ Source = (Join-Path $ExternalRoot "models\TERMS.txt");      Dest = (Join-Path $destStreamingRoot "models") },
        @{ Source = (Join-Path $ExternalRoot "models\README.txt");     Dest = (Join-Path $destStreamingRoot "models") },
        @{ Source = (Join-Path $ExternalRoot "onnxruntime\TERMS.txt"); Dest = (Join-Path $destStreamingRoot "onnxruntime") },
        @{ Source = (Join-Path $ExternalRoot "c_api\LICENSE");         Dest = (Join-Path $destStreamingRoot "c_api") }
    )
    foreach ($item in $legal) {
        if (Test-Path $item.Source) {
            $plan += New-CopyItem -Source $item.Source -DestinationDir $item.Dest
        } else {
            $problems += "規約・ライセンスファイルが見つかりません: $($item.Source)"
        }
    }

    return [pscustomobject]@{ Plan = $plan; Problems = $problems }
}

# ---- 立ち絵（春日部つむぎ立ち絵、External/tsumugi/）の配置（issue #24） ------
#
# コピー内容:
#   $TsumugiRoot 直下の *.zip（春日部つむぎ立ち絵_公式_v2.0.zip、レビュー L4: ファイル名はワイルドカードで探す）
#     -> (展開) $tsumugiExtractDir/
#   展開先の v2.0 PNG -> <データルート>/tsumugi/tsumugi_v2.png
#     （データルートは Resolve-AppDataRoot が解決する AppPaths.DataRoot 相当。Assets/ には一切コピーしない）
#
# 立ち絵素材は二次配布禁止（docs/licenses.md §3）のため、voicevox_core と異なり
# 未配置でもスクリプト全体を失敗させない（CharacterView が非表示にフォールバックする設計、
# docs/tts.md §8.2 / External/README.md §5.5）。原本 PNG/PSD・zip 自体は Assets/ に一切コピーしない
# （レビュー H1: 「git にコミットしない」＝ .gitignore と、「Assets/ に置かない」は別の話であり、
# Assets/StreamingAssets/ 配下であっても原本を置くこと自体を避ける）。
function Install-TsumugiCharacterArt {
    if ($SkipTsumugi) {
        Write-Host "-SkipTsumugi が指定されたため立ち絵の配置をスキップします。" -ForegroundColor DarkGray
        return
    }

    Write-Host ""
    Write-Host "===== 立ち絵（春日部つむぎ）を配置します =====" -ForegroundColor Cyan
    Write-Host "Tsumugi  : $TsumugiRoot"

    # レビュー L4: 固定ファイル名ではなく $TsumugiRoot 直下の *.zip をワイルドカードで探す
    # （zip の実ファイル名が将来のバージョンで変わっても追従できるようにする）。
    $zipCandidates = @()
    if (Test-Path $TsumugiRoot) {
        $zipCandidates = @(Get-ChildItem -Path $TsumugiRoot -Filter "*.zip" -File -ErrorAction SilentlyContinue)
    }

    if ($zipCandidates.Count -eq 0) {
        Write-Host "立ち絵素材(zip)が見つかりません: $TsumugiRoot\*.zip" -ForegroundColor Yellow
        Write-Host "二次配布禁止のため zip 自体は同梱していません。External/README.md §5 の手順で公式サイトから入手し配置してください。" -ForegroundColor Yellow
        Write-Host "未配置のままでもゲームは動作します（CharacterView が立ち絵を表示しないだけで進行できます）。" -ForegroundColor Yellow
        return
    }

    if ($zipCandidates.Count -gt 1) {
        Write-Host "$TsumugiRoot に zip が複数見つかりました。最初の 1 件を使います: $($zipCandidates[0].Name)" -ForegroundColor Yellow
        foreach ($candidate in $zipCandidates) { Write-Host "  - $($candidate.Name)" -ForegroundColor DarkGray }
    }

    $zipPath = $zipCandidates[0].FullName

    $dataRootInfo = Resolve-AppDataRoot -ExplicitDataRoot $DataRoot
    if ($null -eq $dataRootInfo) {
        Write-Host "データルート（AppPaths.DataRoot 相当）を特定できませんでした" -ForegroundColor Yellow
        Write-Host "（ProjectSettings/ProjectSettings.asset の companyName/productName が読めないか、環境変数 TSUMUGI_DATA_ROOT も未設定）。" -ForegroundColor Yellow
        Write-Host "展開だけ行うので、'$tsumugiExtractDir' 配下の v2.0 PNG を、実行環境の" -ForegroundColor Yellow
        Write-Host "Application.persistentDataPath/tsumugi/tsumugi_v2.png へ手動で配置してください。" -ForegroundColor Yellow
    }

    $tsumugiDestFile = $null
    if ($null -ne $dataRootInfo) {
        $resolvedDataRoot = $dataRootInfo.Path

        # レビュー M-c: 環境変数由来のデータルートはテスト実行用（scripts/verify.ps1 が worktree
        # ごとに分離するための値）の可能性があり、実機でアプリが実際に使うデータルートとは
        # 限らない。明示的な -DataRoot を優先するよう案内する。
        if ($dataRootInfo.Source -eq "Environment") {
            Write-Host "データルートは環境変数 TSUMUGI_DATA_ROOT から解決しました: $resolvedDataRoot" -ForegroundColor Yellow
            Write-Host "これはテスト用データルートの可能性があり、アプリ実行時には参照されない場合があります。" -ForegroundColor Yellow
            Write-Host "実機で実際にアプリが使うデータルートへ確実に配置したい場合は -DataRoot <パス> を明示指定してください。" -ForegroundColor Yellow
        }

        # レビュー M-b: 解決したデータルートが誤って Assets/ 配下を指していないか確認する
        # （-DataRoot の誤指定、環境変数の設定ミス等）。
        Assert-PathOutsideAssets -ParamName "TSUMUGI_DATA_ROOT / データルート" -PathValue $resolvedDataRoot

        $tsumugiDestDir = Join-Path $resolvedDataRoot "tsumugi"
        $tsumugiDestFile = Join-Path $tsumugiDestDir $tsumugiDestFileName
        Write-Host "配置先   : $tsumugiDestFile"
    }

    if ($DryRun) {
        Write-Host "[DryRun] $zipPath -> $tsumugiExtractDir (展開)" -ForegroundColor DarkGray
        if ($null -ne $tsumugiDestFile) {
            Write-Host "[DryRun] <展開先>\...v2.0.png -> $tsumugiDestFile" -ForegroundColor DarkGray
        }
        return
    }

    New-Item -ItemType Directory -Force -Path $tsumugiExtractDir | Out-Null

    try {
        Expand-Archive -Path $zipPath -DestinationPath $tsumugiExtractDir -Force
    } catch {
        Write-Host "立ち絵 zip の展開に失敗しました: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "立ち絵なしで続行します（docs/tts.md §8.1 の状態切り替えは実機確認できません）。" -ForegroundColor Yellow
        return
    }

    # zip 内は「春日部つむぎ立ち絵_公式_v2.0」フォルダに本体が入っている（External/README.md §3）。
    # フォルダ名の揺れに耐えるため展開先を再帰検索し、v1.1.1 を除く v2.0 の PNG を 1 枚選ぶ。
    $v2Png = Get-ChildItem -Path $tsumugiExtractDir -Recurse -Filter "*v2.0.png" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch "v1\.1\.1" } |
        Select-Object -First 1

    if ($null -eq $v2Png) {
        Write-Host "展開後に v2.0 の PNG が見つかりませんでした（$tsumugiExtractDir 配下）。" -ForegroundColor Red
        Write-Host "立ち絵なしで続行します。zip の中身（ファイル名）を確認してください。" -ForegroundColor Yellow
        return
    }

    Write-Host "展開: $($v2Png.FullName)" -ForegroundColor Green

    if ($null -eq $tsumugiDestFile) {
        # データルートが特定できなかった場合はここで終了（展開のみ）。上で案内済み。
        return
    }

    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $tsumugiDestFile) | Out-Null
        Copy-Item -Path $v2Png.FullName -Destination $tsumugiDestFile -Force
    } catch {
        Write-Host "データルートへのコピーに失敗しました: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "手動で '$($v2Png.FullName)' を '$tsumugiDestFile' へコピーしてください。" -ForegroundColor Yellow
        return
    }

    Write-Host "配置: $tsumugiDestFile（元: $($v2Png.Name)）" -ForegroundColor Green
    Write-Host "（Assets/ には一切コピーしていません。データルート配下はアプリのユーザーデータであり、git 管理外です）" -ForegroundColor DarkGray
}

# ---- 実行 ------------------------------------------------------------------

Write-Host "===== voicevox_core を Assets/ へ配置します =====" -ForegroundColor Cyan
Write-Host "External: $ExternalRoot"
Write-Host "Plugins : $destPluginsDir"
Write-Host "Streaming: $destStreamingRoot"
Write-Host ""

Assert-PathOutsideAssets -ParamName "ExternalRoot" -PathValue $ExternalRoot
Assert-PathOutsideAssets -ParamName "TsumugiRoot" -PathValue $TsumugiRoot
Assert-Gitignored

# 立ち絵は voicevox_core と独立した素材のため、voicevox_core 側の存在チェック（次の Test-Path、
# 未配置だと exit 1 する）より先に処理する。どちらか片方が欠けていても、もう片方の配置結果を確認できるようにする。
Install-TsumugiCharacterArt

if (-not (Test-Path $ExternalRoot)) {
    Write-Host "External/voicevox_core が見つかりません: $ExternalRoot" -ForegroundColor Red
    Write-Host "External/README.md の手順（scripts/fetch-voicevox.ps1）で取得してください。" -ForegroundColor Yellow
    exit 1
}

$result = Get-CopyPlan

if ($result.Problems.Count -gt 0) {
    Write-Host "External/ に必要なファイルがそろっていません:" -ForegroundColor Red
    foreach ($problem in $result.Problems) { Write-Host "  - $problem" }
    Write-Host ""
    Write-Host "External/README.md の手順（scripts/fetch-voicevox.ps1）で取得してください。" -ForegroundColor Yellow
    exit 1
}

foreach ($item in $result.Plan) {
    $destination = Join-Path $item.DestinationDir (Split-Path $item.Source -Leaf)

    if ($DryRun) {
        Write-Host "[DryRun] $($item.Source) -> $destination" -ForegroundColor DarkGray
        continue
    }

    try {
        New-Item -ItemType Directory -Force -Path $item.DestinationDir | Out-Null

        if ($item.Kind -eq "Directory") {
            if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
            Copy-Item -Path $item.Source -Destination $destination -Recurse -Force
        } else {
            Copy-Item -Path $item.Source -Destination $destination -Force
        }
    } catch [System.UnauthorizedAccessException], [System.IO.IOException] {
        Write-Host "コピーに失敗しました: $($item.Source) -> $destination" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "DLL が使用中の可能性があります。Unity Editor（およびビルドした exe）を閉じてから再実行してください。" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "配置: $destination" -ForegroundColor Green
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DryRun のためコピーは行いませんでした。" -ForegroundColor Yellow
    exit 0
}

# ---- 配置後の検証 ----------------------------------------------------------

$expected = @(
    (Join-Path $destPluginsDir "voicevox_core.dll"),
    (Join-Path $destPluginsDir $onnxruntimeDllName),
    (Join-Path $destStreamingRoot "models\vvms"),
    # 配布時に同梱が必要な規約・ライセンス（docs/licenses.md）
    (Join-Path $destStreamingRoot "models\TERMS.txt"),
    (Join-Path $destStreamingRoot "models\README.txt"),
    (Join-Path $destStreamingRoot "onnxruntime\TERMS.txt"),
    (Join-Path $destStreamingRoot "c_api\LICENSE")
)

$missing = @($expected | Where-Object { -not (Test-Path $_) })
if ($missing.Count -gt 0) {
    Write-Host "配置後の検証に失敗しました。次のパスがありません:" -ForegroundColor Red
    foreach ($path in $missing) { Write-Host "  - $path" }
    exit 1
}

$vvmDest = @(Get-ChildItem -Path (Join-Path $destStreamingRoot "models\vvms") -Filter "*.vvm" -File -ErrorAction SilentlyContinue)
if ($vvmDest.Count -eq 0) {
    Write-Host "音声モデル（*.vvm）が配置されていません。" -ForegroundColor Red
    exit 1
}

$dictDest = @(Get-ChildItem -Path (Join-Path $destStreamingRoot "dict") -Directory -Filter "open_jtalk_dic*" -ErrorAction SilentlyContinue)
if ($dictDest.Count -eq 0 -or -not (Test-Path (Join-Path $dictDest[0].FullName "sys.dic"))) {
    Write-Host "Open JTalk 辞書（sys.dic）が配置されていません。" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "配置が完了しました。" -ForegroundColor Green
Write-Host "  - $destPluginsDir"
Write-Host "  - $destStreamingRoot"

exit 0
