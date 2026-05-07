using Avalonia.Media;
using Avalonia;

namespace WidgetDesktop.Styles;

public static class WidgetTheme
{
    /* ======================
       Fonts
       ====================== */
    // Inter: Notion이 웹에서 사용하는 폰트 (Avalonia.Fonts.Inter 패키지로 내장)
    // Malgun Gothic: 한국어 fallback
    public static readonly FontFamily FontFamilyPrimary = new FontFamily("Inter,Malgun Gothic");

    public const double FontSizeTitle = 18;
    public const double FontSizeBody = 14;
    public const double FontSizeStatus = 12;

    /* ======================
       Base Colors & Brushes (다크 모드)
       ====================== */
    public static readonly Color BgWindow    = Color.Parse("#0E0826");
    public static readonly Color BgItem      = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
    public static readonly Color BgItemHover = Color.FromArgb(0x32, 0xFF, 0xFF, 0xFF);
    public static readonly Color BorderItem  = Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);

    public static readonly SolidColorBrush BgWindowBrush    = new SolidColorBrush(BgWindow);
    public static readonly SolidColorBrush BgItemBrush      = new SolidColorBrush(BgItem);
    public static readonly SolidColorBrush BgItemHoverBrush = new SolidColorBrush(BgItemHover);
    public static readonly SolidColorBrush BorderItemBrush  = new SolidColorBrush(BorderItem);

    public static readonly Color TextPrimary = Color.Parse("#F0EEFF");
    public static readonly SolidColorBrush TextPrimaryBrush = new SolidColorBrush(TextPrimary);
    public static readonly Color TextSecondary = Color.Parse("#8080A8");
    public static readonly SolidColorBrush TextSecondaryBrush = new SolidColorBrush(TextSecondary);

    /* ======================
       Shape
       ====================== */
    private const double RadiusWindow = 12f;
    private const double RadiusItem = 8f;   // 리스트 아이템은 너무 둥글지 않게 수정
    private const double RadiusStatus = 4f; // 상태 버튼도 약간 각진 느낌으로

    public static readonly CornerRadius RadiusWindowCorner = new CornerRadius(RadiusWindow);
    public static readonly CornerRadius RadiusItemCorner = new CornerRadius(RadiusItem);
    public static readonly CornerRadius RadiusStatusCorner = new CornerRadius(RadiusStatus);

    /* ======================
       Status Color Tokens  (파스텔 ↔ 선명 중간, 채도 낮춰 자연스럽게)
       ====================== */
    public static readonly Color StatusDefault = Color.Parse("#AABAC8");
    public static readonly Color StatusGray    = Color.Parse("#AABAC8");
    public static readonly Color StatusBrown   = Color.Parse("#C9A882");
    public static readonly Color StatusOrange  = Color.Parse("#F4A96A");
    public static readonly Color StatusYellow  = Color.Parse("#F0C456");
    public static readonly Color StatusGreen   = Color.Parse("#72CFA0");
    public static readonly Color StatusBlue    = Color.Parse("#7AB8E8");
    public static readonly Color StatusPurple  = Color.Parse("#B89AE0");
    public static readonly Color StatusPink    = Color.Parse("#EFA0C4");
    public static readonly Color StatusRed     = Color.Parse("#F49494");

    /* ======================
       Status Foreground Tokens (배경 동색계 짙은 톤)
       ====================== */
    public static readonly Color StatusFgDefault = Color.Parse("#2E3F4F");
    public static readonly Color StatusFgGray    = Color.Parse("#2E3F4F");
    public static readonly Color StatusFgBrown   = Color.Parse("#4A2E14");
    public static readonly Color StatusFgOrange  = Color.Parse("#5C2A08");
    public static readonly Color StatusFgYellow  = Color.Parse("#4A3000");
    public static readonly Color StatusFgGreen   = Color.Parse("#0F4028");
    public static readonly Color StatusFgBlue    = Color.Parse("#0C2E50");
    public static readonly Color StatusFgPurple  = Color.Parse("#2A1550");
    public static readonly Color StatusFgPink    = Color.Parse("#4A1030");
    public static readonly Color StatusFgRed     = Color.Parse("#4A1010");

    public static Color GetStatusFg(string? colorName) => colorName?.ToLowerInvariant() switch
    {
        "gray"   => StatusFgGray,
        "brown"  => StatusFgBrown,
        "orange" => StatusFgOrange,
        "yellow" => StatusFgYellow,
        "green"  => StatusFgGreen,
        "blue"   => StatusFgBlue,
        "purple" => StatusFgPurple,
        "pink"   => StatusFgPink,
        "red"    => StatusFgRed,
        _        => StatusFgDefault
    };

    /* ======================
       Status Button Opacity
       ====================== */
    public const double StatusHoverOpacity = 0.85;
    public const double StatusPressedOpacity = 0.70;
}