using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ModernComboBox : ComboBox
{
    public ModernComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.BgInput;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        ItemHeight = 28;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color bg = selected ? Theme.BgHover : Theme.BgInput;
        Color fg = selected ? Theme.TextPrimary : Theme.TextSecondary;

        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, e.Bounds);

        TextRenderer.DrawText(g, Items[e.Index]?.ToString(), Font, e.Bounds, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.CreateRoundedRect(rect, Theme.RadiusSmall);

        using var fillBrush = new SolidBrush(Theme.BgInput);
        g.FillPath(fillBrush, path);

        using var borderPen = new Pen(Focused ? Theme.Accent : Theme.Border, 1f);
        g.DrawPath(borderPen, path);

        // Draw text
        string text = SelectedItem?.ToString() ?? "";
        var textRect = new Rectangle(12, 0, Width - 36, Height);
        TextRenderer.DrawText(g, text, Font, textRect, Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // Draw chevron
        int cx = Width - 22;
        int cy = Height / 2;
        using var chevronPen = new Pen(Theme.TextMuted, 1.5f);
        g.DrawLine(chevronPen, cx - 4, cy - 2, cx, cy + 2);
        g.DrawLine(chevronPen, cx, cy + 2, cx + 4, cy - 2);
    }
}
