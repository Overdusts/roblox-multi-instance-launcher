using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = Theme.Radius;
    public Color FillColor { get; set; } = Theme.BgCard;
    public Color BorderColor { get; set; } = Theme.Border;
    public bool ShowBorder { get; set; } = true;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = CreateRoundedRect(rect, CornerRadius);

        using var brush = new SolidBrush(FillColor);
        g.FillPath(brush, path);

        if (ShowBorder)
        {
            using var pen = new Pen(BorderColor, 1.2f);
            g.DrawPath(pen, path);
        }
    }

    public static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
