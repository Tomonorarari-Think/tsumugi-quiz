# scripts/window-capture.ps1
# Process のメインウィンドウを System.Drawing でスクリーンショットとして保存するヘルパー。
# scripts/run-multi.ps1 から dot-source して使う（issue #8）。
# 使い方: . "$PSScriptRoot/window-capture.ps1"

Add-Type -AssemblyName System.Drawing

if (-not ("TqWindowCaptureNative" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class TqWindowCaptureNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    // #105 レビュー M-3: GetWindowRect は Windows 11 の DWM が付ける不可視のリサイズ余白まで
    //含んでしまい、CopyFromScreen がその余白越しに背後の別ウィンドウを写し込むことがある
    // （実測: 隣接するウィンドウの内容が撮影結果の端に写り込んだ）。
    // DWMWA_EXTENDED_FRAME_BOUNDS (9) は実際に見えているウィンドウ枠のみを返すため、
    // こちらを使う。取得に失敗した場合のみ GetWindowRect にフォールバックする。
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    // issue #131 レビュー L-8: 撮影直前に、撮影対象矩形の中心に実際にあるウィンドウの所有 PID を
    // 照合するために使う（固定パス運用では同名の実行ファイルが複数 worktree で並行動作するため、
    // MainWindowHandle 取得後にウィンドウが移動・重なった等で誤ったウィンドウを撮影する事故を防ぐ）。
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
"@
}

# 指定したウィンドウの実際に見える範囲（DWM の不可視リサイズ余白を含まない）を取得する。
# 取得に失敗した場合は GetWindowRect の結果にフォールバックする。
function Get-VisibleWindowRect {
    param([Parameter(Mandatory = $true)][IntPtr]$Handle)

    $rect = New-Object TqWindowCaptureNative+RECT
    $DWMWA_EXTENDED_FRAME_BOUNDS = 9
    $hr = [TqWindowCaptureNative]::DwmGetWindowAttribute(
        $Handle, $DWMWA_EXTENDED_FRAME_BOUNDS, [ref]$rect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type][TqWindowCaptureNative+RECT]))
    if ($hr -eq 0) {
        return $rect
    }

    Write-Warning ("DwmGetWindowAttribute に失敗しました (HRESULT=0x{0:X8})。GetWindowRect にフォールバックします。" -f $hr)
    $fallbackRect = New-Object TqWindowCaptureNative+RECT
    if (-not [TqWindowCaptureNative]::GetWindowRect($Handle, [ref]$fallbackRect)) {
        throw "GetWindowRect にも失敗しました。"
    }
    return $fallbackRect
}

# 指定した Process のメインウィンドウをスクリーンショットして PNG として保存する。
# メインウィンドウがまだ無い（起動直後等）・取得に失敗した場合は警告を出してスキップする
# （呼び出し側の全体処理を止めないため。例外は投げない）。
function Save-ProcessWindowScreenshot {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $Process.Refresh()
    $handle = $Process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        Write-Warning "メインウィンドウが見つかりません（PID $($Process.Id)）。スクリーンショットをスキップします。"
        return
    }

    try {
        $rect = Get-VisibleWindowRect -Handle $handle
    } catch {
        Write-Warning "ウィンドウ範囲の取得に失敗しました（PID $($Process.Id)）: $_"
        return
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) {
        Write-Warning "ウィンドウサイズが不正です（PID $($Process.Id)、幅=$width 高さ=$height）。"
        return
    }

    # issue #131 レビュー L-8: 撮影対象矩形の中心に実際にあるウィンドウの所有 PID を照合する。
    # 固定パス運用では他 worktree の同名プロセスが並行動作していることが常態のため、
    # MainWindowHandle 取得後にウィンドウが移動・重なった等で誤ったウィンドウ（他プロセスの
    # ウィンドウ）を撮影してしまう事故を防ぐ（PR #124 レビュー L-a 由来）。
    $centerPoint = New-Object TqWindowCaptureNative+POINT
    $centerPoint.X = $rect.Left + [int](($rect.Right - $rect.Left) / 2)
    $centerPoint.Y = $rect.Top + [int](($rect.Bottom - $rect.Top) / 2)
    $hwndAtCenter = [TqWindowCaptureNative]::WindowFromPoint($centerPoint)
    $ownerPid = 0
    [void][TqWindowCaptureNative]::GetWindowThreadProcessId($hwndAtCenter, [ref]$ownerPid)
    if ($ownerPid -ne $Process.Id) {
        # レビュー L-3: 重なっているウィンドウの所有プロセス名も警告に含める（取得できれば）。
        # 固定パス運用では他 worktree の同名プロセスが重なっているケースがほとんどのため、
        # 何が重なっているかが分かれば「そのプロセスを除いて撮り直す」対処に直結する。
        $overlappingProcessName = $null
        try {
            $overlappingProcessName = (Get-Process -Id $ownerPid -ErrorAction Stop).ProcessName
        } catch {
            $overlappingProcessName = "取得不可"
        }
        Write-Warning "撮影対象の矩形中心にあるウィンドウの所有 PID ($ownerPid、プロセス名: $overlappingProcessName) が撮影対象プロセス（PID $($Process.Id)）と一致しません。他プロセスのウィンドウが重なっているため誤ったウィンドウを撮影するおそれがあり、撮影をスキップします（重なりの原因を取り除いてから撮り直してください）。"
        return
    }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
            $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Host "スクリーンショットを保存しました: $OutputPath"
        } finally {
            $graphics.Dispose()
        }
    } finally {
        $bitmap.Dispose()
    }
}
