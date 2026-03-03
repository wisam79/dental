using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.ViewModels;

public partial class StartupViewModel : ViewModelBase, IDisposable
{
    public const string TeethModelKey = "teeth";
    public const string PathologyModelKey = "pathology";
    public const string EncoderModelKey = "encoder";

    public const string StatePending = "Pending";
    public const string StateValidating = "Validating";
    public const string StateVerified = "Verified";
    public const string StateLoading = "Loading";
    public const string StateReady = "Ready";
    public const string StateError = "Error";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _version = "v1.0.0 (Forensic Edition)";

    [ObservableProperty]
    private string _teethModelState = StatePending;

    [ObservableProperty]
    private string _pathologyModelState = StatePending;

    [ObservableProperty]
    private string _encoderModelState = StatePending;

    [ObservableProperty]
    private double _teethModelProgress = 0;

    [ObservableProperty]
    private double _pathologyModelProgress = 0;

    [ObservableProperty]
    private double _encoderModelProgress = 0;

    public bool IsIntegrityReady => ProgressValue >= 20;
    public bool IsDatabaseReady => ProgressValue >= 50;
    public bool IsAiReady => ProgressValue >= 80;

    public double IntegrityOpacity => IsIntegrityReady ? 1.0 : 0.35;
    public double DatabaseOpacity => IsDatabaseReady ? 1.0 : 0.35;
    public double AiOpacity => IsAiReady ? 1.0 : 0.35;

    public bool IsModelPanelVisible => ProgressValue >= 70 || ReadyModelsCount > 0 || HasModelError;
    public bool HasModelError => TeethModelState == StateError ||
                                 PathologyModelState == StateError ||
                                 EncoderModelState == StateError;

    public int ReadyModelsCount =>
        CountReady(TeethModelState) + CountReady(PathologyModelState) + CountReady(EncoderModelState);

    public string ModelsSummary
    {
        get
        {
            var format = L("Startup_ModelsSummary", "{0}/3 models ready");
            try
            {
                return string.Format(format, ReadyModelsCount);
            }
            catch
            {
                return $"{ReadyModelsCount}/3 models ready";
            }
        }
    }
    public double ModelsSummaryProgress => (ReadyModelsCount / 3.0) * 100.0;

    public string TeethModelStateDisplay => LocalizeState(TeethModelState);
    public string PathologyModelStateDisplay => LocalizeState(PathologyModelState);
    public string EncoderModelStateDisplay => LocalizeState(EncoderModelState);

    public double TeethModelOpacity => ResolveCardOpacity(TeethModelState);
    public double PathologyModelOpacity => ResolveCardOpacity(PathologyModelState);
    public double EncoderModelOpacity => ResolveCardOpacity(EncoderModelState);

    public bool TeethShowLoadingIcon => IsLoadingState(TeethModelState);
    public bool PathologyShowLoadingIcon => IsLoadingState(PathologyModelState);
    public bool EncoderShowLoadingIcon => IsLoadingState(EncoderModelState);

    public bool TeethShowReadyIcon => TeethModelState == StateReady;
    public bool PathologyShowReadyIcon => PathologyModelState == StateReady;
    public bool EncoderShowReadyIcon => EncoderModelState == StateReady;

    public bool TeethShowErrorIcon => TeethModelState == StateError;
    public bool PathologyShowErrorIcon => PathologyModelState == StateError;
    public bool EncoderShowErrorIcon => EncoderModelState == StateError;

    public StartupViewModel()
    {
        StatusMessage = L("Startup_Status_Initializing", "Initializing System...");
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    private bool _isDisposed;

    partial void OnProgressValueChanged(double value)
    {
        OnPropertyChanged(nameof(IsIntegrityReady));
        OnPropertyChanged(nameof(IsDatabaseReady));
        OnPropertyChanged(nameof(IsAiReady));
        OnPropertyChanged(nameof(IntegrityOpacity));
        OnPropertyChanged(nameof(DatabaseOpacity));
        OnPropertyChanged(nameof(AiOpacity));
        OnPropertyChanged(nameof(IsModelPanelVisible));
    }

    public void UpdateStatus(string message, double progress)
    {
        StatusMessage = message;
        ProgressValue = progress;
    }

    public void ResetModelStates()
    {
        TeethModelState = StatePending;
        PathologyModelState = StatePending;
        EncoderModelState = StatePending;

        TeethModelProgress = 0;
        PathologyModelProgress = 0;
        EncoderModelProgress = 0;
    }

    public void UpdateModelStatus(string modelKey, string state, double progress)
    {
        var normalizedState = NormalizeState(state);
        var clampedProgress = Math.Clamp(progress, 0, 100);
        var key = modelKey.Trim().ToLowerInvariant();

        switch (key)
        {
            case TeethModelKey:
                TeethModelState = normalizedState;
                TeethModelProgress = clampedProgress;
                break;
            case PathologyModelKey:
                PathologyModelState = normalizedState;
                PathologyModelProgress = clampedProgress;
                break;
            case EncoderModelKey:
                EncoderModelState = normalizedState;
                EncoderModelProgress = clampedProgress;
                break;
            default:
                throw new ArgumentException($"Unknown model key '{modelKey}'.", nameof(modelKey));
        }
    }

    partial void OnTeethModelStateChanged(string value) => RaiseModelStateProperties();
    partial void OnPathologyModelStateChanged(string value) => RaiseModelStateProperties();
    partial void OnEncoderModelStateChanged(string value) => RaiseModelStateProperties();

    partial void OnTeethModelProgressChanged(double value) => RaiseModelProgressProperties();
    partial void OnPathologyModelProgressChanged(double value) => RaiseModelProgressProperties();
    partial void OnEncoderModelProgressChanged(double value) => RaiseModelProgressProperties();

    private void RaiseModelStateProperties()
    {
        OnPropertyChanged(nameof(HasModelError));
        OnPropertyChanged(nameof(ReadyModelsCount));
        OnPropertyChanged(nameof(ModelsSummary));
        OnPropertyChanged(nameof(ModelsSummaryProgress));
        OnPropertyChanged(nameof(IsModelPanelVisible));

        OnPropertyChanged(nameof(TeethModelOpacity));
        OnPropertyChanged(nameof(PathologyModelOpacity));
        OnPropertyChanged(nameof(EncoderModelOpacity));

        OnPropertyChanged(nameof(TeethShowLoadingIcon));
        OnPropertyChanged(nameof(PathologyShowLoadingIcon));
        OnPropertyChanged(nameof(EncoderShowLoadingIcon));

        OnPropertyChanged(nameof(TeethShowReadyIcon));
        OnPropertyChanged(nameof(PathologyShowReadyIcon));
        OnPropertyChanged(nameof(EncoderShowReadyIcon));

        OnPropertyChanged(nameof(TeethShowErrorIcon));
        OnPropertyChanged(nameof(PathologyShowErrorIcon));
        OnPropertyChanged(nameof(EncoderShowErrorIcon));
        OnPropertyChanged(nameof(TeethModelStateDisplay));
        OnPropertyChanged(nameof(PathologyModelStateDisplay));
        OnPropertyChanged(nameof(EncoderModelStateDisplay));
    }

    private void RaiseModelProgressProperties()
    {
        OnPropertyChanged(nameof(ModelsSummaryProgress));
    }

    private static int CountReady(string state) => state == StateReady ? 1 : 0;

    private static bool IsLoadingState(string state) =>
        state == StateValidating || state == StateLoading || state == StateVerified;

    private static double ResolveCardOpacity(string state) =>
        state == StatePending ? 0.45 : 1.0;

    private static string NormalizeState(string state) =>
        string.IsNullOrWhiteSpace(state) ? StatePending : state.Trim();

    private void OnLanguageChanged(object? sender, string e)
    {
        OnPropertyChanged(nameof(ModelsSummary));
        OnPropertyChanged(nameof(TeethModelStateDisplay));
        OnPropertyChanged(nameof(PathologyModelStateDisplay));
        OnPropertyChanged(nameof(EncoderModelStateDisplay));
    }

    private static string LocalizeState(string state) => state switch
    {
        StatePending => L("Startup_State_Pending", StatePending),
        StateValidating => L("Startup_State_Validating", StateValidating),
        StateVerified => L("Startup_State_Verified", StateVerified),
        StateLoading => L("Startup_State_Loading", StateLoading),
        StateReady => L("Startup_State_Ready", StateReady),
        StateError => L("Startup_State_Error", StateError),
        _ => state
    };

    private static string L(string key, string fallback)
    {
        var value = Loc.Instance[key];
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, $"[{key}]", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
    }
}
