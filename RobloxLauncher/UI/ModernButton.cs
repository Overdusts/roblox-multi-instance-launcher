using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ModernButton : Control
{
    public Color ButtonColor { get; set; } = Theme.Accent;
    public Color HoverColor { get; set; } = Theme.AccentHover;
    public Color PressColor { get; set; } = Theme.AccentDim;
    public Color TextColor { get; set; } = Color.White;
    public int CornerRadius { get; set; } = Theme.RadiusSmall;
    public Image? Icon { get; set; }

    private bool _hovering;
    private bool _pressing;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.FontButton;
        Size = new Size(120, 36);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = Enabled
            ? (_pressing ? PressColor : (_hovering ? HoverColor : ButtonColor))
            : Color.FromArgb(40, 40, 50);

        using var path = RoundedPanel.CreateRoundedRect(rect, CornerRadius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);

        // Subtle top highlight
        if (Enabled && !_pressing)
        {
            using var highlightBrush = new SolidBrush(Color.FromArgb(15, 255, 255, 255));
            var topRect = new Rectangle(0, 0, Width - 1, Height / 2);
            using var topPath = RoundedPanel.CreateRoundedRect(topRect, CornerRadius);
            g.FillPath(highlightBrush, topPath);
        }

        // Text
        Color textCol = Enabled ? TextColor : Color.FromArgb(80, 80, 90);
        var textRect = ClientRectangle;

        if (Icon != null)
        {
            int iconSize = 16;
            var totalWidth = iconSize + 6 + (int)g.MeasureString(Text, Font).Width;
            int startX = (Width - totalWidth) / 2;
            g.DrawImage(Icon, startX, (Height - iconSize) / 2, iconSize, iconSize);
            textRect = new Rectangle(startX + iconSize + 6, 0, Width - startX - iconSize - 6, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, textRect, textCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovering = false; _pressing = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressing = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressing = false; Invalidate(); base.OnMouseUp(e); }
}
