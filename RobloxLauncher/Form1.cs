using System.Runtime.InteropServices;
using RobloxLauncher.Core;
using RobloxLauncher.UI;

namespace RobloxLauncher;

public partial class Form1 : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS margins);
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int left, right, top, bottom; }

    private const int RESIZE_BORDER = 6;

    private readonly RobloxInstanceLauncher _launcher = new();
    private readonly AntiAFK _antiAfk;
    private CancellationTokenSource? _launchCts;

    // Controls
    private ModernLog lstLog = null!;
    private ModernComboBox cmbQuality = null!;
    private NumericUpDown nudInstances = null!;
    private NumericUpDown nudDelay = null!;
    private NumericUpDown nudAfkInterval = null!;
    private ModernButton btnLaunch = null!;
    private ModernButton btnLaunchOne = null!;
    private ModernButton btnStop = null!;
    private ModernButton btnCloseAll = null!;
    private ModernButton btnApplyFFlags = null!;
    private ModernButton btnResetFFlags = null!;
    private CheckBox chkAntiAfk = null!;
    private Label lblStatus = null!;
    private Label lblRobloxPath = null!;
    private Label lblRunningCount = null!;
    private Label lblMemUsage = null!;
    private ProgressIndicator progressBar = null!;
    private TitleBar titleBar = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    // Instance list
    private ModernListView lstInstances = null!;

    public Form1()
    {
        InitializeComponent();

        _antiAfk = new AntiAFK(_launcher);
        _launcher.OnLog += msg => BeginInvoke(() => lstLog.AddLog(msg));
        _launcher.OnInstanceChanged += () => BeginInvoke(RefreshInstances);
        _antiAfk.OnLog += msg => BeginInvoke(() => lstLog.AddLog(msg));

        SetupUI();
        DetectRoblox();

        // Periodic refresh for memory stats
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (s, e) => RefreshInstances();
        _refreshTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x20000;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084)
        {
            base.WndProc(ref m);
            var c = PointToClient(Cursor.Position);
            if (c.Y <= RESIZE_BORDER)
            {
                if (c.X <= RESIZE_BORDER) m.Result = (IntPtr)13;
                else if (c.X >= Width - RESIZE_BORDER) m.Result = (IntPtr)14;
                else m.Result = (IntPtr)12;
            }
            else if (c.Y >= Height - RESIZE_BORDER)
            {
                if (c.X <= RESIZE_BORDER) m.Result = (IntPtr)16;
                else if (c.X >= Width - RESIZE_BORDER) m.Result = (IntPtr)17;
                else m.Result = (IntPtr)15;
            }
            else if (c.X <= RESIZE_BORDER) m.Result = (IntPtr)10;
            else if (c.X >= Width - RESIZE_BORDER) m.Result = (IntPtr)11;
            return;
        }
        base.WndProc(ref m);
    }

    private void SetupUI()
    {
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(820, 660);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgMain;
        DoubleBuffered = true;

        titleBar = new TitleBar(this) { Title = "Roblox Multi-Instance Launcher" };
        Controls.Add(titleBar);

        var mainPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 16),
            BackColor = Theme.BgMain,
        };

        // ═══════════════════════════════════════
        // TOP — Launch Controls Card
        // ═══════════════════════════════════════
        var controlsCard = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 200,
            FillColor = Theme.BgCard,
            Padding = new Padding(20, 16, 20, 16),
        };

        var controlsInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var lblTitle = new Label
        {
            Text = "Launch Settings",
            Font = Theme.FontTitle,
            ForeColor = Theme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 28,
        };

        // Settings grid
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // label
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));   // control
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // label
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));   // control
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // label
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));   // control

        // Row 0: Instances, Quality, Delay
        AddLabel(grid, "Instances", 0, 0);
        nudInstances = new NumericUpDown
        {
            Minimum = 1, Maximum = 20, Value = 2, Increment = 1,
            BackColor = Theme.BgInput, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
            Font = Theme.FontBody,
        };
        grid.Controls.Add(nudInstances, 1, 0);

        AddLabel(grid, "Quality", 2, 0);
        cmbQuality = new ModernComboBox { Dock = DockStyle.Fill, Height = 36 };
        cmbQuality.Items.AddRange(new object[] { "Potato (Max)", "Low", "Medium", "Default" });
        cmbQuality.SelectedIndex = 0;
        grid.Controls.Add(cmbQuality, 3, 0);

        AddLabel(grid, "Delay (ms)", 4, 0);
        nudDelay = new NumericUpDown
        {
            Minimum = 2000, Maximum = 30000, Value = 5000, Increment = 1000,
            BackColor = Theme.BgInput, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
            Font = Theme.FontBody,
        };
        grid.Controls.Add(nudDelay, 5, 0);

        // Row 1: Anti-AFK, AFK interval, FFlags
        chkAntiAfk = new CheckBox
        {
            Text = "Anti-AFK",
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontBody,
            Checked = true,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0),
        };
        grid.Controls.Add(chkAntiAfk, 0, 1);
        grid.SetColumnSpan(chkAntiAfk, 2);

        AddLabel(grid, "AFK (sec)", 2, 1);
        nudAfkInterval = new NumericUpDown
        {
            Minimum = 15, Maximum = 300, Value = 60, Increment = 15,
            BackColor = Theme.BgInput, ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill,
            Font = Theme.FontBody,
        };
        grid.Controls.Add(nudAfkInterval, 3, 1);

        btnApplyFFlags = new ModernButton
        {
            Text = "Apply FFlags", Width = 110, Height = 32, Dock = DockStyle.Fill,
            ButtonColor = Color.FromArgb(50, 55, 75), HoverColor = Color.FromArgb(65, 70, 95),
            PressColor = Color.FromArgb(40, 44, 60),
        };
        grid.Controls.Add(btnApplyFFlags, 4, 1);
        grid.SetColumnSpan(btnApplyFFlags, 1);

        btnResetFFlags = new ModernButton
        {
            Text = "Reset FFlags", Width = 110, Height = 32, Dock = DockStyle.Fill,
            ButtonColor = Color.FromArgb(60, 45, 45), HoverColor = Color.FromArgb(80, 55, 55),
            PressColor = Color.FromArgb(50, 35, 35),
        };
        grid.Controls.Add(btnResetFFlags, 5, 1);

        // Row 2: Launch buttons
        var launchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 2, 0, 0),
        };

        btnLaunch = new ModernButton { Text = "Launch Instances", Width = 160, Height = 38, ButtonColor = Theme.Accent, HoverColor = Theme.AccentHover, PressColor = Theme.AccentDim };
        btnLaunchOne = new ModernButton { Text = "+1 Instance", Width = 120, Height = 38, ButtonColor = Theme.Success, HoverColor = Color.FromArgb(70, 200, 145), PressColor = Color.FromArgb(45, 140, 100) };
        btnStop = new ModernButton { Text = "Stop", Width = 80, Height = 38, ButtonColor = Theme.Warning, HoverColor = Color.FromArgb(255, 188, 50), PressColor = Color.FromArgb(190, 128, 15), Enabled = false };
        btnCloseAll = new ModernButton { Text = "Close All", Width = 100, Height = 38, ButtonColor = Theme.Danger, HoverColor = Color.FromArgb(255, 85, 88), PressColor = Color.FromArgb(180, 50, 52) };

        launchRow.Controls.AddRange(new Control[] { btnLaunch, btnLaunchOne, btnStop, btnCloseAll });
        grid.Controls.Add(launchRow, 0, 2);
        grid.SetColumnSpan(launchRow, 6);

        controlsInner.Controls.Add(grid);
        controlsInner.Controls.Add(lblTitle);
        controlsCard.Controls.Add(controlsInner);

        // ═══════════════════════════════════════
        // MIDDLE — Running Instances + Log (split)
        // ═══════════════════════════════════════
        var spacer = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Color.Transparent };

        var bottomPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        // Left: instances list
        var instancesCard = new RoundedPanel
        {
            Dock = DockStyle.Left,
            Width = 280,
            FillColor = Theme.BgCard,
            Padding = new Padding(14),
        };

        var instInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var instHeader = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
        var lblInstTitle = new Label { Text = "Instances", Font = Theme.FontTitle, ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 4) };
        lblRunningCount = new Label { Text = "0 running", Font = Theme.FontSmall, ForeColor = Theme.TextMuted, AutoSize = true };
        instHeader.Controls.AddRange(new Control[] { lblInstTitle, lblRunningCount });
        instHeader.Layout += (s, e) => lblRunningCount.Location = new Point(instHeader.Width - lblRunningCount.Width - 4, 8);

        lstInstances = new ModernListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = false,
        };
        lstInstances.Columns.Add("#", 35);
        lstInstances.Columns.Add("PID", 60);
        lstInstances.Columns.Add("RAM (MB)", 70);
        lstInstances.Columns.Add("Uptime", 80);

        lblMemUsage = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Total RAM: 0 MB",
        };

        instInner.Controls.Add(lstInstances);
        instInner.Controls.Add(lblMemUsage);
        instInner.Controls.Add(instHeader);
        instancesCard.Controls.Add(instInner);

        // Right: log
        var logSpacer = new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Color.Transparent };

        var logCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Theme.BgCard,
            Padding = new Padding(14),
        };

        var logInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var logHeader = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.Transparent };
        var lblLogTitle = new Label { Text = "Activity Log", Font = Theme.FontTitle, ForeColor = Theme.TextPrimary, AutoSize = true, Location = new Point(0, 4) };
        lblStatus = new Label { Font = Theme.FontSmall, ForeColor = Theme.TextMuted, AutoSize = true, Text = "Ready" };
        logHeader.Controls.AddRange(new Control[] { lblLogTitle, lblStatus });
        logHeader.Layout += (s, e) => lblStatus.Location = new Point(logHeader.Width - lblStatus.Width - 4, 8);

        progressBar = new ProgressIndicator { Dock = DockStyle.Top, Height = 4, Visible = false };
        lstLog = new ModernLog { Dock = DockStyle.Fill };

        lblRobloxPath = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = Theme.FontSmall,
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.MiddleRight,
        };

        logInner.Controls.Add(lstLog);
        logInner.Controls.Add(lblRobloxPath);
        logInner.Controls.Add(progressBar);
        logInner.Controls.Add(logHeader);
        logCard.Controls.Add(logInner);

        bottomPanel.Controls.Add(logCard);
        bottomPanel.Controls.Add(logSpacer);
        bottomPanel.Controls.Add(instancesCard);

        mainPanel.Controls.Add(bottomPanel);
        mainPanel.Controls.Add(spacer);
        mainPanel.Controls.Add(controlsCard);

        Controls.Add(mainPanel);

        // Wire events
        btnLaunch.Click += BtnLaunch_Click;
        btnLaunchOne.Click += BtnLaunchOne_Click;
        btnStop.Click += (s, e) => { _launchCts?.Cancel(); lstLog.AddLog("Stopping..."); };
        btnCloseAll.Click += (s, e) => { _launcher.CloseAll(); RefreshInstances(); };
        btnApplyFFlags.Click += BtnApplyFFlags_Click;
        btnResetFFlags.Click += BtnResetFFlags_Click;

        chkAntiAfk.CheckedChanged += (s, e) =>
        {
            if (chkAntiAfk.Checked && _launcher.RunningCount > 0)
            {
                _antiAfk.IntervalSeconds = (int)nudAfkInterval.Value;
                _antiAfk.Start();
            }
            else
            {
                _antiAfk.Stop();
            }
        };

        nudAfkInterval.ValueChanged += (s, e) =>
        {
            if (_antiAfk.Enabled)
            {
                _antiAfk.Stop();
                _antiAfk.IntervalSeconds = (int)nudAfkInterval.Value;
                _antiAfk.Start();
            }
        };
    }

    private void AddLabel(TableLayoutPanel grid, string text, int col, int row)
    {
        grid.Controls.Add(new Label
        {
            Text = text,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontBody,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        }, col, row);
    }

    private void DetectRoblox()
    {
        string? path = QualityOptimizer.GetRobloxPath();
        lblRobloxPath.Text = path != null ? $"Roblox: {path}" : "Roblox not found";
        if (path == null)
            lstLog.AddLog("[WARNING] Roblox not found. Install Roblox first.");
        else
            lstLog.AddLog($"Roblox found: {path}");
    }

    private async void BtnLaunch_Click(object? sender, EventArgs e)
    {
        int count = (int)nudInstances.Value;
        var quality = (QualityPreset)cmbQuality.SelectedIndex;
        int delay = (int)nudDelay.Value;

        SetLaunchingState(true);
        _launchCts = new CancellationTokenSource();

        progressBar.Visible = true;
        progressBar.Maximum = count;
        progressBar.Value = 0;

        var progress = new Progress<(int current, int total)>(p =>
        {
            progressBar.Value = p.current;
            lblStatus.Text = $"Launching {p.current}/{p.total}...";
        });

        try
        {
            await _launcher.LaunchMultiple(count, quality, delay, _launchCts.Token, progress);
            lstLog.AddLog($"Done — {count} instance(s) launched. Log in & join games yourself.");

            // Start anti-AFK if enabled
            if (chkAntiAfk.Checked)
            {
                _antiAfk.IntervalSeconds = (int)nudAfkInterval.Value;
                _antiAfk.Start();
            }
        }
        catch (OperationCanceledException)
        {
            lstLog.AddLog("Launch cancelled.");
        }
        finally
        {
            SetLaunchingState(false);
            progressBar.Visible = false;
            RefreshInstances();
        }
    }

    private async void BtnLaunchOne_Click(object? sender, EventArgs e)
    {
        var quality = (QualityPreset)cmbQuality.SelectedIndex;
        btnLaunchOne.Enabled = false;

        int num = _launcher.RunningCount + 1;
        await _launcher.LaunchOne(quality, num);

        if (chkAntiAfk.Checked && !_antiAfk.Enabled)
        {
            _antiAfk.IntervalSeconds = (int)nudAfkInterval.Value;
            _antiAfk.Start();
        }

        btnLaunchOne.Enabled = true;
        RefreshInstances();
    }

    private void BtnApplyFFlags_Click(object? sender, EventArgs e)
    {
        string? path = QualityOptimizer.GetRobloxPath();
        if (path == null) { lstLog.AddLog("[ERROR] Roblox not found"); return; }

        var quality = (QualityPreset)cmbQuality.SelectedIndex;
        QualityOptimizer.ApplyFFlags(path, quality);
        lstLog.AddLog($"FFlags applied: {quality} preset");
    }

    private void BtnResetFFlags_Click(object? sender, EventArgs e)
    {
        string? path = QualityOptimizer.GetRobloxPath();
        if (path == null) { lstLog.AddLog("[ERROR] Roblox not found"); return; }

        QualityOptimizer.ResetFFlags(path);
        lstLog.AddLog("FFlags reset to default");
    }

    private void SetLaunchingState(bool launching)
    {
        btnLaunch.Enabled = !launching;
        btnLaunchOne.Enabled = !launching;
        btnStop.Enabled = launching;
    }

    private void RefreshInstances()
    {
        _launcher.CleanupExited();

        lstInstances.Items.Clear();
        long totalMem = 0;

        foreach (var inst in _launcher.Instances)
        {
            if (!inst.IsRunning) continue;

            long mem = inst.MemoryMB;
            totalMem += mem;
            var uptime = DateTime.Now - inst.LaunchedAt;

            var item = new ListViewItem($"#{inst.InstanceNumber}");
            item.SubItems.Add(inst.Process!.Id.ToString());
            item.SubItems.Add($"{mem}");
            item.SubItems.Add($"{(int)uptime.TotalMinutes}m {uptime.Seconds}s");
            item.Tag = inst;
            item.ForeColor = Theme.Success;
            lstInstances.Items.Add(item);
        }

        int running = _launcher.RunningCount;
        lblRunningCount.Text = $"{running} running";
        lblMemUsage.Text = $"Total RAM: {totalMem} MB";
        lblStatus.Text = running > 0 ? $"{running} running" : "Ready";

        // Stop anti-AFK if no instances
        if (running == 0 && _antiAfk.Enabled)
            _antiAfk.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _antiAfk.Dispose();
        _refreshTimer.Stop();
        base.OnFormClosing(e);
    }
}
