# scripts/run-multi.ps1
# 同一 PC で Builds/Windows/TsumugiQuiz.exe を複数起動し、ホスト 1 + クライアント (Count-1) の
# マルチプロセス手動検証を行う（docs/dev-workflow.md §3.3、docs/network.md §10.3、issue #8）。
# 開発者のローカル環境専用のツールであり、配布物（Builds/ の zip 等）には含めない（L7）。
#
# 流れ:
#   1. ホストを -tq-host -tq-port 0（OS に空きポートを選ばせる）で起動する
#      （HostSetup 画面が起動時に自動でホストを開始し、AppPaths.DataRoot 配下へ join-code.txt を書き出す。
#      本スクリプトは -tq-data-root でホスト専用の一時データルートを明示指定する）
#   2. join-code.txt を読み取り（内容を参加コード形式の正規表現で検証してから使う。H-2）、
#      その参加コードでクライアントを (Count-1) 個起動する
#      （-tq-join <code>。各クライアントの Join 画面が起動時に自動で接続する。クライアントも
#      それぞれ専用の一時データルートを持つ。M-3）
#   3. -KillAfter が指定されていれば、その秒数だけ待ってから各プロセスのウィンドウを
#      Logs/multi/<name>.png としてスクリーンショットし（System.Drawing、失敗しても続行）、
#      全プロセスを終了する。未指定ならプロセスは起動したままにする（手動確認向け）。
#   スクリプト自体が失敗した場合は、-KillAfter の指定に関わらず起動済みの全プロセスを
#   Kill(true)（プロセスツリーごと）で強制終了してから終了する。Ctrl+C による中断は
#   PowerShell のホスト・実行状況によって finally が実行されないことがあり、必ず後始末できる
#   保証はない（L-8。その場合はタスクマネージャー等で手動終了すること）。
#
# 各プロセスのログは Logs/multi/<name>.log に分かれる。
#
# 使い方:
#   pwsh ./scripts/run-multi.ps1                      # ホスト1 + クライアント2（既定 Count=3）
#   pwsh ./scripts/run-multi.ps1 -Count 2              # ホスト1 + クライアント1
#   pwsh ./scripts/run-multi.ps1 -BuildFirst           # 起動前に scripts/build.ps1 を実行する
#   pwsh ./scripts/run-multi.ps1 -KillAfter 20         # 20秒後にスクリーンショット→全プロセス終了（自動検証向け）
#   pwsh ./scripts/run-multi.ps1 -Count 2 -KeepDataRoots  # データルートを使い回す（issue #15。#69 の
#                                                          # 再接続検証にはこれ単独では不十分。docs/dev-workflow.md §3.3 参照）
#   pwsh ./scripts/run-multi.ps1 -IsolateDocuments        # 問題フォルダ（Documents）もプロセスごとに隔離する（#112）
#   pwsh ./scripts/run-multi.ps1 -HostPort 7777            # ホストの待ち受けポートを固定する（既定 0 = OS 任せ。
#                                                          # スクリーンショット撮影時に実ポートを写さないため。#132）
#                                                          # ※ 固定すると、前回のホストが残っていると待ち受けに失敗する
#   pwsh ./scripts/run-multi.ps1 -BuildDir E:\Claude\tsumugi-quiz\Builds\verify\Windows
#                                                          # 固定パスのビルドを使う（実機確認のたびに
#                                                          # ファイアウォール許可ダイアログが出るのを防ぐ。#131。
#                                                          # docs/dev-workflow.md §3.3.1）
#   pwsh ./scripts/run-multi.ps1 -BuildDir <固定パス> -AllowForeignBuild
#                                                          # 固定パスのビルドが自分の worktree の
#                                                          # HEAD と異なる場合でもそのまま使う（既定は失敗。#131 H-1）
#
# 終了コード: 0 = 成功、非 0 = 失敗（M-8。ビルド失敗・join-code.txt 未検出・タイムアウト・
# ホストプロセスの異常終了等）。H-1: 本体を Invoke-RunMulti 関数に切り出し、その中の return は
# 関数からの復帰（finally 実行後、$exitCode を確実に呼び出し元へ返す）として扱われる。
# トップレベルの return はスクリプト全体を即座に終了させ、末尾の exit 文へ到達しない
# （finally は走るが exit コードが常に既定の 0 になってしまう）という罠を踏んだため（実測）。

[CmdletBinding()]
param(
    # ホスト込みの総プロセス数（2〜8）。
    [ValidateRange(2, 8)]
    [int]$Count = 3,

    [switch]$BuildFirst,

    # 0 以下（既定）ならプロセスを起動したままにする（手動確認向け）。
    [ValidateRange(0, 3600)]
    [int]$KillAfter = 0,

    # issue #15: 指定すると、実行のたびに各プロセスの一時データルート（session-token.json 等を
    # 含む）を作り直さずに保持する。issue #15 レビュー H-2: これ単独では #69（同名での切断→再接続）を
    # 再現できない（ホストは -tq-port 0 で毎回別ポートになり、再接続トークンは接続先の
    # アドレス:ポート をキーに保存されるため）。#69 の手動検証手順は docs/dev-workflow.md §3.3 を参照。
    [switch]$KeepDataRoots,

    # #112: 指定すると、各プロセスの Documents ルート（問題フォルダ Documents\TsumugiQuiz\Questions\ ・
    # プリセットフォルダの親）を Logs/multi/documents-<役割> へ隔離し、-tq-documents-root で渡す。
    # 実ユーザーの Documents に一切触れずに検証したいとき（並行して PlayMode テストを回している等）に使う。
    # 既定（未指定）は実ユーザーの Documents をそのまま使う（従来どおりの挙動）。
    # -KeepDataRoots と併用すると、隔離先の Documents ルートも作り直さずに保持する（L-3）。
    [switch]$IsolateDocuments,

    [ValidateRange(160, 3840)]
    [int]$WindowWidth = 640,

    [ValidateRange(120, 2160)]
    [int]$WindowHeight = 480,

    # join-code.txt の書き出しを待つ最大秒数。
    [ValidateRange(1, 600)]
    [int]$JoinCodeTimeoutSeconds = 60,

    # issue #131: ビルド済み exe (TsumugiQuiz.exe) を探すフォルダ。既定は自 worktree の
    # Builds\Windows。scripts/package-release.ps1 の -BuildDir と引数名・意味を揃えてある。
    # 固定パス（例: E:\Claude\tsumugi-quiz\Builds\verify\Windows）を指定すると、
    # scripts/build.ps1 -OutputDir <同じ固定パス> でビルドした実行ファイルをそのまま使える
    # （実機確認のたびに Windows ファイアウォールの許可ダイアログが出るのを防ぐ。
    # docs/dev-workflow.md §3.3.1）。
    [string]$BuildDir,

    # issue #132: ホストの待ち受けポート。既定 0（OS に空きポートを選ばせる。従来どおり）。
    # README 等のスクリーンショットを撮るときは 7777 のような固定値を指定し、撮影機の実際の
    # 待ち受けポートが参加コードに符号化されて写り込まないようにする（#36 / #105 の撮影手順）。
    [ValidateRange(0, 65535)]
    [int]$HostPort = 0,

    # issue #131 レビュー H-1: -BuildDir 指定時、対象フォルダの .build-info.json に記録された
    # gitCommit が自分の worktree の現在の HEAD と一致しない場合は既定で失敗する（固定パスは
    # 複数 worktree の共有資源であり、他 worktree が最後にビルドしたものを自分の実機証跡と
    # 誤認する事故を防ぐため）。意図的に他 worktree のビルドをそのまま使う場合のみ指定する。
    [switch]$AllowForeignBuild
)

. "$PSScriptRoot/common.ps1"

# H-2: ホストが書き出す join-code.txt の内容を検証する正規表現。
# L-3: docs/network-joincode.md §1.3 の Crockford Base32 アルファベット
# （"0123456789ABCDEFGHJKMNPQRSTVWXYZ"、I/L/O/U を含まない）に合わせ、
# 大文字小文字を区別する -cmatch で検証する（-match は既定で大文字小文字を区別しないため）。
$script:JoinCodePattern = '^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){2}$'

function Test-JoinCode {
    param([string]$Code)
    return $Code -and ($Code -cmatch $script:JoinCodePattern)
}

# M-3: 一時データルートを作り直し、通常起動時のデータルートに consent.json があれば
# それをコピーして引き継ぐ（同意ゲート #37 は迂回しない。参照するだけで代行・偽装はしない）。
# ホスト・クライアントの両方（それぞれ別の一時データルートを持つ。M-3）に適用する共通関数。
# issue #15 レビュー LOW: -KeepExisting（-KeepDataRoots 由来）を指定した場合は、既存のデータルートが
# あれば作り直さず・consent.json も再コピーせずそのまま使う分岐に入る（詳細は $KeepExisting の
# 各分岐を参照）。
function Initialize-ProcessDataRoot {
    param(
        [Parameter(Mandatory = $true)][string]$DataRootPath,
        # issue #15: -KeepDataRoots 指定時は、既存のデータルート（あれば）をそのまま使い続ける
        # （session-token.json 等を消さない。#69 の再接続を run-multi.ps1 の複数回実行で
        # 手動検証できるようにするため）。
        [switch]$KeepExisting,
        # issue #15 レビュー LOW: 通常起動時のデータルートの解決結果。全プロセスで同じ値になるため、
        # 呼び出し元（Invoke-RunMulti）で 1 回だけ解決・ログ出力し、ここでは受け取るだけにする。
        [Parameter(Mandatory = $true)][AllowNull()][object]$DefaultDataRootInfo
    )

    $dataRootAlreadyExists = Test-Path $DataRootPath
    if ($KeepExisting -and $dataRootAlreadyExists) {
        Write-Host "-KeepDataRoots: 既存のデータルートを保持します: $DataRootPath" -ForegroundColor Cyan
    } else {
        if ($dataRootAlreadyExists) {
            Remove-Item $DataRootPath -Recurse -Force -ErrorAction Stop
        }
        New-Item -ItemType Directory -Force -Path $DataRootPath | Out-Null
    }

    if ($null -eq $DefaultDataRootInfo) {
        Write-Warning "通常起動時のデータルートを特定できませんでした（ProjectSettings.asset の companyName/productName が読めません）。'$DataRootPath' への同意記録の引き継ぎをスキップします。"
        return
    }

    $defaultDataRoot = $DefaultDataRootInfo.Path
    $defaultConsentPath = Join-Path $defaultDataRoot "consent.json"
    $targetConsentPath = Join-Path $DataRootPath "consent.json"

    if ($KeepExisting -and (Test-Path $targetConsentPath)) {
        Write-Host "-KeepDataRoots: 既存の同意記録を保持します: $targetConsentPath" -ForegroundColor Cyan
        return
    }

    if (Test-Path $defaultConsentPath) {
        Copy-Item $defaultConsentPath $targetConsentPath -Force
        Write-Host "同意記録 (consent.json) を引き継ぎました: $defaultConsentPath -> $DataRootPath" -ForegroundColor Cyan
    } else {
        Write-Warning "同意記録 (consent.json) が見つかりません ($defaultConsentPath)。'$DataRootPath' を使うプロセスは利用規約同意画面（Terms）で停止し、-tq-host / -tq-join による自動化は行われません。先に一度アプリを通常起動して同意してください。"
    }
}

# #112: -IsolateDocuments 用。プロセス専用の Documents ルートを作り直し、
# 問題フォルダ（<ルート>\TsumugiQuiz\Questions）へサンプル問題セットを複製する。
# 複製元は QuestionLibrary が初回起動時に書き出すのと同じ Resources のサンプル
# （Assets/TsumugiQuiz/Resources/Questions/sample-questions.json + sample-image.bytes）。
# 画像 (images/sample.png) も一緒に置かないと imagePath の検証に失敗してセットごとスキップされる。
function Initialize-ProcessDocumentsRoot {
    param(
        [Parameter(Mandatory = $true)][string]$DocumentsRootPath,
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        # PR #118 レビュー L-3: -KeepDataRoots はデータルートと同様に Documents ルートも保持する
        # （利用者が隔離先の問題セットを編集して、次の実行でも使いたい場合があるため）。
        [switch]$KeepExisting
    )

    if ($KeepExisting -and (Test-Path $DocumentsRootPath)) {
        Write-Host "-KeepDataRoots: 既存の Documents ルートを保持します: $DocumentsRootPath" -ForegroundColor Cyan
        return
    }

    if (Test-Path $DocumentsRootPath) {
        Remove-Item $DocumentsRootPath -Recurse -Force -ErrorAction Stop
    }

    $questionsDir = Join-Path $DocumentsRootPath "TsumugiQuiz\Questions"
    $imagesDir = Join-Path $questionsDir "images"
    New-Item -ItemType Directory -Force -Path $imagesDir | Out-Null

    $sampleJson = Join-Path $ProjectRoot "Assets\TsumugiQuiz\Resources\Questions\sample-questions.json"
    $sampleImage = Join-Path $ProjectRoot "Assets\TsumugiQuiz\Resources\Questions\sample-image.bytes"

    if (-not (Test-Path $sampleJson)) {
        throw "サンプル問題セットが見つかりません: $sampleJson"
    }
    if (-not (Test-Path $sampleImage)) {
        throw "サンプル画像が見つかりません: $sampleImage"
    }

    Copy-Item $sampleJson (Join-Path $questionsDir "sample-questions.json") -Force
    Copy-Item $sampleImage (Join-Path $imagesDir "sample.png") -Force

    Write-Host "-IsolateDocuments: Documents ルートを隔離しました: $DocumentsRootPath（サンプル問題セットを複製）" -ForegroundColor Cyan
}

# System.Diagnostics.ProcessStartInfo.ArgumentList（配列）を使う（Start-Process -ArgumentList の
# 文字列連結はエスケープが崩れやすいため使わない。docs/dev-workflow.md §5 の罠）。
function Start-TsumugiProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$ExtraArguments,
        [Parameter(Mandatory = $true)][string]$ExePath,
        [Parameter(Mandatory = $true)][string]$LogsDir
    )

    $logPath = Join-Path $LogsDir "$Name.log"
    if (Test-Path $logPath) {
        Remove-Item $logPath -Force -ErrorAction Stop
    }

    $arguments = @("-logFile", $logPath) + $ExtraArguments

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $ExePath
    $startInfo.UseShellExecute = $false
    foreach ($arg in $arguments) {
        # LOW: ArgumentList は string のコレクションのため、数値パラメータも明示的に文字列化して渡す。
        $startInfo.ArgumentList.Add([string]$arg)
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()

    Write-Host "起動: $Name (PID $($process.Id))"
    return $process
}

# 複数プロセスのウィンドウをディスプレイ幅・高さで折り返してグリッド状に並べる（L-6）。
# System.Windows.Forms が使えない環境（headless 等）では 1920x1080 相当を仮定してフォールバックする。
# 1 画面に収まる数（列数 x 行数）を超える分は、同じマス目へ折り返して重ねて配置する
# （手動検証用の簡易配置のため、Count が既定の上限 8 程度であれば実用上問題にならない）。
function Get-PrimaryDisplaySize {
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        return [pscustomobject]@{ Width = $bounds.Width; Height = $bounds.Height }
    } catch {
        Write-Warning "プライマリディスプレイのサイズを取得できませんでした。既定値 1920x1080 を仮定します: $_"
        return [pscustomobject]@{ Width = 1920; Height = 1080 }
    }
}

function Get-WindowGridPosition {
    param(
        [int]$Index,
        [int]$ColumnsPerRow,
        [int]$RowsPerScreen,
        [int]$CellWidth,
        [int]$CellHeight
    )

    $cellsPerScreen = [math]::Max(1, $ColumnsPerRow * $RowsPerScreen)
    $wrappedIndex = $Index % $cellsPerScreen
    $column = $wrappedIndex % $ColumnsPerRow
    $row = [math]::Floor($wrappedIndex / $ColumnsPerRow)
    return [pscustomobject]@{ X = $column * $CellWidth; Y = $row * $CellHeight }
}

# H-1: 本体を関数化する。トップレベルスクリプトで try 内に return を書くと、finally は実行
# されるものの、スクリプト自体がそこで終了してしまい、末尾の exit 文（呼び出し元へ終了コードを
# 伝える箇所）に到達しない（実測: タイムアウト時に $exitCode = 1 を設定していても、
# 呼び出し元から見た終了コードは 0 のままだった）。関数内の return は正しく
# 「finally を実行してから関数を抜ける」ため、この問題が起きない。
function Invoke-RunMulti {
    $projectRoot = Get-ProjectRoot

    # issue #131: -BuildDir 未指定時は既定どおり自 worktree の Builds\Windows を使う。
    $buildDirExplicit = -not [string]::IsNullOrWhiteSpace($BuildDir)
    $resolvedBuildDir = if ($buildDirExplicit) {
        Resolve-FullPathAllowMissing -Path $BuildDir
    } else {
        Join-Path $projectRoot "Builds\Windows"
    }
    $exePath = Join-Path $resolvedBuildDir "TsumugiQuiz.exe"

    if ($BuildFirst) {
        Write-Host "===== -BuildFirst: scripts/build.ps1 を実行 =====" -ForegroundColor Cyan
        if ($buildDirExplicit) {
            & "$PSScriptRoot/build.ps1" -OutputDir $resolvedBuildDir
        } else {
            & "$PSScriptRoot/build.ps1"
        }
        if ($LASTEXITCODE -ne 0) {
            throw "scripts/build.ps1 が失敗しました（終了コード $LASTEXITCODE）。"
        }
    }

    if (-not (Test-Path $exePath)) {
        $buildHint = if ($buildDirExplicit) { "pwsh ./scripts/build.ps1 -OutputDir `"$resolvedBuildDir`"" } else { "pwsh ./scripts/build.ps1" }
        throw "ビルド済み exe が見つかりません: $exePath （先に $buildHint を実行するか -BuildFirst を指定してください）"
    }

    # issue #131 レビュー H-1/M-1/M-2/L-1: -BuildDir 指定時のみ、固定パスのビルドが自分の
    # worktree のものかどうかを Test-BuildProvenance（scripts/common.ps1）で検証する。
    # 固定パスは複数 worktree の共有資源であり、出所を確認しないと他 worktree が最後に
    # ビルドしたバイナリを自分の実機証跡と誤認しうる。検証できない場合（.build-info.json が
    # 無い・壊れている・gitCommit が取得できない）も、不一致と同様に既定で失敗する（M-1）。
    if ($buildDirExplicit) {
        $provenance = Test-BuildProvenance -BuildDir $resolvedBuildDir -ProjectRoot $projectRoot -AllowForeignBuild:$AllowForeignBuild

        if ($provenance.BuildInfo) {
            Write-Host ("ビルド出所: gitCommit={0} gitBranch={1} sourceWorktree={2} builtAtUtc={3} dirty={4}" -f `
                $provenance.BuildInfo.gitCommit, $provenance.BuildInfo.gitBranch, $provenance.BuildInfo.sourceWorktree, `
                $provenance.BuildInfo.builtAtUtc, [bool]$provenance.BuildInfo.dirty) -ForegroundColor DarkGray
        }

        foreach ($w in $provenance.Warnings) {
            Write-Warning $w
        }

        if (-not $provenance.Allowed) {
            throw "固定パス ($resolvedBuildDir) の出所を検証できないか、自分の worktree（$projectRoot）と一致しません（理由: $($provenance.Reason)）。自分の worktree から `"pwsh ./scripts/run-multi.ps1 -BuildFirst -BuildDir `"$resolvedBuildDir`"`" 等で再ビルドしてから実行するか、意図的に他 worktree のビルドを使う場合は -AllowForeignBuild を指定してください。"
        }
    }

    # issue #15 レビュー LOW、issue #131 レビュー H-2 で文言更新: 前回実行等の TsumugiQuiz.exe が
    # 残っていると、本スクリプトが起動するプロセスと見分けが付かなくなったり（PID・ウィンドウの
    # 取り違え）、意図せずポートやファイルを取り合ったりするおそれがある。処理は続行するが、
    # 事前に警告だけ出す。固定パス運用では他 worktree の検証プロセスが同じ実行ファイルパスで
    # 動いていることが常態のため、実行ファイルパスによる識別はできない。**自分が起動していない
    # プロセスは終了しないこと。** 自分が起動したプロセスの PID は、本スクリプトが後段
    # （起動直後・「起動: <name> (PID ...)」、および「起動したプロセス」一覧）で出力する。
    $existingProcesses = Get-Process -Name "TsumugiQuiz" -ErrorAction SilentlyContinue
    if ($existingProcesses) {
        $existingPids = ($existingProcesses | ForEach-Object { $_.Id }) -join ", "
        Write-Warning "既に起動中の TsumugiQuiz.exe プロセスがあります（PID: $existingPids）。他 worktree の検証プロセスの可能性があるため、自分が起動していないプロセスは終了しないこと。自分の PID は本スクリプトが後段で出力する。"
    }

    $logsDir = Join-Path $projectRoot "Logs\multi"
    New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

    # M-5: 通常起動時のデータルート解決は scripts/common.ps1 の Resolve-AppDataRoot
    # （scripts/setup-external.ps1 と共有）に統合してある。ここでは明示引数を渡さないため、
    # 環境変数 TSUMUGI_DATA_ROOT > ProjectSettings.asset の companyName/productName の順で解決される。
    # issue #15 レビュー LOW: 全プロセスで同じ値になるため、ここで 1 回だけ解決・ログ出力する
    # （以前は Initialize-ProcessDataRoot 内でプロセスごとに毎回ログしていた）。
    $defaultDataRootInfo = Resolve-AppDataRoot
    if ($null -eq $defaultDataRootInfo) {
        Write-Warning "通常起動時のデータルートを特定できませんでした（ProjectSettings.asset の companyName/productName が読めません）。同意記録の引き継ぎをスキップします。"
    } else {
        # issue #15 レビュー L: どの優先順位（Explicit/Environment/ProjectSettings）で解決されたかを
        # ログする。同意記録の引き継ぎ元を取り違えていないか実行時に確認できるようにするため。
        Write-Host "通常起動時のデータルート解決元: $($defaultDataRootInfo.Source)（$($defaultDataRootInfo.Path)）" -ForegroundColor DarkGray
    }

    # ホストの一時データルート。実行のたびに作り直し、通常起動時の consent.json を引き継ぐ。
    # M-4（既知の制約、正確な説明）: -tq-data-root は AppPaths.DataRoot 全体（consent.json だけでなく
    # session-token.json・TtsCache 等も含む）を切り替える。一方、HostSetup / Join 画面が読み書きする
    # PlayerPrefs（プレイヤー名・ポート・host.role 等、Windows ではレジストリ）は分離されない。
    # 同一 PC で本スクリプトを繰り返し実行すると、各プロセスの PlayerPrefs に最後に使った値
    # （-tq-name Host 等）が残り続けるが、-tq-name / -tq-port は常に CLI 引数が優先されるため
    # 動作には影響しない（詳細は docs/dev-workflow.md §3.3 を参照）。
    $dataRoot = Join-Path $logsDir "data-root"
    Initialize-ProcessDataRoot -DataRootPath $dataRoot -KeepExisting:$KeepDataRoots -DefaultDataRootInfo $defaultDataRootInfo
    $joinCodePath = Join-Path $dataRoot "join-code.txt"

    # H-1（レビュー実測: -KeepDataRoots 指定時、前回実行の join-code.txt が残ったまま新しいホストを
    # 起動すると、ホストがまだ書き出す前に「古い」参加コードを読んでクライアントを起動してしまうことがある。
    # ホストは -tq-port 0 のため実際のポートは毎回変わり、古いコードで接続を試みたクライアントは
    # 接続できずロビーに到達しない）。-KeepDataRoots の有無に関わらず、ホスト起動前に必ず削除する
    # （データルート自体・consent.json・session-token.json は削除しない。join-code.txt のみ）。
    if (Test-Path $joinCodePath) {
        Remove-Item $joinCodePath -Force -ErrorAction Stop
        Write-Host "前回実行の参加コードファイルを削除しました: $joinCodePath" -ForegroundColor DarkGray
    }

    $displaySize = Get-PrimaryDisplaySize
    $columnsPerRow = [math]::Max(1, [math]::Floor($displaySize.Width / $WindowWidth))
    $rowsPerScreen = [math]::Max(1, [math]::Floor($displaySize.Height / $WindowHeight))

    $exitCode = 0
    $processes = @()
    # true になったら finally での強制終了をスキップする
    # （正常完了で -KillAfter 未指定 = 起動したままにする意図的な状態、または既に自前で片付け済みの状態）。
    $leaveProcessesRunning = $false

    try {
        # --- ホスト起動 ---
        $hostPosition = Get-WindowGridPosition -Index 0 -ColumnsPerRow $columnsPerRow -RowsPerScreen $rowsPerScreen -CellWidth $WindowWidth -CellHeight $WindowHeight
        $hostArgs = @(
            "-tq-host"
            "-tq-port", "$HostPort"
            "-tq-name", "Host"
            "-tq-data-root", $dataRoot
            "-tq-window", "$($hostPosition.X),$($hostPosition.Y),$WindowWidth,$WindowHeight"
            "-screen-width", $WindowWidth
            "-screen-height", $WindowHeight
            "-screen-fullscreen", "0"
        )
        if ($IsolateDocuments) {
            $hostDocumentsRoot = Join-Path $logsDir "documents-Host"
            Initialize-ProcessDocumentsRoot -DocumentsRootPath $hostDocumentsRoot -ProjectRoot $projectRoot -KeepExisting:$KeepDataRoots
            $hostArgs += @("-tq-documents-root", $hostDocumentsRoot)
        }
        # H-1: ホスト起動直前の時刻を記録し、ポーリングでは「この時刻より後に書かれたファイル」だけを
        # 有効な参加コードとして採用する（削除漏れ・書き込み途中の他プロセスの古いファイル等を
        # 誤って読まないための二重の安全策。上の事前削除と合わせて、古いコードを読む経路を塞ぐ）。
        $hostStartedUtc = [DateTime]::UtcNow
        $hostProcess = Start-TsumugiProcess -Name "Host" -ExtraArguments $hostArgs -ExePath $exePath -LogsDir $logsDir
        $processes += [pscustomobject]@{ Name = "Host"; Process = $hostProcess }

        # --- join-code.txt の書き出しを待つ（H-2: 正規表現で検証し、不一致なら再ポーリングする） ---
        Write-Host "参加コード ($joinCodePath) の書き出しを待機しています..." -ForegroundColor Cyan
        $joinCode = $null
        $elapsedSeconds = 0
        while ($elapsedSeconds -lt $JoinCodeTimeoutSeconds) {
            # L-5: ホストが起動直後にクラッシュ等で終了した場合、タイムアウトいっぱい待たず即座に失敗させる。
            if ($hostProcess.HasExited) {
                throw "ホストプロセス (PID $($hostProcess.Id)) が参加コードを書き出す前に終了しました（終了コード $($hostProcess.ExitCode)）。Logs/multi/Host.log を確認してください。"
            }

            $fileInfo = Get-Item -LiteralPath $joinCodePath -ErrorAction SilentlyContinue
            # H-1: LastWriteTimeUtc がホスト起動時刻より後のものだけを採用する。
            if ($fileInfo -and $fileInfo.LastWriteTimeUtc -gt $hostStartedUtc) {
                $candidate = $null
                try {
                    $candidate = (Get-Content $joinCodePath -Raw -ErrorAction Stop).Trim()
                } catch {
                    # 書き込み途中（File.Move 前後の一瞬）に読めないことがあるため、次のポーリングへ回す。
                    $candidate = $null
                }

                if (Test-JoinCode $candidate) {
                    $joinCode = $candidate
                    break
                }
            }

            Start-Sleep -Seconds 1
            $elapsedSeconds++
        }

        if (-not $joinCode) {
            # H-1: ここは throw にする（return だとトップレベル関数の外側にある `exit $exitCode` に
            # 依存せずに済むが、念のため throw で catch 経由の $exitCode = 1 に統一しておく）。
            throw "有効な参加コードが ${JoinCodeTimeoutSeconds}秒以内に書き出されませんでした（形式: $script:JoinCodePattern）。Logs/multi/Host.log を確認してください。"
        }

        Write-Host "参加コード: $joinCode" -ForegroundColor Green

        # --- クライアント起動 ---
        for ($i = 1; $i -lt $Count; $i++) {
            $clientName = "Client$i"

            # M-3: クライアントごとに専用の一時データルートを持たせ、それぞれへ consent.json を引き継ぐ
            # （ホストと同じデータルートを共有すると、複数プロセスが同じ consent.json /
            # session-token.json / TtsCache を同時に読み書きして競合するおそれがあるため）。
            $clientDataRoot = Join-Path $logsDir "data-root-$clientName"
            Initialize-ProcessDataRoot -DataRootPath $clientDataRoot -KeepExisting:$KeepDataRoots -DefaultDataRootInfo $defaultDataRootInfo

            $position = Get-WindowGridPosition -Index $i -ColumnsPerRow $columnsPerRow -RowsPerScreen $rowsPerScreen -CellWidth $WindowWidth -CellHeight $WindowHeight
            $clientArgs = @(
                "-tq-join", $joinCode
                "-tq-name", $clientName
                "-tq-data-root", $clientDataRoot
                "-tq-window", "$($position.X),$($position.Y),$WindowWidth,$WindowHeight"
                "-screen-width", $WindowWidth
                "-screen-height", $WindowHeight
                "-screen-fullscreen", "0"
            )
            if ($IsolateDocuments) {
                # クライアントも同じサンプルを持たせる（出題はホストの問題データで進むが、
                # HostSetup / QuestionEditor 画面が実ユーザーの Documents を作らないようにするため）。
                $clientDocumentsRoot = Join-Path $logsDir "documents-$clientName"
                Initialize-ProcessDocumentsRoot -DocumentsRootPath $clientDocumentsRoot -ProjectRoot $projectRoot -KeepExisting:$KeepDataRoots
                $clientArgs += @("-tq-documents-root", $clientDocumentsRoot)
            }
            $clientProcess = Start-TsumugiProcess -Name $clientName -ExtraArguments $clientArgs -ExePath $exePath -LogsDir $logsDir
            $processes += [pscustomobject]@{ Name = $clientName; Process = $clientProcess }

            # 同時多発の接続試行による輻輳を避けるための小さな間隔。
            Start-Sleep -Milliseconds 500
        }

        Write-Host ""
        Write-Host "===== 起動したプロセス: $($processes.Count) 件 =====" -ForegroundColor Cyan
        $processes | ForEach-Object { Write-Host ("  {0}: PID {1}" -f $_.Name, $_.Process.Id) }

        if ($KillAfter -le 0) {
            Write-Host "-KillAfter 未指定のため、プロセスは起動したままです。確認後に手動で終了してください。" -ForegroundColor Yellow
            $leaveProcessesRunning = $true
            return $exitCode
        }

        Write-Host "${KillAfter} 秒待ってからスクリーンショットを撮り、全プロセスを終了します..." -ForegroundColor Cyan
        Start-Sleep -Seconds $KillAfter

        . "$PSScriptRoot/window-capture.ps1"
        foreach ($entry in $processes) {
            $screenshotPath = Join-Path $logsDir "$($entry.Name).png"
            try {
                Save-ProcessWindowScreenshot -Process $entry.Process -OutputPath $screenshotPath
            } catch {
                Write-Warning "$($entry.Name) のスクリーンショットに失敗しました: $_"
            }
        }

        foreach ($entry in $processes) {
            try {
                if (-not $entry.Process.HasExited) {
                    $entry.Process.Kill($true)
                    [void]$entry.Process.WaitForExit(10000)
                }
            } catch {
                Write-Warning "$($entry.Name) (PID $($entry.Process.Id)) の終了に失敗しました: $_"
            }
        }

        # 上で明示的に後始末済みなので、finally での二重終了は不要（無害だが冗長）。
        $leaveProcessesRunning = $true

        Write-Host "全プロセスを終了しました。" -ForegroundColor Green
        return $exitCode
    }
    catch {
        Write-Error "run-multi.ps1 が失敗しました: $_"
        $exitCode = 1
        return $exitCode
    }
    finally {
        # H-1: 失敗時（Ctrl+C は L-8 の注記のとおり保証外）は、-KillAfter の指定に関わらず
        # 起動済みプロセスをプロセスツリーごと強制終了する（孤児プロセスを残さないため）。
        if (-not $leaveProcessesRunning) {
            foreach ($entry in $processes) {
                try {
                    if ($entry.Process -and -not $entry.Process.HasExited) {
                        Write-Warning "後始末: $($entry.Name) (PID $($entry.Process.Id)) を強制終了します。"
                        $entry.Process.Kill($true)
                        [void]$entry.Process.WaitForExit(10000)
                    }
                } catch {
                    Write-Warning "$($entry.Name) の後始末に失敗しました: $_"
                }
            }
        }
    }
}

exit (Invoke-RunMulti)
