namespace RobloxLauncher.UI;

public class ModernLog : ListBox
{
    public ModernLog()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.BgDark;
        ForeColor = Theme.TextSecondary;
        Font = Theme.FontMono;
        ItemHeight = 22;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        string text = Items[e.Index]?.ToString() ?? "";

        // Alternating rows
        Color bg = e.Index % 2 == 0 ? Theme.BgDark : Color.FromArgb(15, 15, 20);
        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, e.Bounds);

        // Color-code by content
        Color textColor = Theme.TextSecondary;
        if (text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            textColor = Theme.Danger;
        else if (text.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            textColor = Theme.Warning;
        else if (text.Contains("Successfully", StringComparison.OrdinalIgnoreCase) || text.Contains("Valid", StringComparison.OrdinalIgnoreCase))
            textColor = Theme.Success;
        else if (text.Contains("Launching", StringComparison.OrdinalIgnoreCase) || text.Contains("Getting", StringComparison.OrdinalIgnoreCase))
            textColor = Theme.Info;

        var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(g, text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    public void AddLog(string message)
    {
        Items.Add(message);
        TopIndex = Items.Count - 1;
    }
}
