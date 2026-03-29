using System.Drawing.Drawing2D;

namespace RobloxLauncher.UI;

public class ProgressIndicator : Control
{
    private int _value;
    private int _maximum = 100;

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, _maximum); Invalidate(); }
    }

    public int Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(1, value); Invalidate(); }
    }

    public ProgressIndicator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 4;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Track
        var trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var trackPath = RoundedPanel.CreateRoundedRect(trackRect, Height / 2);
        using var trackBrush = new SolidBrush(Theme.BgCard);
        g.FillPath(trackBrush, trackPath);

        // Fill
        if (_value > 0 && _maximum > 0)
        {
            float pct = (float)_value / _maximum;
            int fillWidth = Math.Max(Height, (int)(Width * pct));
            var fillRect = new Rectangle(0, 0, fillWidth - 1, Height - 1);
            using var fillPath = RoundedPanel.CreateRoundedRect(fillRect, Height / 2);

            using var fillBrush = new LinearGradientBrush(fillRect, Theme.Accent, Theme.AccentHover, LinearGradientMode.Horizontal);
            g.FillPath(fillBrush, fillPath);
        }
    }
}
