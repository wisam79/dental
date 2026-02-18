using Avalonia.Controls;

namespace DentalID.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.SystemDecorations = SystemDecorations.Full;
        this.ExtendClientAreaToDecorationsHint = false;
    }

}
