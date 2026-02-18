using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DentalID.Desktop.Views;

public partial class ImportWizardView : UserControl
{
    public ImportWizardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
