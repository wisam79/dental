using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Data;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Converts a string path to a Bitmap for display.
/// </summary>
public class BitmapAssetValueConverter : IValueConverter
{
    public static BitmapAssetValueConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                if (path.StartsWith("avares://"))
                {
                    return new Bitmap(AssetLoader.Open(new Uri(path)));
                }

                if (System.IO.File.Exists(path))
                {
                    return new Bitmap(path);
                }
            }
            catch
            {
                // Fallback or null
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
