using Avalonia.Media;
using Avalonia;

namespace WidgetDesktop.Styles;

public static class WidgetTheme
{
    /* ======================
       Fonts
       ====================== */
    private const string FontFamilyName = "Segoe UI";
    public static readonly FontFamily FontFamilyPrimary = new FontFamily(FontFamilyName);

    public const double FontSizeTitle = 18;
    public const double FontSizeBody = 14;
    public const double FontSizeStatus = 12;

    /* ======================
       Base Colors & Brushes (라이트 모드로 변경)
       ====================== */
    // 배경: 완전한 흰색 또는 아주 밝은 회색
    public static readonly Color BgWindow = Colors.White;
    public static readonly Color BgItem = Color.Parse("#F7F7F7"); // 아주 연한 회색 (아이템 구분용)
    public static readonly Color BgItemHover = Color.Parse("#EFEFEF");
    public static readonly Color BorderItem = Color.Parse("#E0E0E0"); // 부드러운 테두리
    
    public static readonly IBrush BgWindowBrush = Brushes.White;
    public static readonly IBrush BgItemBrush = new SolidColorBrush(BgItem);
    public static readonly IBrush BgItemHoverBrush = new SolidColorBrush(BgItemHover);
    public static readonly IBrush BorderItemBrush = new SolidColorBrush(BorderItem);

    // 텍스트: 검은색 및 진한 회색
    public static readonly Color TextPrimary = Color.Parse("#37352F"); // 노션 기본 검정색
    public static readonly IBrush TextPrimaryBrush = new SolidColorBrush(TextPrimary);
    public static readonly Color TextSecondary = Color.Parse("#787774");
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondary);

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
       Status Color Tokens (라이트 모드용 채도 조정)
       ====================== */
    public static readonly Color StatusDefault = Color.Parse("#DFE1E2");
    public static readonly Color StatusGray = Color.Parse("#DFE1E2");
    public static readonly Color StatusBrown = Color.Parse("#F7DDC3");
    public static readonly Color StatusOrange = Color.Parse("#F9DEC9");
    public static readonly Color StatusYellow = Color.Parse("#FDECC8");
    public static readonly Color StatusGreen = Color.Parse("#DBEDDB");
    public static readonly Color StatusBlue = Color.Parse("#D3E5EF");
    public static readonly Color StatusPurple = Color.Parse("#E8DEEE");
    public static readonly Color StatusPink = Color.Parse("#F5E0E9");
    public static readonly Color StatusRed = Color.Parse("#FFE2DD");

    /* ======================
       Status Button Opacity
       ====================== */
    public const double StatusHoverOpacity = 0.85;
    public const double StatusPressedOpacity = 0.70;
}