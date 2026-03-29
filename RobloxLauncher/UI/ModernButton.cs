using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ModernButton : Control
{
    public Color ButtonColor { get; set; } = Theme.Accent;
    public Color HoverColor { get; set; } = Theme.AccentHover;
    public Color PressColor { get; set; } = Theme.AccentDim;
    public Color TextColor { get; set; } = Color.White;
    public int CornerRadius { get; set; } = Theme.RadiusSmall;
    public bool Outlined { get; set; } = false;
    public bool GlowEffect { get; set; } = true;

    private bool _hovering;
    private bool _pressing;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.FontButton;
        Size = new Size(130, 38);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        Color fill = Enabled
            ? (_pressing ? PressColor : (_hovering ? HoverColor : ButtonColor))
            : Color.FromArgb(25, 28, 40);

        using var path = RoundedPanel.CreateRoundedRect(rect, CornerRadius);

        // Glow shadow behind button on hover
        if (Enabled && _hovering && GlowEffect)
        {
            var glowRect = new Rectangle(-1, 2, Width + 1, Height);
            using var glowPath = RoundedPanel.CreateRoundedRect(glowRect, CornerRadius + 2);
            using var glowBrush = new SolidBrush(Color.FromArgb(30, fill));
            g.FillPath(glowBrush, glowPath);
        }

        if (Outlined && !_hovering)
        {
            // Outlined style — transparent fill with colored border
            using var borderPen = new Pen(fill, 1.5f);
            g.DrawPath(borderPen, path);
        }
        else
        {
            // Filled
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);

            // Top highlight (glass)
            if (Enabled && !_pressing)
            {
                var topRect = new Rectangle(1, 1, Width - 3, (Height - 3) / 2);
                using var topPath = RoundedPanel.CreateRoundedRect(topRect, CornerRadius);
                using var highlightBrush = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
                g.FillPath(highlightBrush, topPath);
            }
        }

        // Text
        Color tc = Enabled ? TextColor : Color.FromArgb(55, 58, 70);
        if (Outlined && !_hovering) tc = fill;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, tc,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovering = false; _pressing = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressing = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressing = false; Invalidate(); base.OnMouseUp(e); }
}
