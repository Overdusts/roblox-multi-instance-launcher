using System.Diagnostics;

namespace RobloxLauncher.Core;

public class InstanceInfo
{
    public int InstanceNumber { get; set; }
    public Process? Process { get; set; }
    public DateTime LaunchedAt { get; set; }
    public RobloxAccount? Account { get; set; }

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

    public double CpuPercent
    {
        get
        {
            try
            {
                if (!IsRunning) return 0;
                Process!.Refresh();
                return Process.TotalProcessorTime.TotalMilliseconds /
                       (Environment.ProcessorCount * (DateTime.Now - LaunchedAt).TotalMilliseconds) * 100;
            }
            catch { return 0; }
        }
    }

    public string AccountLabel => Account?.Label ?? "No Account";
}

public class RobloxInstanceLauncher
{
    private readonly List<InstanceInfo> _instances = new();
    private bool _flagsApplied;
    private int _nextInstanceNum = 1;

    public IReadOnlyList<InstanceInfo> Instances => _instances.AsReadOnly();
    public event Action<string>? OnLog;
    public event Action? OnInstanceChanged;

    /// <summary>
    /// Launch a Roblox instance. If account is provided, uses auth ticket for automatic login.
    /// </summary>
    public async Task<InstanceInfo?> LaunchOne(QualityPreset quality, int instanceNum = 0,
        RobloxAccount? account = null, AccountManager? accountManager = null)
    {
        if (instanceNum == 0)
            instanceNum = _nextInstanceNum;
        _nextInstanceNum = instanceNum + 1;

        string? robloxPath = QualityOptimizer.GetRobloxPath();
        if (robloxPath == null)
        {
            Log("ERROR: Roblox not found");
            return null;
        }

        string playerExe = Path.Combine(robloxPath, "RobloxPlayerBeta.exe");
        if (!File.Exists(playerExe))
        {
            Log($"ERROR: exe not found at {playerExe}");
            return null;
        }

        if (!_flagsApplied)
        {
            Log($"Applying {quality} optimization...");
            QualityOptimizer.ApplyFFlags(robloxPath, quality);
            _flagsApplied = true;
        }

        if (!MutexBypass.IsMonitoring)
        {
            MutexBypass.OnLog += msg => OnLog?.Invoke(msg);
            MutexBypass.StartMonitor(1000);
        }

        MutexBypass.KillAllMutexes();
        await Task.Delay(500);

        // Get auth ticket if account is provided
        string? authTicket = null;
        if (account != null && accountManager != null)
        {
            Log($"Getting auth ticket for {account.Label}...");
            authTicket = await accountManager.GetAuthTicket(account);
            if (authTicket == null)
            {
                Log($"WARNING: Failed to get auth ticket for {account.Label} — launching without account");
            }
            else
            {
                Log($"Auth ticket obtained for {account.Label} (length: {authTicket.Length})");
            }
        }

        string accountLabel = account?.Label ?? "Guest";
        Log($"Launching instance #{instanceNum} ({accountLabel})...");

        try
        {
            // Create isolated LOCALAPPDATA per instance with junction to shared Versions
            string realLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string instanceAppData = Path.Combine(realLocalAppData, "RobloxInstances", $"instance{instanceNum}");
            string instanceRobloxDir = Path.Combine(instanceAppData, "Roblox");
            string realRobloxDir = Path.Combine(realLocalAppData, "Roblox");

            // Set up isolated directory with junction to shared binaries
            SetupInstanceIsolation(instanceRobloxDir, realRobloxDir, instanceNum);

            // Also apply FFlags to the instance's Roblox path (via the junction they'll land in the real dir)
            // Copy ClientSettings from real Versions dir so FFlags work
            CopyClientSettings(realRobloxDir, instanceRobloxDir);

            ProcessStartInfo psi;
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string browserTrackerId = Random.Shared.NextInt64(100000000000, 999999999999).ToString();

            if (authTicket != null)
            {
                // Build the protocol URI string and pass it as an argument to the exe directly
                // This way we can use UseShellExecute=false to set LOCALAPPDATA
                string protocolArg = $"roblox-player:1+launchmode:app+gameinfo:{authTicket}" +
                    $"+launchtime:{launchTime}+browsertrackerid:{browserTrackerId}" +
                    $"+robloxLocale:en_us+gameLocale:en_us+channel:";

                Log($"Launching with auth ticket + isolated profile...");
                psi = new ProcessStartInfo
                {
                    FileName = playerExe,
                    Arguments = protocolArg,
                    UseShellExecute = false,
                    WorkingDirectory = robloxPath,
                };
                psi.Environment["LOCALAPPDATA"] = instanceAppData;
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = playerExe,
                    Arguments = "--app",
                    UseShellExecute = false,
                    WorkingDirectory = robloxPath,
                };
                psi.Environment["LOCALAPPDATA"] = instanceAppData;
            }

            var process = Process.Start(psi);

            if (process == null)
            {
                Log("ERROR: Failed to start Roblox process");
                return null;
            }

            var instance = new InstanceInfo
            {
                InstanceNumber = instanceNum,
                Process = process,
                LaunchedAt = DateTime.Now,
                Account = account,
            };
            _instances.Add(instance);

            _ = Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); } catch { }
                Log($"Instance #{instanceNum} ({accountLabel}) closed");
                OnInstanceChanged?.Invoke();
            });

            Log($"Killing mutexes...");
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(500);
                MutexBypass.KillAllMutexes();
            }

            Log($"Instance #{instanceNum} ready — {accountLabel} (PID: {process.Id})");
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

    /// <summary>
    /// Launch multiple instances, one per account.
    /// </summary>
    public async Task LaunchAccounts(IEnumerable<RobloxAccount> accounts, QualityPreset quality,
        int delayMs, AccountManager accountManager, CancellationToken ct = default)
    {
        int num = 1;
        foreach (var account in accounts)
        {
            if (ct.IsCancellationRequested) break;
            await LaunchOne(quality, num++, account, accountManager);
            if (!ct.IsCancellationRequested)
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
        catch (Exception ex) { Log($"Error: {ex.Message}"); }
        OnInstanceChanged?.Invoke();
    }

    public void CloseAll()
    {
        foreach (var inst in _instances.Where(i => i.IsRunning).ToList())
            try { inst.Process!.Kill(); } catch { }
        _instances.Clear();
        _nextInstanceNum = 1;
        MutexBypass.StopMonitor();
        Log("All instances closed");
        OnInstanceChanged?.Invoke();
    }

    public void CleanupExited()
    {
        _instances.RemoveAll(i => !i.IsRunning);
        if (RunningCount == 0) MutexBypass.StopMonitor();
    }

    public int RunningCount => _instances.Count(i => i.IsRunning);

    public long TotalMemoryMB => _instances.Where(i => i.IsRunning).Sum(i => i.MemoryMB);

    public double TotalCpuPercent => _instances.Where(i => i.IsRunning).Sum(i => i.CpuPercent);

    /// <summary>
    /// Create isolated Roblox data directory per instance.
    /// Uses a junction so Versions/ (binaries) is shared, but session data is separate.
    /// </summary>
    private void SetupInstanceIsolation(string instanceRobloxDir, string realRobloxDir, int instanceNum)
    {
        try
        {
            string instanceVersions = Path.Combine(instanceRobloxDir, "Versions");
            string realVersions = Path.Combine(realRobloxDir, "Versions");

            if (!Directory.Exists(instanceRobloxDir))
            {
                Directory.CreateDirectory(instanceRobloxDir);
                Log($"Created isolated profile dir for instance #{instanceNum}");
            }

            // Create junction: instanceDir\Roblox\Versions → realDir\Roblox\Versions
            // This shares the binaries without duplicating ~500MB per instance
            if (!Directory.Exists(instanceVersions) && Directory.Exists(realVersions))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{instanceVersions}\" \"{realVersions}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var p = Process.Start(psi);
                p?.WaitForExit(5000);
                Log($"Junction created: Versions → shared binaries");
            }

            // Copy essential config files from real Roblox dir (not Versions)
            foreach (var file in Directory.GetFiles(realRobloxDir))
            {
                string destFile = Path.Combine(instanceRobloxDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                {
                    try { File.Copy(file, destFile); } catch { }
                }
            }

            // Copy GlobalSettings subdirectories (not Versions)
            foreach (var dir in Directory.GetDirectories(realRobloxDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Equals("Versions", StringComparison.OrdinalIgnoreCase)) continue;

                string destDir = Path.Combine(instanceRobloxDir, dirName);
                if (!Directory.Exists(destDir))
                {
                    try
                    {
                        Directory.CreateDirectory(destDir);
                        foreach (var f in Directory.GetFiles(dir))
                            File.Copy(f, Path.Combine(destDir, Path.GetFileName(f)));
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"WARNING: Instance isolation setup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy ClientSettings (FFlags) into the instance's Versions dir via the junction.
    /// </summary>
    private void CopyClientSettings(string realRobloxDir, string instanceRobloxDir)
    {
        try
        {
            string realVersions = Path.Combine(realRobloxDir, "Versions");
            if (!Directory.Exists(realVersions)) return;

            // FFlags are already applied to the real Versions dir, and the junction points there
            // So they'll be shared automatically — no extra copy needed
        }
        catch { }
    }

    private void Log(string msg) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
}
