using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ModernListView : ListView
{
    public ModernListView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.BgDark;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var bgBrush = new SolidBrush(Theme.BgCard);
        g.FillRectangle(bgBrush, e.Bounds);

        // Bottom border
        using var borderPen = new Pen(Theme.Border, 1f);
        g.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        TextRenderer.DrawText(g, e.Header?.Text, Theme.FontSmall, textRect, Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        // Handled in OnDrawSubItem
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null) return;

        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool selected = e.Item.Selected;
        bool isChecked = e.Item.Checked;
        Color bg = selected ? Theme.BgSelected : (e.ItemIndex % 2 == 0 ? Theme.BgDark : Color.FromArgb(16, 16, 21));

        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, e.Bounds);

        if (selected)
        {
            using var accentPen = new Pen(Theme.Accent, 2f);
            g.DrawLine(accentPen, e.Bounds.Left, e.Bounds.Top, e.Bounds.Left, e.Bounds.Bottom);
        }

        // Checkbox on first column
        if (e.ColumnIndex == 0 && CheckBoxes)
        {
            int cbSize = 16;
            int cbX = e.Bounds.X + 6;
            int cbY = e.Bounds.Y + (e.Bounds.Height - cbSize) / 2;
            var cbRect = new Rectangle(cbX, cbY, cbSize, cbSize);

            using var cbPath = RoundedPanel.CreateRoundedRect(cbRect, 3);

            if (isChecked)
            {
                using var cbFill = new SolidBrush(Theme.Accent);
                g.FillPath(cbFill, cbPath);

                // Checkmark
                using var checkPen = new Pen(Color.White, 1.8f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(checkPen, cbX + 3, cbY + 8, cbX + 6, cbY + 11);
                g.DrawLine(checkPen, cbX + 6, cbY + 11, cbX + 12, cbY + 5);
                g.SmoothingMode = SmoothingMode.None;
            }
            else
            {
                using var cbBorder = new Pen(Theme.BorderLight, 1.2f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(cbBorder, cbPath);
                g.SmoothingMode = SmoothingMode.None;
            }

            // Text after checkbox
            var textRect = new Rectangle(cbX + cbSize + 8, e.Bounds.Y, e.Bounds.Width - cbSize - 20, e.Bounds.Height);
            Color textColor = e.Item.ForeColor != Color.Empty ? e.Item.ForeColor : Theme.TextPrimary;
            TextRenderer.DrawText(g, e.SubItem?.Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            Color textColor = e.ColumnIndex == 0
                ? (e.Item.ForeColor != Color.Empty ? e.Item.ForeColor : Theme.TextPrimary)
                : Theme.TextSecondary;
            TextRenderer.DrawText(g, e.SubItem?.Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
