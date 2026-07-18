using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Keen.VRage.Library.Mathematics;
using Keen.VRage.UI.Shared.Extensions;

namespace UI_Revamp.Screens.Paint;

public sealed class HsvToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ColorHSV color ? new SolidColorBrush(color.ToAvalonia()) : value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
