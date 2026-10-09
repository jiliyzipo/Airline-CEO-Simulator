# ScreenClipMonitor.ps1
# Ctrl+V       = paste image  (clipboard holds image only)
# Ctrl+Shift+V = type path    (SendInput Unicode — does NOT touch clipboard)
# Encoding: UTF-8 with BOM (PowerShell 5.x requirement)
# v6: path via keyboard SendInput (no clipboard fight)
param(
    [string]$WatchDir = "$env:USERPROFILE\Pictures\Screenshots",
    [int]$ClipboardWaitSeconds = 2,
    [int]$PollMs = 400
)

if (-not (Test-Path -LiteralPath $WatchDir)) {
    Write-Host "ERROR: Dir not found - $WatchDir" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$nativeSource = @'
using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;

public class ScmNativeV6 : Form {
    public const int WM_HOTKEY = 0x0312;
    public event EventHandler HotKeyPressed;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const int  VK_SHIFT    = 0x10;
    public const int  VK_CONTROL  = 0x11;
    public const int  VK_V        = 0x56;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT {
        public uint type;
        public InputUnion U;
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_HOTKEY) {
            EventHandler h = HotKeyPressed;
            if (h != null) h(this, EventArgs.Empty);
        }
        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value) {
        base.SetVisibleCore(false);
    }

    public static bool IsKeyDown(int vKey) {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    public static void TypeText(string text) {
        if (string.IsNullOrEmpty(text)) return;
        int size = Marshal.SizeOf(typeof(INPUT));
        foreach (char ch in text) {
            INPUT down = new INPUT();
            down.type = INPUT_KEYBOARD;
            down.U.ki.wVk = 0;
            down.U.ki.wScan = ch;
            down.U.ki.dwFlags = KEYEVENTF_UNICODE;
            down.U.ki.time = 0;
            down.U.ki.dwExtraInfo = IntPtr.Zero;

            INPUT up = down;
            up.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

            SendInput(1, new INPUT[] { down }, size);
            SendInput(1, new INPUT[] { up }, size);
        }
    }
}
'@

if (-not ('ScmNativeV6' -as [type])) {
    $refAssemblies = @(
        [System.Windows.Forms.Form].Assembly.Location,
        [System.Drawing.Size].Assembly.Location
    )
    try {
        Add-Type -TypeDefinition $nativeSource -ReferencedAssemblies $refAssemblies -ErrorAction Stop
    } catch {
        Write-Host "ERROR: Failed to compile native helper:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        exit 1
    }
}

$script:WatchDir = $WatchDir
$script:ClipboardWaitSeconds = $ClipboardWaitSeconds
$script:lastSeen = @{}
$script:currentShotPath = $null
$script:pastingPath = $false
$script:lastHotkeyUtc = [datetime]::MinValue
$script:form = $null

Get-ChildItem -LiteralPath $WatchDir -Filter "*.png" -File -ErrorAction SilentlyContinue | ForEach-Object {
    $script:lastSeen[$_.FullName] = $_.LastWriteTimeUtc
}

function Wait-ClipboardFree {
    param([int]$TimeoutMs = 5000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        if ([ScmNativeV6]::OpenClipboard([IntPtr]::Zero)) {
            [void][ScmNativeV6]::CloseClipboard()
            return $true
        }
        Start-Sleep -Milliseconds 40
    }
    return $false
}

function Wait-HotkeysReleased {
    param([int]$TimeoutMs = 2000)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $ctrl  = [ScmNativeV6]::IsKeyDown([ScmNativeV6]::VK_CONTROL)
        $shift = [ScmNativeV6]::IsKeyDown([ScmNativeV6]::VK_SHIFT)
        $v     = [ScmNativeV6]::IsKeyDown([ScmNativeV6]::VK_V)
        if (-not $ctrl -and -not $shift -and -not $v) { return $true }
        Start-Sleep -Milliseconds 20
    }
    return $false
}

function Set-ClipboardDataObject {
    param(
        [System.Windows.Forms.DataObject]$Data,
        [int]$TimeoutMs = 8000
    )
    if (-not (Wait-ClipboardFree -TimeoutMs $TimeoutMs)) {
        throw "Clipboard busy (OpenClipboard timeout)"
    }

    # Drop previous OLE ownership that can block the next set
    if ([ScmNativeV6]::OpenClipboard([IntPtr]::Zero)) {
        try { [void][ScmNativeV6]::EmptyClipboard() } finally { [void][ScmNativeV6]::CloseClipboard() }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lastErr = $null
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        try {
            [System.Windows.Forms.Clipboard]::SetDataObject($Data, $true)
            return
        } catch {
            $lastErr = $_.Exception.Message
            Start-Sleep -Milliseconds 80
            [void](Wait-ClipboardFree -TimeoutMs 800)
        }
    }
    throw "SetDataObject failed: $lastErr"
}

function Test-FileReady {
    param([string]$ImagePath)
    try {
        $fs = [System.IO.File]::Open(
            $ImagePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        try {
            if ($fs.Length -lt 32) { return $false }
            $img = [System.Drawing.Image]::FromStream($fs, $false, $false)
            $null = $img.Width
            $img.Dispose()
            return $true
        } finally {
            $fs.Dispose()
        }
    } catch {
        return $false
    }
}

function Set-ImageOnlyClipboard {
    param([string]$ImagePath)

    $fs = $null; $img = $null; $ms = $null; $bmp = $null; $ms2 = $null
    try {
        $fs = [System.IO.File]::Open(
            $ImagePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        $img = [System.Drawing.Image]::FromStream($fs)
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
        $img.Dispose(); $img = $null
        $fs.Dispose(); $fs = $null
        $ms.Dispose(); $ms = $null

        $ms2 = New-Object System.IO.MemoryStream(,$bytes)
        $bmp = New-Object System.Drawing.Bitmap $ms2

        Start-Sleep -Seconds $script:ClipboardWaitSeconds

        $data = New-Object System.Windows.Forms.DataObject
        $data.SetData([System.Windows.Forms.DataFormats]::Bitmap, $true, $bmp)
        Set-ClipboardDataObject -Data $data -TimeoutMs 8000

        Start-Sleep -Milliseconds 50
        $check = $null
        try { $check = [System.Windows.Forms.Clipboard]::GetImage() } catch { }
        if (-not $check) { throw "Clipboard has no image after set" }
        $check.Dispose()
        return $true
    } catch {
        Write-Host "  Clipboard(image) failed: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    } finally {
        if ($img) { $img.Dispose() }
        if ($fs) { $fs.Dispose() }
        if ($bmp) { $bmp.Dispose() }
        if ($ms) { $ms.Dispose() }
        if ($ms2) { $ms2.Dispose() }
    }
}

function Invoke-TypePathCore {
    if ($script:pastingPath) { return }
    if (-not $script:currentShotPath) {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] No screenshot path to type" -ForegroundColor DarkYellow
        return
    }

    $script:pastingPath = $true
    $path = $script:currentShotPath
    try {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Type path via Ctrl+Shift+V" -ForegroundColor Cyan

        [void](Wait-HotkeysReleased -TimeoutMs 2000)
        Start-Sleep -Milliseconds 60

        # Type into focused window — clipboard (image) stays untouched
        [ScmNativeV6]::TypeText($path)

        Write-Host "  Path typed (image still on clipboard)" -ForegroundColor Green
    } catch {
        Write-Host "  Path type failed: $($_.Exception.Message)" -ForegroundColor Red
    } finally {
        $script:pastingPath = $false
    }
}

function Request-TypePath {
    # Debounce hotkey auto-repeat / double taps
    $now = [datetime]::UtcNow
    if (($now - $script:lastHotkeyUtc).TotalMilliseconds -lt 1200) { return }
    if ($script:pastingPath) { return }
    $script:lastHotkeyUtc = $now

    # Never run inside WM_HOTKEY — defer via Timer
    $t = New-Object System.Windows.Forms.Timer
    $t.Interval = 80
    $t.add_Tick({
        param($sender, $e)
        $sender.Stop()
        $sender.Dispose()
        Invoke-TypePathCore
    })
    $t.Start()
}

function Update-ScreenshotPoll {
    try {
        $files = Get-ChildItem -LiteralPath $script:WatchDir -Filter "*.png" -File -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            $full = $f.FullName
            $writeUtc = $f.LastWriteTimeUtc

            if ($script:lastSeen.ContainsKey($full) -and $script:lastSeen[$full] -eq $writeUtc) {
                continue
            }

            $ready = $false
            for ($i = 0; $i -lt 20; $i++) {
                if (Test-FileReady -ImagePath $full) { $ready = $true; break }
                Start-Sleep -Milliseconds 250
            }

            try { $f.Refresh(); $writeUtc = $f.LastWriteTimeUtc } catch { }

            if (-not $ready) {
                Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Still locked, will retry: $($f.Name)" -ForegroundColor DarkYellow
                continue
            }

            $script:lastSeen[$full] = $writeUtc
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] New screenshot: $($f.Name)" -ForegroundColor Green

            if (Set-ImageOnlyClipboard -ImagePath $full) {
                $script:currentShotPath = $full
                Write-Host "  Image on clipboard. Ctrl+V = image, Ctrl+Shift+V = type path" -ForegroundColor Green
            }
        }

        if ($script:lastSeen.Count -gt 500) {
            $alive = @{}
            foreach ($f in $files) { $alive[$f.FullName] = $true }
            foreach ($k in @($script:lastSeen.Keys)) {
                if (-not $alive.ContainsKey($k)) { $script:lastSeen.Remove($k) }
            }
        }
    } catch {
        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Loop error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Screenshot Monitor (v6 - type path)" -ForegroundColor Cyan
Write-Host " Dir : $WatchDir" -ForegroundColor White
Write-Host " Ctrl+V       -> paste image" -ForegroundColor Yellow
Write-Host " Ctrl+Shift+V -> type path (no clipboard)" -ForegroundColor Yellow
Write-Host " Clipboard wait: ${ClipboardWaitSeconds}s" -ForegroundColor White
Write-Host " Ctrl+C to exit" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan

$script:form = New-Object ScmNativeV6
$null = $script:form.Handle

$hotkeyId = 1
$mod = [uint32]([ScmNativeV6]::MOD_CONTROL -bor [ScmNativeV6]::MOD_SHIFT)
$vkV = [uint32][ScmNativeV6]::VK_V
if (-not [ScmNativeV6]::RegisterHotKey($script:form.Handle, $hotkeyId, $mod, $vkV)) {
    Write-Host "ERROR: Could not register Ctrl+Shift+V hotkey (already in use?)" -ForegroundColor Red
    exit 1
}

$script:form.add_HotKeyPressed({ Request-TypePath })
$script:form.add_FormClosed({
    [void][ScmNativeV6]::UnregisterHotKey($script:form.Handle, $hotkeyId)
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $PollMs
$timer.add_Tick({ Update-ScreenshotPoll })
$timer.Start()

Write-Host "Ready. Watching for new screenshots..." -ForegroundColor DarkGray

try {
    [System.Windows.Forms.Application]::Run($script:form)
}
finally {
    $timer.Stop()
    $timer.Dispose()
    [void][ScmNativeV6]::UnregisterHotKey($script:form.Handle, $hotkeyId)
    $script:form.Dispose()
    Write-Host "`nMonitor stopped." -ForegroundColor Yellow
}
