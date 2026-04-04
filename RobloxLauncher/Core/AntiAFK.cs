using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

public class AntiAFK : IDisposable
{
    // ── Win32 APIs ──
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd); // Returns true if window is minimized

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ── SendInput structures ──
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint INPUT_MOUSE = 0;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_ACTIVATEAPP = 0x001C;

    private const int SW_MINIMIZE = 6;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_RESTORE = 9;

    // Virtual key codes: W A S D Space F
    private static readonly ushort[] GameKeys = { 0x57, 0x41, 0x53, 0x44, 0x20, 0x46 };

    private System.Threading.Timer? _timer;
    private readonly RobloxInstanceLauncher _launcher;
    private int _intervalMs;
    private bool _enabled;
    private int _tickCount;
    private readonly object _lock = new();
    private bool _reMinimizeAfterPoke;

    public bool Enabled => _enabled;
    public int IntervalSeconds
    {
        get => _intervalMs / 1000;
        set => _intervalMs = value * 1000;
    }

    /// <summary>If true, windows will be re-minimized after poking (for AFK/Optimize mode).</summary>
    public bool ReMinimizeAfterPoke
    {
        get => _reMinimizeAfterPoke;
        set => _reMinimizeAfterPoke = value;
    }

    public event Action<string>? OnLog;

    public AntiAFK(RobloxInstanceLauncher launcher, int intervalSeconds = 30)
    {
        _launcher = launcher;
        _intervalMs = intervalSeconds * 1000;
    }

    public void Start()
    {
        if (_enabled) return;
        _enabled = true;
        _tickCount = 0;
        _timer = new System.Threading.Timer(Tick, null, _intervalMs, _intervalMs);
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK started (every {IntervalSeconds}s)");
    }

    public void Stop()
    {
        if (!_enabled) return;
        _enabled = false;
        _timer?.Dispose();
        _timer = null;
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK stopped");
    }

    /// <summary>
    /// Find ALL windows for a Roblox process — including minimized ones.
    /// This is critical: the old code used IsWindowVisible which skips minimized windows.
    /// </summary>
    private List<IntPtr> GetProcessWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
            {
                // Check window title OR class name — catch both visible and minimized
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var sb = new System.Text.StringBuilder(length + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                    {
                        windows.Add(hWnd);
                        return true;
                    }
                }

                // Also check by class name (Roblox uses specific window classes)
                var classSb = new System.Text.StringBuilder(256);
                GetClassName(hWnd, classSb, classSb.Capacity);
                string className = classSb.ToString();
                if (className.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(hWnd);
                }
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private void Tick(object? state)
    {
        lock (_lock)
        {
            _tickCount++;
            int poked = 0;

            // Save current foreground window to restore after
            IntPtr originalForeground = GetForegroundWindow();
            var windowsToReMinimize = new List<IntPtr>();

            foreach (var inst in _launcher.Instances)
            {
                if (!inst.IsRunning) continue;

                try
                {
                    int pid = inst.Process!.Id;
                    var windows = GetProcessWindows(pid);

                    // Fallback: try MainWindowHandle
                    if (windows.Count == 0)
                    {
                        try
                        {
                            inst.Process.Refresh();
                            IntPtr mwh = inst.Process.MainWindowHandle;
                            if (mwh != IntPtr.Zero)
                                windows.Add(mwh);
                        }
                        catch { }
                    }

                    foreach (var hwnd in windows)
                    {
                        bool wasMinimized = IsIconic(hwnd);

                        // If minimized, temporarily restore so SendInput works
                        if (wasMinimized)
                        {
                            ShowWindow(hwnd, SW_RESTORE);
                            Thread.Sleep(80);
                            windowsToReMinimize.Add(hwnd);
                        }

                        // Focus the window and send REAL input via SendInput
                        // This is the ONLY method Roblox accepts for anti-idle
                        PokeWithRealInput(hwnd);

                        poked++;
                        Thread.Sleep(120);
                    }
                }
                catch { }
            }

            // Small delay to let inputs register
            Thread.Sleep(200);

            // Restore original foreground window
            if (originalForeground != IntPtr.Zero)
            {
                try { ForceForeground(originalForeground); } catch { }
            }

            // Re-minimize windows that were minimized before
            if (_reMinimizeAfterPoke)
            {
                Thread.Sleep(100);
                foreach (var hwnd in windowsToReMinimize)
                {
                    try { ShowWindow(hwnd, SW_MINIMIZE); } catch { }
                }
            }

            if (poked > 0)
                OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK: poked {poked} window(s) (tick #{_tickCount})");
        }
    }

    /// <summary>
    /// The ONLY reliable anti-AFK method: focus the window and use SendInput.
    /// Roblox ignores PostMessage — it uses GetAsyncKeyState/RawInput which
    /// only responds to real hardware-level input from SendInput.
    /// </summary>
    private void PokeWithRealInput(IntPtr hwnd)
    {
        try
        {
            // Focus the window
            ForceForeground(hwnd);
            Thread.Sleep(80);

            // Pick a key (rotate through WASD, Space, F)
            ushort key = GameKeys[_tickCount % GameKeys.Length];

            // Send: key press + mouse jiggle
            var inputs = new INPUT[6];

            // Key down
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = key;

            // Key up
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = key;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            // Mouse move right
            inputs[2].type = INPUT_MOUSE;
            inputs[2].u.mi.dx = 3;
            inputs[2].u.mi.dy = 2;
            inputs[2].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            // Mouse move back
            inputs[3].type = INPUT_MOUSE;
            inputs[3].u.mi.dx = -3;
            inputs[3].u.mi.dy = -2;
            inputs[3].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            // Second key (different one for variety)
            ushort key2 = GameKeys[(_tickCount + 2) % GameKeys.Length];
            inputs[4].type = INPUT_KEYBOARD;
            inputs[4].u.ki.wVk = key2;

            inputs[5].type = INPUT_KEYBOARD;
            inputs[5].u.ki.wVk = key2;
            inputs[5].u.ki.dwFlags = KEYEVENTF_KEYUP;

            SendInput(6, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(50);

            // Also send PostMessage as backup
            PostMessage(hwnd, WM_ACTIVATEAPP, (IntPtr)1, IntPtr.Zero);
            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            Thread.Sleep(20);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)key, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>
    /// Force a window to foreground using AttachThreadInput trick.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == hwnd) return;

        uint foreThread = GetWindowThreadProcessId(foreground, out _);
        uint curThread = GetCurrentThreadId();

        if (foreThread != curThread)
        {
            AttachThreadInput(curThread, foreThread, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(curThread, foreThread, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
