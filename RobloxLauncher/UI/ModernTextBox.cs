using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ModernTextBox : UserControl
{
    private readonly TextBox _inner;
    private bool _focused;

    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value;
    }

    public string PlaceholderText
    {
        get => _inner.PlaceholderText;
        set => _inner.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set => _inner.ReadOnly = value;
    }

    public int CornerRadius { get; set; } = Theme.RadiusSmall;

    public ModernTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.BgInput,
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontBody,
        };

        _inner.GotFocus += (s, e) => { _focused = true; Invalidate(); };
        _inner.LostFocus += (s, e) => { _focused = false; Invalidate(); };

        Controls.Add(_inner);
        Height = 36;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_inner == null) return;
        _inner.Location = new Point(12, (Height - _inner.Height) / 2);
        _inner.Width = Math.Max(1, Width - 24);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.CreateRoundedRect(rect, CornerRadius);

        using var fillBrush = new SolidBrush(Theme.BgInput);
        g.FillPath(fillBrush, path);

        Color borderCol = _focused ? Theme.Accent : Theme.Border;
        using var pen = new Pen(borderCol, _focused ? 1.5f : 1f);
        g.DrawPath(pen, path);
    }

    protected override void OnClick(EventArgs e)
    {
        _inner.Focus();
        base.OnClick(e);
    }
}
