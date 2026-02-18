using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DentalID.Desktop.Views;

public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
