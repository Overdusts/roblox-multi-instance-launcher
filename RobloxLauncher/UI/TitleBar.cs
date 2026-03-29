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
    private readonly Dictionary<Panel, Action> _buttonActions = new();

    public string Title
    {
        get => _titleLabel.Text;
        set => _titleLabel.Text = value;
    }

    public TitleBar(Form parentForm)
    {
        _parentForm = parentForm;
        Height = 40;
        Dock = DockStyle.Top;
        BackColor = Theme.BgDeep;
        Padding = new Padding(14, 0, 0, 0);

        // Neon accent dot
        var dot = new Panel { Size = new Size(8, 8), Location = new Point(16, 16), BackColor = Theme.Accent };

        _titleLabel = new Label
        {
            Text = "Roblox Launcher",
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(32, 10),
            BackColor = Color.Transparent,
        };

        var btnClose = MakeWindowButton("\u00d7", Theme.Danger, () => _parentForm.Close());
        var btnMin = MakeWindowButton("\u2500", Theme.TextMuted, () => _parentForm.WindowState = FormWindowState.Minimized);
        var btnMax = MakeWindowButton("\u25a1", Theme.TextMuted, () =>
        {
            _parentForm.WindowState = _parentForm.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
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
        btnPanel.Controls.Add(btnMin);
        btnPanel.Controls.Add(btnMax);
        btnPanel.Controls.Add(btnClose);

        Controls.Add(btnPanel);
        Controls.Add(_titleLabel);
        Controls.Add(dot);

        MouseDown += OnDrag;
        _titleLabel.MouseDown += OnDrag;
        dot.MouseDown += OnDrag;

        _titleLabel.DoubleClick += (s, e) =>
            _parentForm.WindowState = _parentForm.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private Panel MakeWindowButton(string symbol, Color hoverColor, Action onClick)
    {
        var btn = new Panel
        {
            Size = new Size(46, 40),
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand,
        };
        _buttonActions[btn] = onClick;

        var lbl = new Label
        {
            Text = symbol,
            ForeColor = Theme.TextMuted,
            Font = new Font("Segoe UI", 12f),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };

        lbl.MouseEnter += (s, e) => { btn.BackColor = Color.FromArgb(35, hoverColor); lbl.ForeColor = hoverColor; };
        lbl.MouseLeave += (s, e) => { btn.BackColor = Color.Transparent; lbl.ForeColor = Theme.TextMuted; };
        lbl.Click += (s, e) => { if (_buttonActions.TryGetValue(btn, out var a)) a(); };
        btn.Click += (s, e) => { if (_buttonActions.TryGetValue(btn, out var a)) a(); };

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
        // Subtle bottom border with accent hint
        using var pen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        // Tiny accent glow line
        using var accentPen = new Pen(Color.FromArgb(30, Theme.Accent), 1f);
        e.Graphics.DrawLine(accentPen, 0, Height - 2, Width / 3, Height - 2);
    }
}
