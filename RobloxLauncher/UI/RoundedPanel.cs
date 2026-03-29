using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = Theme.Radius;
    public Color FillColor { get; set; } = Theme.BgCard;
    public Color BorderColor { get; set; } = Theme.Border;
    public bool ShowBorder { get; set; } = true;
    public bool GlowOnHover { get; set; } = false;
    public Color GlowColor { get; set; } = Theme.AccentGlow;

    private bool _hovering;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = CreateRoundedRect(rect, CornerRadius);

        // Glow effect behind the panel
        if (GlowOnHover && _hovering)
        {
            var glowRect = new Rectangle(-2, -2, Width + 3, Height + 3);
            using var glowPath = CreateRoundedRect(glowRect, CornerRadius + 2);
            using var glowBrush = new SolidBrush(GlowColor);
            g.FillPath(glowBrush, glowPath);
        }

        // Fill
        using var brush = new SolidBrush(FillColor);
        g.FillPath(brush, path);

        // Subtle top gradient (glass effect)
        var topRect = new Rectangle(1, 1, Width - 3, Math.Min(Height / 3, 40));
        using var topPath = CreateRoundedRect(topRect, CornerRadius);
        using var glassBrush = new SolidBrush(Theme.Glass);
        g.FillPath(glassBrush, topPath);

        if (ShowBorder)
        {
            Color bc = (_hovering && GlowOnHover) ? Theme.BorderLight : BorderColor;
            using var pen = new Pen(bc, 1f);
            g.DrawPath(pen, path);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovering = true; if (GlowOnHover) Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovering = false; if (GlowOnHover) Invalidate(); base.OnMouseLeave(e); }

    public static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d > rect.Width) d = rect.Width;
        if (d > rect.Height) d = rect.Height;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
