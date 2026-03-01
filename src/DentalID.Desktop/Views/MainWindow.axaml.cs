using Avalonia.Controls;
using Avalonia;
using System;

namespace DentalID.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        EnsureVisibleWithinWorkingArea();
    }

    private void EnsureVisibleWithinWorkingArea()
    {
        if (WindowState != WindowState.Normal)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null)
            return;

        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var work = screen.WorkingArea;

        // Keep a small visual margin from screen edges.
        const double edgeMarginDip = 24;
        var maxWidthDip = Math.Max(800, (work.Width / scale) - edgeMarginDip);
        var maxHeightDip = Math.Max(600, (work.Height / scale) - edgeMarginDip);

        if (double.IsNaN(Width) || Width <= 0 || Width > maxWidthDip)
            Width = maxWidthDip;

        if (double.IsNaN(Height) || Height <= 0 || Height > maxHeightDip)
            Height = maxHeightDip;

        var widthPx = (int)Math.Round(Width * scale);
        var heightPx = (int)Math.Round(Height * scale);
        var x = work.X + Math.Max(0, (work.Width - widthPx) / 2);
        var y = work.Y + Math.Max(0, (work.Height - heightPx) / 2);

        Position = new PixelPoint(x, y);
    }
}
