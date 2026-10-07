# scripts/tests/log-scan.tests.ps1
# scripts/log-scan.ps1 の判定ロジックに対する回帰テスト（擬似ログ行ベース）。
#
# 実行:
#   pwsh ./scripts/tests/log-scan.tests.ps1
#
# Pester 5 系がインストールされていればそれを使い、無ければ素の PowerShell アサーションに
# フォールバックして exit 1 で失敗を返す（#61）。
# 開発機に Windows PowerShell 同梱の Pester 3.4 系がある場合、構文非互換（Should -Be 等）で
# 誤判定するおそれがあるため、意図的に「5 系のみ」を対象にしている。詳細は docs/dev-workflow.md。

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/../log-scan.ps1"

# ---- ケース定義 ------------------------------------------------------------
# ヒットすべき例（実際の Unity ログから採取したもの、または issue #61 記載の対象カテゴリを代表する例）
$hitCases = @(
    [pscustomobject]@{
        Name = "コンパイルエラー(error CS)"
        Line = 'Assets\TsumugiQuiz\Tests\EditMode\TempCompileErrorProbe.cs(9,38): error CS1002: ; expected'
    }
    [pscustomobject]@{
        Name = "コンパイル失敗の確定行"
        Line = 'Scripts have compiler errors.'
    }
    [pscustomobject]@{
        Name = "Error: で始まる行"
        Line = 'Error: Failed to load assembly definition file'
    }
    [pscustomobject]@{
        Name = "ERROR で始まる行"
        Line = 'ERROR: Unrecoverable failure during batch run'
    }
    [pscustomobject]@{
        Name = "例外型名+コロンが行頭"
        Line = 'System.InvalidOperationException: Something went wrong'
    }
    [pscustomobject]@{
        Name = "Failed to で始まる行"
        Line = 'Failed to resolve package dependencies'
    }
    [pscustomobject]@{
        Name = "UnityException"
        Line = 'UnityException: Load of AssetBundle failed'
    }
    [pscustomobject]@{
        Name = "Aborting batchmode"
        Line = 'Aborting batchmode due to fatal error: '
    }
    [pscustomobject]@{
        Name = "TtsEngineFactoryGuardSetUp の発火（#156）"
        Line = '[TtsEngineFactoryGuardSetUp] TtsService が engineFactory 省略のまま本番実装（voicevox_core）へフォールバックしようとしました。EditMode は External/ 非依存（docs/tts.md §11.1）。呼び出し元のテストで TtsService.DefaultEngineFactoryOverrideForTesting をフェイクエンジンへ明示的に差し替えること（#156）。'
    }
)

# 無視すべき例（実際の Unity ログ Logs/verify-EditMode.log / Logs/verify-PlayMode.log から採取。
# scripts/log-scan.ps1 のコメントに記載の実測結果と対応する）
$ignoreCases = @(
    [pscustomobject]@{
        Name = "テストランナーのスタックトレース行(汎用)"
        Line = 'UnityEngine.TestTools.EnumerableTestMethodCommand/<ExecuteEnumerableAndRecordExceptions>d__4:MoveNext () (at ./Library/PackageCache/com.unity.test-framework@7a3849e09bd0/UnityEngine.TestRunner/NUnitExtensions/Commands/EnumerableTestMethodCommand.cs:85)'
    }
    [pscustomobject]@{
        Name = "テストメソッド名に Failed を含むスタックトレース行"
        Line = 'TsumugiQuiz.Tests.EditMode.Network.Nat.PortMappingServiceTests/<MapAsync_WhenDeviceThrowsUnexpectedException_ReturnsFailed>d__11:MoveNext () (at Assets/TsumugiQuiz/Tests/EditMode/Network/Nat/PortMappingServiceTests.cs:220)'
    }
    [pscustomobject]@{
        Name = "例外型を引数に持つメソッドシグネチャ（過去の誤検知の原因、PR #22/#25/#18 相当）"
        Line = 'SomeClass:Handler (System.Exception ex)'
    }
    [pscustomobject]@{
        Name = ".NET 形式スタックトレース継続行（IL オフセット付き）"
        Line = '  at TsumugiQuiz.Tests.Shared.Tts.FakeTtsSynthesisEngine.SynthesizeAsync (System.String text, System.Single speed, System.Threading.CancellationToken cancellationToken) [0x000c5] in E:\Claude\tsumugi-quiz\.claude\worktrees\i67\Assets\TsumugiQuiz\Tests\Shared\Tts\FakeTtsSynthesisEngine.cs:64'
    }
    [pscustomobject]@{
        Name = "LogAssert.Expect で想定済みの意図的エラーログ(PortMappingService)"
        Line = '[PortMappingService] ポートマッピングを作成できませんでした: TsumugiQuiz.Network.Nat.NatDeviceException: 拒否'
    }
    [pscustomobject]@{
        Name = "LogAssert.Expect で想定済みの意図的エラーログ(TtsService)"
        Line = '[TtsService] 合成に失敗しました（音声合成、key=92c2289df54565cbd00b2ae923c5bccc）: InvalidOperationException: 合成できません（テスト）。'
    }
    [pscustomobject]@{
        Name = "既知の無害な行(ライセンストークン警告)"
        Line = '[Licensing::Module] Error: Access token is unavailable; failed to update'
    }
    [pscustomobject]@{
        Name = "ジェネリック型引数にException/Errorを含むメソッドシグネチャ"
        Line = 'UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface:EmitExceptionAsError<UnityEditor.Scripting.ScriptCompilation.EditorCompilation/CompileStatus> (System.Func`1<UnityEditor.Scripting.ScriptCompilation.EditorCompilation/CompileStatus>,UnityEditor.Scripting.ScriptCompilation.EditorCompilation/CompileStatus)'
    }
)

# ---- 実行本体 ----------------------------------------------------------------
function Invoke-LogScanLineCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Line
    )

    if (Test-LogScanIsStackTraceLine -Line $Line) { return $false }
    if (-not (Test-LogScanIsErrorLine -Line $Line)) { return $false }

    foreach ($pattern in $script:LogScanIgnorePatterns) {
        if ($Line -match $pattern) { return $false }
    }

    return $true
}

$pesterModule = Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -ge [version]"5.0.0" } | Select-Object -First 1

if ($pesterModule) {
    Write-Host "Pester $($pesterModule.Version) を使用してテストを実行します。" -ForegroundColor Cyan
    Import-Module $pesterModule -Force

    $container = New-PesterContainer -ScriptBlock {
        Describe "Test-LogScanIsErrorLine / Test-LogScanIsStackTraceLine" {
            foreach ($case in $hitCases) {
                It "ヒットする: $($case.Name)" {
                    (Invoke-LogScanLineCheck -Line $case.Line) | Should -BeTrue
                }
            }
            foreach ($case in $ignoreCases) {
                It "無視する: $($case.Name)" {
                    (Invoke-LogScanLineCheck -Line $case.Line) | Should -BeFalse
                }
            }
        }
    } -Data @{ hitCases = $hitCases; ignoreCases = $ignoreCases }

    $config = New-PesterConfiguration
    $config.Run.Container = $container
    $config.Run.PassThru = $true
    $config.Output.Verbosity = "Detailed"

    $result = Invoke-Pester -Configuration $config

    if ($result.FailedCount -gt 0) {
        exit 1
    }
    exit 0
}

# ---- Pester 5 系が無い場合: 素の PowerShell アサーション ----------------------
Write-Host "Pester 5 系が見つからないため、素の PowerShell でテストを実行します。" -ForegroundColor Cyan

$failures = @()
$passCount = 0

foreach ($case in $hitCases) {
    $actual = Invoke-LogScanLineCheck -Line $case.Line
    if ($actual -eq $true) {
        $passCount++
        Write-Host "  [OK]   ヒットする: $($case.Name)" -ForegroundColor Green
    } else {
        $failures += "ヒットするはずが無視された: $($case.Name) => $($case.Line)"
        Write-Host "  [NG]   ヒットする: $($case.Name)" -ForegroundColor Red
    }
}

foreach ($case in $ignoreCases) {
    $actual = Invoke-LogScanLineCheck -Line $case.Line
    if ($actual -eq $false) {
        $passCount++
        Write-Host "  [OK]   無視する: $($case.Name)" -ForegroundColor Green
    } else {
        $failures += "無視するはずがヒットした: $($case.Name) => $($case.Line)"
        Write-Host "  [NG]   無視する: $($case.Name)" -ForegroundColor Red
    }
}

$totalCount = $hitCases.Count + $ignoreCases.Count
Write-Host ""
Write-Host ("結果: {0}/{1} 件成功" -f $passCount, $totalCount) -ForegroundColor Cyan

if ($failures.Count -gt 0) {
    Write-Host "----- 失敗したケース -----" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host $f -ForegroundColor Red
    }
    exit 1
}

Write-Host "すべてのログ判定テストに成功しました。" -ForegroundColor Green
exit 0
