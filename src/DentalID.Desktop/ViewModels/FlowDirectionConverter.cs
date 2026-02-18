using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Converts boolean IsRtl to FlowDirection for XAML binding.
/// </summary>
public class FlowDirectionConverter : IValueConverter
{
    public static readonly FlowDirectionConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRtl && isRtl)
            return FlowDirection.RightToLeft;
        return FlowDirection.LeftToRight;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
