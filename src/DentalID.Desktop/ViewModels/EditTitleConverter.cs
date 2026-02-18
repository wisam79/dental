using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Converts IsEditing boolean to dialog title text.
/// </summary>
public class EditTitleConverter : IValueConverter
{
    public static readonly EditTitleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEditing)
            return isEditing ? "✏️ Edit Subject" : "➕ New Subject";
        return "Subject";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
