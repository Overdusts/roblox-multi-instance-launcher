using System.Diagnostics;

namespace RobloxLauncher.Core;

public class InstanceInfo
{
    public int InstanceNumber { get; set; }
    public Process? Process { get; set; }
    public DateTime LaunchedAt { get; set; }
    public string? ProfilePath { get; set; }
    public bool IsRunning
    {
        get
        {
            try { return Process != null && !Process.HasExited; }
            catch { return false; }
        }
    }
    public long MemoryMB
    {
        get
        {
            try
            {
                if (!IsRunning) return 0;
                Process!.Refresh(); // Force refresh to get live memory
                return Process.WorkingSet64 / (1024 * 1024);
            }
            catch { return 0; }
        }
    }
}

public class RobloxInstanceLauncher
{
    private readonly List<InstanceInfo> _instances = new();
    private bool _flagsApplied;
    private int _nextInstanceNum = 1;
    private string? _robloxPath;

    public IReadOnlyList<InstanceInfo> Instances => _instances.AsReadOnly();
    public event Action<string>? OnLog;
    public event Action? OnInstanceChanged;

    /// <summary>
    /// Base directory for per-instance profiles.
    /// Each instance gets its own LOCALAPPDATA so accounts stay separate.
    /// </summary>
    private static string ProfilesBaseDir =>
        Path.Combine(AppContext.BaseDirectory, "Profiles");

    public async Task<InstanceInfo?> LaunchOne(QualityPreset quality, int instanceNum = 0)
    {
        if (instanceNum == 0)
            instanceNum = _nextInstanceNum;
        _nextInstanceNum = instanceNum + 1;

        _robloxPath ??= QualityOptimizer.GetRobloxPath();
        if (_robloxPath == null)
        {
            Log("ERROR: Roblox installation not found");
            return null;
        }

        string playerExe = Path.Combine(_robloxPath, "RobloxPlayerBeta.exe");
        if (!File.Exists(playerExe))
        {
            Log("ERROR: RobloxPlayerBeta.exe not found");
            return null;
        }

        // Apply FFlags to the real Roblox directory once
        if (!_flagsApplied)
        {
            Log($"Applying {quality} optimization...");
            QualityOptimizer.ApplyFFlags(_robloxPath, quality);
            _flagsApplied = true;
        }

        // Create per-instance profile directory
        string profileDir = Path.Combine(ProfilesBaseDir, $"Instance{instanceNum}");
        SetupInstanceProfile(profileDir, _robloxPath);
        Log($"Instance #{instanceNum} profile: {profileDir}");

        // Start continuous mutex monitor if not running
        if (!MutexBypass.IsMonitoring)
        {
            MutexBypass.OnLog += msg => OnLog?.Invoke(msg);
            MutexBypass.StartMonitor(1000);
        }

        // Pre-kill any existing mutexes
        MutexBypass.KillAllMutexes();
        await Task.Delay(500);

        Log($"Launching instance #{instanceNum}...");
        try
        {
            // Launch with isolated LOCALAPPDATA so each instance has its own session
            var startInfo = new ProcessStartInfo
            {
                FileName = playerExe,
                UseShellExecute = false,
                WorkingDirectory = _robloxPath,
            };

            // Copy current environment and override LOCALAPPDATA
            foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
            {
                startInfo.Environment[env.Key!.ToString()!] = env.Value?.ToString();
            }
            startInfo.Environment["LOCALAPPDATA"] = profileDir;

            var process = Process.Start(startInfo);

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
                ProfilePath = profileDir,
            };

            _instances.Add(instance);

            // Monitor exit
            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); } catch { }
                Log($"Instance #{instanceNum} closed");
                OnInstanceChanged?.Invoke();
            });

            // Aggressive post-launch mutex killing
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

    /// <summary>
    /// Sets up a per-instance profile directory with proper Roblox folder structure.
    /// Copies ClientSettings (FFlags) into the instance's Roblox versions directory.
    /// </summary>
    private void SetupInstanceProfile(string profileDir, string robloxPath)
    {
        // Create the LOCALAPPDATA structure Roblox expects
        // Real path: %LOCALAPPDATA%\Roblox\Versions\version-xxx\ClientSettings
        // We replicate: profileDir\Roblox\Versions\version-xxx\ClientSettings

        string realVersionDir = Path.GetFileName(robloxPath); // e.g., version-abc123
        string realVersionsParent = Path.GetDirectoryName(robloxPath)!; // e.g., ...\Roblox\Versions
        string relativeFromLocalAppData = Path.GetRelativePath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            robloxPath);

        // Create the mirrored path inside the profile
        string instanceVersionDir = Path.Combine(profileDir, relativeFromLocalAppData);
        Directory.CreateDirectory(instanceVersionDir);

        // Copy ClientSettings (FFlags) if they exist
        string realClientSettings = Path.Combine(robloxPath, "ClientSettings");
        string instanceClientSettings = Path.Combine(instanceVersionDir, "ClientSettings");

        if (Directory.Exists(realClientSettings))
        {
            Directory.CreateDirectory(instanceClientSettings);
            foreach (var file in Directory.GetFiles(realClientSettings))
            {
                File.Copy(file, Path.Combine(instanceClientSettings, Path.GetFileName(file)), true);
            }
        }

        // Also ensure the Roblox base directory exists for cookie/session storage
        string robloxBaseInProfile = Path.Combine(profileDir, "Roblox");
        Directory.CreateDirectory(robloxBaseInProfile);
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
