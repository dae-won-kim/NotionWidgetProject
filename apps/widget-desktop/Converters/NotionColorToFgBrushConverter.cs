using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WidgetDesktop.Styles;

namespace WidgetDesktop.Converters;

public sealed class NotionColorToFgBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? __, CultureInfo ___)
        => new SolidColorBrush(WidgetTheme.GetStatusFg(value as string));

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___)
        => throw new NotSupportedException();
}
