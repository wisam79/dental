using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DentalID.Desktop.ViewModels;

namespace DentalID.Desktop.Views;

public partial class AnalysisLabView : UserControl
{
    public AnalysisLabView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnResetZoom(object? sender, RoutedEventArgs e)
    {
        var viewer = this.FindControl<DentalID.Desktop.Views.Controls.AdvancedImageViewer>("AdvancedViewer");
        if (viewer != null)
        {
            viewer.ResetTransform();
        }
    }
}
