using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

/// <summary>
/// Dashboard-style stat card showing a big number with label and glow accent.
/// </summary>
public class StatCard : Control
{
    private string _value = "0";
    private string _label = "Label";
    private Color _accentColor = Theme.Accent;

    public string Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    public string Label
    {
        get => _label;
        set { _label = value; Invalidate(); }
    }

    public Color AccentColor
    {
        get => _accentColor;
        set { _accentColor = value; Invalidate(); }
    }

    public StatCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(160, 90);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = RoundedPanel.CreateRoundedRect(rect, Theme.Radius);

        // Card background
        using var bgBrush = new SolidBrush(Theme.BgCard);
        g.FillPath(bgBrush, path);

        // Glass top
        var topRect = new Rectangle(1, 1, Width - 3, Height / 3);
        using var topPath = RoundedPanel.CreateRoundedRect(topRect, Theme.Radius);
        using var glassBrush = new SolidBrush(Theme.Glass);
        g.FillPath(glassBrush, topPath);

        // Left accent bar
        var barRect = new Rectangle(1, 1, 4, Height - 3);
        using var barPath = RoundedPanel.CreateRoundedRect(barRect, 2);
        using var barBrush = new SolidBrush(_accentColor);
        g.FillPath(barBrush, barPath);

        // Accent glow on left side
        using var glowBrush = new LinearGradientBrush(
            new Point(0, 0), new Point(50, 0),
            Color.FromArgb(20, _accentColor), Color.Transparent);
        g.FillRectangle(glowBrush, 0, 0, 50, Height);

        // Border
        using var borderPen = new Pen(Theme.Border, 1f);
        g.DrawPath(borderPen, path);

        // Value text (big)
        var valueRect = new Rectangle(18, 12, Width - 24, Height / 2);
        TextRenderer.DrawText(g, _value, Theme.FontStat, valueRect, _accentColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Label text
        var labelRect = new Rectangle(18, Height / 2 + 2, Width - 24, Height / 2 - 8);
        TextRenderer.DrawText(g, _label, Theme.FontSmall, labelRect, Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.Top);
    }
}
