# scripts/tests/tsumugi-expressions-tool.tests.ps1
# 配布 zip 同梱の表情生成ツールの起動スクリプト（scripts/tsumugi-expressions-tool/make-expressions.ps1、#219）の
# Python の検出（Microsoft Store のエイリアスの見分け）に対する回帰テスト。
#
# 実行:
#   pwsh ./scripts/tests/tsumugi-expressions-tool.tests.ps1
#   powershell -ExecutionPolicy Bypass -File ./scripts/tests/tsumugi-expressions-tool.tests.ps1   # 5.1 でも動く
#
# Python を実際には起動しない（Get-Command と起動の結果を差し替える）。
# Python の検出は Python より前に動く必要があるため、Python の単体テストではなく PowerShell で確かめる。

. "$PSScriptRoot/../tsumugi-expressions-tool/make-expressions.ps1"
$ErrorActionPreference = "Stop"

$script:Failures = @()

function Assert-Equal {
    param($Expected, $Actual, [string]$Label)
    if ($Expected -ne $Actual) {
        $script:Failures += "$Label（期待: '$Expected' / 実際: '$Actual'）"
    }
}

function Assert-Match {
    param([string]$Pattern, [string]$Actual, [string]$Label)
    if ($Actual -notmatch $Pattern) {
        $script:Failures += "$Label（'$Pattern' を含まない: $Actual）"
    }
}

$storeStub = 'C:\Users\someone\AppData\Local\Microsoft\WindowsApps\python.exe'
$realPython = 'C:\Python314\python.exe'
$pyLauncher = 'C:\WINDOWS\py.exe'

# 偽の Get-Command / 起動結果を作る。$Commands: 名前 -> パス、$ExitCodes: パス -> 終了コード。
function New-FakeEnvironment {
    param([hashtable]$Commands, [hashtable]$ExitCodes)

    $probed = New-Object System.Collections.ArrayList
    return [pscustomobject]@{
        Probed     = $probed
        GetCommand = { param($name) if ($Commands.ContainsKey($name)) { [pscustomobject]@{ Source = $Commands[$name] } } }.GetNewClosure()
        Probe      = { param($command) [void]$probed.Add($command); $ExitCodes[$command] }.GetNewClosure()
    }
}

# ---- Test-StoreAliasPath ----
Assert-Equal $true (Test-StoreAliasPath $storeStub) "WindowsApps のエイリアスを検出する"
Assert-Equal $true (Test-StoreAliasPath 'c:\users\x\appdata\local\microsoft\windowsapps\py.exe') "大文字小文字を区別しない"
Assert-Equal $false (Test-StoreAliasPath $realPython) "通常の Python はエイリアスではない"
Assert-Equal $false (Test-StoreAliasPath '') "空文字はエイリアスではない"

# ---- py が使えればそれを使い、python は起動しない ----
$fake = New-FakeEnvironment -Commands @{ py = $pyLauncher; python = $storeStub } -ExitCodes @{ $pyLauncher = 0; $storeStub = 9009 }
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal $true $result.Found "py があれば見つかる"
Assert-Equal $pyLauncher $result.Command "py のパスを使う"
Assert-Equal 1 $fake.Probed.Count "py で見つかったら python は起動しない"

# ---- py が無く、python が Store のエイリアスだけ（終了コード 9009） ----
$fake = New-FakeEnvironment -Commands @{ python = $storeStub } -ExitCodes @{ $storeStub = 9009 }
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal $false $result.Found "Store のエイリアスだけなら見つからない扱い"
$aliasDiagnostics = @($result.Diagnostics | Where-Object { $_.StoreAlias })
Assert-Equal 1 $aliasDiagnostics.Count "エイリアスとして記録する"
$message = Get-PythonMissingMessage -Diagnostics $result.Diagnostics
Assert-Match 'Microsoft Store' $message "案内に Store のエイリアスの説明を入れる"
Assert-Match 'python\.org' $message "案内に入手先を入れる"

# ---- WindowsApps でも実際に動くもの（Python install manager / Store 版の Python）は使う ----
$fake = New-FakeEnvironment -Commands @{ python = $storeStub } -ExitCodes @{ $storeStub = 0 }
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal $true $result.Found "WindowsApps でも起動できれば使う"
Assert-Equal $storeStub $result.Command "WindowsApps のパスを使う"

# ---- 古い Python ----
$fake = New-FakeEnvironment -Commands @{ python = $realPython } -ExitCodes @{ $realPython = 3 }
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal $false $result.Found "3.10 より古ければ見つからない扱い"
Assert-Match '古すぎ' (Get-PythonMissingMessage -Diagnostics $result.Diagnostics) "古いことを案内する"

# ---- 何も無い ----
$fake = New-FakeEnvironment -Commands @{} -ExitCodes @{}
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal $false $result.Found "何も無ければ見つからない"
Assert-Equal 2 @($result.Diagnostics).Count "py と python の両方を調べる"
$message = Get-PythonMissingMessage -Diagnostics $result.Diagnostics
if ($message -match 'Microsoft Store') { $script:Failures += "エイリアスが無いのに Store の説明を出している" }

# ---- -Python の指定があればそれだけを調べる ----
$fake = New-FakeEnvironment -Commands @{ 'D:\venv\python.exe' = 'D:\venv\python.exe'; py = $pyLauncher } -ExitCodes @{ 'D:\venv\python.exe' = 0; $pyLauncher = 0 }
$result = Find-PythonCommand -Explicit 'D:\venv\python.exe' -GetCommand $fake.GetCommand -Probe $fake.Probe
Assert-Equal 'D:\venv\python.exe' $result.Command "-Python の指定を使う"
Assert-Equal 1 $fake.Probed.Count "-Python の指定があれば py は起動しない"

# ---- 起動に失敗（例外）しても止まらずに次の候補へ進む ----
$fake = New-FakeEnvironment -Commands @{ py = $pyLauncher; python = $realPython } -ExitCodes @{ $realPython = 0 }
$throwingProbe = { param($command) if ($command -eq $pyLauncher) { throw "起動できない" } 0 }
$result = Find-PythonCommand -GetCommand $fake.GetCommand -Probe $throwingProbe
Assert-Equal $realPython $result.Command "例外の候補は飛ばす"
Assert-Equal -1 @($result.Diagnostics)[0].ExitCode "例外は終了コード -1 として記録する"

# ---- 引数の引用（ConvertTo-CommandLineArgument）: Windows の標準の解釈で元に戻ること ----
Assert-Equal 'abc' (ConvertTo-CommandLineArgument 'abc') "空白の無い引数はそのまま"
Assert-Equal '""' (ConvertTo-CommandLineArgument '') "空文字は 2 つの引用符"
Assert-Equal '"C:\a b\\"' (ConvertTo-CommandLineArgument 'C:\a b\') "末尾の \ を 2 倍にする"
Assert-Equal '"say \"hi\""' (ConvertTo-CommandLineArgument 'say "hi"') "引用符をエスケープする"

Add-Type -Namespace TsumugiQuizTests -Name ArgvParser -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
private static extern System.IntPtr CommandLineToArgvW([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string cmdLine, out int numArgs);
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
private static extern System.IntPtr LocalFree(System.IntPtr hMem);
public static string[] Parse(string commandLine) {
    int count;
    System.IntPtr argv = CommandLineToArgvW(commandLine, out count);
    try {
        string[] result = new string[count];
        for (int i = 0; i < count; i++) {
            result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, i * System.IntPtr.Size));
        }
        return result;
    } finally {
        LocalFree(argv);
    }
}
'@
$roundTripCases = @(
    'C:\Users\山田 太郎\Downloads\春日部つむぎ立ち絵_公式_v2.0.zip',
    'D:\data root\',
    'D:\data root\\',
    'a"b',
    'a\"b',
    'tab	inside',
    'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 3)',
    '-Python',
    ''
)
$line = "prog " + (($roundTripCases | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " ")
$parsed = [TsumugiQuizTests.ArgvParser]::Parse($line)
Assert-Equal ($roundTripCases.Count + 1) $parsed.Length "引用した引数の数が変わらない"
for ($i = 0; $i -lt $roundTripCases.Count; $i++) {
    Assert-Equal $roundTripCases[$i] $parsed[$i + 1] "引数 $i が元に戻る"
}

if ($script:Failures.Count -gt 0) {
    Write-Host "表情生成ツールの起動スクリプトのテストに失敗しました:" -ForegroundColor Red
    foreach ($failure in $script:Failures) { Write-Host "  - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host "OK: 表情生成ツールの起動スクリプト（Python の検出・Store のエイリアスの見分け・引数の引用）のテストに合格しました。" -ForegroundColor Green
exit 0
