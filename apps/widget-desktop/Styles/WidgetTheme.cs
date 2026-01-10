using Avalonia.Media;

namespace WidgetDesktop.Styles;

public static class WidgetTheme
{
    /* ======================
       Fonts
       ====================== */
    public const string FontFamilyPrimary = "Segoe UI";
    public const double FontSizeTitle = 18;
    public const double FontSizeBody = 14;
    public const double FontSizeStatus = 12;

    /* ======================
       Base Colors
       ====================== */
    public static readonly Color BgWindow = Color.Parse("#1E1E1E");
    public static readonly Color BgItem = Color.Parse("#262626");
    public static readonly Color BgItemHover = Color.Parse("#303030");
    public static readonly Color BorderItem = Color.Parse("#3A3A3A");
    public static readonly Color TextPrimary = Colors.White;
    public static readonly Color TextSecondary = Color.Parse("#B5B5B5");

    /* ======================
       Shape
       ====================== */
    public const double RadiusWindow = 16;
    public const double RadiusItem = 999;
    public const double RadiusStatus = 999;

    /* ======================
       Status Color Tokens  ✅ 추가된 부분
       ====================== */
    public static readonly Color StatusDefault = Color.Parse("#6B7280");
    public static readonly Color StatusGray    = Color.Parse("#6B7280");
    public static readonly Color StatusBrown   = Color.Parse("#92400E");
    public static readonly Color StatusOrange  = Color.Parse("#F97316");
    public static readonly Color StatusYellow  = Color.Parse("#EAB308");
    public static readonly Color StatusGreen   = Color.Parse("#22C55E");
    public static readonly Color StatusBlue    = Color.Parse("#3B82F6");
    public static readonly Color StatusPurple  = Color.Parse("#A855F7");
    public static readonly Color StatusPink    = Color.Parse("#EC4899");
    public static readonly Color StatusRed     = Color.Parse("#EF4444");

    /* ======================
       Status Button Opacity
       ====================== */
    public const double StatusHoverOpacity = 0.90;
    public const double StatusPressedOpacity = 0.78;
}
