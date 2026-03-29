using System.Diagnostics;

namespace RobloxLauncher.Core;

public class InstanceInfo
{
    public int InstanceNumber { get; set; }
    public Process? Process { get; set; }
    public DateTime LaunchedAt { get; set; }
    public bool IsRunning => Process != null && !Process.HasExited;
    public long MemoryMB
    {
        get
        {
            try { return IsRunning ? Process!.WorkingSet64 / (1024 * 1024) : 0; }
            catch { return 0; }
        }
    }
}

public class RobloxInstanceLauncher
{
    private readonly List<InstanceInfo> _instances = new();
    private bool _flagsApplied;
    private int _nextInstanceNum = 1;

    public IReadOnlyList<InstanceInfo> Instances => _instances.AsReadOnly();
    public event Action<string>? OnLog;
    public event Action? OnInstanceChanged;

    public async Task<InstanceInfo?> LaunchOne(QualityPreset quality, int instanceNum = 0)
    {
        if (instanceNum == 0)
            instanceNum = _nextInstanceNum;
        _nextInstanceNum = instanceNum + 1;

        string? robloxPath = QualityOptimizer.GetRobloxPath();
        if (robloxPath == null)
        {
            Log("ERROR: Roblox installation not found");
            return null;
        }

        string playerExe = Path.Combine(robloxPath, "RobloxPlayerBeta.exe");
        if (!File.Exists(playerExe))
        {
            Log("ERROR: RobloxPlayerBeta.exe not found");
            return null;
        }

        // Apply FFlags once
        if (!_flagsApplied)
        {
            Log($"Applying {quality} optimization...");
            QualityOptimizer.ApplyFFlags(robloxPath, quality);
            _flagsApplied = true;
        }

        // Start continuous mutex monitor if not running
        if (!MutexBypass.IsMonitoring)
        {
            MutexBypass.OnLog += msg => OnLog?.Invoke(msg);
            MutexBypass.StartMonitor(1000); // Check every 1 second
        }

        // Pre-kill any existing mutexes
        MutexBypass.KillAllMutexes();
        await Task.Delay(500);

        Log($"Launching instance #{instanceNum}...");
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = playerExe,
                UseShellExecute = true,
                WorkingDirectory = robloxPath,
            });

            if (process == null)
            {
                Log("ERROR: Failed to start Roblox");
                return null;
            }

            var instance = new InstanceInfo
            {
                InstanceNumber = instanceNum,
                Process = process,
                LaunchedAt = DateTime.Now,
            };

            _instances.Add(instance);

            // Monitor exit
            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); } catch { }
                Log($"Instance #{instanceNum} closed");
                OnInstanceChanged?.Invoke();
            });

            // Wait for Roblox to create its window, then kill mutex immediately
            Log($"Waiting for instance #{instanceNum} to initialize...");
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(500);
                MutexBypass.KillAllMutexes();
            }

            Log($"Instance #{instanceNum} ready (PID: {process.Id})");
            OnInstanceChanged?.Invoke();
            return instance;
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            return null;
        }
    }

    public async Task LaunchMultiple(int count, QualityPreset quality, int delayMs,
        CancellationToken ct = default, IProgress<(int current, int total)>? progress = null)
    {
        for (int i = 0; i < count; i++)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report((i + 1, count));

            await LaunchOne(quality, i + 1);

            if (i < count - 1 && !ct.IsCancellationRequested)
            {
                Log($"Waiting {delayMs}ms before next...");
                await Task.Delay(delayMs, ct);
            }
        }
    }

    public void CloseInstance(InstanceInfo instance)
    {
        try
        {
            if (instance.IsRunning)
            {
                instance.Process!.Kill();
                Log($"Killed instance #{instance.InstanceNumber}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        OnInstanceChanged?.Invoke();
    }

    public void CloseAll()
    {
        foreach (var inst in _instances.Where(i => i.IsRunning).ToList())
        {
            try { inst.Process!.Kill(); } catch { }
        }
        _instances.Clear();
        _nextInstanceNum = 1;

        // Stop mutex monitor when no instances
        MutexBypass.StopMonitor();

        Log("All instances closed");
        OnInstanceChanged?.Invoke();
    }

    public void CleanupExited()
    {
        _instances.RemoveAll(i => !i.IsRunning);
        if (RunningCount == 0)
            MutexBypass.StopMonitor();
    }

    public int RunningCount => _instances.Count(i => i.IsRunning);

    private void Log(string msg) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
