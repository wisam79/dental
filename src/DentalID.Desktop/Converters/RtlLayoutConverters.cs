using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace DentalID.Desktop.Converters;

public class BoolToLogicalHorizontalAlignmentConverter : IValueConverter
{
    public static readonly BoolToLogicalHorizontalAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "Start").Trim();

        if (logical.Equals("End", StringComparison.OrdinalIgnoreCase))
            return isRtl ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        return isRtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToLogicalTextAlignmentConverter : IValueConverter
{
    public static readonly BoolToLogicalTextAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "Start").Trim();

        if (logical.Equals("End", StringComparison.OrdinalIgnoreCase))
            return isRtl ? TextAlignment.Left : TextAlignment.Right;

        return isRtl ? TextAlignment.Right : TextAlignment.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToMirroredThicknessConverter : IValueConverter
{
    public static readonly BoolToMirroredThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        Thickness thickness = ParseThickness(parameter as string);

        if (!isRtl)
            return thickness;

        return new Thickness(thickness.Right, thickness.Top, thickness.Left, thickness.Bottom);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Thickness ParseThickness(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var all))
            return new Thickness(all);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new Thickness(h, v);
        if (parts.Length == 4 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return new Thickness(l, t, r, b);

        return default;
    }
}

public class BoolToMirroredCornerRadiusConverter : IValueConverter
{
    public static readonly BoolToMirroredCornerRadiusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        CornerRadius radius = ParseCornerRadius(parameter as string);

        if (!isRtl)
            return radius;

        return new CornerRadius(radius.TopRight, radius.TopLeft, radius.BottomLeft, radius.BottomRight);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static CornerRadius ParseCornerRadius(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var all))
            return new CornerRadius(all);
        if (parts.Length == 4 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tl) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var tr) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var br) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var bl))
            return new CornerRadius(tl, tr, br, bl);

        return default;
    }
}

public class BoolToLogicalDockConverter : IValueConverter
{
    public static readonly BoolToLogicalDockConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "Start").Trim();

        if (logical.Equals("End", StringComparison.OrdinalIgnoreCase))
            return isRtl ? Dock.Left : Dock.Right;

        return isRtl ? Dock.Right : Dock.Left;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToFlyoutPlacementConverter : IValueConverter
{
    public static readonly BoolToFlyoutPlacementConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "End").Trim();

        if (logical.Equals("Start", StringComparison.OrdinalIgnoreCase))
            return isRtl ? PlacementMode.Right : PlacementMode.Left;

        return isRtl ? PlacementMode.Left : PlacementMode.Right;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToArrowGeometryConverter : IValueConverter
{
    public static readonly BoolToArrowGeometryConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string direction = (parameter as string ?? "Next").Trim();
        bool isPrev = direction.Equals("Prev", StringComparison.OrdinalIgnoreCase);

        string resourceKey;
        if (isPrev)
            resourceKey = isRtl ? "IconArrowRight" : "IconArrowLeft";
        else
            resourceKey = isRtl ? "IconArrowLeft" : "IconArrowRight";

        if (Avalonia.Application.Current != null &&
            Avalonia.Application.Current.TryFindResource(resourceKey, out var resource) &&
            resource is Geometry geometry)
        {
            return geometry;
        }

        const string fallbackLeft = "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z";
        const string fallbackRight = "M4,11V13H16L10.5,18.5L11.92,19.92L19.84,12L11.92,4.08L10.5,5.5L16,11H4Z";
        return resourceKey == "IconArrowLeft" ? fallbackLeft : fallbackRight;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToLogicalGridColumnConverter : IValueConverter
{
    public static readonly BoolToLogicalGridColumnConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "Start").Trim();

        if (logical.Equals("End", StringComparison.OrdinalIgnoreCase))
            return isRtl ? 0 : 1;

        return isRtl ? 1 : 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToLogicalColumnWidthConverter : IValueConverter
{
    public static readonly BoolToLogicalColumnWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isRtl = value is bool b && b;
        string logical = (parameter as string ?? "Start").Trim();

        bool isStart = !logical.Equals("End", StringComparison.OrdinalIgnoreCase);
        bool isSidebarColumn = isStart ? !isRtl : isRtl;

        return isSidebarColumn
            ? new GridLength(92, GridUnitType.Pixel)
            : new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
