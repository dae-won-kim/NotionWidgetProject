using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WidgetDesktop.Styles;

namespace WidgetDesktop.Converters;

public sealed class NotionColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? __, CultureInfo ___)
    {
        var color = (value as string)?.ToLowerInvariant();

        return new SolidColorBrush(color switch
        {
            "blue"   => WidgetTheme.StatusBlue,
            "green"  => WidgetTheme.StatusGreen,
            "yellow" => WidgetTheme.StatusYellow,
            "red"    => WidgetTheme.StatusRed,
            "gray"   => WidgetTheme.StatusGray,
            _        => WidgetTheme.StatusDefault
        });
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___)
        => throw new NotSupportedException();
}
