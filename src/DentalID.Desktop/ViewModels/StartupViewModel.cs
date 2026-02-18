using CommunityToolkit.Mvvm.ComponentModel;

namespace DentalID.Desktop.ViewModels;

public partial class StartupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusMessage = "Initializing System...";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _version = "v1.0.0 (Forensic Edition)";

    public void UpdateStatus(string message, double progress)
    {
        StatusMessage = message;
        ProgressValue = progress;
    }
}
