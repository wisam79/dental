using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DentalID.Desktop.Views.Controls;

public partial class SkeletonLoader : UserControl
{
    public SkeletonLoader()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
