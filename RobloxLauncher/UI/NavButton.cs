using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

/// <summary>
/// Sidebar navigation button with icon text, active indicator, and hover glow.
/// </summary>
public class NavButton : Control
{
    public bool IsActive { get; set; }
    public string Icon { get; set; } = ""; // Unicode symbol
    public Color ActiveColor { get; set; } = Theme.Accent;

    private bool _hovering;

    public NavButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.FontNav;
        Size = new Size(200, 42);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(4, 2, Width - 8, Height - 4);

        if (IsActive || _hovering)
        {
            using var path = RoundedPanel.CreateRoundedRect(rect, Theme.RadiusSmall);
            Color bg = IsActive ? Color.FromArgb(25, ActiveColor) : Theme.BgHover;
            using var bgBrush = new SolidBrush(bg);
            g.FillPath(bgBrush, path);

            // Active left indicator
            if (IsActive)
            {
                var indicatorRect = new Rectangle(0, Height / 2 - 10, 3, 20);
                using var indicatorPath = RoundedPanel.CreateRoundedRect(indicatorRect, 2);
                using var indicatorBrush = new SolidBrush(ActiveColor);
                g.FillPath(indicatorBrush, indicatorPath);
            }
        }

        // Icon
        Color textColor = IsActive ? ActiveColor : (_hovering ? Theme.TextPrimary : Theme.TextSecondary);
        var iconRect = new Rectangle(16, 0, 28, Height);
        TextRenderer.DrawText(g, Icon, new Font("Segoe UI", 12f), iconRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Text
        var textRect = new Rectangle(46, 0, Width - 52, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovering = false; Invalidate(); base.OnMouseLeave(e); }
}
