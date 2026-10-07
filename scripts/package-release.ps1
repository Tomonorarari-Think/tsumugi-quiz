# scripts/package-release.ps1
# scripts/build.ps1 の出力（Builds/Windows/）を基に、配布用 zip を作成する
# （docs/tts.md §10.2 の構成例、docs/licenses.md §12、docs/dev-workflow.md §8、issue #34）。
#
# 前提: 事前に以下を実行しておくこと
#   pwsh ./scripts/setup-external.ps1 -ExternalRoot <External/voicevox_core のパス> -SkipTsumugi
#   pwsh ./scripts/build.ps1
#
# 使い方:
#   pwsh ./scripts/package-release.ps1                  # Builds/Windows/ から既定の版番号で zip を作る
#   pwsh ./scripts/package-release.ps1 -Version 1.0.0    # 版番号を明示指定（既定は ProjectSettings の bundleVersion）
#   pwsh ./scripts/package-release.ps1 -KeepStaging      # zip 作成後もステージングフォルダを残す（内容確認用）
#   pwsh ./scripts/package-release.ps1 -BuildDir <path> -OutputDir <path>
#   pwsh ./scripts/package-release.ps1 -SelfTest         # 同梱禁止物検査ロジック自体の自己診断のみ実行する
#
# 出力: <OutputDir>/TsumugiQuiz-v<Version>-win-x64.zip（既定 OutputDir は Builds/）
# 注意: 本スクリプトが生成する zip・ステージングフォルダはいずれも Builds/ 配下（git 管理外）。
#       本スクリプト自体は立ち絵原本（PSD/PNG/zip）を一切参照・同梱しない
#       （docs/licenses.md §3 (d)。参照するのは scripts/build.ps1 のビルド成果物のみ）。
#       zip には立ち絵の表情生成ツール（tools/tsumugi-expressions/、#219）を入れるが、中身は
#       リポジトリのコードと設定だけ（$script:ExpressionToolFiles。画像・PSD は許可リストの検査で弾く）。
#
# 【重要な前提】トップレベルのコピー許可リスト（$script:TopLevelItemsToCopy）は
# Mono バックエンド（scripts/build.ps1 の既定）でのビルド出力を実測して決めたものであり、
# `scripts/build.ps1 -IL2CPP` の出力構成では検証していない。IL2CPP ビルドは
# `GameAssembly.dll` 等、許可リストに無い実行に必須のファイルをトップレベルに生成するため、
# 許可リストが古いまま IL2CPP 出力に対して本スクリプトを実行すると、必須ファイルが
# 「安全のため無視した」まま黙って欠落した zip ができかねない。これを防ぐため、
# 許可リスト・既知の除外パターンのどちらにも一致しないトップレベル項目を検出した場合は
# 一覧を表示して失敗する（H1。docs/dev-workflow.md §8 にも明記）。IL2CPP を使う場合は、
# 実際の出力を確認したうえで許可リストを更新してから使うこと。
#
# 終了コード: 途中経路で問題を検出した場合は必ず非 0 で終了する。本体を Invoke-Main 関数に
# まとめ、失敗時は関数内で `return <非0>` する。最終的な終了コードは $script:ExitCode に
# 集約し、スクリプト末尾で 1 箇所だけ `exit $script:ExitCode` する（レビュー L4。個別の
# `exit` 呼び出しを散在させると、将来コードを追加した際に途中経路で終了コードが未設定の
# まま return し忘れる・cleanup 処理をスキップする、といった事故を起こしやすいため）。
# 想定外の例外（Compress-Archive 失敗等）は try/catch で捕捉し、必ず非 0 で終了する。

param(
    [string]$BuildDir,
    [string]$OutputDir,
    [string]$Version,
    [switch]$KeepStaging,
    # レビュー M4: 同梱禁止物検査ロジック自体が正しく機能しているかどうかだけを自己診断する。
    # 一時ディレクトリに dummy.psd を置いて検査関数を呼び、検出できることを確認する
    # （実際のビルド・zip 作成は行わない）。
    [switch]$SelfTest
)

. "$PSScriptRoot/common.ps1"

# 手順書の HTML 化（PR #218）と表情生成ツールの同梱（#219）は別ファイルに分けている（800 行以下に保つため）。
. "$PSScriptRoot/package-release-manual.ps1"
. "$PSScriptRoot/package-release-tool.ps1"

$ErrorActionPreference = "Stop"

# ---- 許可リスト・除外パターン（Invoke-Main / Invoke-SelfTest 共通） -----------------

# Builds/Windows/ 直下のうち、配布に含める項目の明示的な許可リスト（Mono バックエンド実測、上記コメント参照）。
$script:TopLevelItemsToCopy = @(
    "TsumugiQuiz.exe",
    "TsumugiQuiz_Data",
    "UnityPlayer.dll",
    "UnityCrashHandler64.exe",
    "MonoBleedingEdge",
    "D3D12",
    "dstorage.dll",
    "dstoragecore.dll"
)

# 許可リストに無くても「配布に含めるべきではないと分かっている」ため、未知項目としては
# 扱わない（エラーにしない）既知の Unity 生成物。プロダクト名を含む完全一致ではなく、
# 将来 productName が変わっても追従できるようワイルドカードで判定する（H1）。
$script:KnownExcludedTopLevelPatterns = @(
    "*_BackUpThisFolder_ButDontShipItWithYourGame*",
    # レビュー M-10: 実際のフォルダ名は productName が先頭に付く
    # `TsumugiQuiz_BurstDebugInformation_DoNotShip`（`BurstDebugInformation` 単体ではない）。
    "*_BurstDebugInformation_DoNotShip*",
    # issue #131 レビュー M-1: 固定パスでの実機確認（scripts/build.ps1 -OutputDir）が書き出す
    # 出所記録・ロックファイル。-BuildDir で固定パスを指定した場合にトップレベルへ現れうるが、
    # 配布に含めるべきではない既知の項目。
    ".build-info.json",
    ".build.lock"
)

# リポジトリ直下から配布 zip のトップレベルへ入れるライセンス文書の許可リスト（docs/licenses.md §17）。
# LICENSE は MIT の本文、NOTICE.md は適用範囲の注記。どちらかが無いとパッケージ作成を中止する。
$script:RepositoryLicenseFiles = @(
    "LICENSE",
    "NOTICE.md"
)

# 同梱してはいけないファイル名パターン（docs/licenses.md §3 (d): 立ち絵原本(PSD/PNG/zip)は
# 二次配布禁止。加えて .pdb（デバッグシンボル）・Logs（ビルドログ）・Live2D 素材
# （*.moc3 / *.model3.json。立ち絵は Live2D 化されていないが、将来の誤混入に備えた保険）・
# ロゴ原本も対象に含める、レビュー M4）。
# 【注意】このファイル名検査はあくまで補助的なセーフティネットであり、立ち絵非同梱の実質的な
# 担保は docs/licenses.md §3 (d) の運用（立ち絵原本を Assets/ にもリポジトリにも置かない）と、
# 実行時に AppPaths.DataRoot（Application.persistentDataPath 相当）からのみ読み込む実装
# （docs/tts.md §8.2、External/README.md §5.5）である。ファイル名が検査パターンに
# 一致しない別名で紛れ込むケースまでは検出できない。
$script:ForbiddenNamePatterns = @(
    "*tsumugi_v2*",
    # 表情生成ツール（#219）が作る表情差分（tsumugi_idle.png など 9 枚）。ツールを同梱したので、生成物の
    # 誤混入にも備える（生成物の実質の担保は、ツールが出力先をデータルートにしアプリのフォルダを拒否すること）。
    "tsumugi_*.png",
    "*tsumugi_logo*",
    "*tsumugi*立ち絵*",
    "*.psd",
    "*立ち絵*.zip",
    "*.pdb",
    "*.moc3",
    "*.model3.json",
    "*BackUpThisFolder*"
)
$script:ForbiddenDirNamePatterns = @(
    "*Logs*",
    "*BackUpThisFolder*"
)

# ---- 版番号の解決・検証 ---------------------------------------------------------------

# レビュー M1: ProjectSettings.asset が見つからない・bundleVersion が読めない場合に
# 黙って "0.0.0" にフォールバックすると、意図しない版番号の zip ができてしまう。
# 呼び出し側が気づけるよう例外にする。
function Resolve-BundleVersion {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $settingsPath = Join-Path $ProjectRoot "ProjectSettings\ProjectSettings.asset"
    if (-not (Test-Path $settingsPath)) {
        throw "ProjectSettings.asset が見つからないため版番号を解決できません: $settingsPath`n-Version で明示的に指定してください。"
    }
    $content = Get-Content $settingsPath -Raw
    if ($content -match "(?m)^\s*bundleVersion:\s*(\S+)\s*$") {
        return $Matches[1].Trim()
    }
    throw "ProjectSettings.asset から bundleVersion を読み取れませんでした: $settingsPath`n-Version で明示的に指定してください。"
}

# レビュー M2: -Version はそのまま zip 名・ステージングフォルダ名に使われるため、
# パス区切り文字やワイルドカード等が混入しないよう形式を検証する。
function Assert-ValidVersionFormat {
    param(
        [Parameter(Mandatory = $true)][string]$VersionValue
    )

    if ($VersionValue -notmatch "^[0-9A-Za-z][0-9A-Za-z._-]*$") {
        throw "-Version の形式が不正です: '$VersionValue'（英数字・ドット・アンダースコア・ハイフンのみ、先頭は英数字。例: 1.0.0）"
    }
}

# ---- H1: トップレベルの未知項目検出（IL2CPP の GameAssembly.dll 漏れ対策） -------------

function Get-UnexpectedTopLevelItems {
    param(
        [Parameter(Mandatory = $true)][string]$BuildDirPath
    )

    $items = @(Get-ChildItem -Path $BuildDirPath -Force)
    $unexpected = @()
    foreach ($item in $items) {
        if ($script:TopLevelItemsToCopy -contains $item.Name) {
            continue
        }
        $isKnownExcluded = $false
        foreach ($pattern in $script:KnownExcludedTopLevelPatterns) {
            if ($item.Name -like $pattern) {
                $isKnownExcluded = $true
                break
            }
        }
        if (-not $isKnownExcluded) {
            $unexpected += $item
        }
    }

    return @($unexpected)
}

# ---- 同梱禁止物の検査（Invoke-Main / Invoke-SelfTest 共通） ---------------------------
# docs/licenses.md §3 (d) 等（上記 $script:ForbiddenNamePatterns のコメント参照）。

function Get-ForbiddenStagingItems {
    param(
        [Parameter(Mandatory = $true)][string]$StagingDirPath
    )

    $stagedFiles = @(Get-ChildItem -Path $StagingDirPath -Recurse -File)
    $forbiddenFileHits = @()
    foreach ($pattern in $script:ForbiddenNamePatterns) {
        $forbiddenFileHits += @($stagedFiles | Where-Object { $_.Name -like $pattern })
    }

    $forbiddenDirHits = @(Get-ChildItem -Path $StagingDirPath -Recurse -Directory | Where-Object {
        $name = $_.Name
        ($script:ForbiddenDirNamePatterns | Where-Object { $name -like $_ }).Count -gt 0
    })

    # レビュー L5: 同じファイルが複数パターンに一致すると $forbiddenFileHits に重複して
    # 入りうるため、FullName で一意化してから返す。
    $uniqueFileHits = @($forbiddenFileHits | Sort-Object -Property FullName -Unique)
    $uniqueDirHits = @($forbiddenDirHits | Sort-Object -Property FullName -Unique)

    return [pscustomobject]@{
        Files = $uniqueFileHits
        Dirs  = $uniqueDirHits
    }
}

# ---- -SelfTest: 同梱禁止物検査ロジック自体の自己診断（レビュー M4） -------------------

function Invoke-SelfTest {
    Write-Host "===== -SelfTest: 同梱禁止物検査ロジックの自己診断 =====" -ForegroundColor Cyan

    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("package-release-selftest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

    try {
        # 正常系ダミー: 検査に引っかかってはいけないファイル。
        Set-Content -Path (Join-Path $tempDir "README.txt") -Value "dummy" -Encoding UTF8

        # 異常系ダミー: 検査で必ず検出されるべきファイル（*.psd）。
        $dummyPsdPath = Join-Path $tempDir "dummy.psd"
        Set-Content -Path $dummyPsdPath -Value "dummy" -Encoding UTF8

        $result = Get-ForbiddenStagingItems -StagingDirPath $tempDir

        $detected = @($result.Files | Where-Object { $_.FullName -eq $dummyPsdPath })
        if ($detected.Count -eq 0) {
            Write-Host "自己診断に失敗しました: dummy.psd が同梱禁止物検査で検出されませんでした。" -ForegroundColor Red
            Write-Host "Get-ForbiddenStagingItems / `$script:ForbiddenNamePatterns を確認してください。" -ForegroundColor Red
            return 1
        }

        # 正常系ダミー（README.txt）が誤検出されていないことも確認する。
        $falsePositives = @($result.Files | Where-Object { $_.Name -eq "README.txt" })
        if ($falsePositives.Count -gt 0) {
            Write-Host "自己診断に失敗しました: 正常なファイル（README.txt）を誤って検出しました。" -ForegroundColor Red
            return 1
        }

        # LICENSE / NOTICE.md が許可リストにあり、リポジトリ直下に実在し、同梱禁止物として弾かれないこと。
        $licenseProject = Get-ProjectRoot
        foreach ($licenseFileName in @("LICENSE", "NOTICE.md")) {
            if ($script:RepositoryLicenseFiles -notcontains $licenseFileName) {
                Write-Host "自己診断に失敗しました: $licenseFileName が `$script:RepositoryLicenseFiles にありません。" -ForegroundColor Red
                return 1
            }
            if (-not (Test-Path (Join-Path $licenseProject $licenseFileName))) {
                Write-Host "自己診断に失敗しました: リポジトリ直下に $licenseFileName がありません。" -ForegroundColor Red
                return 1
            }
            $licenseProbeDir = Join-Path $tempDir "license-probe"
            New-Item -ItemType Directory -Force -Path $licenseProbeDir | Out-Null
            Copy-Item -Path (Join-Path $licenseProject $licenseFileName) -Destination $licenseProbeDir -Force
        }
        $licenseForbidden = Get-ForbiddenStagingItems -StagingDirPath (Join-Path $tempDir "license-probe")
        if ($licenseForbidden.Files.Count -gt 0 -or $licenseForbidden.Dirs.Count -gt 0) {
            Write-Host "自己診断に失敗しました: LICENSE / NOTICE.md が同梱禁止物として検出されました。" -ForegroundColor Red
            return 1
        }
        Write-Host "OK: LICENSE / NOTICE.md が許可リストにあり、実在し、同梱禁止物ではないことを確認しました。" -ForegroundColor Green

        Write-Host "OK: dummy.psd が同梱禁止物として正しく検出され、正常なファイルは誤検出されませんでした。" -ForegroundColor Green

        $manualResult = Invoke-ManualHtmlSelfTest -TempRoot $tempDir
        if ($manualResult -ne 0) {
            return $manualResult
        }

        return (Invoke-ExpressionToolSelfTest -TempRoot $tempDir)
    } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- メイン処理 ------------------------------------------------------------------

function Invoke-Main {
    $projectRoot = Get-ProjectRoot

    $buildDir = $BuildDir
    if ([string]::IsNullOrWhiteSpace($buildDir)) {
        $buildDir = Join-Path $projectRoot "Builds\Windows"
    }
    # レビュー M3: $outputDir を解決したら、他の処理より前に（zip 削除やステージング作成より先に）
    # ディレクトリ自体の存在を保証しておく。
    $outputDir = $OutputDir
    if ([string]::IsNullOrWhiteSpace($outputDir)) {
        $outputDir = Join-Path $projectRoot "Builds"
    }
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    $version = $Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = Resolve-BundleVersion -ProjectRoot $projectRoot
    }
    Assert-ValidVersionFormat -VersionValue $version

    $packageName = "TsumugiQuiz-v$version-win-x64"
    $stagingDir = Join-Path $outputDir $packageName
    $zipPath = Join-Path $outputDir "$packageName.zip"

    Write-Host "===== 配布パッケージ作成: $packageName =====" -ForegroundColor Cyan
    Write-Host "BuildDir : $buildDir"
    Write-Host "Staging  : $stagingDir"
    Write-Host "Zip      : $zipPath"
    Write-Host ""

    # ---- ビルド成果物の存在確認 ----------------------------------------------------

    $exePath = Join-Path $buildDir "TsumugiQuiz.exe"
    if (-not (Test-Path $exePath)) {
        Write-Host "ビルド成果物が見つかりません: $exePath" -ForegroundColor Red
        Write-Host "先に 'pwsh ./scripts/build.ps1' を実行してください。" -ForegroundColor Yellow
        return 1
    }

    $dataDir = Join-Path $buildDir "TsumugiQuiz_Data"
    if (-not (Test-Path $dataDir)) {
        Write-Host "ビルド成果物が見つかりません: $dataDir" -ForegroundColor Red
        return 1
    }

    # H1: 許可リスト・既知の除外パターンのどちらにも一致しないトップレベル項目がないか検証する。
    # IL2CPP ビルドの GameAssembly.dll のように、許可リストに無いまま黙って zip から漏れると
    # 実行できない配布物になってしまうものを検出するための安全策（スクリプト冒頭のコメント参照）。
    $unexpectedTopLevel = @(Get-UnexpectedTopLevelItems -BuildDirPath $buildDir)
    if ($unexpectedTopLevel.Count -gt 0) {
        Write-Host "$buildDir 直下に、許可リスト（`$script:TopLevelItemsToCopy）にも既知の除外パターンにも一致しない項目があります:" -ForegroundColor Red
        foreach ($item in $unexpectedTopLevel) {
            Write-Host ("  - {0}{1}" -f $item.Name, $(if ($item.PSIsContainer) { " (dir)" } else { "" }))
        }
        Write-Host ""
        Write-Host "IL2CPP ビルド（GameAssembly.dll 等）や Unity のバージョンアップで出力構成が" -ForegroundColor Yellow
        Write-Host "変わった可能性があります。実際に配布へ含めるべきか確認し、`$script:TopLevelItemsToCopy" -ForegroundColor Yellow
        Write-Host "（本スクリプト冒頭）を更新してから再実行してください（本許可リストは Mono バックエンド" -ForegroundColor Yellow
        Write-Host "前提で、-IL2CPP ビルドでは未検証です。docs/dev-workflow.md §8 参照）。" -ForegroundColor Yellow
        return 1
    }

    # External/voicevox_core 由来のファイルは git 管理外なので、setup-external.ps1 実行漏れのまま
    # ビルドすると同梱されない。配布に必須なファイルが実際にビルド成果物に含まれているか検証する。
    $requiredUnderData = @(
        "Plugins\x86_64\voicevox_core.dll",
        "Plugins\x86_64\voicevox_onnxruntime.dll",
        "StreamingAssets\voicevox_core\models\TERMS.txt",
        "StreamingAssets\voicevox_core\onnxruntime\TERMS.txt",
        "StreamingAssets\voicevox_core\c_api\LICENSE"
    )
    $missingRequired = @()
    foreach ($rel in $requiredUnderData) {
        $full = Join-Path $dataDir $rel
        if (-not (Test-Path $full)) {
            $missingRequired += $full
        }
    }

    $vvmDir = Join-Path $dataDir "StreamingAssets\voicevox_core\models\vvms"
    $vvmFiles = @()
    if (Test-Path $vvmDir) {
        $vvmFiles = @(Get-ChildItem -Path $vvmDir -Filter "*.vvm" -File -ErrorAction SilentlyContinue)
    }
    if ($vvmFiles.Count -eq 0) {
        $missingRequired += "$vvmDir\*.vvm"
    }

    $dictDir = Join-Path $dataDir "StreamingAssets\voicevox_core\dict"
    $dictOk = $false
    if (Test-Path $dictDir) {
        $dictCandidates = @(Get-ChildItem -Path $dictDir -Directory -Filter "open_jtalk_dic*" -ErrorAction SilentlyContinue)
        if ($dictCandidates.Count -gt 0 -and (Test-Path (Join-Path $dictCandidates[0].FullName "sys.dic"))) {
            $dictOk = $true
        }
    }
    if (-not $dictOk) {
        $missingRequired += "$dictDir\open_jtalk_dic*\sys.dic"
    }

    if ($missingRequired.Count -gt 0) {
        Write-Host "ビルド成果物に voicevox_core 由来の必須ファイルが含まれていません（External/ は git 管理外のため、setup-external.ps1 の実行漏れの可能性があります）:" -ForegroundColor Red
        foreach ($m in $missingRequired) { Write-Host "  - $m" }
        Write-Host ""
        Write-Host "次の手順で External を配置してから再ビルドしてください:" -ForegroundColor Yellow
        Write-Host "  pwsh ./scripts/setup-external.ps1 -ExternalRoot <External/voicevox_core のパス> -SkipTsumugi" -ForegroundColor Yellow
        Write-Host "  pwsh ./scripts/build.ps1" -ForegroundColor Yellow
        return 1
    }

    Write-Host "voicevox_core 由来の必須ファイルの同梱を確認しました。" -ForegroundColor Green
    Write-Host "トップレベル項目はすべて許可リスト・既知の除外パターンで説明できることを確認しました。" -ForegroundColor Green

    # ---- THIRD-PARTY-NOTICES.txt の生成 --------------------------------------------

    $noticesPath = Join-Path $outputDir "THIRD-PARTY-NOTICES.txt"
    Write-Host ""
    Write-Host "----- THIRD-PARTY-NOTICES.txt を生成します -----" -ForegroundColor Cyan
    & "$PSScriptRoot/gen-third-party-notices.ps1" -OutputPath $noticesPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "THIRD-PARTY-NOTICES.txt の生成に失敗しました。" -ForegroundColor Red
        return 1
    }

    # issue #100 レビュー M-3: Unity Editor Software Terms Section 2.12 の帰属定型文に含まれる
    # 「Copyright © 2005-xxxx Unity Technologies.」の年が、生成時点の現在年になっているかを検証する。
    # gen-third-party-notices.ps1 が連結時に置換する仕様のため、通常は必ず一致するはずだが、
    # スクリプトの不具合や生成物の使い回しで古い年のまま配布されることを防ぐ安全策として検査する。
    $noticesContent = Get-Content -Path $noticesPath -Raw -Encoding UTF8
    $copyrightMatch = [regex]::Match($noticesContent, 'Copyright © 2005-(\d{4}) Unity Technologies\. All rights reserved\.')
    if (-not $copyrightMatch.Success) {
        Write-Host "THIRD-PARTY-NOTICES.txt に Unity 帰属定型文（Copyright © 2005-xxxx ...）が見つかりません。" -ForegroundColor Red
        return 1
    }
    $noticesYear = [int]$copyrightMatch.Groups[1].Value
    $currentYear = (Get-Date).Year
    if ($noticesYear -ne $currentYear) {
        Write-Host "THIRD-PARTY-NOTICES.txt の Unity 帰属定型文の年が現在年と一致しません（記載: $noticesYear 年 / 現在: $currentYear 年）。" -ForegroundColor Red
        return 1
    }

    # ---- ステージングフォルダの組み立て --------------------------------------------

    Write-Host ""
    Write-Host "----- ステージングフォルダを組み立てます -----" -ForegroundColor Cyan

    if (Test-Path $stagingDir) {
        Remove-Item -Path $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

    foreach ($name in $script:TopLevelItemsToCopy) {
        $source = Join-Path $buildDir $name
        if (-not (Test-Path $source)) {
            # MonoBleedingEdge は IL2CPP ビルドでは存在しないため、無ければスキップするだけにする
            # （Mono バックエンドでは必須。IL2CPP では逆に不要）。他の必須ファイルは前段の Test-Path で検証済み。
            Write-Host "  (スキップ: $name が見つかりません)" -ForegroundColor DarkGray
            continue
        }
        $destination = Join-Path $stagingDir $name
        if ((Get-Item $source).PSIsContainer) {
            Copy-Item -Path $source -Destination $destination -Recurse -Force
        } else {
            Copy-Item -Path $source -Destination $destination -Force
        }
        Write-Host "  コピー: $name"
    }

    Copy-Item -Path $noticesPath -Destination (Join-Path $stagingDir "THIRD-PARTY-NOTICES.txt") -Force

    # リポジトリのライセンス文書（LICENSE / NOTICE.md）。無ければ中止する。
    foreach ($licenseFileName in $script:RepositoryLicenseFiles) {
        $licenseSource = Join-Path $projectRoot $licenseFileName
        if (-not (Test-Path $licenseSource)) {
            Write-Host "リポジトリのライセンス文書が見つかりません。パッケージ作成を中止します: $licenseSource" -ForegroundColor Red
            Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $noticesPath -Force -ErrorAction SilentlyContinue
            return 1
        }
        Copy-Item -Path $licenseSource -Destination (Join-Path $stagingDir $licenseFileName) -Force
        Write-Host "  コピー: $licenseFileName"
    }

    # 利用者向け手順書（docs/manual/*.md）を HTML にして manual/ に入れる。
    $manualSourceDir = Join-Path $projectRoot $script:ManualSourceRelativeDir
    $manualDestinationDir = Join-Path $stagingDir $script:ManualDestinationDirName
    try {
        $manualPages = @(Export-ManualHtml -SourceDir $manualSourceDir -DestinationDir $manualDestinationDir)
    } catch {
        # 失敗した実行の残骸（作りかけのステージングと中間生成物）を成功物と取り違えないよう片付ける。
        Write-Host "手順書の HTML 化に失敗しました。パッケージ作成を中止します:" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
        Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $noticesPath -Force -ErrorAction SilentlyContinue
        return 1
    }
    foreach ($page in $manualPages) {
        Write-Host "  生成: $($script:ManualDestinationDirName)\$(Split-Path $page -Leaf)"
    }

    # 立ち絵の表情生成ツール（#219）。コードと設定だけを tools\tsumugi-expressions\ に入れる。
    $toolDestinationDir = Join-Path $stagingDir $script:ExpressionToolDestinationRelativeDir
    $toolProblems = @()
    try {
        $toolFiles = @(Export-ExpressionTool -ProjectRoot $projectRoot -DestinationDir $toolDestinationDir)
        $toolProblems = @(Get-ExpressionToolProblems -ToolDir $toolDestinationDir -ProjectRoot $projectRoot)
        $termsMissing = @(Get-TermsMissingFromBuild -DataDir $dataDir -TermsSourceDir (Join-Path $projectRoot $script:ExpressionToolTermsSourceRelativeDir))
        foreach ($name in $termsMissing) {
            $toolProblems += "ビルドに入っている規約の本文が $name と違います（規約を直したあとビルドし直していない可能性があります。scripts/build.ps1 を実行し直してください）"
        }
    } catch {
        $toolProblems = @($_.Exception.Message)
    }
    if ($toolProblems.Count -gt 0) {
        Write-Host "表情生成ツールの同梱に失敗しました。パッケージ作成を中止します:" -ForegroundColor Red
        foreach ($p in $toolProblems) { Write-Host "  - $p" -ForegroundColor Red }
        Remove-Item -Path $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $noticesPath -Force -ErrorAction SilentlyContinue
        return 1
    }
    foreach ($toolFile in $toolFiles) {
        Write-Host "  コピー: $($script:ExpressionToolDestinationRelativeDir)\$(Split-Path $toolFile -Leaf)"
    }

    # レビュー L2: $noticesPath（$outputDir 直下、Builds/THIRD-PARTY-NOTICES.txt）はステージングへ
    # コピーするための中間生成物に過ぎない。正本は zip 内と、生成元の Assets/.../Resources/*.txt
    # （git 管理下）なので、コピー後は削除して Builds/ 直下に紛らわしい重複を残さない。
    Remove-Item -Path $noticesPath -Force -ErrorAction SilentlyContinue

    $readmeContent = @"
つむぎクイズ (TsumugiQuiz) v$version
====================================

■ 手順書
くわしい手順は manual フォルダの手順書をブラウザで開いて読んでください
（ファイルをダブルクリックすると開きます）。

  manual\setup.html  セットアップ手順書（展開・初回起動・読み上げの確認・立ち絵・
                     ホスト役の準備・アップデート・アンインストール）
  manual\usage.html  操作手順書（画面ごとの操作・ルーム設定・問題エディタ・困ったとき）

■ 起動方法
zip をすべて展開してから、TsumugiQuiz.exe をダブルクリックして起動してください。
初めて起動すると利用規約への同意画面が出ます。

■ 遊び方
- ホスト役の 1 人が「ホストとして開始」を選び、「ホストを開始」を押すと参加コードが出ます。
- ほかの人は「参加コードで参加」を選び、その参加コードを入力してホストへ直接つなぎます。
- 常設サーバーやアカウント登録は不要です。
- 参加者全員が同じ zip を使ってください。ホストと zip（ビルド）が違うと
  「バージョンが異なります（ホスト: x / あなた: y）。」と出て参加できません。
  ビルド番号は「クレジット」画面のいちばん下で確かめられます。
- インターネット越しにつながらないときは、manual\setup.html の
  「ホスト役の準備」（ポート開放・Tailscale）を見てください。

■ 立ち絵（春日部つむぎ）について
権利上の理由（立ち絵の利用規約で二次配布が禁止されているため）により、
立ち絵は本パッケージに入っていません。立ち絵がなくてもクイズはすべて遊べます。
表示したい場合は、公式の配布元から各自で入手し、manual\setup.html の
「立ち絵を表示する」の手順で次のフォルダに置いてください。

  %USERPROFILE%\AppData\LocalLow\Tomonorarari-Think\TsumugiQuiz\tsumugi\

場面ごとの表情（9 種類）は、tools\tsumugi-expressions フォルダのツールで、
入手した立ち絵の zip から自分のパソコンで作れます（Python が必要です。先にアプリを起動して
立ち絵の規約を含む利用規約に同意しておく必要があります）。
手順は manual\setup.html の 6.4 と、tools\tsumugi-expressions\README.txt を見てください。
立ち絵の zip や作った画像を、このフォルダ（アプリのフォルダ）の中に置かないでください。

■ 音声合成（VOICEVOX）について
音声合成には VOICEVOX の音声ライブラリ「春日部つむぎ」を使用しています。
本パッケージには音声合成に必要なライブラリ・音声モデル一式を同梱済みのため、
追加のダウンロードや設定は不要です。

■ 権利表記
本アプリは VOICEVOX の音声ライブラリ「春日部つむぎ」を使用しています（VOICEVOX:春日部つむぎ）。
本アプリが利用する第三者ソフトウェア・素材の権利表記・ライセンス全文は
THIRD-PARTY-NOTICES.txt を参照してください。アプリ内の「クレジット」画面からも
同内容（要約表示）を確認できます。

本アプリの自作部分（ソースコード等）は MIT License です（LICENSE。適用範囲は NOTICE.md）。
同梱の Unity・voicevox_core・音声モデル・ONNX Runtime などは MIT License の対象外で、
THIRD-PARTY-NOTICES.txt に記載した各ライセンス・規約に従います。

■ 動作環境
Windows 64bit（Windows Standalone 版のみ配布しています）。
"@

    # レビュー L1: Windows のメモ帳等でも素直に開けるよう、改行を CRLF に統一する
    # （ここまでの here-string はソースファイル自体の改行コード（LF）をそのまま含むため）。
    $readmeContent = ($readmeContent -replace "`r`n", "`n") -replace "`n", "`r`n"

    Set-Content -Path (Join-Path $stagingDir "README.txt") -Value $readmeContent -Encoding UTF8

    Write-Host "  生成: THIRD-PARTY-NOTICES.txt"
    Write-Host "  生成: README.txt"

    # レビュー M-11: 古い zip の削除は、ここまでの検証（ビルド成果物の存在・トップレベル項目・
    # voicevox_core 必須ファイル・ステージング組み立て）がすべて通過した後、同梱禁止物検査の
    # 直前まで遅らせる。引数の指定ミス等で早期に return する失敗実行のたびに、既存の正しい
    # zip を消してしまわないようにするため（L3 の当初案は検証より前に削除していたが、
    # 早期失敗のたびに正常な既存 zip が失われる副作用があった）。
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
        Write-Host "既存の zip を削除しました（今回の実行が失敗した場合、古い zip を誤って成功物と誤認しないため）: $zipPath" -ForegroundColor DarkGray
    }

    # ---- 同梱禁止物の検査 ------------------------------------------------------------
    # docs/licenses.md §3 (d) 等（$script:ForbiddenNamePatterns 定義部のコメント参照。
    # このファイル名検査は補助的なセーフティネットであり、実質の担保ではない点に注意）。

    Write-Host ""
    Write-Host "----- 同梱禁止物の検査 -----" -ForegroundColor Cyan

    $forbidden = Get-ForbiddenStagingItems -StagingDirPath $stagingDir

    if ($forbidden.Files.Count -gt 0 -or $forbidden.Dirs.Count -gt 0) {
        Write-Host "同梱してはいけないファイル・フォルダが見つかりました。パッケージ作成を中止します:" -ForegroundColor Red
        foreach ($f in $forbidden.Files) { Write-Host "  - $($f.FullName)" }
        foreach ($d in $forbidden.Dirs) { Write-Host "  - $($d.FullName) (dir)" }
        Remove-Item -Path $stagingDir -Recurse -Force
        return 1
    }

    Write-Host "同梱禁止物（立ち絵原本・PSD・.pdb・Live2D 素材・Logs 等）は検出されませんでした。" -ForegroundColor Green

    # ---- zip 化 ------------------------------------------------------------------

    Write-Host ""
    Write-Host "----- zip を作成します -----" -ForegroundColor Cyan

    Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

    if (-not (Test-Path $zipPath)) {
        Write-Host "zip の作成に失敗しました: $zipPath" -ForegroundColor Red
        return 1
    }

    $zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

    Write-Host ""
    Write-Host "===== 配布パッケージの内容一覧 =====" -ForegroundColor Cyan
    $stagedFiles = @(Get-ChildItem -Path $stagingDir -Recurse -File)
    foreach ($f in ($stagedFiles | Sort-Object FullName)) {
        $relPath = $f.FullName.Substring($stagingDir.Length + 1)
        $sizeKB = [math]::Round($f.Length / 1KB, 1)
        Write-Host ("  {0} ({1} KB)" -f $relPath, $sizeKB)
    }

    Write-Host ""
    Write-Host "ファイル数: $($stagedFiles.Count)"
    Write-Host "zip: $zipPath ($zipSizeMB MB)" -ForegroundColor Green

    if ($KeepStaging) {
        Write-Host "ステージングフォルダを保持します: $stagingDir" -ForegroundColor DarkGray
    } else {
        Remove-Item -Path $stagingDir -Recurse -Force
        Write-Host "ステージングフォルダを削除しました（zip のみ保持）: $stagingDir" -ForegroundColor DarkGray
    }

    return 0
}

$script:ExitCode = 0
try {
    if ($SelfTest) {
        $script:ExitCode = Invoke-SelfTest
    } else {
        $script:ExitCode = Invoke-Main
    }
} catch {
    Write-Host "予期しないエラーで処理に失敗しました:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
    $script:ExitCode = 1
}

# レビュー L-9: $script:ExitCode は通常 [int] のスカラーだが、Invoke-Main / Invoke-SelfTest
# 内で将来コードが増えた際に、抑制し忘れた出力（パイプラインに漏れたオブジェクト）が
# 混ざって配列になってしまう事故を警戒し、末尾の要素（`return` した本来の終了コード）だけを
# 取り出して int にキャストしてから exit する。
exit ([int]($script:ExitCode | Select-Object -Last 1))
