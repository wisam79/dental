using Avalonia.Controls;

namespace DentalID.Desktop.Views;

public partial class ReportGeneratorView : UserControl
{
    public ReportGeneratorView()
    {
        InitializeComponent();
        Loaded += (s, e) => 
        {
            if (DataContext is ViewModels.ReportGeneratorViewModel vm)
            {
                vm.InitializeCommand.Execute(null);
            }
        };
    }
}