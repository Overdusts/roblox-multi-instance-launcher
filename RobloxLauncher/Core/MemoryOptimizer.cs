using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>
/// Aggressively reduces memory usage of running Roblox instances.
/// Uses EmptyWorkingSet to force Windows to page out unused memory,
/// sets process priority to Idle, and minimizes windows.
/// </summary>
public static class MemoryOptimizer
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    public static event Action<string>? OnLog;

    /// <summary>
    /// Enable AFK mode: minimize all Roblox windows, set priority to Idle,
    /// and force-trim working set memory.
    /// Typically reduces each instance from ~1.5GB to ~200-400MB.
    /// </summary>
    public static void EnableAfkMode(RobloxInstanceLauncher launcher)
    {
        int trimmed = 0;
        long totalSaved = 0;

        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            try
            {
                var proc = inst.Process!;
                proc.Refresh();
                long beforeMB = proc.WorkingSet64 / (1024 * 1024);

                // 1) Set priority to Idle — minimum CPU usage
                proc.PriorityClass = ProcessPriorityClass.Idle;

                // 2) Minimize all windows for this process
                MinimizeProcessWindows(proc.Id);

                // 3) Force empty working set — pages out unused memory
                EmptyWorkingSet(proc.Handle);

                // Let Windows actually release the pages
                Thread.Sleep(100);

                proc.Refresh();
                long afterMB = proc.WorkingSet64 / (1024 * 1024);
                long saved = beforeMB - afterMB;
                totalSaved += saved;
                trimmed++;

                Log($"Instance #{inst.InstanceNumber}: {beforeMB}MB -> {afterMB}MB (saved {saved}MB)");
            }
            catch (Exception ex)
            {
                Log($"Instance #{inst.InstanceNumber}: trim failed — {ex.Message}");
            }
        }

        if (trimmed > 0)
            Log($"AFK Mode ON — trimmed {trimmed} instance(s), saved ~{totalSaved}MB total");
        else
            Log("No running instances to optimize");
    }

    /// <summary>
    /// Disable AFK mode: restore windows and set normal priority.
    /// </summary>
    public static void DisableAfkMode(RobloxInstanceLauncher launcher)
    {
        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            try
            {
                var proc = inst.Process!;

                // Restore priority
                proc.PriorityClass = ProcessPriorityClass.Normal;

                // Restore windows
                RestoreProcessWindows(proc.Id);
            }
            catch { }
        }

        Log("AFK Mode OFF — instances restored to normal");
    }

    /// <summary>
    /// Just trim memory without changing priority or minimizing.
    /// Can be called periodically.
    /// </summary>
    public static void TrimAll(RobloxInstanceLauncher launcher)
    {
        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;
            try
            {
                EmptyWorkingSet(inst.Process!.Handle);
            }
            catch { }
        }
    }

    private static void MinimizeProcessWindows(int processId)
    {
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
                ShowWindow(hWnd, SW_MINIMIZE);
            return true;
        }, IntPtr.Zero);
    }

    private static void RestoreProcessWindows(int processId)
    {
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId)
                ShowWindow(hWnd, SW_RESTORE);
            return true;
        }, IntPtr.Zero);
    }

    private static void Log(string msg) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
