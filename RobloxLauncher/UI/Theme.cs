namespace RobloxLauncher.UI;

public static class Theme
{
    // ═══ 2026 Cyberpunk Dark ═══
    // Deep blacks with neon accent — inspired by Discord/gaming launchers

    // Backgrounds — layered depth (never pure black)
    public static readonly Color BgDeep = Color.FromArgb(8, 8, 14);
    public static readonly Color BgDark = Color.FromArgb(12, 12, 20);
    public static readonly Color BgMain = Color.FromArgb(16, 16, 26);
    public static readonly Color BgCard = Color.FromArgb(20, 22, 34);
    public static readonly Color BgCardHover = Color.FromArgb(26, 28, 42);
    public static readonly Color BgInput = Color.FromArgb(14, 15, 25);
    public static readonly Color BgHover = Color.FromArgb(30, 32, 48);
    public static readonly Color BgSelected = Color.FromArgb(35, 38, 58);
    public static readonly Color BgSidebar = Color.FromArgb(10, 10, 18);

    // Glass effect
    public static readonly Color Glass = Color.FromArgb(18, 255, 255, 255);
    public static readonly Color GlassBorder = Color.FromArgb(30, 255, 255, 255);

    // Borders
    public static readonly Color Border = Color.FromArgb(32, 35, 52);
    public static readonly Color BorderLight = Color.FromArgb(45, 48, 68);
    public static readonly Color BorderGlow = Color.FromArgb(60, 130, 180, 255);

    // Text
    public static readonly Color TextPrimary = Color.FromArgb(240, 242, 255);
    public static readonly Color TextSecondary = Color.FromArgb(145, 150, 180);
    public static readonly Color TextMuted = Color.FromArgb(75, 80, 105);
    public static readonly Color TextAccent = Color.FromArgb(130, 170, 255);

    // Neon accent — electric blue
    public static readonly Color Accent = Color.FromArgb(80, 140, 255);
    public static readonly Color AccentHover = Color.FromArgb(110, 160, 255);
    public static readonly Color AccentDim = Color.FromArgb(50, 90, 180);
    public static readonly Color AccentGlow = Color.FromArgb(40, 80, 140, 255);

    // Secondary accent — cyan/teal
    public static readonly Color Cyan = Color.FromArgb(0, 210, 210);
    public static readonly Color CyanDim = Color.FromArgb(0, 150, 150);
    public static readonly Color CyanGlow = Color.FromArgb(30, 0, 210, 210);

    // Status — neon versions
    public static readonly Color Success = Color.FromArgb(45, 212, 120);
    public static readonly Color SuccessGlow = Color.FromArgb(25, 45, 212, 120);
    public static readonly Color Danger = Color.FromArgb(255, 65, 80);
    public static readonly Color DangerGlow = Color.FromArgb(25, 255, 65, 80);
    public static readonly Color Warning = Color.FromArgb(255, 180, 40);
    public static readonly Color WarningGlow = Color.FromArgb(25, 255, 180, 40);
    public static readonly Color Info = Color.FromArgb(60, 150, 255);

    // Fonts
    public static readonly Font FontTitle = new("Segoe UI Semibold", 14f);
    public static readonly Font FontSubtitle = new("Segoe UI Semibold", 11f);
    public static readonly Font FontBody = new("Segoe UI", 9.5f);
    public static readonly Font FontSmall = new("Segoe UI", 8.5f);
    public static readonly Font FontMono = new("Cascadia Code, Consolas", 8.5f);
    public static readonly Font FontButton = new("Segoe UI Semibold", 9.5f);
    public static readonly Font FontNav = new("Segoe UI Semibold", 10f);
    public static readonly Font FontBig = new("Segoe UI Semibold", 22f);
    public static readonly Font FontStat = new("Segoe UI Semibold", 16f);

    public static readonly int Radius = 12;
    public static readonly int RadiusSmall = 8;
    public static readonly int RadiusLarge = 16;
}
