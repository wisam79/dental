using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Converts an index to a boolean for RadioButton IsChecked binding.
/// Usage: IsChecked="{Binding Index, Converter={x:Static IndexToCheckedConverter.Instance}, ConverterParameter=0}"
/// </summary>
public class IndexToCheckedConverter : IValueConverter
{
    public static readonly IndexToCheckedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return index == target;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return target;
        return -1; // Not checked, don't update
    }
}
