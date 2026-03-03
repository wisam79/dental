using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DentalID.Desktop.Converters;

/// <summary>
/// Converts a boolean (IsRtl) to FlowDirection.
/// True -> RightToLeft
/// False -> LeftToRight
/// </summary>
public class BoolToFlowDirectionConverter : IValueConverter
{
    public static readonly BoolToFlowDirectionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRtl && isRtl)
        {
            return FlowDirection.RightToLeft;
        }
        return FlowDirection.LeftToRight;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FlowDirection direction)
        {
            return direction == FlowDirection.RightToLeft;
        }
        return false;
    }
}
