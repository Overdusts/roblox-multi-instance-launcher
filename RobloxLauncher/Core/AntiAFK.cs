using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>
/// Sends periodic input to Roblox windows to prevent idle/AFK kick.
/// Simulates a tiny mouse jiggle + key press in each Roblox window.
/// </summary>
public class AntiAFK : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;

    // Virtual key codes
    private const int VK_W = 0x57;
    private const int VK_SPACE = 0x20;
    private const int VK_F13 = 0x7C; // F13 — doesn't do anything visible in-game

    private System.Threading.Timer? _timer;
    private readonly RobloxInstanceLauncher _launcher;
    private int _intervalMs;
    private bool _enabled;
    private int _tickCount;

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

    private void Tick(object? state)
    {
        _tickCount++;
        int poked = 0;

        foreach (var inst in _launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            try
            {
                IntPtr hwnd = inst.Process!.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;

                // Send F13 key (invisible to game, but counts as input)
                PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_F13, IntPtr.Zero);
                PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_F13, IntPtr.Zero);

                // Small mouse movement to keep alive
                int jiggle = (_tickCount % 2 == 0) ? 1 : -1;
                IntPtr lParam = (IntPtr)((100 + jiggle) | ((100 + jiggle) << 16));
                PostMessage(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lParam);

                poked++;
            }
            catch { }
        }

        if (poked > 0)
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Anti-AFK: poked {poked} instance(s)");
    }

    public void Dispose()
    {
        Stop();
    }
}
