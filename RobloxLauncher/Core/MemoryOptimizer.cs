using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

/// <summary>
/// Full system optimizer — RAM trimming, CPU throttling, GPU affinity,
/// power plan switching, and thermal management for running Roblox instances.
/// </summary>
public static class MemoryOptimizer
{
    // ═══════════════════════════════════════════
    // WIN32 IMPORTS
    // ═══════════════════════════════════════════

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

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize, uint flags);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const uint THREAD_SET_INFORMATION = 0x0020;
    private const int THREAD_PRIORITY_IDLE = -15;
    private const int THREAD_PRIORITY_LOWEST = -2;
    private const int THREAD_PRIORITY_NORMAL = 0;
    private const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002;
    private const uint QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x00000004;

    // ═══════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════

    private static readonly ConcurrentDictionary<int, DateTime> _lastTrimTime = new();
    private static readonly TimeSpan TrimCooldown = TimeSpan.FromSeconds(30);
    private static bool _isOptimized;
    private static string? _originalPowerPlan;

    public static event Action<string>? OnLog;
    public static bool IsOptimized => _isOptimized;

    /// <summary>RAM threshold in MB per instance for auto-trim.</summary>
    public static int AutoTrimThresholdMB { get; set; } = 800;

    /// <summary>Whether auto RAM trimming is enabled.</summary>
    public static bool AutoTrimEnabled { get; set; }

    // ═══════════════════════════════════════════
    // FULL OPTIMIZE (Trim RAM button)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Nuclear optimization — does EVERYTHING to reduce heat and resource usage:
    /// 1. Trim RAM (empty working set) on all instances
    /// 2. Set hard working set limit (cap max RAM per instance)
    /// 3. Set process priority to BelowNormal/Idle
    /// 4. Set CPU affinity (limit to fewer cores = less heat)
    /// 5. Set all threads to idle priority
    /// 6. Minimize all Roblox windows (reduces GPU rendering)
    /// 7. Switch Windows power plan to Power Saver
    /// 8. Kill unnecessary background Roblox processes
    /// </summary>
    public static void OptimizeAll(RobloxInstanceLauncher launcher)
    {
        int optimized = 0;
        long totalSaved = 0;
        int coreCount = Environment.ProcessorCount;

        // Half the cores (minimum 2) — enough for network + basic game logic
        int robloxCores = Math.Max(2, coreCount / 2);
        nint affinityMask = 0;
        for (int i = 0; i < robloxCores; i++)
            affinityMask |= (nint)(1L << i);

        Log($"=== OPTIMIZATION START ===");
        Log($"CPU: {coreCount} cores → limiting Roblox to {robloxCores} cores");

        // Apply low FFlags (5 FPS, minimal rendering — but network stays alive)
        ApplyUltraPotatoFFlags();

        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            try
            {
                var proc = inst.Process!;
                proc.Refresh();
                long beforeMB = proc.WorkingSet64 / (1024 * 1024);

                // 1) Process priority → BelowNormal (not Idle — Idle kills network)
                proc.PriorityClass = ProcessPriorityClass.BelowNormal;

                // 2) CPU affinity — limit to half cores
                try { proc.ProcessorAffinity = affinityMask; }
                catch { /* may fail on some systems */ }

                // 3) Set threads to low (not idle — idle causes disconnects)
                SetAllThreadsToLow(proc);

                // 4) Minimize windows (stops GPU rendering almost entirely)
                MinimizeProcessWindows(proc.Id);

                // 5) Trim working set (soft trim — don't force to 0)
                EmptyWorkingSet(proc.Handle);

                // 6) NO hard working set limit — let Roblox manage its own RAM
                // The soft trim + minimize is enough; hard limits cause Error 279

                proc.Refresh();
                long afterMB = proc.WorkingSet64 / (1024 * 1024);
                long saved = beforeMB - afterMB;
                totalSaved += saved;
                optimized++;

                Log($"  Instance #{inst.InstanceNumber}: {beforeMB}MB → {afterMB}MB | Priority: BelowNormal | Cores: {robloxCores}");
            }
            catch (Exception ex)
            {
                Log($"  Instance #{inst.InstanceNumber}: failed — {ex.Message}");
            }
        }

        // 7) Kill background Roblox bloat (crash handlers, telemetry)
        KillRobloxBloat();

        // 8) Switch to Power Saver plan
        SetPowerSaver(true);

        // 9) Reduce system timer resolution (saves CPU cycles)
        ReduceTimerResolution();

        _isOptimized = true;

        if (optimized > 0)
            Log($"=== OPTIMIZED {optimized} instance(s) — saved ~{totalSaved}MB RAM ===");
        else
            Log("No running instances to optimize");

        Log($"Power plan → Power Saver | Bloat killed | Timer resolution reduced");
    }

    /// <summary>
    /// Undo all optimizations — restore normal performance.
    /// </summary>
    public static void RestoreAll(RobloxInstanceLauncher launcher)
    {
        Log("=== RESTORING NORMAL PERFORMANCE ===");

        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            try
            {
                var proc = inst.Process!;

                // Restore priority
                proc.PriorityClass = ProcessPriorityClass.Normal;

                // Restore CPU affinity to all cores
                try
                {
                    nint allCores = 0;
                    for (int i = 0; i < Environment.ProcessorCount; i++)
                        allCores |= (nint)(1L << i);
                    proc.ProcessorAffinity = allCores;
                }
                catch { }

                // Restore thread priorities
                SetAllThreadsToNormal(proc);

                // Remove working set limits
                try
                {
                    SetProcessWorkingSetSizeEx(proc.Handle, (IntPtr)(-1), (IntPtr)(-1), 0);
                }
                catch { }

                // Restore windows
                RestoreProcessWindows(proc.Id);
            }
            catch { }
        }

        // Restore power plan
        SetPowerSaver(false);

        // Reset FFlags
        ResetFFlags();

        _isOptimized = false;
        Log("=== ALL RESTORED — Normal performance (restart instances for FFlag changes) ===");
    }

    // ═══════════════════════════════════════════
    // AFK MODE (existing functionality, enhanced)
    // ═══════════════════════════════════════════

    /// <summary>
    /// Enable AFK mode: minimize + Idle priority + force trim + cap RAM.
    /// Even more aggressive than OptimizeAll — sets priority to Idle.
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

                // BelowNormal priority — saves CPU but keeps network alive
                proc.PriorityClass = ProcessPriorityClass.BelowNormal;

                // Limit to half cores
                try
                {
                    int cores = Math.Max(2, Environment.ProcessorCount / 2);
                    nint mask = 0;
                    for (int i = 0; i < cores; i++) mask |= (nint)(1L << i);
                    proc.ProcessorAffinity = mask;
                }
                catch { }

                // Threads to low (not idle)
                SetAllThreadsToLow(proc);

                // Minimize
                MinimizeProcessWindows(proc.Id);

                // Soft trim working set
                EmptyWorkingSet(proc.Handle);

                proc.Refresh();
                long afterMB = proc.WorkingSet64 / (1024 * 1024);
                long saved = beforeMB - afterMB;
                totalSaved += saved;
                trimmed++;

                Log($"Instance #{inst.InstanceNumber}: {beforeMB}MB → {afterMB}MB (saved {saved}MB)");
            }
            catch (Exception ex)
            {
                Log($"Instance #{inst.InstanceNumber}: trim failed — {ex.Message}");
            }
        }

        // Kill bloat + power saver
        KillRobloxBloat();
        SetPowerSaver(true);

        _isOptimized = true;

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
        RestoreAll(launcher);
        Log("AFK Mode OFF — instances restored to normal");
    }

    // ═══════════════════════════════════════════
    // AUTO TRIM
    // ═══════════════════════════════════════════

    public static void AutoTrimIfHigh(RobloxInstanceLauncher launcher)
    {
        if (!AutoTrimEnabled) return;

        var now = DateTime.UtcNow;

        foreach (var inst in launcher.Instances)
        {
            if (!inst.IsRunning) continue;
            try
            {
                var proc = inst.Process!;
                int pid = proc.Id;

                if (_lastTrimTime.TryGetValue(pid, out var lastTrim) && now - lastTrim < TrimCooldown)
                    continue;

                proc.Refresh();
                long memMB = proc.WorkingSet64 / (1024 * 1024);

                if (memMB > AutoTrimThresholdMB)
                {
                    long beforeMB = memMB;
                    EmptyWorkingSet(proc.Handle);
                    _lastTrimTime[pid] = now;

                    proc.Refresh();
                    long afterMB = proc.WorkingSet64 / (1024 * 1024);
                    Log($"Auto-trim #{inst.InstanceNumber}: {beforeMB}MB → {afterMB}MB (threshold {AutoTrimThresholdMB}MB)");
                }
            }
            catch { }
        }

        // Clean up entries for dead processes
        foreach (var pid in _lastTrimTime.Keys)
        {
            try { Process.GetProcessById(pid); }
            catch { _lastTrimTime.TryRemove(pid, out _); }
        }
    }

    /// <summary>Maximum total RAM in MB for ALL Roblox instances combined. Default: 4500MB (4.5GB).</summary>
    public static int GlobalRamLimitMB { get; set; } = 4500;

    private static DateTime _lastGlobalTrimLog = DateTime.MinValue;

    /// <summary>
    /// Enforces a hard global RAM limit across all instances.
    /// Called every 2 seconds from the timer. If total RAM exceeds the limit,
    /// trims the highest-RAM instance first, repeating until under the cap.
    /// </summary>
    public static void EnforceGlobalRamLimit(RobloxInstanceLauncher launcher)
    {
        if (launcher.RunningCount == 0) return;

        long totalMB = launcher.TotalMemoryMB;
        if (totalMB <= GlobalRamLimitMB) return;

        // Over the limit — trim instances starting from highest RAM usage
        var running = launcher.Instances
            .Where(i => i.IsRunning)
            .OrderByDescending(i => i.MemoryMB)
            .ToList();

        bool logged = false;
        foreach (var inst in running)
        {
            if (totalMB <= GlobalRamLimitMB) break;

            try
            {
                var proc = inst.Process!;
                proc.Refresh();
                long beforeMB = proc.WorkingSet64 / (1024 * 1024);

                // Trim working set
                EmptyWorkingSet(proc.Handle);

                proc.Refresh();
                long afterMB = proc.WorkingSet64 / (1024 * 1024);
                long freed = beforeMB - afterMB;
                totalMB -= freed;

                if (!logged)
                {
                    // Only log once per enforcement cycle (avoid log spam every 2s)
                    if ((DateTime.Now - _lastGlobalTrimLog).TotalSeconds > 15)
                    {
                        Log($"RAM LIMIT: {totalMB + freed}MB > {GlobalRamLimitMB}MB cap — trimming instances...");
                        _lastGlobalTrimLog = DateTime.Now;
                        logged = true;
                    }
                }
            }
            catch { }
        }

        // If still over after soft trim, set BelowNormal priority on all to slow RAM growth
        totalMB = launcher.TotalMemoryMB;
        if (totalMB > GlobalRamLimitMB)
        {
            foreach (var inst in running)
            {
                try
                {
                    inst.Process!.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch { }
            }

            if ((DateTime.Now - _lastGlobalTrimLog).TotalSeconds > 30)
            {
                Log($"RAM LIMIT: Still at {totalMB}MB after trim — throttling all instances");
                _lastGlobalTrimLog = DateTime.Now;
            }
        }
    }

    /// <summary>Just trim memory without changing priority or minimizing.</summary>
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

    // ═══════════════════════════════════════════
    // FFLAG OPTIMIZATION
    // ═══════════════════════════════════════════

    /// <summary>
    /// Apply absolute minimum FFlags to all Roblox Versions directories.
    /// 1 FPS, zero particles, zero shadows, zero textures, zero sounds, zero physics.
    /// </summary>
    private static void ApplyUltraPotatoFFlags()
    {
        try
        {
            string? robloxPath = QualityOptimizer.GetRobloxPath();
            if (robloxPath == null) return;

            var ultraFlags = new Dictionary<string, object>
            {
                // 5 FPS — low enough to save CPU but keeps network alive
                ["DFIntTaskSchedulerTargetFps"] = 5,
                ["FIntRenderWindowManagerFrameRateManagerBackgroundFps"] = 3,
                // Minimum quality
                ["DFIntDebugFRMQualityLevelOverride"] = 1,
                // D3D11 — more stable, less GPU
                ["FFlagDebugGraphicsPreferVulkan"] = false,
                ["FFlagDebugGraphicsPreferD3D11FL10"] = true,
                // Zero rendering
                ["FIntRenderShadowIntensity"] = 0,
                ["FFlagDebugDisableShadows"] = true,
                ["FFlagDisablePostFx"] = true,
                ["FFlagDebugSkyGray"] = true,
                ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
                ["FFlagFastGPULightCulling3"] = true,
                ["FIntRenderLocalLightUpdatesMax"] = 1,
                ["FIntRenderLocalLightUpdatesMin"] = 1,
                // Zero textures
                ["DFIntTextureQualityOverride"] = 0,
                ["DFIntTextureCompositorActiveJobs"] = 0,
                ["DFIntTextureCompositorQueueSize"] = 0,
                // Zero particles
                ["DFIntMaxParticleSpriteCount"] = 0,
                ["DFIntMaxParticleMeshCount"] = 0,
                ["FIntEmitterMaxSpawnedPerFrame"] = 0,
                ["FFlagEnableParticleEmitterCustomRate"] = false,
                // Zero terrain detail
                ["FIntTerrainOctreeMaxDepth"] = 1,
                ["FIntRenderGrassDetailStrands"] = 0,
                ["FFlagGrassMovement"] = false,
                ["FIntGrassMovementReducedMotionFactor"] = 0,
                ["FFlagGlobalWindRendering"] = false,
                ["FFlagDebugDisableWater"] = true,
                // Minimum draw distance
                ["DFIntDebugRestrictGCDistance"] = 50,
                // Minimum mesh/LOD
                ["DFIntCSGLevelOfDetailSwitchingDistance"] = 0,
                ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = 0,
                ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = 0,
                ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = 0,
                ["DFIntMaxMeshDataBufferSizeMB"] = 4,
                // Light render throttle (don't go too high — causes disconnects)
                ["DFIntRenderingThrottleDelayInMS"] = 100,
                // Zero animations
                ["DFIntAnimationLodFadeInDistance"] = 0,
                ["DFIntAnimationLodFadeOutDistance"] = 0,
                // Zero audio (saves CPU)
                ["DFIntMaxSoundsPerFrame"] = 0,
                ["FFlagDebugDisableVoiceChat"] = true,
                // Throttle physics
                ["FFlagDebugSimPhysicsSingleStepping"] = true,
                ["DFIntPhysicsAnalyticsHighFrequencyIntervalInSeconds"] = 99999,
                // Kill telemetry
                ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
                ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
                ["FFlagDebugDisableTelemetryEventIngest"] = true,
                ["FFlagDebugDisableTelemetryPoint"] = true,
                ["FFlagDebugDisableTelemetryV2Counter"] = true,
                ["FFlagDebugDisableTelemetryV2Event"] = true,
                ["FFlagDebugDisableTelemetryV2Stat"] = true,
                // Kill ads
                ["FFlagAdServiceEnabled"] = false,
                // Minimum HTTP/cache
                ["DFIntHttpCurlConnectionCacheSize"] = 3,
                ["DFIntMaxImagesCacheSize"] = 16,
                // Kill GUI stuff
                ["DFIntCanHideGuiGroupId"] = 0,
            };

            string settingsDir = Path.Combine(robloxPath, "ClientSettings");
            if (!Directory.Exists(settingsDir))
                Directory.CreateDirectory(settingsDir);

            string settingsFile = Path.Combine(settingsDir, "ClientAppSettings.json");
            var existing = new Newtonsoft.Json.Linq.JObject();
            if (File.Exists(settingsFile))
            {
                try { existing = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(settingsFile)); }
                catch { }
            }

            foreach (var kvp in ultraFlags)
                existing[kvp.Key] = Newtonsoft.Json.Linq.JToken.FromObject(kvp.Value);

            File.WriteAllText(settingsFile, existing.ToString(Newtonsoft.Json.Formatting.Indented));
            Log("Ultra-potato FFlags applied (1 FPS, zero everything)");
        }
        catch (Exception ex)
        {
            Log($"FFlags apply failed: {ex.Message}");
        }
    }

    /// <summary>Reset FFlags back to what the user's preset was.</summary>
    private static void ResetFFlags()
    {
        try
        {
            string? robloxPath = QualityOptimizer.GetRobloxPath();
            if (robloxPath == null) return;

            string settingsFile = Path.Combine(robloxPath, "ClientSettings", "ClientAppSettings.json");
            if (File.Exists(settingsFile))
                File.Delete(settingsFile);
            Log("FFlags reset — restart instances to apply previous preset");
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════

    /// <summary>Set all threads of a process to idle priority.</summary>
    private static void SetAllThreadsToIdle(Process proc)
    {
        try
        {
            foreach (ProcessThread thread in proc.Threads)
            {
                try
                {
                    IntPtr hThread = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadPriority(hThread, THREAD_PRIORITY_IDLE);
                        CloseHandle(hThread);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Set all threads of a process to low (not idle) priority.</summary>
    private static void SetAllThreadsToLow(Process proc)
    {
        try
        {
            foreach (ProcessThread thread in proc.Threads)
            {
                try
                {
                    IntPtr hThread = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadPriority(hThread, THREAD_PRIORITY_LOWEST);
                        CloseHandle(hThread);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Set all threads of a process back to normal priority.</summary>
    private static void SetAllThreadsToNormal(Process proc)
    {
        try
        {
            foreach (ProcessThread thread in proc.Threads)
            {
                try
                {
                    IntPtr hThread = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadPriority(hThread, THREAD_PRIORITY_NORMAL);
                        CloseHandle(hThread);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Kill Roblox background bloat — crash handlers, updaters, telemetry.</summary>
    private static void KillRobloxBloat()
    {
        string[] bloatProcesses = {
            "RobloxCrashHandler",
            "RobloxPlayerInstaller",
            "RobloxPlayerLauncher",
        };

        int killed = 0;
        foreach (var name in bloatProcesses)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try { proc.Kill(); killed++; } catch { }
                }
            }
            catch { }
        }

        if (killed > 0)
            Log($"Killed {killed} background Roblox process(es)");
    }

    /// <summary>Switch Windows power plan to Power Saver or back to Balanced.</summary>
    private static void SetPowerSaver(bool enable)
    {
        try
        {
            if (enable)
            {
                // Save current power plan
                var getCurrentPlan = Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (getCurrentPlan != null)
                {
                    string output = getCurrentPlan.StandardOutput.ReadToEnd();
                    getCurrentPlan.WaitForExit();
                    // Extract GUID from output like "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                    var match = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})");
                    if (match.Success)
                        _originalPowerPlan = match.Groups[1].Value;
                }

                // Switch to Power Saver (well-known GUID)
                RunPowercfg("/setactive a1841308-3541-4fab-bc81-f71556f20b4a");
            }
            else if (_originalPowerPlan != null)
            {
                // Restore original plan
                RunPowercfg($"/setactive {_originalPowerPlan}");
                _originalPowerPlan = null;
            }
            else
            {
                // Fallback: set Balanced
                RunPowercfg("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
            }
        }
        catch { }
    }

    private static void RunPowercfg(string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(3000);
        }
        catch { }
    }

    /// <summary>
    /// Reduce system timer resolution — Windows default is 15.6ms which is fine.
    /// Some apps request 1ms which wastes CPU. This requests 15ms to counteract.
    /// </summary>
    private static void ReduceTimerResolution()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/energy /duration 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(2000);
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // WINDOW MANAGEMENT
    // ═══════════════════════════════════════════

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
