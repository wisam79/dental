using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Desktop.Messages;
using DentalID.Desktop.Models;
using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Application.Interfaces;
using DentalID.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DentalID.Desktop.ViewModels;

public partial class AnalysisLabViewModel : ViewModelBase, IDisposable
{
    private const float MinDisplayNormalizedSize = 0.005f;

    private readonly IForensicAnalysisService _forensicService;
    private readonly ISubjectRepository _subjectRepo;
    private readonly IReportService _reportService;
    private readonly ILoggerService _logger;
    private readonly IFileService _fileService;
    private readonly IToastService _toastService;
    private readonly INavigationService _navigationService;
    private readonly ILocalizationService _localizationService;

    // ── State Machine ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsLoadingImage))]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsAnalyzing))]
    [NotifyPropertyChangedFor(nameof(IsReview))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsImageLoaded))] 
    [NotifyPropertyChangedFor(nameof(CanRunAnalysis))]
    [NotifyPropertyChangedFor(nameof(IsAnalysisComplete))]
    [NotifyPropertyChangedFor(nameof(IsSaving))]
    [NotifyCanExecuteChangedFor(nameof(RunAnalysisCommand))]
    private AnalysisState _currentState = AnalysisState.Idle;

    [ObservableProperty] private string _errorMessage = "";

    public bool IsIdle => CurrentState == AnalysisState.Idle;
    public bool IsLoadingImage => CurrentState == AnalysisState.LoadingImage;
    public bool IsReady => CurrentState == AnalysisState.Ready;
    public bool IsAnalyzing => CurrentState == AnalysisState.Analyzing;
    public bool IsReview => CurrentState == AnalysisState.Review;
    public bool IsAnalysisComplete => IsReview;
    public bool IsError => CurrentState == AnalysisState.Error;
    public bool IsSaving => CurrentState == AnalysisState.Saving;
    
    public bool HasImage => !string.IsNullOrEmpty(LoadedImagePath) && CurrentState != AnalysisState.LoadingImage;

    // Computed property for View compatibility
    public bool IsImageLoaded => HasImage && (CurrentState == AnalysisState.Ready || 
                                 CurrentState == AnalysisState.Analyzing || 
                                 CurrentState == AnalysisState.Review ||
                                 CurrentState == AnalysisState.Error ||
                                 CurrentState == AnalysisState.Saving);

    // ── Image State ──
    [ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasImage))]
[NotifyPropertyChangedFor(nameof(IsImageLoaded))]
[NotifyPropertyChangedFor(nameof(CanRunAnalysis))]
[NotifyCanExecuteChangedFor(nameof(RunAnalysisCommand))]
private string? _loadedImagePath;
    [ObservableProperty] private string? _imageFileName;
    [ObservableProperty] private double _imageWidth;
    [ObservableProperty] private double _imageHeight;

    // ── Subject Linking ──
    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private bool _isSaveDialogOpen;
    
    // Dialog Inputs
    [ObservableProperty] private string _newSubjectName = "";
    [ObservableProperty] private string _newSubjectGender = "Unknown";
    [ObservableProperty] private int _newSubjectGenderIndex = 2;
    [ObservableProperty] private string _newSubjectNationalId = "";
    [ObservableProperty] private DateTime? _newSubjectDob;

    partial void OnNewSubjectGenderChanged(string value) => NewSubjectGenderIndex = ToGenderIndex(value);
    partial void OnNewSubjectGenderIndexChanged(int value) => NewSubjectGender = value switch
    {
        0 => "Male",
        1 => "Female",
        _ => "Unknown"
    };

    // ── Results ──
    public ObservableCollection<DetectedTooth> DetectedTeeth { get; } = new();
    public ObservableCollection<DetectedPathology> DetectedPathologies { get; } = new();
    public ObservableCollection<string> ForensicFlags { get; } = new();
    public ObservableCollection<AiMessage> SmartInsights { get; } = new();
    
    [ObservableProperty] private OdontogramViewModel _odontogram;

    private AnalysisResult? _currentAnalysisResult;
    private Avalonia.Media.Imaging.Bitmap? _currentBitmap;

    // Statistics
    [ObservableProperty] private int _teethDetectedCount;
    [ObservableProperty] private int _pathologiesCount;
    [ObservableProperty] private int? _estimatedAge;
    [ObservableProperty] private string? _estimatedGender;

    // ── Fingerprint Display ──
    [ObservableProperty] private string _fingerprintCode = "";
    [ObservableProperty] private double _uniquenessScore;
    [ObservableProperty] private bool _hasFeatureVector;

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _showTeethOverlay = true;
    [ObservableProperty] private bool _showPathosOverlay = false;
    [ObservableProperty] private double _windowCenter = 0.5;
    [ObservableProperty] private double _windowWidth = 1.0;
    [ObservableProperty] private double _analysisVisualProgress;
    [ObservableProperty] private string _analysisElapsed = "00:00";
    [ObservableProperty] private string _analysisPhase = "";
    [ObservableProperty] private double _analysisSpinnerAngle;
    
    public ObservableCollection<DetectedTooth> RawTeeth { get; } = new();
    public ILocalizationService LocalizationService => _localizationService;
    [ObservableProperty] private float _sensitivity = 0.5f;
    private DispatcherTimer? _analysisFeedbackTimer;
    private DateTime _analysisStartedAtUtc;
    private int _analysisTick;

    public bool HasImageEnhancements =>
        Math.Abs(WindowCenter - 0.5) > 0.0001 ||
        Math.Abs(WindowWidth - 1.0) > 0.0001;

    public string ImageEnhancementSummary => $"B:{WindowCenter:F2} C:{WindowWidth:F2}";

    [RelayCommand]
    private void CancelSave()
    {
        IsSaveDialogOpen = false;
        NewSubjectName = "";
        NewSubjectGender = "Unknown";
        NewSubjectGenderIndex = 2;
        NewSubjectNationalId = "";
        NewSubjectDob = null;
        SelectedSubject = null;
    }

    public AnalysisLabViewModel(
        IForensicAnalysisService forensicService,
        ISubjectRepository subjectRepo,
        IReportService reportService,
        ILoggerService logger,
        IFileService fileService,
        IToastService toastService,
        INavigationService navigationService,
        ILocalizationService localizationService)
    {
        Title = "Analysis Lab";
        _forensicService = forensicService;
        _subjectRepo = subjectRepo;
        _reportService = reportService;
        _logger = logger;
        _fileService = fileService;
        _toastService = toastService;
        _navigationService = navigationService;
        _localizationService = localizationService;
        _odontogram = new OdontogramViewModel();
        
        StatusMessage = _localizationService["Msg_LabReady"];
    }

    private async Task LoadRecentSubjectsAsync()
    {
        try
        {
            var subjects = await _subjectRepo.GetAllAsync(page: 1, pageSize: 20);
            Subjects = new ObservableCollection<Subject>(subjects);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent subjects");
        }
    }

    [RelayCommand]
    private async Task BrowseImage()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = desktop.MainWindow;
                if (window == null) return;

                var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = _localizationService["Dialog_SelectEvidence"],
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.dcm" } } }
                });

                if (files.Count > 0)
                {
                    var path = files[0].TryGetLocalPath();
                    if (path != null)
                    {
                        var extension = Path.GetExtension(path).ToLower();
                        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".dcm" }.Contains(extension))
                        {
                            _toastService.Show(_localizationService["Msg_InvalidFileTitle"], _localizationService["Msg_InvalidFileBody"], ToastType.Error);
                            return;
                        }

                        CurrentState = AnalysisState.LoadingImage;
                        StatusMessage = "Loading image...";

                        LoadedImagePath = path;
                        ImageFileName = Path.GetFileName(path);
                        
                        _currentBitmap?.Dispose();
                        bool loadSuccess = false;
                        try
                        {
                            _currentBitmap = await Task.Run(() => 
                            {
                                using var stream = _fileService.OpenRead(path);
                                return new Avalonia.Media.Imaging.Bitmap(stream);
                            });
                            
                            ImageWidth = _currentBitmap.Size.Width;
                            ImageHeight = _currentBitmap.Size.Height;
                            loadSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to load image bitmap");
                            StatusMessage = FormatSafe(_localizationService["Msg_PreviewFail"] ?? "Preview failed: {0}", ex.Message);
                            CurrentState = AnalysisState.Error;
                            ErrorMessage = ex.Message;
                        }

                        if (loadSuccess)
                        {
                            ClearResults();
                            CurrentState = AnalysisState.Ready;
                            StatusMessage = _localizationService["Msg_EvidenceLoaded"];
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to browse for image");
            _toastService.Show("Error", _localizationService["Msg_SelectFail"], ToastType.Error);
            CurrentState = AnalysisState.Error;
            ErrorMessage = ex.Message;
        }
    }

    private string FormatSafe(string format, params object[] args)
    {
        try
        {
            return string.Format(format, args);
        }
        catch
        {
            return $"{format} - {string.Join(", ", args)}";
        }
    }

    private bool CanRunAnalysis => CurrentState == AnalysisState.Ready || 
                                   CurrentState == AnalysisState.Review || 
                                   CurrentState == AnalysisState.Error ||
                                   (CurrentState == AnalysisState.Idle && !string.IsNullOrWhiteSpace(LoadedImagePath));

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunAnalysis()
    {
        if (!CanRunAnalysis) return;

        CurrentState = AnalysisState.Analyzing;
        StatusMessage = _localizationService["Lab_Analyzing"];
        StartAnalysisFeedback();

        await SafeExecuteAsync(async () =>
        {
            if (LoadedImagePath == null) return;
            var imagePath = LoadedImagePath;
            var sensitivity = Sensitivity;
            StatusMessage = _localizationService["Lab_Analyzing"];

            // Move heavy ONNX work off the UI thread to keep the desktop shell responsive.
            var result = await Task.Run(() => _forensicService.AnalyzeImageAsync(imagePath, sensitivity));

            if (result.IsSuccess)
            {
                AnalysisVisualProgress = 100;
                AnalysisPhase = _localizationService["Msg_AnalysisComplete"];
                UpdateUIResults(result);
                _currentAnalysisResult = result;
                CurrentState = AnalysisState.Review;
                StatusMessage = _localizationService["Msg_AnalysisComplete"];
                
                var successBody = FormatSafe(
                    _localizationService["Msg_FoundTeethPathos"] ?? "Found {0} teeth, {1} pathologies",
                    TeethDetectedCount,
                    PathologiesCount);
                WeakReferenceMessenger.Default.Send(new ShowToastMessage(_localizationService["Msg_AnalysisComplete"], successBody, ToastType.Success));
                
                _logger.LogInformation(
                    $"Analysis completed successfully: displayed={TeethDetectedCount} teeth, displayed={PathologiesCount} pathologies; raw={result.Teeth.Count} teeth, raw={result.Pathologies.Count} pathologies");
            }
            else
            {
                var errorMsg = result.Error ?? "Unknown failure during analysis";
                AnalysisPhase = _localizationService["Lab_Phase_Finalize"];
                ErrorMessage = errorMsg;
                CurrentState = AnalysisState.Error;
                StatusMessage = FormatSafe(_localizationService["Msg_AnalysisFailed"] ?? "Analysis failed: {0}", errorMsg);
                _logger.LogError(new Exception(errorMsg), $"Analysis failed: {errorMsg}");
                // SafeExecuteAsync handles logging too, but redundant is safer
            }
        });

        // Ensure we don't get stuck in Analyzing if an unhandled exception occurred within SafeExecuteAsync (which swallows it)
        if (CurrentState == AnalysisState.Analyzing)
        {
             CurrentState = AnalysisState.Error;
             ErrorMessage = "An unexpected error occurred.";
             StatusMessage = "Analysis interrupted.";
        }

        StopAnalysisFeedback();
    }

    [RelayCommand]
    private async Task SaveToSubject()
    {
        if (_currentAnalysisResult == null || LoadedImagePath == null)
        {
             _toastService.Show(_localizationService["Msg_NoEvidenceTitle"], _localizationService["Msg_NoEvidenceBody"], ToastType.Warning);
             return;
        }
        await LoadRecentSubjectsAsync(); // Refresh list safely
        IsSaveDialogOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmSaveNewSubject()
    {
        if (_currentAnalysisResult == null || string.IsNullOrWhiteSpace(LoadedImagePath))
        {
            _toastService.Show(_localizationService["Msg_NoEvidenceTitle"], _localizationService["Msg_NoEvidenceBody"], ToastType.Warning);
            return;
        }

        var analysisResult = _currentAnalysisResult!;
        var imagePath = LoadedImagePath!;
        Subject? targetSubject = SelectedSubject;

        if (targetSubject == null)
        {
            if (string.IsNullOrWhiteSpace(NewSubjectName))
            {
                _toastService.Show(_localizationService["Msg_MissingInfoTitle"], _localizationService["Msg_MissingInfoBody"], ToastType.Warning);
                return;
            }
             targetSubject = new Subject
            {
                SubjectId = $"SUB-{Guid.NewGuid():N}",
                FullName = NewSubjectName,
                NationalId = NewSubjectNationalId,
                Gender = ToGenderCode(NewSubjectGenderIndex),
                DateOfBirth = NewSubjectDob,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        var previousState = CurrentState;
        CurrentState = AnalysisState.Saving;
        var savedSubjectCode = targetSubject.SubjectId;
        var saveSucceeded = false;
        int? newlyCreatedSubjectId = null;
        var subjectNameForToast = targetSubject.FullName ?? "Unknown Subject";

        await SafeExecuteAsync(async () =>
        {
            if (targetSubject.Id == 0)
            {
                var created = await _subjectRepo.AddAsync(targetSubject);
                targetSubject = created;
                savedSubjectCode = created.SubjectId;
                newlyCreatedSubjectId = created.Id;
            }

             // Link image to subject
             try 
             {
                 await _forensicService.SaveEvidenceAsync(imagePath, analysisResult, targetSubject.Id);
                 saveSucceeded = true;
             }
             catch
             {
                 // Re-throw to be caught by SafeExecuteAsync and trigger rollback logic
                 throw;
             }
             
             // Log
             _logger.LogInformation($"Saved analysis for subject {subjectNameForToast}");
             
             IsSaveDialogOpen = false;
             
        },
        errorMessage: _localizationService["Msg_SaveFailedBody"] ?? "Failed to save evidence record.",
        successMessage: FormatSafe(_localizationService["Msg_LinkSuccess"] ?? "Evidence linked to {0}", subjectNameForToast),
        errorTitle: _localizationService["Msg_SaveFailedTitle"] ?? "Save Failed");

        if (CurrentState == AnalysisState.Saving)
            CurrentState = previousState;

        if (!saveSucceeded && newlyCreatedSubjectId.HasValue)
        {
            try
            {
                await _subjectRepo.DeleteAsync(newlyCreatedSubjectId.Value);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, $"Failed to rollback newly created subject {newlyCreatedSubjectId.Value} after evidence save failure.");
            }
        }

        if (saveSucceeded)
        {
            NavigateToSavedSubject(savedSubjectCode);
        }
    }

    private void NavigateToSavedSubject(string? savedSubjectCode)
    {
        // Unit tests/headless runs may not have a desktop lifetime or a running UI loop.
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            return;
        }

        void NavigateAndFocus()
        {
            var subjectsVm = _navigationService.NavigateTo<SubjectsViewModel>();
            if (subjectsVm == null)
            {
                _logger.LogWarning("[NAV] Save succeeded but navigation to SubjectsViewModel returned null.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(savedSubjectCode))
            {
                _ = subjectsVm.FocusSubjectByCodeAsync(savedSubjectCode);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            NavigateAndFocus();
            return;
        }

        Dispatcher.UIThread.Post(NavigateAndFocus);
    }

    [RelayCommand]
    private void ResetView()
    {
        CurrentState = AnalysisState.Idle;
        ClearResults();
        LoadedImagePath = null;
        ImageFileName = null;
        _currentBitmap?.Dispose();
        _currentBitmap = null;
        IsSaveDialogOpen = false;
        _logger.LogInformation("View Reset Triggered");
    }

    private void UpdateUIResults(AnalysisResult result)
    {
        ClearResults();

        var normalizedTeeth = new List<DetectedTooth>();
        foreach (var tooth in result.Teeth)
        {
            if (TryNormalizeToothForDisplay(tooth, out var normalized))
                normalizedTeeth.Add(normalized);
        }

        var displayTeeth = normalizedTeeth
            .GroupBy(t => t.FdiNumber)
            .Select(g => g.OrderByDescending(t => t.Confidence).First())
            .OrderBy(t => t.FdiNumber)
            .ToList();

        TeethDetectedCount = displayTeeth.Count;
        PathologiesCount = result.Pathologies.Count;
        EstimatedAge = result.EstimatedAge;
        EstimatedGender = result.EstimatedGender;

        // Fingerprint
        if (result.Fingerprint != null)
        {
            FingerprintCode = result.Fingerprint.Code ?? "";
            UniquenessScore = result.Fingerprint.UniquenessScore;
            HasFeatureVector = result.FeatureVector is { Length: > 0 };
        }

        foreach (var t in displayTeeth)
        {
            DetectedTeeth.Add(t);
        }
        foreach (var t in result.RawTeeth)
        {
            RawTeeth.Add(t);
        }
        foreach (var p in result.Pathologies) DetectedPathologies.Add(p);
        foreach (var f in result.Flags) ForensicFlags.Add(f);
        // 4. Insights
        foreach (var insight in result.SmartInsights)
        {
            SmartInsights.Add(new AiMessage { Role = "System", Content = insight, Timestamp = DateTime.UtcNow });
        }

        // 5. Update Odontogram
        Odontogram.Update(result);
    }

    private void StartAnalysisFeedback()
    {
        _analysisFeedbackTimer?.Stop();
        _analysisFeedbackTimer = null;

        _analysisStartedAtUtc = DateTime.UtcNow;
        _analysisTick = 0;
        AnalysisVisualProgress = 6;
        AnalysisElapsed = "00:00";
        AnalysisPhase = _localizationService["Lab_Phase_Prepare"];
        AnalysisSpinnerAngle = 0;

        _analysisFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };

        _analysisFeedbackTimer.Tick += OnAnalysisFeedbackTick;
        _analysisFeedbackTimer.Start();
    }

    private void StopAnalysisFeedback()
    {
        if (_analysisFeedbackTimer != null)
        {
            _analysisFeedbackTimer.Stop();
            _analysisFeedbackTimer.Tick -= OnAnalysisFeedbackTick;
            _analysisFeedbackTimer = null;
        }

        if (CurrentState != AnalysisState.Review && CurrentState != AnalysisState.Saving)
        {
            AnalysisVisualProgress = 0;
        }
    }

    private void OnAnalysisFeedbackTick(object? sender, EventArgs e)
    {
        _analysisTick++;
        AnalysisSpinnerAngle = (AnalysisSpinnerAngle + 14) % 360;

        var elapsed = DateTime.UtcNow - _analysisStartedAtUtc;
        AnalysisElapsed = elapsed.ToString(@"mm\:ss");

        if (AnalysisVisualProgress < 92)
        {
            AnalysisVisualProgress = Math.Min(92, AnalysisVisualProgress + 0.7);
        }

        AnalysisPhase = _analysisTick switch
        {
            < 12 => _localizationService["Lab_Phase_Prepare"],
            < 26 => _localizationService["Lab_Phase_Detect"],
            < 40 => _localizationService["Lab_Phase_Fusing"],
            _ => _localizationService["Lab_Phase_Finalize"]
        };
    }

    private void ClearResults()
    {
        _currentAnalysisResult = null;
        TeethDetectedCount = 0;
        PathologiesCount = 0;
        EstimatedAge = null;
        EstimatedGender = null;
        FingerprintCode = "";
        UniquenessScore = 0.0;
        HasFeatureVector = false;
        
        DetectedTeeth.Clear();
        RawTeeth.Clear();
        DetectedPathologies.Clear();
        ForensicFlags.Clear();
        SmartInsights.Clear();
        Odontogram.Clear();
    }

    public void Dispose()
    {
        StopAnalysisFeedback();
        _currentBitmap?.Dispose();
    }

    private static bool TryNormalizeToothForDisplay(DetectedTooth tooth, out DetectedTooth normalized)
    {
        normalized = null!;
        if (tooth == null)
            return false;

        if (!IsFinite(tooth.X) || !IsFinite(tooth.Y) || !IsFinite(tooth.Width) || !IsFinite(tooth.Height))
            return false;

        if (tooth.Width <= 0f || tooth.Height <= 0f)
            return false;

        // Intersect with normalized image bounds first (avoid dropping partially-outside boxes).
        var left = Math.Max(0f, tooth.X);
        var top = Math.Max(0f, tooth.Y);
        var right = Math.Min(1f, tooth.X + tooth.Width);
        var bottom = Math.Min(1f, tooth.Y + tooth.Height);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0f || height <= 0f)
            return false;

        // Ensure tiny but valid teeth remain visible instead of being dropped.
        if (width < MinDisplayNormalizedSize)
        {
            var cx = (left + right) * 0.5f;
            width = MinDisplayNormalizedSize;
            left = Math.Clamp(cx - (width * 0.5f), 0f, 1f - width);
        }

        if (height < MinDisplayNormalizedSize)
        {
            var cy = (top + bottom) * 0.5f;
            height = MinDisplayNormalizedSize;
            top = Math.Clamp(cy - (height * 0.5f), 0f, 1f - height);
        }

        normalized = new DetectedTooth
        {
            FdiNumber = tooth.FdiNumber,
            Confidence = tooth.Confidence,
            X = left,
            Y = top,
            Width = width,
            Height = height,
            Outline = tooth.Outline?.Select(p => (X: Math.Clamp(p.X, 0f, 1f), Y: Math.Clamp(p.Y, 0f, 1f))).ToList()
        };

        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static int ToGenderIndex(string? rawGender) => Subject.NormalizeGenderCode(rawGender) switch
    {
        "Male" => 0,
        "Female" => 1,
        _ => 2
    };

    private static string ToGenderCode(int index) => index switch
    {
        0 => "Male",
        1 => "Female",
        _ => "Unknown"
    };
}


