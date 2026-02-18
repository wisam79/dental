using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Converts validation errors to CSS classes for styling.
/// </summary>
public class ValidationConverter : IValueConverter
{
    public static readonly ValidationConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value != null)
        {
            return "invalid";
        }
        return "valid";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
