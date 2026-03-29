using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RobloxLauncher.Core;
using RobloxLauncher.UI;

namespace RobloxLauncher;

public partial class Form1 : Form
{
    // ── Win32 for borderless resize ──
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int RESIZE_BORDER = 6;

    // ── Core ──
    private readonly RobloxInstanceLauncher _launcher = new();
    private readonly AntiAFK _antiAfk;
    private CancellationTokenSource? _launchCts;

    // ── UI Layout ──
    private TitleBar _titleBar = null!;
    private Panel _sidebar = null!;
    private Panel _contentArea = null!;

    // Sidebar nav buttons
    private NavButton _navLaunch = null!;
    private NavButton _navInstances = null!;
    private NavButton _navSettings = null!;

    // Pages (panels that swap in _contentArea)
    private Panel _pageLaunch = null!;
    private Panel _pageInstances = null!;
    private Panel _pageSettings = null!;

    // Launch page controls
    private StatCard _statRunning = null!;
    private StatCard _statRAM = null!;
    private StatCard _statUptime = null!;
    private ModernComboBox _cmbPreset = null!;
    private ModernTextBox _txtCount = null!;
    private ModernTextBox _txtDelay = null!;
    private ModernButton _btnLaunch = null!;
    private ModernButton _btnLaunchOne = null!;
    private ModernButton _btnStopLaunch = null!;
    private ModernButton _btnCloseAll = null!;
    private ModernLog _logBox = null!;

    // Instances page
    private ModernListView _instanceList = null!;
    private ModernButton _btnKillSelected = null!;
    private ModernButton _btnRefreshList = null!;

    // Settings page
    private CheckBox _chkAntiAfk = null!;
    private ModernTextBox _txtAfkInterval = null!;
    private ModernButton _btnApplyFlags = null!;
    private ModernButton _btnResetFlags = null!;
    private CheckBox _chkTray = null!;
    private Label _lblRobloxPath = null!;

    // Tray
    private NotifyIcon _trayIcon = null!;
    private bool _minimizeToTray;

    // Timer for live stats
    private System.Windows.Forms.Timer _statsTimer = null!;

    public Form1()
    {
        InitializeComponent();
        _antiAfk = new AntiAFK(_launcher, 60);

        SetupForm();
        BuildTitleBar();
        BuildSidebar();
        BuildContentArea();
        BuildLaunchPage();
        BuildInstancesPage();
        BuildSettingsPage();
        BuildTray();
        SetupEvents();

        // Default page
        ShowPage("launch");

        // Stats refresh timer
        _statsTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _statsTimer.Tick += (s, e) => RefreshStats();
        _statsTimer.Start();
    }

    // ═══════════════════════════════════════════
    // FORM SETUP
    // ═══════════════════════════════════════════

    private void SetupForm()
    {
        Text = "Roblox Launcher";
        Size = new Size(960, 640);
        MinimumSize = new Size(800, 540);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.BgMain;
        StartPosition = FormStartPosition.CenterScreen;

        // Dark title bar (Windows 11)
        try
        {
            int val = 1;
            DwmSetWindowAttribute(Handle, 20, ref val, sizeof(int));
        }
        catch { }
    }

    private void BuildTitleBar()
    {
        _titleBar = new TitleBar(this) { Title = "  ROBLOX LAUNCHER" };
        Controls.Add(_titleBar);
    }

    // ═══════════════════════════════════════════
    // SIDEBAR
    // ═══════════════════════════════════════════

    private void BuildSidebar()
    {
        _sidebar = new Panel
        {
            Width = 200,
            Dock = DockStyle.Left,
            BackColor = Theme.BgSidebar,
            Padding = new Padding(8, 12, 8, 12),
        };

        // Accent line at top of sidebar
        var accentLine = new Panel
        {
            Height = 2,
            Dock = DockStyle.Top,
            BackColor = Theme.Accent,
        };
        _sidebar.Controls.Add(accentLine);

        // Nav buttons
        _navLaunch = new NavButton { Text = "Launch", Icon = "\u25B6", ActiveColor = Theme.Accent, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 2) };
        _navInstances = new NavButton { Text = "Instances", Icon = "\u25A0", ActiveColor = Theme.Cyan, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };
        _navSettings = new NavButton { Text = "Settings", Icon = "\u2699", ActiveColor = Theme.Warning, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };

        _navLaunch.Click += (s, e) => ShowPage("launch");
        _navInstances.Click += (s, e) => ShowPage("instances");
        _navSettings.Click += (s, e) => ShowPage("settings");

        // Version label at bottom
        var versionLabel = new Label
        {
            Text = "v1.0 \u2022 Multi-Instance",
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 30,
        };

        _sidebar.Controls.Add(versionLabel);
        // Add in reverse order since Dock=Top stacks from top
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navInstances);
        _sidebar.Controls.Add(_navLaunch);

        // Border on right edge
        var sidebarBorder = new Panel
        {
            Width = 1,
            Dock = DockStyle.Right,
            BackColor = Theme.Border,
        };
        _sidebar.Controls.Add(sidebarBorder);

        Controls.Add(_sidebar);
    }

    // ═══════════════════════════════════════════
    // CONTENT AREA
    // ═══════════════════════════════════════════

    private void BuildContentArea()
    {
        _contentArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgMain,
            Padding = new Padding(24, 16, 24, 16),
        };
        Controls.Add(_contentArea);

        // Ensure correct z-order (titlebar on top, sidebar left, content fills rest)
        _contentArea.BringToFront();
    }

    // ═══════════════════════════════════════════
    // LAUNCH PAGE
    // ═══════════════════════════════════════════

    private void BuildLaunchPage()
    {
        _pageLaunch = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        // ── Header ──
        var header = new Label
        {
            Text = "Dashboard",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _pageLaunch.Controls.Add(header);

        // ── Stat cards row ──
        int cardY = 36;
        int cardSpacing = 12;

        _statRunning = new StatCard { Value = "0", Label = "Running", AccentColor = Theme.Success, Location = new Point(0, cardY), Size = new Size(170, 90) };
        _statRAM = new StatCard { Value = "0 MB", Label = "Total RAM", AccentColor = Theme.Accent, Location = new Point(170 + cardSpacing, cardY), Size = new Size(170, 90) };
        _statUptime = new StatCard { Value = "0:00", Label = "Longest Uptime", AccentColor = Theme.Cyan, Location = new Point(340 + cardSpacing * 2, cardY), Size = new Size(170, 90) };

        _pageLaunch.Controls.Add(_statRunning);
        _pageLaunch.Controls.Add(_statRAM);
        _pageLaunch.Controls.Add(_statUptime);

        // ── Quick launch settings ──
        int settingsY = cardY + 104;

        var lblPreset = MakeLabel("Quality Preset:", 0, settingsY + 5);
        _cmbPreset = new ModernComboBox { Location = new Point(120, settingsY), Size = new Size(160, 34) };
        _cmbPreset.Items.AddRange(new object[] { "Potato (Max Instances)", "Low", "Medium", "Default" });
        _cmbPreset.SelectedIndex = 0;

        var lblCount = MakeLabel("Instances:", 300, settingsY + 5);
        _txtCount = new ModernTextBox { Location = new Point(400, settingsY), Size = new Size(60, 34), Text = "3" };

        var lblDelay = MakeLabel("Delay (ms):", 480, settingsY + 5);
        _txtDelay = new ModernTextBox { Location = new Point(570, settingsY), Size = new Size(80, 34), Text = "5000" };

        _pageLaunch.Controls.Add(lblPreset);
        _pageLaunch.Controls.Add(_cmbPreset);
        _pageLaunch.Controls.Add(lblCount);
        _pageLaunch.Controls.Add(_txtCount);
        _pageLaunch.Controls.Add(lblDelay);
        _pageLaunch.Controls.Add(_txtDelay);

        // ── Action buttons ──
        int btnY = settingsY + 48;

        _btnLaunch = new ModernButton { Text = "\u25B6  Launch All", ButtonColor = Theme.Accent, Size = new Size(140, 38), Location = new Point(0, btnY) };
        _btnLaunchOne = new ModernButton { Text = "+1 Instance", ButtonColor = Theme.Cyan, Size = new Size(120, 38), Location = new Point(152, btnY) };
        _btnStopLaunch = new ModernButton { Text = "\u25A0  Stop", ButtonColor = Theme.Warning, Outlined = true, Size = new Size(100, 38), Location = new Point(284, btnY) };
        _btnCloseAll = new ModernButton { Text = "Close All", ButtonColor = Theme.Danger, Outlined = true, Size = new Size(100, 38), Location = new Point(396, btnY) };

        _btnLaunch.Click += OnLaunchAll;
        _btnLaunchOne.Click += OnLaunchOne;
        _btnStopLaunch.Click += OnStopLaunch;
        _btnCloseAll.Click += OnCloseAll;

        _pageLaunch.Controls.Add(_btnLaunch);
        _pageLaunch.Controls.Add(_btnLaunchOne);
        _pageLaunch.Controls.Add(_btnStopLaunch);
        _pageLaunch.Controls.Add(_btnCloseAll);

        // ── Log box ──
        int logY = btnY + 50;
        _logBox = new ModernLog { Location = new Point(0, logY), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        _pageLaunch.Controls.Add(_logBox);

        // Resize handler to keep log filling remaining space
        _pageLaunch.Resize += (s, e) =>
        {
            _logBox.Size = new Size(_pageLaunch.ClientSize.Width, Math.Max(100, _pageLaunch.ClientSize.Height - logY));
        };
    }

    // ═══════════════════════════════════════════
    // INSTANCES PAGE
    // ═══════════════════════════════════════════

    private void BuildInstancesPage()
    {
        _pageInstances = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var header = new Label
        {
            Text = "Running Instances",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _pageInstances.Controls.Add(header);

        // Button bar
        int btnY = 36;
        _btnKillSelected = new ModernButton { Text = "Kill Selected", ButtonColor = Theme.Danger, Size = new Size(130, 36), Location = new Point(0, btnY) };
        _btnRefreshList = new ModernButton { Text = "Refresh", ButtonColor = Theme.AccentDim, Outlined = true, Size = new Size(100, 36), Location = new Point(142, btnY) };

        _btnKillSelected.Click += OnKillSelected;
        _btnRefreshList.Click += (s, e) => RefreshInstanceList();

        _pageInstances.Controls.Add(_btnKillSelected);
        _pageInstances.Controls.Add(_btnRefreshList);

        // Instance list
        int listY = btnY + 46;
        _instanceList = new ModernListView
        {
            Location = new Point(0, listY),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        _instanceList.Columns.Add("#", 50);
        _instanceList.Columns.Add("PID", 80);
        _instanceList.Columns.Add("RAM (MB)", 100);
        _instanceList.Columns.Add("Uptime", 120);
        _instanceList.Columns.Add("Status", 100);

        _pageInstances.Controls.Add(_instanceList);

        _pageInstances.Resize += (s, e) =>
        {
            _instanceList.Size = new Size(_pageInstances.ClientSize.Width, Math.Max(100, _pageInstances.ClientSize.Height - listY));
        };
    }

    // ═══════════════════════════════════════════
    // SETTINGS PAGE
    // ═══════════════════════════════════════════

    private void BuildSettingsPage()
    {
        _pageSettings = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var header = new Label
        {
            Text = "Settings",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _pageSettings.Controls.Add(header);

        int y = 44;
        int sectionGap = 36;

        // ── Anti-AFK Section ──
        var lblAfk = new Label { Text = "ANTI-AFK", Font = Theme.FontSubtitle, ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblAfk);
        y += 28;

        _chkAntiAfk = MakeCheckBox("Enable Anti-AFK", 0, y);
        _chkAntiAfk.CheckedChanged += (s, e) =>
        {
            if (_chkAntiAfk.Checked) _antiAfk.Start();
            else _antiAfk.Stop();
        };
        _pageSettings.Controls.Add(_chkAntiAfk);
        y += 30;

        var lblInterval = MakeLabel("Interval (seconds):", 0, y + 4);
        _txtAfkInterval = new ModernTextBox { Location = new Point(160, y), Size = new Size(70, 34), Text = "60" };
        var btnApplyInterval = new ModernButton { Text = "Apply", ButtonColor = Theme.Cyan, Size = new Size(70, 32), Location = new Point(240, y + 1) };
        btnApplyInterval.Click += (s, e) =>
        {
            if (int.TryParse(_txtAfkInterval.Text, out int sec) && sec >= 10)
            {
                _antiAfk.IntervalSeconds = sec;
                if (_antiAfk.Enabled) { _antiAfk.Stop(); _antiAfk.Start(); }
                Log($"Anti-AFK interval set to {sec}s");
            }
        };
        _pageSettings.Controls.Add(lblInterval);
        _pageSettings.Controls.Add(_txtAfkInterval);
        _pageSettings.Controls.Add(btnApplyInterval);
        y += sectionGap + 16;

        // ── FFlags Section ──
        var lblFlags = new Label { Text = "FFLAGS", Font = Theme.FontSubtitle, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblFlags);
        y += 28;

        _btnApplyFlags = new ModernButton { Text = "Apply Potato FFlags", ButtonColor = Theme.Accent, Size = new Size(170, 36), Location = new Point(0, y) };
        _btnResetFlags = new ModernButton { Text = "Reset FFlags", ButtonColor = Theme.Danger, Outlined = true, Size = new Size(130, 36), Location = new Point(182, y) };

        _btnApplyFlags.Click += (s, e) =>
        {
            string? path = QualityOptimizer.GetRobloxPath();
            if (path != null) { QualityOptimizer.ApplyFFlags(path, QualityPreset.Potato); Log("FFlags applied (Potato)"); }
            else Log("ERROR: Roblox not found");
        };
        _btnResetFlags.Click += (s, e) =>
        {
            string? path = QualityOptimizer.GetRobloxPath();
            if (path != null) { QualityOptimizer.ResetFFlags(path); Log("FFlags reset to default"); }
            else Log("ERROR: Roblox not found");
        };
        _pageSettings.Controls.Add(_btnApplyFlags);
        _pageSettings.Controls.Add(_btnResetFlags);
        y += sectionGap + 16;

        // ── System Section ──
        var lblSystem = new Label { Text = "SYSTEM", Font = Theme.FontSubtitle, ForeColor = Theme.Warning, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblSystem);
        y += 28;

        _chkTray = MakeCheckBox("Minimize to system tray", 0, y);
        _chkTray.CheckedChanged += (s, e) => _minimizeToTray = _chkTray.Checked;
        _pageSettings.Controls.Add(_chkTray);
        y += 34;

        var lblPathTitle = MakeLabel("Roblox Path:", 0, y + 2);
        string rPath = QualityOptimizer.GetRobloxPath() ?? "Not found";
        _lblRobloxPath = new Label
        {
            Text = rPath,
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            Size = new Size(500, 20),
            Location = new Point(100, y + 3),
        };
        _pageSettings.Controls.Add(lblPathTitle);
        _pageSettings.Controls.Add(_lblRobloxPath);
    }

    // ═══════════════════════════════════════════
    // SYSTEM TRAY
    // ═══════════════════════════════════════════

    private void BuildTray()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "Roblox Launcher",
            Icon = SystemIcons.Application,
            Visible = false,
        };

        var trayMenu = new ContextMenuStrip();
        trayMenu.BackColor = Theme.BgCard;
        trayMenu.ForeColor = Theme.TextPrimary;
        trayMenu.Items.Add("Show", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; _trayIcon.Visible = false; });
        trayMenu.Items.Add("Close All Instances", null, (s, e) => _launcher.CloseAll());
        trayMenu.Items.Add("-");
        trayMenu.Items.Add("Exit", null, (s, e) => { _trayIcon.Visible = false; Application.Exit(); });

        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; _trayIcon.Visible = false; };
    }

    // ═══════════════════════════════════════════
    // EVENTS
    // ═══════════════════════════════════════════

    private void SetupEvents()
    {
        _launcher.OnLog += msg => BeginInvoke(() => { _logBox.AddLog(msg); });
        _launcher.OnInstanceChanged += () => BeginInvoke(() => { RefreshStats(); RefreshInstanceList(); });
        _antiAfk.OnLog += msg => BeginInvoke(() => { _logBox.AddLog(msg); });
    }

    // ═══════════════════════════════════════════
    // PAGE NAVIGATION
    // ═══════════════════════════════════════════

    private void ShowPage(string page)
    {
        _navLaunch.IsActive = page == "launch";
        _navInstances.IsActive = page == "instances";
        _navSettings.IsActive = page == "settings";
        _navLaunch.Invalidate();
        _navInstances.Invalidate();
        _navSettings.Invalidate();

        _contentArea.SuspendLayout();
        _contentArea.Controls.Clear();

        switch (page)
        {
            case "launch":
                _contentArea.Controls.Add(_pageLaunch);
                // Trigger resize to fix log box size
                _pageLaunch.Size = _contentArea.ClientSize;
                break;
            case "instances":
                _contentArea.Controls.Add(_pageInstances);
                _pageInstances.Size = _contentArea.ClientSize;
                RefreshInstanceList();
                break;
            case "settings":
                _contentArea.Controls.Add(_pageSettings);
                _pageSettings.Size = _contentArea.ClientSize;
                break;
        }

        _contentArea.ResumeLayout();
    }

    // ═══════════════════════════════════════════
    // BUTTON HANDLERS
    // ═══════════════════════════════════════════

    private async void OnLaunchAll(object? sender, EventArgs e)
    {
        if (!int.TryParse(_txtCount.Text, out int count) || count < 1) count = 3;
        if (!int.TryParse(_txtDelay.Text, out int delay) || delay < 1000) delay = 5000;
        var preset = GetSelectedPreset();

        _launchCts = new CancellationTokenSource();
        _btnLaunch.Enabled = false;
        _btnLaunchOne.Enabled = false;

        try
        {
            await _launcher.LaunchMultiple(count, preset, delay, _launchCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Launch sequence stopped");
        }
        finally
        {
            _btnLaunch.Enabled = true;
            _btnLaunchOne.Enabled = true;
        }
    }

    private async void OnLaunchOne(object? sender, EventArgs e)
    {
        var preset = GetSelectedPreset();
        _btnLaunchOne.Enabled = false;
        await _launcher.LaunchOne(preset);
        _btnLaunchOne.Enabled = true;
    }

    private void OnStopLaunch(object? sender, EventArgs e)
    {
        _launchCts?.Cancel();
        Log("Stopping launch sequence...");
    }

    private void OnCloseAll(object? sender, EventArgs e)
    {
        _launchCts?.Cancel();
        _antiAfk.Stop();
        _launcher.CloseAll();
    }

    private void OnKillSelected(object? sender, EventArgs e)
    {
        var selected = _instanceList.CheckedItems.Cast<ListViewItem>().ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            int idx = item.Index;
            if (idx < _launcher.Instances.Count)
            {
                _launcher.CloseInstance(_launcher.Instances[idx]);
            }
        }
        RefreshInstanceList();
    }

    // ═══════════════════════════════════════════
    // STATS & REFRESH
    // ═══════════════════════════════════════════

    private void RefreshStats()
    {
        _launcher.CleanupExited();
        int running = _launcher.RunningCount;
        long totalRam = 0;
        TimeSpan longest = TimeSpan.Zero;

        foreach (var inst in _launcher.Instances)
        {
            if (!inst.IsRunning) continue;
            totalRam += inst.MemoryMB;
            var uptime = DateTime.Now - inst.LaunchedAt;
            if (uptime > longest) longest = uptime;
        }

        _statRunning.Value = running.ToString();
        _statRAM.Value = $"{totalRam} MB";
        _statUptime.Value = longest.TotalHours >= 1
            ? $"{(int)longest.TotalHours}h {longest.Minutes:D2}m"
            : $"{longest.Minutes}:{longest.Seconds:D2}";

        _titleBar.Title = $"  ROBLOX LAUNCHER  \u2022  {running} running";
    }

    private void RefreshInstanceList()
    {
        _instanceList.Items.Clear();
        foreach (var inst in _launcher.Instances)
        {
            var item = new ListViewItem(inst.InstanceNumber.ToString());
            bool alive = inst.IsRunning;
            item.SubItems.Add(alive ? inst.Process!.Id.ToString() : "-");
            item.SubItems.Add(alive ? inst.MemoryMB.ToString() : "-");
            var uptime = alive ? DateTime.Now - inst.LaunchedAt : TimeSpan.Zero;
            item.SubItems.Add(alive ? $"{(int)uptime.TotalMinutes}:{uptime.Seconds:D2}" : "-");
            item.SubItems.Add(alive ? "Running" : "Closed");
            item.ForeColor = alive ? Theme.Success : Theme.TextMuted;
            _instanceList.Items.Add(item);
        }
    }

    // ═══════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════

    private QualityPreset GetSelectedPreset()
    {
        return _cmbPreset.SelectedIndex switch
        {
            0 => QualityPreset.Potato,
            1 => QualityPreset.Low,
            2 => QualityPreset.Medium,
            _ => QualityPreset.Default,
        };
    }

    private void Log(string msg) => _logBox.AddLog($"[{DateTime.Now:HH:mm:ss}] {msg}");

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Font = Theme.FontBody,
        ForeColor = Theme.TextSecondary,
        AutoSize = true,
        Location = new Point(x, y),
    };

    private static CheckBox MakeCheckBox(string text, int x, int y) => new()
    {
        Text = text,
        Font = Theme.FontBody,
        ForeColor = Theme.TextPrimary,
        AutoSize = true,
        Location = new Point(x, y),
        FlatStyle = FlatStyle.Flat,
    };

    // ═══════════════════════════════════════════
    // BORDERLESS RESIZE
    // ═══════════════════════════════════════════

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var pos = PointToClient(new Point(m.LParam.ToInt32()));
            int w = ClientSize.Width, h = ClientSize.Height;

            if (pos.Y <= RESIZE_BORDER)
            {
                if (pos.X <= RESIZE_BORDER) m.Result = (IntPtr)HTTOPLEFT;
                else if (pos.X >= w - RESIZE_BORDER) m.Result = (IntPtr)HTTOPRIGHT;
                else m.Result = (IntPtr)HTTOP;
            }
            else if (pos.Y >= h - RESIZE_BORDER)
            {
                if (pos.X <= RESIZE_BORDER) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (pos.X >= w - RESIZE_BORDER) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else m.Result = (IntPtr)HTBOTTOM;
            }
            else if (pos.X <= RESIZE_BORDER)
                m.Result = (IntPtr)HTLEFT;
            else if (pos.X >= w - RESIZE_BORDER)
                m.Result = (IntPtr)HTRIGHT;

            return;
        }
        base.WndProc(ref m);
    }

    // ═══════════════════════════════════════════
    // MINIMIZE TO TRAY & CLEANUP
    // ═══════════════════════════════════════════

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_minimizeToTray && WindowState == FormWindowState.Minimized)
        {
            Hide();
            _trayIcon.Visible = true;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statsTimer?.Stop();
        _antiAfk?.Dispose();
        MutexBypass.StopMonitor();
        _trayIcon?.Dispose();
        base.OnFormClosing(e);
    }
}
