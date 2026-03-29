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

    // Virtual key codes
    private const ushort VK_W = 0x57;
    private const ushort VK_A = 0x41;
    private const ushort VK_S = 0x53;
    private const ushort VK_D = 0x44;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_TAB = 0x09;

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

    public AntiAFK(RobloxInstanceLauncher launcher, int intervalSeconds = 60)
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
    /// Find ALL windows belonging to a process (not just MainWindowHandle).
    /// </summary>
    private List<IntPtr> GetProcessWindows(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                // Check it's a real Roblox window (has title text)
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var sb = new System.Text.StringBuilder(length + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    // Roblox game windows contain "Roblox" in title
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

            // Save current foreground window so we can restore it
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
                        // Fallback to MainWindowHandle
                        IntPtr mwh = inst.Process.MainWindowHandle;
                        if (mwh != IntPtr.Zero)
                            windows.Add(mwh);
                    }

                    foreach (var hwnd in windows)
                    {
                        // Method 1: PostMessage game keys (works in background)
                        PokeWithPostMessage(hwnd);

                        // Method 2: Every 3rd tick, briefly focus and use SendInput
                        // This is more reliable but briefly steals focus
                        if (_tickCount % 3 == 0)
                        {
                            PokeWithFocus(hwnd);
                        }

                        poked++;
                    }
                }
                catch { }
            }

            // Restore original foreground window
            if (_tickCount % 3 == 0 && originalForeground != IntPtr.Zero)
            {
                try { SetForegroundWindow(originalForeground); } catch { }
            }

            if (poked > 0)
                OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK: poked {poked} window(s)");
        }
    }

    /// <summary>
    /// Send game inputs via PostMessage (works without focus).
    /// Uses actual game keys that Roblox recognizes.
    /// </summary>
    private void PokeWithPostMessage(IntPtr hwnd)
    {
        // Cycle through different keys each tick to look more natural
        ushort key = (_tickCount % 4) switch
        {
            0 => VK_W,
            1 => VK_D,
            2 => VK_S,
            3 => VK_A,
            _ => VK_SPACE,
        };

        // Simulate WM_ACTIVATEAPP to trick Roblox into thinking window is active
        PostMessage(hwnd, WM_ACTIVATEAPP, (IntPtr)1, IntPtr.Zero);

        // Key down then up (brief tap)
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
        Thread.Sleep(50);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)key, IntPtr.Zero);

        // Small mouse movement
        int jiggle = (_tickCount % 2 == 0) ? 2 : -2;
        IntPtr lParam = (IntPtr)((300 + jiggle) | ((300 + jiggle) << 16));
        PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);
    }

    /// <summary>
    /// Briefly focus the window and use SendInput for guaranteed input.
    /// More reliable than PostMessage but briefly steals focus.
    /// </summary>
    private void PokeWithFocus(IntPtr hwnd)
    {
        try
        {
            // Bring window to front briefly
            SetForegroundWindow(hwnd);
            Thread.Sleep(80);

            // Send a space bar press via SendInput (most reliable)
            var inputs = new INPUT[4];

            // Key down - space
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_SPACE;

            // Key up - space
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_SPACE;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

            // Mouse move
            inputs[2].type = INPUT_MOUSE;
            inputs[2].u.mi.dx = 1;
            inputs[2].u.mi.dy = 1;
            inputs[2].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            inputs[3].type = INPUT_MOUSE;
            inputs[3].u.mi.dx = -1;
            inputs[3].u.mi.dy = -1;
            inputs[3].u.mi.dwFlags = MOUSEEVENTF_MOVE;

            SendInput(4, inputs, Marshal.SizeOf<INPUT>());
            Thread.Sleep(50);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
