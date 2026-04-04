using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RobloxLauncher.Core;
using RobloxLauncher.UI;

namespace RobloxLauncher;

public partial class Form1 : Form
{
    // ── Win32 ──
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Anti-sleep: prevent system from going to sleep
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private const int WM_NCHITTEST = 0x84;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int RESIZE_BORDER = 6;

    // ── Core ──
    private readonly RobloxInstanceLauncher _launcher = new();
    private readonly AntiAFK _antiAfk;
    private readonly AccountManager _accountManager;
    private CancellationTokenSource? _launchCts;
    private bool _antiSleepEnabled;

    // ── UI Layout ──
    private TitleBar _titleBar = null!;
    private Panel _sidebar = null!;
    private Panel _contentArea = null!;

    private NavButton _navLaunch = null!;
    private NavButton _navAccounts = null!;
    private NavButton _navInstances = null!;
    private NavButton _navSettings = null!;

    private Panel _pageLaunch = null!;
    private Panel _pageAccounts = null!;
    private Panel _pageInstances = null!;
    private Panel _pageSettings = null!;

    // Launch page
    private StatCard _statRunning = null!;
    private StatCard _statRAM = null!;
    private StatCard _statCPU = null!;
    private StatCard _statUptime = null!;
    private ModernComboBox _cmbPreset = null!;
    private ModernTextBox _txtCount = null!;
    private ModernTextBox _txtDelay = null!;
    private ModernButton _btnLaunch = null!;
    private ModernButton _btnLaunchOne = null!;
    private ModernButton _btnStopLaunch = null!;
    private ModernButton _btnCloseAll = null!;
    private ModernButton _btnAfkMode = null!;
    private ModernButton _btnTrimRam = null!;
    private bool _afkModeOn;
    private ModernLog _logBox = null!;

    // Accounts page
    private ModernListView _accountList = null!;
    private ModernButton _btnAddAccount = null!;
    private ModernButton _btnRemoveAccount = null!;
    private ModernButton _btnValidateAccount = null!;
    private ModernButton _btnLaunchAccount = null!;
    private ModernButton _btnLaunchAllAccounts = null!;

    // Instances page
    private ModernListView _instanceList = null!;
    private ModernButton _btnKillSelected = null!;
    private ModernButton _btnRefreshList = null!;

    // Settings page
    private CheckBox _chkAntiAfk = null!;
    private ModernTextBox _txtAfkInterval = null!;
    private CheckBox _chkAntiSleep = null!;
    private CheckBox _chkAutoTrim = null!;
    private ModernTextBox _txtTrimThreshold = null!;
    private CheckBox _chkTray = null!;
    private Label _lblRobloxPath = null!;

    // Tray
    private NotifyIcon _trayIcon = null!;
    private bool _minimizeToTray;

    private System.Windows.Forms.Timer _statsTimer = null!;

    public Form1()
    {
        InitializeComponent();
        _antiAfk = new AntiAFK(_launcher, 45);

        // Accounts saved next to the exe
        string accountsPath = Path.Combine(AppContext.BaseDirectory, "accounts.json");
        _accountManager = new AccountManager(accountsPath);
        _accountManager.OnLog += msg => Log(msg);

        SetupForm();
        BuildTitleBar();
        BuildSidebar();
        BuildContentArea();
        BuildLaunchPage();
        BuildAccountsPage();
        BuildInstancesPage();
        BuildSettingsPage();
        BuildTray();
        SetupEvents();

        ShowPage("launch");

        _statsTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _statsTimer.Tick += (s, e) =>
        {
            RefreshStats();
            if (_pageInstances.Parent != null)
                RefreshInstanceList();
            if (_pageAccounts.Parent != null)
                RefreshAccountList();

            // Keep anti-sleep alive
            if (_antiSleepEnabled)
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);

            // Periodic RAM trim in AFK mode
            if (_afkModeOn)
                MemoryOptimizer.TrimAll(_launcher);

            // Auto trim high RAM instances
            MemoryOptimizer.AutoTrimIfHigh(_launcher);

            // HARD 4.5GB global RAM limit — trim aggressively if exceeded
            MemoryOptimizer.EnforceGlobalRamLimit(_launcher);
        };
        _statsTimer.Start();
    }

    // ═══════════════════════════════════════════
    // FORM SETUP
    // ═══════════════════════════════════════════

    private void SetupForm()
    {
        Text = "Roblox Launcher";
        Size = new Size(1040, 700);
        MinimumSize = new Size(900, 600);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.BgMain;
        StartPosition = FormStartPosition.CenterScreen;

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

        var accentLine = new Panel { Height = 2, Dock = DockStyle.Top, BackColor = Theme.Accent };
        _sidebar.Controls.Add(accentLine);

        _navLaunch = new NavButton { Text = "Launch", Icon = "\u25B6", ActiveColor = Theme.Accent, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 2) };
        _navAccounts = new NavButton { Text = "Accounts", Icon = "\u263A", ActiveColor = Theme.Success, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };
        _navInstances = new NavButton { Text = "Instances", Icon = "\u25A0", ActiveColor = Theme.Cyan, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };
        _navSettings = new NavButton { Text = "Settings", Icon = "\u2699", ActiveColor = Theme.Warning, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2) };

        _navLaunch.Click += (s, e) => ShowPage("launch");
        _navAccounts.Click += (s, e) => ShowPage("accounts");
        _navInstances.Click += (s, e) => ShowPage("instances");
        _navSettings.Click += (s, e) => ShowPage("settings");

        var versionLabel = new Label
        {
            Text = "v2.0 \u2022 Marvel Rivals",
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleCenter,
            Height = 30,
        };

        _sidebar.Controls.Add(versionLabel);
        _sidebar.Controls.Add(_navSettings);
        _sidebar.Controls.Add(_navInstances);
        _sidebar.Controls.Add(_navAccounts);
        _sidebar.Controls.Add(_navLaunch);

        var sidebarBorder = new Panel { Width = 1, Dock = DockStyle.Right, BackColor = Theme.Border };
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
        _contentArea.BringToFront();
    }

    // ═══════════════════════════════════════════
    // LAUNCH PAGE
    // ═══════════════════════════════════════════

    private void BuildLaunchPage()
    {
        _pageLaunch = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var header = new Label
        {
            Text = "Dashboard",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _pageLaunch.Controls.Add(header);

        int cardY = 36;
        int cardW = 155;
        int cardSpacing = 10;

        _statRunning = new StatCard { Value = "0", Label = "Running", AccentColor = Theme.Success, Location = new Point(0, cardY), Size = new Size(cardW, 90) };
        _statRAM = new StatCard { Value = "0 MB", Label = "Total RAM", AccentColor = Theme.Accent, Location = new Point(cardW + cardSpacing, cardY), Size = new Size(cardW, 90) };
        _statCPU = new StatCard { Value = "0%", Label = "Total CPU", AccentColor = Theme.Warning, Location = new Point((cardW + cardSpacing) * 2, cardY), Size = new Size(cardW, 90) };
        _statUptime = new StatCard { Value = "0:00", Label = "Longest Uptime", AccentColor = Theme.Cyan, Location = new Point((cardW + cardSpacing) * 3, cardY), Size = new Size(cardW, 90) };

        _pageLaunch.Controls.Add(_statRunning);
        _pageLaunch.Controls.Add(_statRAM);
        _pageLaunch.Controls.Add(_statCPU);
        _pageLaunch.Controls.Add(_statUptime);

        int settingsY = cardY + 104;

        var lblPreset = MakeLabel("Quality Preset:", 0, settingsY + 5);
        _cmbPreset = new ModernComboBox { Location = new Point(120, settingsY), Size = new Size(200, 34) };
        _cmbPreset.Items.AddRange(new object[]
        {
            "Marvel Rivals (Balanced)",
            "Marvel Rivals (Potato)",
            "Potato (Max Instances)",
            "Low",
            "Medium",
            "Default"
        });
        _cmbPreset.SelectedIndex = 1; // Default to Marvel Rivals (Potato) — max savings

        var lblCount = MakeLabel("Instances:", 340, settingsY + 5);
        _txtCount = new ModernTextBox { Location = new Point(420, settingsY), Size = new Size(60, 34), Text = "3" };

        var lblDelay = MakeLabel("Delay (ms):", 500, settingsY + 5);
        _txtDelay = new ModernTextBox { Location = new Point(590, settingsY), Size = new Size(80, 34), Text = "8000" };

        _pageLaunch.Controls.Add(lblPreset);
        _pageLaunch.Controls.Add(_cmbPreset);
        _pageLaunch.Controls.Add(lblCount);
        _pageLaunch.Controls.Add(_txtCount);
        _pageLaunch.Controls.Add(lblDelay);
        _pageLaunch.Controls.Add(_txtDelay);

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

        // ── AFK Mode buttons (second row) ──
        int btnY2 = btnY + 46;
        _btnAfkMode = new ModernButton { Text = "AFK Mode", ButtonColor = Theme.Success, Size = new Size(120, 36), Location = new Point(0, btnY2) };
        _btnTrimRam = new ModernButton { Text = "\u26A1 Optimize", ButtonColor = Theme.Cyan, Size = new Size(120, 36), Location = new Point(132, btnY2) };

        _btnAfkMode.Click += (s, e) =>
        {
            _afkModeOn = !_afkModeOn;
            if (_afkModeOn)
            {
                MemoryOptimizer.EnableAfkMode(_launcher);
                _btnAfkMode.Text = "Exit AFK";
                _btnAfkMode.ButtonColor = Theme.Warning;
            }
            else
            {
                MemoryOptimizer.DisableAfkMode(_launcher);
                _btnAfkMode.Text = "AFK Mode";
                _btnAfkMode.ButtonColor = Theme.Success;
            }
            _btnAfkMode.Invalidate();
        };

        _btnTrimRam.Click += (s, e) =>
        {
            if (!MemoryOptimizer.IsOptimized)
            {
                MemoryOptimizer.OptimizeAll(_launcher);
                _btnTrimRam.Text = "\u26A1 Restore";
                _btnTrimRam.ButtonColor = Theme.Warning;
            }
            else
            {
                MemoryOptimizer.RestoreAll(_launcher);
                _btnTrimRam.Text = "\u26A1 Optimize";
                _btnTrimRam.ButtonColor = Theme.Cyan;
            }
            _btnTrimRam.Invalidate();
        };

        _pageLaunch.Controls.Add(_btnAfkMode);
        _pageLaunch.Controls.Add(_btnTrimRam);

        int logY = btnY2 + 46;
        _logBox = new ModernLog { Location = new Point(0, logY), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        _pageLaunch.Controls.Add(_logBox);

        _pageLaunch.Resize += (s, e) =>
        {
            _logBox.Size = new Size(_pageLaunch.ClientSize.Width, Math.Max(100, _pageLaunch.ClientSize.Height - logY));
        };
    }

    // ═══════════════════════════════════════════
    // ACCOUNTS PAGE
    // ═══════════════════════════════════════════

    private void BuildAccountsPage()
    {
        _pageAccounts = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var header = new Label
        {
            Text = "Accounts",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        _pageAccounts.Controls.Add(header);

        var subHeader = new Label
        {
            Text = "Add accounts to launch specific instances. Each account gets its own Roblox session.",
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(0, 24),
        };
        _pageAccounts.Controls.Add(subHeader);

        int btnY = 48;
        _btnAddAccount = new ModernButton { Text = "+ Add Account", ButtonColor = Theme.Success, Size = new Size(140, 36), Location = new Point(0, btnY) };
        _btnRemoveAccount = new ModernButton { Text = "Remove", ButtonColor = Theme.Danger, Outlined = true, Size = new Size(100, 36), Location = new Point(152, btnY) };
        _btnValidateAccount = new ModernButton { Text = "Validate", ButtonColor = Theme.Cyan, Outlined = true, Size = new Size(100, 36), Location = new Point(264, btnY) };
        _btnLaunchAccount = new ModernButton { Text = "\u25B6  Launch Selected", ButtonColor = Theme.Accent, Size = new Size(160, 36), Location = new Point(376, btnY) };
        _btnLaunchAllAccounts = new ModernButton { Text = "\u25B6  Launch All Accs", ButtonColor = Theme.AccentDim, Size = new Size(160, 36), Location = new Point(548, btnY) };

        _btnAddAccount.Click += OnAddAccount;
        _btnRemoveAccount.Click += OnRemoveAccount;
        _btnValidateAccount.Click += OnValidateAccount;
        _btnLaunchAccount.Click += OnLaunchSelectedAccounts;
        _btnLaunchAllAccounts.Click += OnLaunchAllAccounts;

        _pageAccounts.Controls.Add(_btnAddAccount);
        _pageAccounts.Controls.Add(_btnRemoveAccount);
        _pageAccounts.Controls.Add(_btnValidateAccount);
        _pageAccounts.Controls.Add(_btnLaunchAccount);
        _pageAccounts.Controls.Add(_btnLaunchAllAccounts);

        int listY = btnY + 46;
        _accountList = new ModernListView
        {
            Location = new Point(0, listY),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        _accountList.Columns.Add("#", 40);
        _accountList.Columns.Add("Username", 140);
        _accountList.Columns.Add("Display Name", 140);
        _accountList.Columns.Add("User ID", 100);
        _accountList.Columns.Add("Status", 100);
        _accountList.Columns.Add("Last Validated", 150);

        _pageAccounts.Controls.Add(_accountList);

        _pageAccounts.Resize += (s, e) =>
        {
            _accountList.Size = new Size(_pageAccounts.ClientSize.Width, Math.Max(100, _pageAccounts.ClientSize.Height - listY));
        };

        RefreshAccountList();
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

        int btnY = 36;
        _btnKillSelected = new ModernButton { Text = "Kill Selected", ButtonColor = Theme.Danger, Size = new Size(130, 36), Location = new Point(0, btnY) };
        _btnRefreshList = new ModernButton { Text = "Refresh", ButtonColor = Theme.AccentDim, Outlined = true, Size = new Size(100, 36), Location = new Point(142, btnY) };

        _btnKillSelected.Click += OnKillSelected;
        _btnRefreshList.Click += (s, e) => RefreshInstanceList();

        _pageInstances.Controls.Add(_btnKillSelected);
        _pageInstances.Controls.Add(_btnRefreshList);

        int listY = btnY + 46;
        _instanceList = new ModernListView
        {
            Location = new Point(0, listY),
            CheckBoxes = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        _instanceList.Columns.Add("#", 40);
        _instanceList.Columns.Add("Account", 130);
        _instanceList.Columns.Add("PID", 70);
        _instanceList.Columns.Add("RAM (MB)", 90);
        _instanceList.Columns.Add("CPU %", 70);
        _instanceList.Columns.Add("Uptime", 100);
        _instanceList.Columns.Add("Status", 80);

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
        _pageSettings = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true };

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

        // ── Anti-AFK ──
        var lblAfk = new Label { Text = "ANTI-AFK", Font = Theme.FontSubtitle, ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblAfk);
        y += 28;

        _chkAntiAfk = MakeCheckBox("Enable Anti-AFK", 0, y);
        _chkAntiAfk.CheckedChanged += (s, e) =>
        {
            if (_chkAntiAfk.Checked)
            {
                _antiAfk.Start();
                Log("Anti-AFK enabled");
            }
            else
            {
                _antiAfk.Stop();
                Log("Anti-AFK disabled");
            }
        };
        _pageSettings.Controls.Add(_chkAntiAfk);
        y += 32;

        var lblInterval = MakeLabel("Interval (sec):", 0, y + 4);
        _txtAfkInterval = new ModernTextBox { Location = new Point(120, y), Size = new Size(70, 34), Text = "45" };
        var btnApplyInterval = new ModernButton { Text = "Apply", ButtonColor = Theme.Cyan, Size = new Size(70, 32), Location = new Point(200, y + 1) };
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
        y += 52;

        // ── Anti-Sleep ──
        var lblSleep = new Label { Text = "ANTI-SLEEP", Font = Theme.FontSubtitle, ForeColor = Theme.Success, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblSleep);
        y += 28;

        _chkAntiSleep = MakeCheckBox("Prevent laptop from sleeping", 0, y);
        _chkAntiSleep.CheckedChanged += (s, e) =>
        {
            _antiSleepEnabled = _chkAntiSleep.Checked;
            if (_antiSleepEnabled)
            {
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
                Log("Anti-Sleep ON — laptop will stay awake");
            }
            else
            {
                SetThreadExecutionState(ES_CONTINUOUS); // Reset to normal
                Log("Anti-Sleep OFF — normal power settings");
            }
        };
        _pageSettings.Controls.Add(_chkAntiSleep);
        y += 52;

        // ── Auto RAM Trim ──
        var lblAutoTrim = new Label { Text = "AUTO RAM TRIM", Font = Theme.FontSubtitle, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblAutoTrim);
        y += 28;

        _chkAutoTrim = MakeCheckBox("Auto-trim when RAM exceeds threshold", 0, y);
        _chkAutoTrim.CheckedChanged += (s, e) =>
        {
            MemoryOptimizer.AutoTrimEnabled = _chkAutoTrim.Checked;
            Log(MemoryOptimizer.AutoTrimEnabled ? "Auto RAM Trim ON" : "Auto RAM Trim OFF");
        };
        _pageSettings.Controls.Add(_chkAutoTrim);
        y += 32;

        var lblThreshold = MakeLabel("Threshold (MB):", 0, y + 4);
        _txtTrimThreshold = new ModernTextBox { Location = new Point(120, y), Size = new Size(80, 34), Text = "800" };
        var btnApplyThreshold = new ModernButton { Text = "Apply", ButtonColor = Theme.Accent, Size = new Size(70, 32), Location = new Point(210, y + 1) };
        btnApplyThreshold.Click += (s, e) =>
        {
            if (int.TryParse(_txtTrimThreshold.Text, out int mb) && mb >= 100)
            {
                MemoryOptimizer.AutoTrimThresholdMB = mb;
                Log($"Auto-trim threshold set to {mb}MB");
            }
        };
        _pageSettings.Controls.Add(lblThreshold);
        _pageSettings.Controls.Add(_txtTrimThreshold);
        _pageSettings.Controls.Add(btnApplyThreshold);
        y += 52;

        // ── FFlags ──
        var lblFlags = new Label { Text = "FFLAGS", Font = Theme.FontSubtitle, ForeColor = Theme.Accent, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblFlags);
        y += 28;

        var btnApplyMR = new ModernButton { Text = "Apply Marvel Rivals", ButtonColor = Theme.Success, Size = new Size(170, 36), Location = new Point(0, y) };
        var btnApplyPotato = new ModernButton { Text = "Apply Potato", ButtonColor = Theme.Accent, Size = new Size(130, 36), Location = new Point(182, y) };
        var btnResetFlags = new ModernButton { Text = "Reset FFlags", ButtonColor = Theme.Danger, Outlined = true, Size = new Size(130, 36), Location = new Point(324, y) };

        btnApplyMR.Click += (s, e) =>
        {
            string? path = QualityOptimizer.GetRobloxPath();
            if (path != null) { QualityOptimizer.ApplyFFlags(path, QualityPreset.MarvelRivals); Log("FFlags applied (Marvel Rivals)"); }
            else Log("ERROR: Roblox not found");
        };
        btnApplyPotato.Click += (s, e) =>
        {
            string? path = QualityOptimizer.GetRobloxPath();
            if (path != null) { QualityOptimizer.ApplyFFlags(path, QualityPreset.Potato); Log("FFlags applied (Potato)"); }
            else Log("ERROR: Roblox not found");
        };
        btnResetFlags.Click += (s, e) =>
        {
            string? path = QualityOptimizer.GetRobloxPath();
            if (path != null) { QualityOptimizer.ResetFFlags(path); Log("FFlags reset to default"); }
            else Log("ERROR: Roblox not found");
        };
        _pageSettings.Controls.Add(btnApplyMR);
        _pageSettings.Controls.Add(btnApplyPotato);
        _pageSettings.Controls.Add(btnResetFlags);
        y += 52;

        // ── System ──
        var lblSystem = new Label { Text = "SYSTEM", Font = Theme.FontSubtitle, ForeColor = Theme.Warning, AutoSize = true, Location = new Point(0, y) };
        _pageSettings.Controls.Add(lblSystem);
        y += 28;

        _chkTray = MakeCheckBox("Minimize to system tray", 0, y);
        _chkTray.CheckedChanged += (s, e) =>
        {
            _minimizeToTray = _chkTray.Checked;
            Log(_minimizeToTray ? "Minimize to tray ON" : "Minimize to tray OFF");
        };
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
    // TRAY
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
        _launcher.OnLog += msg => BeginInvoke(() => _logBox.AddLog(msg));
        _launcher.OnInstanceChanged += () => BeginInvoke(() => { RefreshStats(); RefreshInstanceList(); });
        _antiAfk.OnLog += msg => BeginInvoke(() => _logBox.AddLog(msg));
        MemoryOptimizer.OnLog += msg => BeginInvoke(() => _logBox.AddLog(msg));
    }

    // ═══════════════════════════════════════════
    // PAGE NAVIGATION
    // ═══════════════════════════════════════════

    private void ShowPage(string page)
    {
        _navLaunch.IsActive = page == "launch";
        _navAccounts.IsActive = page == "accounts";
        _navInstances.IsActive = page == "instances";
        _navSettings.IsActive = page == "settings";
        _navLaunch.Invalidate();
        _navAccounts.Invalidate();
        _navInstances.Invalidate();
        _navSettings.Invalidate();

        _contentArea.SuspendLayout();
        _contentArea.Controls.Clear();

        switch (page)
        {
            case "launch":
                _contentArea.Controls.Add(_pageLaunch);
                _pageLaunch.Size = _contentArea.ClientSize;
                break;
            case "accounts":
                _contentArea.Controls.Add(_pageAccounts);
                _pageAccounts.Size = _contentArea.ClientSize;
                RefreshAccountList();
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
    // BUTTON HANDLERS — LAUNCH
    // ═══════════════════════════════════════════

    private async void OnLaunchAll(object? sender, EventArgs e)
    {
        if (!int.TryParse(_txtCount.Text, out int count) || count < 1) count = 3;
        if (!int.TryParse(_txtDelay.Text, out int delay) || delay < 1000) delay = 8000;
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
        if (_chkAntiAfk != null) _chkAntiAfk.Checked = false;
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
                _launcher.CloseInstance(_launcher.Instances[idx]);
        }
        RefreshInstanceList();
    }

    // ═══════════════════════════════════════════
    // BUTTON HANDLERS — ACCOUNTS
    // ═══════════════════════════════════════════

    private async void OnAddAccount(object? sender, EventArgs e)
    {
        using var dialog = new LoginDialog();
        var result = dialog.ShowDialog(this);

        if (result == DialogResult.OK && !string.IsNullOrEmpty(dialog.CapturedCookie))
        {
            Log("Validating account cookie...");
            var account = await _accountManager.ValidateCookie(dialog.CapturedCookie);
            if (account != null)
            {
                _accountManager.AddAccount(account);
                Log($"Account added: {account.Label} (ID: {account.UserId})");
                RefreshAccountList();
            }
            else
            {
                Log("ERROR: Cookie validation failed — account not added");
            }
        }
    }

    private void OnRemoveAccount(object? sender, EventArgs e)
    {
        var selected = _accountList.CheckedItems.Cast<ListViewItem>().ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            int idx = item.Index;
            if (idx < _accountManager.Accounts.Count)
            {
                var acc = _accountManager.Accounts[idx];
                _accountManager.RemoveAccount(acc);
                Log($"Removed account: {acc.Label}");
            }
        }
        RefreshAccountList();
    }

    private async void OnValidateAccount(object? sender, EventArgs e)
    {
        var selected = _accountList.CheckedItems.Cast<ListViewItem>().ToList();
        if (selected.Count == 0)
        {
            // Validate all
            Log("Validating all accounts...");
            foreach (var acc in _accountManager.Accounts.ToList())
            {
                var validated = await _accountManager.ValidateCookie(acc.Cookie);
                if (validated != null)
                {
                    acc.IsValid = true;
                    acc.LastValidated = DateTime.Now;
                    acc.Username = validated.Username;
                    acc.DisplayName = validated.DisplayName;
                    Log($"  {acc.Label} — Valid");
                }
                else
                {
                    acc.IsValid = false;
                    Log($"  {acc.Label} — INVALID");
                }
            }
            _accountManager.Save();
        }
        else
        {
            foreach (var item in selected)
            {
                int idx = item.Index;
                if (idx >= _accountManager.Accounts.Count) continue;
                var acc = _accountManager.Accounts[idx];
                Log($"Validating {acc.Label}...");
                var validated = await _accountManager.ValidateCookie(acc.Cookie);
                if (validated != null)
                {
                    acc.IsValid = true;
                    acc.LastValidated = DateTime.Now;
                    acc.Username = validated.Username;
                    acc.DisplayName = validated.DisplayName;
                    Log($"  {acc.Label} — Valid");
                }
                else
                {
                    acc.IsValid = false;
                    Log($"  {acc.Label} — INVALID");
                }
            }
            _accountManager.Save();
        }
        RefreshAccountList();
    }

    private async void OnLaunchSelectedAccounts(object? sender, EventArgs e)
    {
        var selected = _accountList.CheckedItems.Cast<ListViewItem>().ToList();
        if (selected.Count == 0)
        {
            Log("No accounts selected — check accounts to launch");
            return;
        }

        if (!int.TryParse(_txtDelay.Text, out int delay) || delay < 1000) delay = 8000;
        var preset = GetSelectedPreset();

        _btnLaunchAccount.Enabled = false;
        _launchCts = new CancellationTokenSource();

        try
        {
            var accounts = selected
                .Where(item => item.Index < _accountManager.Accounts.Count)
                .Select(item => _accountManager.Accounts[item.Index])
                .Where(a => a.IsValid)
                .ToList();

            if (accounts.Count == 0)
            {
                Log("No valid accounts selected");
                return;
            }

            await _launcher.LaunchAccounts(accounts, preset, delay, _accountManager, _launchCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Account launch stopped");
        }
        finally
        {
            _btnLaunchAccount.Enabled = true;
        }
    }

    private async void OnLaunchAllAccounts(object? sender, EventArgs e)
    {
        var validAccounts = _accountManager.Accounts.Where(a => a.IsValid).ToList();
        if (validAccounts.Count == 0)
        {
            Log("No valid accounts to launch — add and validate accounts first");
            return;
        }

        if (!int.TryParse(_txtDelay.Text, out int delay) || delay < 1000) delay = 8000;
        var preset = GetSelectedPreset();

        _btnLaunchAllAccounts.Enabled = false;
        _launchCts = new CancellationTokenSource();

        try
        {
            Log($"Launching {validAccounts.Count} accounts...");
            await _launcher.LaunchAccounts(validAccounts, preset, delay, _accountManager, _launchCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Account launch stopped");
        }
        finally
        {
            _btnLaunchAllAccounts.Enabled = true;
        }
    }

    // ═══════════════════════════════════════════
    // STATS & REFRESH
    // ═══════════════════════════════════════════

    private void RefreshStats()
    {
        _launcher.CleanupExited();
        int running = _launcher.RunningCount;
        long totalRam = _launcher.TotalMemoryMB;
        double totalCpu = _launcher.TotalCpuPercent;
        TimeSpan longest = TimeSpan.Zero;

        foreach (var inst in _launcher.Instances)
        {
            if (!inst.IsRunning) continue;
            var uptime = DateTime.Now - inst.LaunchedAt;
            if (uptime > longest) longest = uptime;
        }

        _statRunning.Value = running.ToString();
        _statRAM.Value = totalRam >= 1024 ? $"{totalRam / 1024.0:F1} GB" : $"{totalRam} MB";
        _statCPU.Value = $"{totalCpu:F1}%";
        _statUptime.Value = longest.TotalHours >= 1
            ? $"{(int)longest.TotalHours}h {longest.Minutes:D2}m"
            : $"{longest.Minutes}:{longest.Seconds:D2}";

        _titleBar.Title = $"  ROBLOX LAUNCHER  \u2022  {running} running  \u2022  {_accountManager.Accounts.Count} accs";
    }

    private void RefreshInstanceList()
    {
        _instanceList.Items.Clear();
        foreach (var inst in _launcher.Instances)
        {
            var item = new ListViewItem(inst.InstanceNumber.ToString());
            bool alive = inst.IsRunning;
            item.SubItems.Add(inst.AccountLabel);
            item.SubItems.Add(alive ? inst.Process!.Id.ToString() : "-");
            item.SubItems.Add(alive ? inst.MemoryMB.ToString() : "-");
            item.SubItems.Add(alive ? $"{inst.CpuPercent:F1}" : "-");
            var uptime = alive ? DateTime.Now - inst.LaunchedAt : TimeSpan.Zero;
            item.SubItems.Add(alive ? $"{(int)uptime.TotalMinutes}:{uptime.Seconds:D2}" : "-");
            item.SubItems.Add(alive ? "Running" : "Closed");
            item.ForeColor = alive ? Theme.Success : Theme.TextMuted;
            _instanceList.Items.Add(item);
        }
    }

    private void RefreshAccountList()
    {
        _accountList.Items.Clear();
        int num = 1;
        foreach (var acc in _accountManager.Accounts)
        {
            var item = new ListViewItem(num.ToString());
            item.SubItems.Add(acc.Username);
            item.SubItems.Add(acc.DisplayName);
            item.SubItems.Add(acc.UserId.ToString());
            item.SubItems.Add(acc.IsValid ? "Valid" : "Invalid");
            item.SubItems.Add(acc.LastValidated > DateTime.MinValue ? acc.LastValidated.ToString("MM/dd HH:mm") : "Never");
            item.ForeColor = acc.IsValid ? Theme.Success : Theme.Danger;
            _accountList.Items.Add(item);
            num++;
        }
    }

    // ═══════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════

    private QualityPreset GetSelectedPreset()
    {
        return _cmbPreset.SelectedIndex switch
        {
            0 => QualityPreset.MarvelRivals,
            1 => QualityPreset.MarvelRivalsPotato,
            2 => QualityPreset.Potato,
            3 => QualityPreset.Low,
            4 => QualityPreset.Medium,
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

    /// <summary>
    /// Creates a visible checkbox on dark backgrounds.
    /// Standard FlatStyle.Flat is invisible on dark themes.
    /// </summary>
    private static CheckBox MakeCheckBox(string text, int x, int y)
    {
        var chk = new CheckBox
        {
            Text = text,
            Font = Theme.FontBody,
            ForeColor = Theme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(x, y),
            FlatStyle = FlatStyle.Standard,
            Appearance = Appearance.Normal,
        };
        return chk;
    }

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

        // Reset power state on exit
        SetThreadExecutionState(ES_CONTINUOUS);

        base.OnFormClosing(e);
    }
}
