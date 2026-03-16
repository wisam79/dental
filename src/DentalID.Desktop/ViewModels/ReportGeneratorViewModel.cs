using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Messages;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.ViewModels;

public partial class ReportGeneratorViewModel : ViewModelBase, IDisposable
{
    public const string TargetSubject = "Subject";
    public const string TargetCase = "Case";
    public const string FormatStandard = "Standard";
    public const string FormatDetailed = "Detailed";

    private readonly IReportService _reportService;
    private readonly ISubjectRepository _subjectRepository;
    private readonly ICaseRepository _caseRepository;

    private readonly List<Subject> _allSubjects = new();
    private readonly List<Case> _allCases = new();
    private string? _ownedPreviewPdfPath;
    private bool _isInitialized;

    [ObservableProperty]
    private string _title = "Report Generator";

    [ObservableProperty]
    private ObservableCollection<Subject> _subjectsList = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateReportCommand))]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private ObservableCollection<Case> _casesList = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateReportCommand))]
    private Case? _selectedCase;

    [ObservableProperty]
    private string _subjectSearchText = string.Empty;

    [ObservableProperty]
    private string _caseSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubjectTarget))]
    [NotifyPropertyChangedFor(nameof(IsCaseTarget))]
    [NotifyPropertyChangedFor(nameof(CanConfigureDetections))]
    [NotifyPropertyChangedFor(nameof(CanConfigureOdontogram))]
    [NotifyPropertyChangedFor(nameof(CanConfigureFingerprint))]
    [NotifyPropertyChangedFor(nameof(CanConfigureMatchHistory))]
    [NotifyCanExecuteChangedFor(nameof(GenerateReportCommand))]
    private string _reportTarget = TargetSubject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailedFormat))]
    private string _reportFormat = FormatStandard;

    // Report options
    [ObservableProperty] private bool _includeProfile = true;
    [ObservableProperty] private bool _includeOdontogram = true;
    [ObservableProperty] private bool _includeDetections = true;
    [ObservableProperty] private bool _includeFingerprint = true;
    [ObservableProperty] private bool _includeMatchHistory = true;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private string? _previewPdfPath;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private string _hintMessage = "Choose report scope and format, then generate.";

    [ObservableProperty]
    private string _lastReportSummary = "No report generated yet.";

    [ObservableProperty]
    private string _generatedAtText = "-";

    [ObservableProperty]
    private string _generatedSizeText = "-";

    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewPdfPath) && File.Exists(PreviewPdfPath);
    public bool IsSubjectTarget => string.Equals(ReportTarget, TargetSubject, StringComparison.OrdinalIgnoreCase);
    public bool IsCaseTarget => string.Equals(ReportTarget, TargetCase, StringComparison.OrdinalIgnoreCase);
    public bool IsDetailedFormat => string.Equals(ReportFormat, FormatDetailed, StringComparison.OrdinalIgnoreCase);

    public bool CanConfigureDetections => IsSubjectTarget && IsDetailedFormat;
    public bool CanConfigureOdontogram => IsSubjectTarget && IsDetailedFormat;
    public bool CanConfigureFingerprint => IsSubjectTarget;
    public bool CanConfigureMatchHistory => IsCaseTarget;
    public bool CanGenerateReport => CanGenerateReportInternal();
    public bool CanPreviewReport => CanPreviewReportInternal();
    public IReadOnlyList<string> ReportTargets { get; } = new[] { TargetSubject, TargetCase };
    public IReadOnlyList<string> ReportFormats { get; } = new[] { FormatStandard, FormatDetailed };

    // Design-time constructor
    public ReportGeneratorViewModel()
    {
        _reportService = null!;
        _subjectRepository = null!;
        _caseRepository = null!;
    }

    public ReportGeneratorViewModel(
        IReportService reportService,
        ISubjectRepository subjectRepository,
        ICaseRepository caseRepository)
    {
        _reportService = reportService;
        _subjectRepository = subjectRepository;
        _caseRepository = caseRepository;
    }

    partial void OnSelectedSubjectChanged(Subject? value)
    {
        if (value != null && IsSubjectTarget)
            SelectedCase = null;
        RefreshState();
    }

    partial void OnSelectedCaseChanged(Case? value)
    {
        if (value != null && IsCaseTarget)
            SelectedSubject = null;
        RefreshState();
    }

    partial void OnSubjectSearchTextChanged(string value)
    {
        _subjectSearchTimer?.Stop();
        _subjectSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _subjectSearchTimer.Tick += async (s, e) => { _subjectSearchTimer.Stop(); await ApplySubjectFilterAsync(); };
        _subjectSearchTimer.Start();
    }

    partial void OnCaseSearchTextChanged(string value)
    {
        _caseSearchTimer?.Stop();
        _caseSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _caseSearchTimer.Tick += async (s, e) => { _caseSearchTimer.Stop(); await ApplyCaseFilterAsync(); };
        _caseSearchTimer.Start();
    }

    private DispatcherTimer? _subjectSearchTimer;
    private DispatcherTimer? _caseSearchTimer;

    partial void OnReportTargetChanged(string value)
    {
        if (!IsSubjectTarget && !IsCaseTarget)
            ReportTarget = TargetSubject;

        if (IsSubjectTarget)
            SelectedCase = null;
        else
            SelectedSubject = null;

        RefreshState();
    }

    partial void OnReportFormatChanged(string value)
    {
        if (!string.Equals(value, FormatStandard, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, FormatDetailed, StringComparison.OrdinalIgnoreCase))
        {
            ReportFormat = FormatStandard;
            return;
        }

        RefreshState();
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        void UpdateState()
        {
            OnPropertyChanged(nameof(CanGenerateReport));
            OnPropertyChanged(nameof(CanPreviewReport));
            GenerateReportCommand.NotifyCanExecuteChanged();
            ExportReportCommand.NotifyCanExecuteChanged();
            PrintReportCommand.NotifyCanExecuteChanged();
            OpenPreviewCommand.NotifyCanExecuteChanged();
        }

        if (Dispatcher.UIThread.CheckAccess())
            UpdateState();
        else
            Dispatcher.UIThread.Post(UpdateState);
    }

    partial void OnPreviewPdfPathChanged(string? value)
    {
        void UpdateState()
        {
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(CanPreviewReport));
            ExportReportCommand.NotifyCanExecuteChanged();
            PrintReportCommand.NotifyCanExecuteChanged();
            OpenPreviewCommand.NotifyCanExecuteChanged();
        }

        if (Dispatcher.UIThread.CheckAccess())
            UpdateState();
        else
            Dispatcher.UIThread.Post(UpdateState);
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_isInitialized || _reportService is null)
            return;

        _isInitialized = true;
        await LoadDataAsync();
        RefreshState();
    }

    private async Task LoadDataAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var subjects = await _subjectRepository.GetAllAsync(1, 500).ConfigureAwait(false);
            var cases = await _caseRepository.GetAllAsync(1, 500).ConfigureAwait(false);

            _allSubjects.Clear();
            _allSubjects.AddRange(subjects.OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase));

            _allCases.Clear();
            _allCases.AddRange(cases.OrderByDescending(c => c.CreatedAt));

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ApplySubjectFilterAsync();
                await ApplyCaseFilterAsync();
            });
        }, errorMessage: "Failed to load report data.");
    }

    private async Task ApplySubjectFilterAsync()
    {
        var search = SubjectSearchText.Trim();
        IEnumerable<Subject> query = _allSubjects;
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                ContainsIgnoreCase(s.FullName, search) ||
                ContainsIgnoreCase(s.SubjectId, search) ||
                ContainsIgnoreCase(s.NationalId, search));
        }

        var list = query.ToList();

        // Bug Fix #70: Server-side search fallback if capped list has no matches
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(search) && _allSubjects.Count >= 500)
        {
            var serverResults = await _subjectRepository.SearchAsync(search, 1, 20);
            list = serverResults;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SubjectsList.Clear();
            foreach (var subject in list)
                SubjectsList.Add(subject);

            if (SelectedSubject != null && !SubjectsList.Any(s => s.Id == SelectedSubject.Id))
                SelectedSubject = null;
        });
    }

    private async Task ApplyCaseFilterAsync()
    {
        var search = CaseSearchText.Trim();
        IEnumerable<Case> query = _allCases;
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                ContainsIgnoreCase(c.CaseNumber, search) ||
                ContainsIgnoreCase(c.Title, search) ||
                ContainsIgnoreCase(c.Location, search) ||
                ContainsIgnoreCase(c.Status.ToString(), search));
        }

        var list = query.ToList();

        // Server-side fallback for cases
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(search) && _allCases.Count >= 500)
        {
            var serverResults = await _caseRepository.GetByCaseNumberAsync(search);
            if (serverResults != null) list = new List<Case> { serverResults };
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CasesList.Clear();
            foreach (var forensicCase in list)
                CasesList.Add(forensicCase);

            if (SelectedCase != null && !CasesList.Any(c => c.Id == SelectedCase.Id))
                SelectedCase = null;
        });
    }

    [RelayCommand]
    private async Task ResetFilters()
    {
        SubjectSearchText = string.Empty;
        CaseSearchText = string.Empty;
        await ApplySubjectFilterAsync();
        await ApplyCaseFilterAsync();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedSubject = null;
        SelectedCase = null;
        ValidationMessage = string.Empty;
        RefreshState();
    }

    [RelayCommand(CanExecute = nameof(CanGenerateReportInternal))]
    private async Task GenerateReportAsync()
    {
        RefreshState();
        if (!CanGenerateReportInternal())
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Validation", ValidationMessage, ToastType.Warning));
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsGenerating = true;
            StatusMessage = "Generating report...";
        });

        try
        {
            var (pdfBytes, summary) = await BuildReportPayloadAsync().ConfigureAwait(false);
            var tempPath = Path.Combine(Path.GetTempPath(), $"DentalID_Report_{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, pdfBytes).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ReplacePreviewPath(tempPath);
                LastReportSummary = summary;
                GeneratedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                GeneratedSizeText = FormatBytes(pdfBytes.Length);
                ValidationMessage = string.Empty;
                StatusMessage = "Report generated successfully.";
            });

            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Success", "Report generated successfully.", ToastType.Success));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ValidationMessage = ex.Message;
                StatusMessage = $"Error: {ex.Message}";
            });
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", ex.Message, ToastType.Error));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsGenerating = false;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewReportInternal))]
    private async Task ExportReportAsync()
    {
        if (!HasPreview || string.IsNullOrWhiteSpace(PreviewPdfPath))
            return;

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow == null)
            {
                return;
            }

            var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Report (PDF)",
                DefaultExtension = "pdf",
                SuggestedFileName = BuildSuggestedFileName(),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF Files") { Patterns = new[] { "*.pdf" } }
                }
            });

            var destinationPath = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(destinationPath))
                return;

            File.Copy(PreviewPdfPath, destinationPath, overwrite: true);
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Success", "Report exported successfully.", ToastType.Success));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", ex.Message, ToastType.Error));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewReportInternal))]
    private Task OpenPreviewAsync()
    {
        if (!HasPreview || string.IsNullOrWhiteSpace(PreviewPdfPath))
            return Task.CompletedTask;

        try
        {
            Process.Start(new ProcessStartInfo(PreviewPdfPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", ex.Message, ToastType.Error));
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanPreviewReportInternal))]
    private Task PrintReportAsync()
    {
        if (!HasPreview || string.IsNullOrWhiteSpace(PreviewPdfPath))
            return Task.CompletedTask;

        try
        {
            Process.Start(new ProcessStartInfo(PreviewPdfPath)
            {
                Verb = "print",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(PreviewPdfPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", ex.Message, ToastType.Error));
            }
        }

        return Task.CompletedTask;
    }

    private bool CanGenerateReportInternal()
    {
        if (IsGenerating)
            return false;

        if (IsSubjectTarget)
            return SelectedSubject != null;

        if (IsCaseTarget)
            return SelectedCase != null;

        return false;
    }

    private bool CanPreviewReportInternal() => HasPreview && !IsGenerating;

    private async Task<(byte[] Bytes, string Summary)> BuildReportPayloadAsync()
    {
        if (IsCaseTarget)
        {
            var reportCase = await ResolveCaseAsync(SelectedCase!).ConfigureAwait(false);
            var printableCase = BuildPrintableCase(reportCase);
            var tempPath = Path.Combine(Path.GetTempPath(), $"DentalID_Case_{Guid.NewGuid():N}.pdf");

            try
            {
                await _reportService.GenerateCaseReportAsync(printableCase, tempPath).ConfigureAwait(false);
                var bytes = await File.ReadAllBytesAsync(tempPath).ConfigureAwait(false);
                return (bytes, $"Case report: {printableCase.CaseNumber}");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        var reportSubject = await ResolveSubjectAsync(SelectedSubject!).ConfigureAwait(false);
        var printableSubject = BuildPrintableSubject(reportSubject);

        if (IsDetailedFormat)
        {
            var detailed = await TryBuildDetailedSubjectPayloadAsync(reportSubject, printableSubject).ConfigureAwait(false);
            if (detailed.HasValue)
                return detailed.Value;
        }

        var standardPdf = await _reportService.GenerateSubjectReportAsync(printableSubject).ConfigureAwait(false);
        return (standardPdf, $"Subject profile: {printableSubject.FullName}");
    }

    private async Task<(byte[] Bytes, string Summary)?> TryBuildDetailedSubjectPayloadAsync(
        Subject originalSubject,
        Subject printableSubject)
    {
        var candidateImages = (originalSubject.DentalImages ?? new List<DentalImage>())
            .OrderByDescending(i => i.UploadedAt)
            .ToList();

        foreach (var image in candidateImages)
        {
            if (!TryExtractAnalysis(image, out var analysis))
                continue;
            if (string.IsNullOrWhiteSpace(image.ImagePath) || !File.Exists(image.ImagePath))
                continue;

            var printableAnalysis = BuildPrintableAnalysis(analysis);
            var pdf = await _reportService
                .GenerateLabReportAsync(printableAnalysis, printableSubject, image.ImagePath)
                .ConfigureAwait(false);
            return (pdf, $"Detailed evidence report: {printableSubject.FullName}");
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            HintMessage = "No valid analyzed image found for detailed report. Generated standard profile instead.";
        });
        WeakReferenceMessenger.Default.Send(new ShowToastMessage(
            "Info",
            "Detailed report requires at least one analyzed image with stored results. Falling back to standard format.",
            ToastType.Warning));

        return null;
    }

    private AnalysisResult BuildPrintableAnalysis(AnalysisResult source)
    {
        var clone = CloneAnalysis(source);

        if (!CanConfigureOdontogram || !IncludeOdontogram)
        {
            clone.Teeth.Clear();
            clone.RawTeeth.Clear();
        }

        if (!CanConfigureDetections || !IncludeDetections)
        {
            clone.Pathologies.Clear();
            clone.RawPathologies.Clear();
        }

        if (!CanConfigureFingerprint || !IncludeFingerprint)
        {
            clone.Fingerprint = null;
            clone.FeatureVector = null;
        }

        return clone;
    }

    private Subject BuildPrintableSubject(Subject source)
    {
        var clonedImages = (source.DentalImages ?? new List<DentalImage>())
            .OrderByDescending(i => i.UploadedAt)
            .Select(i => new DentalImage
            {
                Id = i.Id,
                SubjectId = i.SubjectId,
                ImagePath = i.ImagePath,
                FileHash = i.FileHash,
                ImageType = i.ImageType,
                JawType = i.JawType,
                Quadrant = i.Quadrant,
                CaptureDate = i.CaptureDate,
                QualityScore = i.QualityScore,
                AnalysisResults = i.AnalysisResults,
                UploadedAt = i.UploadedAt,
                UniquenessScore = i.UniquenessScore,
                IsProcessed = i.IsProcessed,
                DigitalSeal = i.DigitalSeal,
                FingerprintCode = IncludeFingerprint ? i.FingerprintCode : null,
                FeatureVectorBlob = IncludeFingerprint ? i.FeatureVectorBlob : null
            })
            .ToList();

        return new Subject
        {
            Id = source.Id,
            SubjectId = IncludeProfile ? source.SubjectId : "REDACTED",
            FullName = IncludeProfile ? source.FullName : "Redacted Subject",
            Gender = IncludeProfile ? source.Gender : "Unknown",
            DateOfBirth = IncludeProfile ? source.DateOfBirth : null,
            NationalId = IncludeProfile ? source.NationalId : null,
            ContactInfo = IncludeProfile ? source.ContactInfo : null,
            Notes = IncludeProfile ? source.Notes : null,
            DentalImages = clonedImages
        };
    }

    private Case BuildPrintableCase(Case source)
    {
        var clonedMatches = (source.Matches ?? new List<Match>())
            .Select(m => new Match
            {
                Id = m.Id,
                CaseId = m.CaseId,
                QueryImageId = m.QueryImageId,
                MatchedSubjectId = m.MatchedSubjectId,
                MatchedImageId = m.MatchedImageId,
                ConfidenceScore = m.ConfidenceScore,
                MatchMethod = m.MatchMethod,
                ResultType = m.ResultType,
                AlgorithmVersion = m.AlgorithmVersion,
                FeatureSimilarity = m.FeatureSimilarity,
                IsConfirmed = m.IsConfirmed,
                ConfirmedById = m.ConfirmedById,
                ConfirmedAt = m.ConfirmedAt,
                Notes = m.Notes,
                MatchedSubject = m.MatchedSubject
            })
            .ToList();

        return new Case
        {
            Id = source.Id,
            CaseNumber = source.CaseNumber,
            Title = source.Title,
            Description = source.Description,
            CaseType = source.CaseType,
            Status = source.Status,
            Priority = source.Priority,
            AssignedToId = source.AssignedToId,
            ReportedBy = IncludeProfile ? source.ReportedBy : null,
            IncidentDate = source.IncidentDate,
            Location = IncludeProfile ? source.Location : null,
            EvidenceCount = source.EvidenceCount,
            Result = source.Result,
            ClosedAt = source.ClosedAt,
            CreatedById = source.CreatedById,
            Matches = IncludeMatchHistory ? clonedMatches : new List<Match>()
        };
    }

    private async Task<Subject> ResolveSubjectAsync(Subject selected)
    {
        if (selected.Id <= 0)
            return selected;
        return await _subjectRepository.GetByIdAsync(selected.Id).ConfigureAwait(false) ?? selected;
    }

    private async Task<Case> ResolveCaseAsync(Case selected)
    {
        if (selected.Id <= 0)
            return selected;
        return await _caseRepository.GetByIdAsync(selected.Id).ConfigureAwait(false) ?? selected;
    }

    private static bool TryExtractAnalysis(DentalImage image, out AnalysisResult analysis)
    {
        analysis = null!;
        try
        {
            var parsed = image.ParsedAnalysisResults;
            if (parsed == null)
                return false;
            analysis = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AnalysisResult CloneAnalysis(AnalysisResult source)
    {
        return new AnalysisResult
        {
            Teeth = (source.Teeth ?? new List<DetectedTooth>()).Select(t => new DetectedTooth
            {
                FdiNumber = t.FdiNumber,
                Confidence = t.Confidence,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height,
                MaskWidth = t.MaskWidth,
                MaskHeight = t.MaskHeight,
                MaskArea = t.MaskArea,
                Outline = t.Outline?.Select(p => (p.X, p.Y)).ToList()
            }).ToList(),
            Pathologies = (source.Pathologies ?? new List<DetectedPathology>()).Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                MaskWidth = p.MaskWidth,
                MaskHeight = p.MaskHeight,
                MaskArea = p.MaskArea,
                Outline = p.Outline?.Select(o => (o.X, o.Y)).ToList()
            }).ToList(),
            RawTeeth = (source.RawTeeth ?? new List<DetectedTooth>()).Select(t => new DetectedTooth
            {
                FdiNumber = t.FdiNumber,
                Confidence = t.Confidence,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height,
                MaskWidth = t.MaskWidth,
                MaskHeight = t.MaskHeight,
                MaskArea = t.MaskArea,
                Outline = t.Outline?.Select(p => (p.X, p.Y)).ToList()
            }).ToList(),
            RawPathologies = (source.RawPathologies ?? new List<DetectedPathology>()).Select(p => new DetectedPathology
            {
                ClassName = p.ClassName,
                Confidence = p.Confidence,
                ToothNumber = p.ToothNumber,
                X = p.X,
                Y = p.Y,
                Width = p.Width,
                Height = p.Height,
                MaskWidth = p.MaskWidth,
                MaskHeight = p.MaskHeight,
                MaskArea = p.MaskArea,
                Outline = p.Outline?.Select(o => (o.X, o.Y)).ToList()
            }).ToList(),
            EstimatedAge = source.EstimatedAge,
            EstimatedAgeRange = source.EstimatedAgeRange,
            EstimatedGender = source.EstimatedGender,
            FeatureVector = source.FeatureVector?.ToArray(),
            ProcessingTimeMs = source.ProcessingTimeMs,
            Error = source.Error,
            Flags = source.Flags?.ToList() ?? new List<string>(),
            SmartInsights = source.SmartInsights?.ToList() ?? new List<string>(),
            Fingerprint = source.Fingerprint == null ? null : new DentalFingerprint
            {
                Code = source.Fingerprint.Code,
                UniquenessScore = source.Fingerprint.UniquenessScore,
                ToothMap = source.Fingerprint.ToothMap?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<int, string>(),
                Features = source.Fingerprint.Features?.ToList() ?? new List<string>(),
                FeatureVector = source.Fingerprint.FeatureVector?.ToArray()
            }
        };
    }

    private void RefreshState()
    {
        if (IsSubjectTarget && SelectedSubject == null)
        {
            ValidationMessage = "Select a subject to generate a report.";
        }
        else if (IsCaseTarget && SelectedCase == null)
        {
            ValidationMessage = "Select a case to generate a report.";
        }
        else
        {
            ValidationMessage = string.Empty;
        }

        if (IsDetailedFormat && IsSubjectTarget)
        {
            HintMessage = "Detailed format uses the latest stored analysis for the selected subject.";
        }
        else if (IsCaseTarget)
        {
            HintMessage = "Case format generates a chain-of-custody style case summary PDF.";
        }
        else
        {
            HintMessage = "Standard format generates a concise subject profile PDF.";
        }

        OnPropertyChanged(nameof(CanGenerateReport));
        OnPropertyChanged(nameof(CanPreviewReport));
    }

    private string BuildSuggestedFileName()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        if (IsCaseTarget && SelectedCase != null)
        {
            var casePart = SanitizeFilePart(SelectedCase.CaseNumber);
            return $"DentalID_Case_{casePart}_{stamp}.pdf";
        }

        if (SelectedSubject != null)
        {
            var subjectPart = SanitizeFilePart(SelectedSubject.SubjectId);
            return $"DentalID_Subject_{subjectPart}_{stamp}.pdf";
        }

        return $"DentalID_Report_{stamp}.pdf";
    }

    private void ReplacePreviewPath(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
            return;

        CleanupOwnedPreviewFile();
        _ownedPreviewPdfPath = newPath;
        PreviewPdfPath = newPath;
    }

    private void CleanupOwnedPreviewFile()
    {
        if (string.IsNullOrWhiteSpace(_ownedPreviewPdfPath))
            return;

        TryDeleteFile(_ownedPreviewPdfPath);
        _ownedPreviewPdfPath = null;
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for temp files.
        }
    }

    private static bool ContainsIgnoreCase(string? source, string value)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";

        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F1} {units[unitIndex]}";
    }

    public void Dispose()
    {
        CleanupOwnedPreviewFile();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
