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

    public bool IsIntegrityReady => ProgressValue >= 20;
    public bool IsDatabaseReady => ProgressValue >= 50;
    public bool IsAiReady => ProgressValue >= 80;

    public double IntegrityOpacity => IsIntegrityReady ? 1.0 : 0.35;
    public double DatabaseOpacity => IsDatabaseReady ? 1.0 : 0.35;
    public double AiOpacity => IsAiReady ? 1.0 : 0.35;

    partial void OnProgressValueChanged(double value)
    {
        OnPropertyChanged(nameof(IsIntegrityReady));
        OnPropertyChanged(nameof(IsDatabaseReady));
        OnPropertyChanged(nameof(IsAiReady));
        OnPropertyChanged(nameof(IntegrityOpacity));
        OnPropertyChanged(nameof(DatabaseOpacity));
        OnPropertyChanged(nameof(AiOpacity));
    }

    public void UpdateStatus(string message, double progress)
    {
        StatusMessage = message;
        ProgressValue = progress;
    }
}
