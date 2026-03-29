namespace RobloxLauncher.UI;

public class ModernLog : ListBox
{
    public ModernLog()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.BgDeep;
        ForeColor = Theme.TextSecondary;
        Font = Theme.FontMono;
        ItemHeight = 24;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        string text = Items[e.Index]?.ToString() ?? "";

        Color bg = e.Index % 2 == 0 ? Theme.BgDeep : Color.FromArgb(10, 10, 17);
        using var bgBrush = new SolidBrush(bg);
        g.FillRectangle(bgBrush, e.Bounds);

        // Color coding with neon colors
        Color textColor = Theme.TextSecondary;
        Color barColor = Color.Transparent;

        if (text.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        { textColor = Theme.Danger; barColor = Theme.Danger; }
        else if (text.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
        { textColor = Theme.Warning; barColor = Theme.Warning; }
        else if (text.Contains("Successfully", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("launched", StringComparison.OrdinalIgnoreCase))
        { textColor = Theme.Success; barColor = Theme.Success; }
        else if (text.Contains("Launching", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Applying", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Bypassing", StringComparison.OrdinalIgnoreCase))
        { textColor = Theme.Info; barColor = Theme.Info; }
        else if (text.Contains("Anti-AFK", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("poked", StringComparison.OrdinalIgnoreCase))
        { textColor = Theme.Cyan; barColor = Theme.Cyan; }
        else if (text.Contains("Killed", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("Mutex", StringComparison.OrdinalIgnoreCase))
        { textColor = Color.FromArgb(160, 130, 255); barColor = Color.FromArgb(160, 130, 255); }

        // Left color bar
        if (barColor != Color.Transparent)
        {
            using var barBrush = new SolidBrush(Color.FromArgb(60, barColor));
            g.FillRectangle(barBrush, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        }

        var textRect = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(g, text, Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    public void AddLog(string message)
    {
        Items.Add(message);
        if (Items.Count > 500) Items.RemoveAt(0);
        TopIndex = Items.Count - 1;
    }
}
