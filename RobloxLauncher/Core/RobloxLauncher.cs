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
    private string? _robloxExePath;  // Full path to RobloxPlayerBeta.exe (resolved ONCE)
    private string? _robloxVersionDir; // The version directory containing the exe
    private string? _realVersionsDir;  // The real Versions directory (never changes)

    private static readonly SemaphoreSlim _launchLock = new(1, 1);

    public IReadOnlyList<InstanceInfo> Instances => _instances.AsReadOnly();
    public event Action<string>? OnLog;
    public event Action? OnInstanceChanged;

    /// <summary>
    /// Profiles stored next to the tool (F: drive), NOT on C: drive.
    /// </summary>
    private static string ProfilesBaseDir =>
        Path.Combine(AppContext.BaseDirectory, "Profiles");

    /// <summary>%LOCALAPPDATA%\Roblox</summary>
    private static string RobloxDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");

    /// <summary>%LOCALAPPDATA%\Roblox_original (backup)</summary>
    private static string RobloxBackupDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox_original");

    public async Task<InstanceInfo?> LaunchOne(QualityPreset quality, int instanceNum = 0)
    {
        if (instanceNum == 0)
            instanceNum = _nextInstanceNum;
        _nextInstanceNum = instanceNum + 1;

        // Resolve Roblox paths ONCE and cache them
        if (_robloxExePath == null)
        {
            string? robloxPath = QualityOptimizer.GetRobloxPath();
            if (robloxPath == null)
            {
                Log("ERROR: Roblox installation not found");
                return null;
            }

            _robloxVersionDir = robloxPath;
            _robloxExePath = Path.Combine(robloxPath, "RobloxPlayerBeta.exe");
            _realVersionsDir = Path.GetDirectoryName(robloxPath)!;

            if (!File.Exists(_robloxExePath))
            {
                Log($"ERROR: RobloxPlayerBeta.exe not found at {_robloxExePath}");
                _robloxExePath = null;
                return null;
            }

            Log($"Roblox found: {_robloxExePath}");
        }

        // Apply FFlags once
        if (!_flagsApplied)
        {
            Log($"Applying {quality} optimization...");
            QualityOptimizer.ApplyFFlags(_robloxVersionDir!, quality);
            _flagsApplied = true;
        }

        // Prepare this instance's profile
        string profileDir = Path.Combine(ProfilesBaseDir, $"Instance{instanceNum}");
        SetupInstanceProfile(profileDir);

        // Start mutex monitor
        if (!MutexBypass.IsMonitoring)
        {
            MutexBypass.OnLog += msg => OnLog?.Invoke(msg);
            MutexBypass.StartMonitor(1000);
        }

        MutexBypass.KillAllMutexes();
        await Task.Delay(500);

        // Acquire launch lock — junction swap is not concurrent-safe
        await _launchLock.WaitAsync();
        try
        {
            Log($"Launching instance #{instanceNum} (profile: Instance{instanceNum})...");

            // Swap: %LOCALAPPDATA%\Roblox -> this instance's profile
            BackupAndSwap(profileDir);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = _robloxExePath,
                UseShellExecute = true,
                WorkingDirectory = _robloxVersionDir,
            });

            if (process == null)
            {
                Log("ERROR: Failed to start Roblox");
                Restore();
                return null;
            }

            // Wait for Roblox to read its data directory
            Log($"Instance #{instanceNum}: initializing...");
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);
                MutexBypass.KillAllMutexes();
            }

            // Restore original directory
            Restore();

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
            Restore();
            return null;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Backup real Roblox dir and create junction to profile.
    /// </summary>
    private void BackupAndSwap(string profileDir)
    {
        string robloxInProfile = Path.Combine(profileDir, "Roblox");

        // Step 1: If Roblox dir exists and is real (not a junction), back it up
        if (Directory.Exists(RobloxDataDir))
        {
            var info = new DirectoryInfo(RobloxDataDir);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Already a junction from previous run — remove it
                Directory.Delete(RobloxDataDir, false);
            }
            else
            {
                // Real directory — rename to backup
                if (Directory.Exists(RobloxBackupDir))
                {
                    // Backup already exists — don't overwrite it
                    // This means we crashed before restoring last time
                    // Just delete the current one (it should be identical to backup)
                    Log("WARNING: Backup already exists, removing current dir");
                    try { Directory.Delete(RobloxDataDir, true); }
                    catch
                    {
                        // Can't delete while Roblox is running — just rename
                        string temp = RobloxDataDir + "_old";
                        if (Directory.Exists(temp)) try { Directory.Delete(temp, true); } catch { }
                        Directory.Move(RobloxDataDir, temp);
                    }
                }
                else
                {
                    Directory.Move(RobloxDataDir, RobloxBackupDir);
                    Log("Backed up original Roblox data");
                }
            }
        }

        // Step 2: Create junction %LOCALAPPDATA%\Roblox -> profileDir\Roblox
        RunMklink(RobloxDataDir, robloxInProfile);

        if (Directory.Exists(RobloxDataDir))
            Log("Junction active -> Instance profile");
        else
            Log("WARNING: Junction creation failed!");
    }

    /// <summary>
    /// Restore original Roblox directory.
    /// </summary>
    private void Restore()
    {
        try
        {
            // Remove junction
            if (Directory.Exists(RobloxDataDir))
            {
                var info = new DirectoryInfo(RobloxDataDir);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    Directory.Delete(RobloxDataDir, false);
                // If it's real, don't touch it
            }

            // Move backup back
            if (Directory.Exists(RobloxBackupDir) && !Directory.Exists(RobloxDataDir))
            {
                Directory.Move(RobloxBackupDir, RobloxDataDir);
                Log("Restored original Roblox data");
            }
        }
        catch (Exception ex)
        {
            Log($"WARNING: Restore failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up a per-instance profile directory.
    /// Creates profileDir/Roblox/ with a Versions junction back to the real install
    /// so the exe is accessible, while LocalStorage is per-instance for separate auth.
    /// </summary>
    private void SetupInstanceProfile(string profileDir)
    {
        string robloxInProfile = Path.Combine(profileDir, "Roblox");
        Directory.CreateDirectory(robloxInProfile);

        // Junction: profileDir/Roblox/Versions -> real Versions dir
        // This is key: the exe lives in Versions, so it must be accessible
        string profileVersions = Path.Combine(robloxInProfile, "Versions");
        string realVersions = _realVersionsDir!;

        // Use backup location if the real one is already renamed
        if (Directory.Exists(Path.Combine(RobloxBackupDir, "Versions")))
            realVersions = Path.Combine(RobloxBackupDir, "Versions");

        if (!Directory.Exists(profileVersions))
        {
            RunMklink(profileVersions, realVersions);
        }
        else
        {
            // Ensure it's a junction, not a real directory
            var info = new DirectoryInfo(profileVersions);
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                try { Directory.Delete(profileVersions, true); } catch { }
                RunMklink(profileVersions, realVersions);
            }
        }

        // Copy GlobalBasicSettings (per-instance)
        string[] settingsFiles = { "GlobalBasicSettings_13.xml", "GlobalSettings_13.xml" };
        foreach (var file in settingsFiles)
        {
            string src = Path.Combine(RobloxBackupDir, file);
            if (!File.Exists(src)) src = Path.Combine(RobloxDataDir, file);
            if (File.Exists(src))
            {
                string dest = Path.Combine(robloxInProfile, file);
                if (!File.Exists(dest))
                    try { File.Copy(src, dest, false); } catch { }
            }
        }

        // Ensure LocalStorage exists (per-instance auth cookies)
        Directory.CreateDirectory(Path.Combine(robloxInProfile, "LocalStorage"));
    }

    private static void RunMklink(string link, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /j \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
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
        Restore();

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
