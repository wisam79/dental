using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DentalID.Core.Entities;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.ViewModels;

public class GenderCodeToDisplayConverter : IValueConverter
{
    public static readonly GenderCodeToDisplayConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var normalized = Subject.NormalizeGenderCode(value?.ToString());
        return normalized switch
        {
            "Male" => Loc.Instance["Lab_Save_Gender_Male"],
            "Female" => Loc.Instance["Lab_Save_Gender_Female"],
            _ => Loc.Instance["Lab_Save_Gender_Unknown"]
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Subject.NormalizeGenderCode(value?.ToString());
    }
}
