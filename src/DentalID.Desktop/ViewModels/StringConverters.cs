using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// String-related value converters.
/// </summary>
public static class StringConverters
{
    public static readonly IsNullOrEmptyConverter IsNullOrEmpty = new IsNullOrEmptyConverter();
    public static readonly IsNotNullOrEmptyConverter IsNotNullOrEmpty = new IsNotNullOrEmptyConverter();
    public static readonly IsNotNullOrWhiteSpaceConverter IsNotNullOrWhiteSpace = new IsNotNullOrWhiteSpaceConverter();

    public class IsNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class IsNotNullOrEmptyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class IsNotNullOrWhiteSpaceConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
