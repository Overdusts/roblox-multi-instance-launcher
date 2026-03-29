using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RobloxLauncher.UI;

public class TitleBar : Panel
{
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    private readonly Form _parentForm;
    private readonly Label _titleLabel;

    // Store actions for window buttons
    private readonly Dictionary<Panel, Action> _buttonActions = new();

    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public TitleBar(Form parentForm)
    {
        _parentForm = parentForm;

        Height = 38;
        Dock = DockStyle.Top;
        BackColor = Theme.BgDark;
        Padding = new Padding(12, 0, 0, 0);

        // App icon dot
        var dot = new Panel
        {
            Size = new Size(10, 10),
            Location = new Point(14, 14),
            BackColor = Theme.Accent,
        };

        _titleLabel = new Label
        {
            Text = "Roblox Launcher",
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(30, 9),
            BackColor = Color.Transparent,
        };

        var btnClose = MakeWindowButton("\u00d7", Theme.Danger, () => _parentForm.Close());
        var btnMinimize = MakeWindowButton("\u2500", Theme.TextMuted, () => _parentForm.WindowState = FormWindowState.Minimized);
        var btnMaximize = MakeWindowButton("\u25a1", Theme.TextMuted, () =>
        {
            _parentForm.WindowState = _parentForm.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        });

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };

        btnPanel.Controls.Add(btnMinimize);
        btnPanel.Controls.Add(btnMaximize);
        btnPanel.Controls.Add(btnClose);

        Controls.Add(btnPanel);
        Controls.Add(_titleLabel);
        Controls.Add(dot);

        // Drag
        MouseDown += OnDrag;
        _titleLabel.MouseDown += OnDrag;
        dot.MouseDown += OnDrag;

        // Double-click title to maximize
        _titleLabel.DoubleClick += (s, e) =>
        {
            _parentForm.WindowState = _parentForm.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        };
    }

    private Panel MakeWindowButton(string symbol, Color hoverColor, Action onClick)
    {
        var btn = new Panel
        {
            Size = new Size(44, 38),
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand,
        };

        _buttonActions[btn] = onClick;

        var lbl = new Label
        {
            Text = symbol,
            ForeColor = Theme.TextMuted,
            Font = new Font("Segoe UI", 11f),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };

        lbl.MouseEnter += (s, e) => { btn.BackColor = Color.FromArgb(40, hoverColor); lbl.ForeColor = hoverColor; };
        lbl.MouseLeave += (s, e) => { btn.BackColor = Color.Transparent; lbl.ForeColor = Theme.TextMuted; };
        lbl.Click += (s, e) =>
        {
            if (_buttonActions.TryGetValue(btn, out var action))
                action();
        };

        btn.Click += (s, e) =>
        {
            if (_buttonActions.TryGetValue(btn, out var action))
                action();
        };

        btn.Controls.Add(lbl);
        return btn;
    }

    private void OnDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(_parentForm.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}
