using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxLauncher.Core;

public static class MutexBypass
{
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_CLOSE_SOURCE = 0x1;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const int CNST_SYSTEM_HANDLE_INFORMATION_EX = 64;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass, IntPtr info, uint size, out uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle,
        IntPtr targetProcess, out IntPtr targetHandle, uint access, bool inheritHandle, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, int infoClass, IntPtr info, uint infoLength, out uint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    private static readonly string[] MutexNames = new[]
    {
        "ROBLOX_singletonMutex",
        "RobloxSingletonMutex",
        "roblox_singleton",
    };

    // ─── Continuous monitor ───
    private static CancellationTokenSource? _monitorCts;
    private static Task? _monitorTask;
    public static event Action<string>? OnLog;
    public static bool IsMonitoring => _monitorTask != null && !_monitorTask.IsCompleted;

    /// <summary>
    /// Starts a background thread that continuously monitors and kills
    /// the Roblox singleton mutex every intervalMs milliseconds.
    /// This is how Fishtrap does it — constant monitoring.
    /// </summary>
    public static void StartMonitor(int intervalMs = 1500)
    {
        if (IsMonitoring) return;

        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;

        _monitorTask = Task.Run(async () =>
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Mutex monitor started (checking every {intervalMs}ms)");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int killed = KillAllMutexes();
                    if (killed > 0)
                        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Killed {killed} mutex handle(s)");
                }
                catch { }

                try { await Task.Delay(intervalMs, ct); } catch (OperationCanceledException) { break; }
            }

            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] Mutex monitor stopped");
        }, ct);
    }

    public static void StopMonitor()
    {
        _monitorCts?.Cancel();
        _monitorCts = null;
        _monitorTask = null;
    }

    /// <summary>
    /// Kills ALL Roblox singleton mutexes across ALL Roblox processes.
    /// Returns how many handles were closed.
    /// </summary>
    public static int KillAllMutexes()
    {
        int totalClosed = 0;
        var robloxProcesses = Process.GetProcessesByName("RobloxPlayerBeta");
        if (robloxProcesses.Length == 0) return 0;

        // Get all system handles once
        var handleData = GetSystemHandles();
        if (handleData == IntPtr.Zero) return 0;

        try
        {
            long handleCount = Marshal.ReadInt64(handleData);
            int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();

            // Build set of Roblox PIDs for fast lookup
            var robloxPids = new HashSet<long>(robloxProcesses.Select(p => (long)p.Id));

            // Open handles to each Roblox process
            var processHandles = new Dictionary<int, IntPtr>();
            foreach (var proc in robloxProcesses)
            {
                var h = OpenProcess(PROCESS_DUP_HANDLE, false, proc.Id);
                if (h != IntPtr.Zero)
                    processHandles[proc.Id] = h;
            }

            try
            {
                for (long i = 0; i < handleCount; i++)
                {
                    var entry = Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                        handleData + IntPtr.Size * 2 + (int)(i * entrySize));

                    int pid = (int)entry.UniqueProcessId.ToInt64();
                    if (!robloxPids.Contains(pid)) continue;
                    if (!processHandles.TryGetValue(pid, out var procHandle)) continue;

                    // Skip handles that are likely not mutexes (by granted access)
                    // Mutant (mutex) objects typically have 0x0001 (MUTANT_QUERY_STATE) or 0x001F0001
                    if (entry.GrantedAccess == 0x0012019F || entry.GrantedAccess == 0x00100000)
                        continue; // These are file/key handles, skip

                    if (DuplicateHandle(procHandle, entry.HandleValue,
                        Process.GetCurrentProcess().Handle, out IntPtr dupHandle, 0, false, 0))
                    {
                        try
                        {
                            string? name = GetObjectName(dupHandle);
                            if (name != null && MutexNames.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                            {
                                CloseHandle(dupHandle);
                                dupHandle = IntPtr.Zero;

                                // Close in the target process
                                DuplicateHandle(procHandle, entry.HandleValue,
                                    IntPtr.Zero, out _, 0, false, DUPLICATE_CLOSE_SOURCE);
                                totalClosed++;
                            }
                        }
                        finally
                        {
                            if (dupHandle != IntPtr.Zero)
                                CloseHandle(dupHandle);
                        }
                    }
                }
            }
            finally
            {
                foreach (var h in processHandles.Values)
                    CloseHandle(h);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(handleData);
        }

        return totalClosed;
    }

    private static IntPtr GetSystemHandles()
    {
        uint size = 0x100000; // 1MB initial
        IntPtr info = Marshal.AllocHGlobal((int)size);

        while (true)
        {
            uint status = NtQuerySystemInformation(CNST_SYSTEM_HANDLE_INFORMATION_EX, info, size, out uint length);
            if (status == STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(info);
                size = length + 0x100000;
                info = Marshal.AllocHGlobal((int)size);
                continue;
            }
            if (status != 0)
            {
                Marshal.FreeHGlobal(info);
                return IntPtr.Zero;
            }
            return info;
        }
    }

    private static string? GetObjectName(IntPtr handle)
    {
        uint length = 0;
        NtQueryObject(handle, 1, IntPtr.Zero, 0, out length);
        if (length == 0) length = 1024;

        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (NtQueryObject(handle, 1, buffer, length, out _) != 0)
                return null;

            int nameLength = Marshal.ReadInt16(buffer);
            if (nameLength <= 0)
                return null;

            IntPtr namePtr = Marshal.ReadIntPtr(buffer + IntPtr.Size);
            return Marshal.PtrToStringUni(namePtr, nameLength / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Legacy single-shot (kept for compatibility)
    public static bool KillMutex() => KillAllMutexes() > 0;
}
