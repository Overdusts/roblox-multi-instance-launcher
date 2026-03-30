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
                Process!.Refresh();
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
    private bool _backupCreated;

    // Lock so only one instance launches at a time (junction swap is not concurrent-safe)
    private static readonly SemaphoreSlim _launchLock = new(1, 1);

    public IReadOnlyList<InstanceInfo> Instances => _instances.AsReadOnly();
    public event Action<string>? OnLog;
    public event Action? OnInstanceChanged;

    /// <summary>
    /// Profiles stored next to the tool (F: drive), NOT on C: drive.
    /// </summary>
    private static string ProfilesBaseDir =>
        Path.Combine(AppContext.BaseDirectory, "Profiles");

    /// <summary>
    /// The real Roblox directory: %LOCALAPPDATA%\Roblox
    /// </summary>
    private static string RobloxDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    /// <summary>
    /// Backup of the original Roblox directory (before we start swapping)
    /// </summary>
    private static string RobloxBackupDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox_original");

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

        // Apply FFlags once
        if (!_flagsApplied)
        {
            Log($"Applying {quality} optimization...");
            QualityOptimizer.ApplyFFlags(_robloxPath, quality);
            _flagsApplied = true;
        }

        // Backup original Roblox directory (first time only)
        if (!_backupCreated)
        {
            BackupRobloxDir();
            _backupCreated = true;
        }

        // Prepare this instance's profile
        string profileDir = Path.Combine(ProfilesBaseDir, $"Instance{instanceNum}");
        SetupInstanceProfile(profileDir, _robloxPath);

        // Start mutex monitor
        if (!MutexBypass.IsMonitoring)
        {
            MutexBypass.OnLog += msg => OnLog?.Invoke(msg);
            MutexBypass.StartMonitor(1000);
        }

        MutexBypass.KillAllMutexes();
        await Task.Delay(500);

        // Acquire launch lock — only one instance can swap the junction at a time
        await _launchLock.WaitAsync();
        try
        {
            Log($"Launching instance #{instanceNum} (profile: Instance{instanceNum})...");

            // Swap junction: %LOCALAPPDATA%\Roblox -> this instance's profile
            SwapJunction(profileDir);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = playerExe,
                UseShellExecute = true,
                WorkingDirectory = _robloxPath,
            });

            if (process == null)
            {
                Log("ERROR: Failed to start Roblox");
                RestoreOriginal();
                return null;
            }

            // Wait for Roblox to read its data (auth, settings) from the junction
            Log($"Instance #{instanceNum}: waiting for initialization...");
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                MutexBypass.KillAllMutexes();
            }

            // Restore original so the next instance can launch
            RestoreOriginal();

            var instance = new InstanceInfo
            {
                InstanceNumber = instanceNum,
                Process = process,
                LaunchedAt = DateTime.Now,
                ProfilePath = profileDir,
            };

            _instances.Add(instance);

            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); } catch { }
                Log($"Instance #{instanceNum} closed");
                OnInstanceChanged?.Invoke();
            });

            Log($"Instance #{instanceNum} ready (PID: {process.Id})");
            OnInstanceChanged?.Invoke();
            return instance;
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            RestoreOriginal();
            return null;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Backup the real %LOCALAPPDATA%\Roblox directory.
    /// Only done once — subsequent launches swap junctions.
    /// </summary>
    private void BackupRobloxDir()
    {
        // If backup already exists, the original was already saved
        if (Directory.Exists(RobloxBackupDir))
        {
            Log("Original Roblox data already backed up");
            return;
        }

        if (Directory.Exists(RobloxDataDir))
        {
            // Check if it's a junction (from a previous session) — delete it
            var info = new DirectoryInfo(RobloxDataDir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(RobloxDataDir, false);
                Log("Cleaned up stale junction");
                return;
            }

            // Real directory — rename to backup
            Directory.Move(RobloxDataDir, RobloxBackupDir);
            Log("Backed up original Roblox data");
        }
    }

    /// <summary>
    /// Create a directory junction: %LOCALAPPDATA%\Roblox -> target profile dir.
    /// Roblox will see this as its real data directory.
    /// </summary>
    private void SwapJunction(string profileDir)
    {
        // Remove current junction or directory at the target path
        if (Directory.Exists(RobloxDataDir))
        {
            var info = new DirectoryInfo(RobloxDataDir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // It's a junction — just delete the link (not the target)
                Directory.Delete(RobloxDataDir, false);
            }
            else
            {
                // Real directory somehow appeared — move it out
                string temp = RobloxDataDir + "_temp_" + Environment.TickCount;
                Directory.Move(RobloxDataDir, temp);
            }
        }

        // Create junction: %LOCALAPPDATA%\Roblox -> profileDir\Roblox
        string robloxInProfile = Path.Combine(profileDir, "Roblox");
        Directory.CreateDirectory(robloxInProfile);

        // Create junction using mklink /j
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /j \"{RobloxDataDir}\" \"{robloxInProfile}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi);
        proc?.WaitForExit(5000);

        if (Directory.Exists(RobloxDataDir))
            Log($"Junction created -> Instance profile");
        else
            Log("WARNING: Failed to create junction");
    }

    /// <summary>
    /// Restore the original Roblox directory after an instance has started.
    /// </summary>
    private void RestoreOriginal()
    {
        try
        {
            // Remove junction
            if (Directory.Exists(RobloxDataDir))
            {
                var info = new DirectoryInfo(RobloxDataDir);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(RobloxDataDir, false);
            }

            // Restore backup
            if (Directory.Exists(RobloxBackupDir) && !Directory.Exists(RobloxDataDir))
            {
                Directory.Move(RobloxBackupDir, RobloxDataDir);
                Log("Restored original Roblox data");
            }
        }
        catch (Exception ex)
        {
            Log($"WARNING: Could not restore original: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up a per-instance profile directory.
    ///
    /// Structure: profileDir/Roblox/
    ///   - Versions -> JUNCTION to Roblox_original/Versions (so exe is accessible)
    ///   - LocalStorage/ (per-instance cookies/auth)
    ///   - GlobalBasicSettings_13.xml (per-instance settings)
    ///
    /// The key: Versions is a junction back to the real install so the exe works,
    /// but auth/cookies in LocalStorage are isolated per instance.
    /// </summary>
    private void SetupInstanceProfile(string profileDir, string robloxPath)
    {
        string robloxInProfile = Path.Combine(profileDir, "Roblox");
        Directory.CreateDirectory(robloxInProfile);

        // Create junction: profileDir\Roblox\Versions -> real Versions directory
        // This makes the exe accessible through the junction chain
        string realVersionsDir = Path.GetDirectoryName(robloxPath)!; // e.g., C:\...\Roblox\Versions or from backup
        string profileVersionsDir = Path.Combine(robloxInProfile, "Versions");

        // Use backup path if it exists (since we renamed the original)
        string backupVersionsDir = Path.Combine(RobloxBackupDir, "Versions");
        string sourceVersions = Directory.Exists(backupVersionsDir) ? backupVersionsDir : realVersionsDir;

        if (!Directory.Exists(profileVersionsDir))
        {
            // Create junction to real Versions directory
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /j \"{profileVersionsDir}\" \"{sourceVersions}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        else
        {
            // Check if existing Versions is a junction — if not, replace it
            var info = new DirectoryInfo(profileVersionsDir);
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // It's a real directory from old setup — remove and re-create as junction
                try { Directory.Delete(profileVersionsDir, true); } catch { }
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /j \"{profileVersionsDir}\" \"{sourceVersions}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
            }
        }

        // Copy GlobalBasicSettings if exists
        string origSettings = Path.Combine(RobloxBackupDir, "GlobalBasicSettings_13.xml");
        if (!File.Exists(origSettings))
            origSettings = Path.Combine(RobloxDataDir, "GlobalBasicSettings_13.xml");
        if (File.Exists(origSettings))
        {
            string dest = Path.Combine(robloxInProfile, "GlobalBasicSettings_13.xml");
            if (!File.Exists(dest))
                File.Copy(origSettings, dest, false);
        }

        // Ensure LocalStorage directory exists (this is where auth cookies go)
        Directory.CreateDirectory(Path.Combine(robloxInProfile, "LocalStorage"));
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

        // Make sure original is restored
        RestoreOriginal();

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
