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

    // Virtual key codes: W A S D Space
    private static readonly ushort[] GameKeys = { 0x57, 0x41, 0x53, 0x44, 0x20 };

    private System.Threading.Timer? _timer;
    private readonly RobloxInstanceLauncher _launcher;
    private int _intervalMs;
    private bool _enabled;
    private int _tickCount;
    private readonly object _lock = new();

    public bool Enabled => _enabled;
    public int IntervalSeconds
    {
        get => _intervalMs / 1000;
        set => _intervalMs = value * 1000;
    }

    public event Action<string>? OnLog;

    public AntiAFK(RobloxInstanceLauncher launcher, int intervalSeconds = 45)
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

    private List<IntPtr> GetProcessWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var sb = new System.Text.StringBuilder(length + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (title.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
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

            foreach (var inst in _launcher.Instances)
            {
                if (!inst.IsRunning) continue;

                try
                {
                    int pid = inst.Process!.Id;
                    var windows = GetProcessWindows(pid);

                    if (windows.Count == 0)
                    {
                        IntPtr mwh = inst.Process.MainWindowHandle;
                        if (mwh != IntPtr.Zero)
                            windows.Add(mwh);
                    }

                    foreach (var hwnd in windows)
                    {
                        // Both methods every tick for maximum reliability

                        // 1) PostMessage — works in background sometimes
                        PokeWithPostMessage(hwnd);

                        // 2) SendInput with focus — guaranteed to work
                        PokeWithFocus(hwnd);

                        poked++;

                        // Delay between instances to avoid input collision
                        Thread.Sleep(150);
                    }
                }
                catch { }
            }

            // Restore original foreground window
            if (originalForeground != IntPtr.Zero)
            {
                try { ForceForeground(originalForeground); } catch { }
            }

            if (poked > 0)
                OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK: poked {poked} window(s)");
        }
    }

    private void PokeWithPostMessage(IntPtr hwnd)
    {
        ushort key = GameKeys[_tickCount % GameKeys.Length];

        PostMessage(hwnd, WM_ACTIVATEAPP, (IntPtr)1, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Thread.Sleep(30);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)key, IntPtr.Zero);

        int jiggle = (_tickCount % 2 == 0) ? 3 : -3;
        IntPtr lParam = (IntPtr)((300 + jiggle) | ((300 + jiggle) << 16));
        PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
    }

    private void PokeWithFocus(IntPtr hwnd)
    {
        try
        {
            ForceForeground(hwnd);
            Thread.Sleep(100);

            ushort key = GameKeys[(_tickCount + 1) % GameKeys.Length];

            var inputs = new INPUT[4];

            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = key;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = key;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            inputs[2].type = INPUT_MOUSE;
            inputs[2].u.mi.dx = 2;
            inputs[2].u.mi.dy = 0;
            inputs[2].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            inputs[3].type = INPUT_MOUSE;
            inputs[3].u.mi.dx = -2;
            inputs[3].u.mi.dy = 0;
            inputs[3].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            SendInput(4, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(60);
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
