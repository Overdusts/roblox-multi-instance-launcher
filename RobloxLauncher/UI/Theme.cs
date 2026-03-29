namespace RobloxLauncher.UI;

public static class Theme
{
    // Base
    public static readonly Color BgDark = Color.FromArgb(13, 13, 17);
    public static readonly Color BgMain = Color.FromArgb(19, 19, 25);
    public static readonly Color BgCard = Color.FromArgb(26, 26, 34);
    public static readonly Color BgInput = Color.FromArgb(22, 22, 30);
    public static readonly Color BgHover = Color.FromArgb(34, 34, 44);
    public static readonly Color BgSelected = Color.FromArgb(40, 40, 54);

    // Borders
    public static readonly Color Border = Color.FromArgb(42, 42, 56);
    public static readonly Color BorderLight = Color.FromArgb(55, 55, 72);
    public static readonly Color BorderAccent = Color.FromArgb(88, 101, 242);

    // Text
    public static readonly Color TextPrimary = Color.FromArgb(235, 235, 245);
    public static readonly Color TextSecondary = Color.FromArgb(148, 148, 168);
    public static readonly Color TextMuted = Color.FromArgb(88, 88, 108);
    public static readonly Color TextAccent = Color.FromArgb(118, 131, 255);

    // Accent
    public static readonly Color Accent = Color.FromArgb(88, 101, 242);
    public static readonly Color AccentHover = Color.FromArgb(105, 117, 255);
    public static readonly Color AccentDim = Color.FromArgb(56, 65, 155);

    // Status
    public static readonly Color Success = Color.FromArgb(59, 183, 126);
    public static readonly Color Danger = Color.FromArgb(237, 66, 69);
    public static readonly Color Warning = Color.FromArgb(250, 168, 26);
    public static readonly Color Info = Color.FromArgb(69, 142, 255);

    // Fonts
    public static readonly Font FontTitle = new("Segoe UI Semibold", 13f);
    public static readonly Font FontSubtitle = new("Segoe UI Semibold", 10f);
    public static readonly Font FontBody = new("Segoe UI", 9.5f);
    public static readonly Font FontSmall = new("Segoe UI", 8.5f);
    public static readonly Font FontMono = new("Cascadia Code, Consolas", 8.5f);
    public static readonly Font FontButton = new("Segoe UI Semibold", 9.5f);

    public static readonly int Radius = 10;
    public static readonly int RadiusSmall = 6;
}
