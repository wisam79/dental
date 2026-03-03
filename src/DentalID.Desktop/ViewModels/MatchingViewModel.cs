using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DentalID.Core.DTOs;
using System.IO;
using DentalID.Application.Configuration;

namespace DentalID.Desktop.ViewModels;

public partial class MatchingViewModel : ViewModelBase
{
    private readonly ISubjectRepository _subjectRepo;
    private readonly IMatchRepository _matchRepo;
    private readonly IDentalImageRepository _imageRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly IAiPipelineService _pipeline;
    private readonly IMatchingService _matchingService;
    private readonly IReportService _reportService;

    [ObservableProperty]
    private string? _queryImagePath;

    [ObservableProperty]
    private bool _isQueryLoaded;

    [ObservableProperty]
    private bool _isMatching;

    [ObservableProperty]
    private int _matchingProgress;

    [ObservableProperty]
    private ObservableCollection<MatchCandidate> _candidates = new();

    [ObservableProperty]
    private MatchCandidate? _selectedCandidate;
    
    [ObservableProperty]
    private float[]? _queryVector;

    [ObservableProperty]
    private ObservableCollection<Case> _activeCases = new();

    [ObservableProperty]
    private Case? _selectedCase;

    private const int CaseLoadBatchSize = 100;
    // MatchThreshold moved to AiConfiguration
    private const int MatchingBatchSize = 1000; // Process 1000 subjects at a time
    private const string QuerySubjectCode = "QRY-SYSTEM";

    [ObservableProperty]
    private double _overlayOpacity = 0.5;

    [ObservableProperty]
    private bool _isOverlayMode = true; // True = Overlay, False = Side-by-Side

    [ObservableProperty]
    private string? _targetImagePath; // The image of the selected candidate

    private int _currentQueryImageId; // Tracks the database ID of the currently loaded query image

    partial void OnSelectedCandidateChanged(MatchCandidate? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.Subject.ThumbnailPath))
        {
            // In a real app, this would be the high-res X-Ray path, not thumbnail
            TargetImagePath = value.Subject.ThumbnailPath;
        }
        else
        {
            TargetImagePath = null;
        }
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    // Design-time constructor — only used by XAML previewer.
    // All fields remain default (null) but this is safe because
    // none of the commands will execute in design mode.
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public MatchingViewModel()
    {
        Title = "Matching Engine (Design)";
        StatusMessage = "Load an image to find matches";
        _subjectRepo = null!;
        _matchRepo = null!;
        _imageRepo = null!;
        _caseRepo = null!;
        _pipeline = null!;
        _matchingService = null!;
        _reportService = null!;
        _fileService = null!;
        _logger = null!;
        _aiConfig = null!;
    }

    private readonly IFileService _fileService;
    private readonly ILoggerService _logger;
    private readonly AiConfiguration _aiConfig;

    public MatchingViewModel(
        ISubjectRepository subjectRepo,
        IMatchRepository matchRepo,
        IDentalImageRepository imageRepo,
        ICaseRepository caseRepo,
        IAiPipelineService pipeline,
        IMatchingService matchingService,
        IReportService reportService,
        IFileService fileService,
        ILoggerService logger,
        AiConfiguration aiConfig)
    {
        Title = "Matching Engine";
        StatusMessage = "Load an image to find matches";
        _subjectRepo = subjectRepo;
        _matchRepo = matchRepo;
        _imageRepo = imageRepo;
        _caseRepo = caseRepo;
        _pipeline = pipeline;
        _matchingService = matchingService;
        _reportService = reportService;
        _fileService = fileService;
        _logger = logger;
        _aiConfig = aiConfig;

        // Bug #22 Fix: Show initialization failure in the UI StatusMessage instead of silently logging.
        // The original code logged errors but the user had no indication the engine failed to start.
        _ = InitializeAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger?.LogError(t.Exception?.InnerException ?? t.Exception!, "MatchingViewModel initialization failed");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    StatusMessage = $"⚠️ Engine Init Failed: {t.Exception?.InnerException?.Message ?? t.Exception?.Message}");
            }
        }, TaskScheduler.Default);
    }
    
    [RelayCommand]
    private async Task BrowseQueryImage()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Query Image",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.dcm" } } }
            });

            if (files.Count > 0)
            {
                var path = files[0].TryGetLocalPath();
                if (path != null)
                {
                    await LoadQueryImage(path);
                }
            }
        }
    }

    public async Task LoadQueryImage(string path)
    {
        QueryImagePath = path;
        IsQueryLoaded = true;
        Candidates.Clear();
        StatusMessage = "Query image loaded. Starting automatic matching...";
        
        // Auto-run matching
        if (RunMatchingCommand.CanExecute(null))
        {
            await RunMatchingCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task RunMatching()
    {
        if (!IsQueryLoaded || QueryImagePath == null) return;

        Candidates.Clear();
        MatchingProgress = 0;
        IsMatching = true;
        try
        {
            await SafeExecuteAsync(async () =>
            {
                // 1. Extract Features from Query Image
                // Uses Stream to ensure file lock safety
                using var stream = _fileService.OpenRead(QueryImagePath);
                StatusMessage = "Extracting features from query image...";
                var (vector, error) = await _pipeline.ExtractFeaturesAsync(stream);
                MatchingProgress = 20;
                
                if (vector == null)
                {
                    throw new Exception($"Feature Extraction Failed: {error ?? "Unknown Error"}");
                }

                QueryVector = vector;

                // 2. Run Matching Service
                StatusMessage = "Matching against subject database...";
                
                // Create probe fingerprint
                var probe = new DentalFingerprint 
                { 
                   Code = "PROBE", 
                   FeatureVector = vector 
                };

                var matches = new List<MatchCandidate>();
                var batch = new List<Subject>(MatchingBatchSize);
                
                await foreach (var subject in _subjectRepo.StreamAllWithVectorsAsync())
                {
                    batch.Add(subject);
                    if (batch.Count >= MatchingBatchSize)
                    {
                        var batchMatches = await Task.Run(() => _matchingService.FindMatches(probe, batch));
                        matches.AddRange(batchMatches.Where(m => m.Score >= _aiConfig.Thresholds.MatchSimilarityThreshold));
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                {
                    var batchMatches = await Task.Run(() => _matchingService.FindMatches(probe, batch));
                    matches.AddRange(batchMatches.Where(m => m.Score >= _aiConfig.Thresholds.MatchSimilarityThreshold));
                }

                MatchingProgress = 80;

                // 3. Persist Query Image & Match Results
                StatusMessage = "Saving results...";
                _currentQueryImageId = await SaveQueryImageAsync(QueryImagePath);

                // 4. Update UI & Persist Candidates
                var topMatches = matches.OrderByDescending(m => m.Score).Take(20).ToList();
                
                foreach (var matchCandidate in topMatches)
                {
                    // Create Match Record
                    var matchRecord = new Match
                    {
                        CaseId = SelectedCase?.Id,
                        QueryImageId = _currentQueryImageId,
                        MatchedSubjectId = matchCandidate.Subject.Id,
                        ConfidenceScore = matchCandidate.Score,
                        MatchMethod = "CosineSimilarity",
                        FeatureSimilarity = matchCandidate.Score,
                        AlgorithmVersion = "v1.0",
                        IsConfirmed = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    var savedMatch = await _matchRepo.AddAsync(matchRecord);
                    matchCandidate.MatchId = savedMatch.Id; // Link back to DTO
                    
                    Candidates.Add(matchCandidate);
                }

                MatchingProgress = 100;
                StatusMessage = $"✅ Found {topMatches.Count} matches. Query saved (ID: {_currentQueryImageId}).";
            }, successMessage: "Matching scan complete");
        }
        finally
        {
            IsMatching = false;
        }
    }

    private async Task<int> SaveQueryImageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Query image path cannot be null or empty.", nameof(path));
        if (!_fileService.Exists(path))
            throw new FileNotFoundException("Query image not found.", path);

        // Keep all probe images under one dedicated system subject to avoid polluting subject records.
        var savedSubject = await GetOrCreateQuerySubjectAsync();
        var managedPath = CopyQueryImageToManagedStorage(path);

        try
        {
            var image = new DentalImage
            {
                SubjectId = savedSubject.Id,
                ImagePath = managedPath,
                UploadedAt = DateTime.UtcNow,
                ImageType = DentalID.Core.Enums.ImageType.Other
            };
            var saved = await _imageRepo.AddAsync(image);
            return saved.Id;
        }
        catch
        {
            try 
            { 
                _fileService.Delete(managedPath); 
            } 
            catch (Exception ex)
            { 
                _logger.LogWarning($"Failed to cleanup managed path after save failure. {ex.Message}");
            }
            throw;
        }
    }

    private string CopyQueryImageToManagedStorage(string originalPath)
    {
        string queryStoreDir = Path.Combine(AppContext.BaseDirectory, "data", "query");
        Directory.CreateDirectory(queryStoreDir);

        string extension = Path.GetExtension(originalPath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".img";

        string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
        string destinationPath = Path.Combine(queryStoreDir, fileName);
        _fileService.Copy(originalPath, destinationPath, overwrite: false);
        return destinationPath;
    }

    private async Task<Subject> GetOrCreateQuerySubjectAsync()
    {
        var existing = await _subjectRepo.GetBySubjectIdAsync(QuerySubjectCode);
        if (existing != null)
            return existing;

        try
        {
            var subject = new Subject
            {
                SubjectId = QuerySubjectCode,
                FullName = "Query Probe (System)",
                Gender = "Unknown",
                Notes = "System-managed container for matching query images."
            };
            return await _subjectRepo.AddAsync(subject);
        }
        catch (Exception ex)
        {
            // Handle concurrent creation in multi-window/session scenarios.
            var createdByAnother = await _subjectRepo.GetBySubjectIdAsync(QuerySubjectCode);
            if (createdByAnother != null)
                return createdByAnother;
                
            _logger.LogError(ex, "Failed to create query subject and fallback also failed.");
            throw;
        }
    }

    [RelayCommand]
    private async Task ConfirmMatch()
    {
        if (SelectedCandidate == null) return;

        await SafeExecuteAsync(async () => 
        {
            // 1. Find the Match record using the candidate's MatchId if available, or fallback to query
            Match? match = null;
            
            if (SelectedCandidate.MatchId.HasValue)
            {
                match = await _matchRepo.GetByIdAsync(SelectedCandidate.MatchId.Value);
            }
            
            if (match == null)
            {
                // Bug #12 fix: check if an unconfirmed match already exists before creating ad-hoc
                var existingMatches = await _matchRepo.GetBySubjectIdAsync(SelectedCandidate.Subject.Id);
                match = existingMatches.FirstOrDefault(m => m.QueryImageId == _currentQueryImageId && !m.IsConfirmed);
            }
        
            if (match == null)
            {
                 // Create ad-hoc match record if missing
                 match = new Match 
                 {
                    QueryImageId = _currentQueryImageId,
                    MatchedSubjectId = SelectedCandidate.Subject.Id,
                    ConfidenceScore = SelectedCandidate.Score,
                    MatchMethod = "Manual/AdHoc",
                    IsConfirmed = false
                 };
                 match = await _matchRepo.AddAsync(match);
            }

            // 2. Update confirmation fields
            if (SelectedCase != null)
            {
                match.CaseId = SelectedCase.Id;
            }
            match.IsConfirmed = true;
            match.ConfirmedAt = DateTime.UtcNow;
            // match.ConfirmedById = null; // No Auth System in Desktop yet

            var caseNote = SelectedCase != null ? $" | Case: {SelectedCase.CaseNumber}" : string.Empty;
            match.Notes = $"Confirmed by user at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}{caseNote}";

            await _matchRepo.UpdateAsync(match);

            // 3. Audit log
            _logger.LogAudit(
                action: "MATCH_CONFIRMED",
                user: Environment.UserName,
                details: $"Subject: {SelectedCandidate.Subject.FullName}, Score: {SelectedCandidate.Score:F4}",
                hash: match.Id.ToString()
            );

            StatusMessage = SelectedCase == null
                ? $"✅ ID CONFIRMED: {SelectedCandidate.Subject.FullName}"
                : $"✅ ID CONFIRMED: {SelectedCandidate.Subject.FullName} (linked to {SelectedCase.CaseNumber})";
        }, successMessage: "Match confirmed and logged");
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Verify pipeline readiness
            if (!_pipeline.IsReady)
            {
                StatusMessage = "⚠️ AI Engine not ready. Load models first.";
                return;
            }

            // Pre-load active cases for quick access
            var cases = await _caseRepo.GetAllAsync(1, CaseLoadBatchSize);
            ActiveCases = new ObservableCollection<Case>(cases);
            StatusMessage = $"Matching Engine Ready — {ActiveCases.Count} active case(s) loaded.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Initialization Failed");
            StatusMessage = "Error: Matching Engine failed to initialize.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private async Task ExportReport()
    {
        if (SelectedCandidate == null) return;

        await SafeExecuteAsync(async () =>
        {
            // Create probe subject (temporary)
            var probe = new Subject
            {
                FullName = "Unknown Subject (Probe)",
                SubjectId = "PROBE-" + DateTime.UtcNow.ToString("yyyyMMddHHmm"),
                DentalImages = new List<DentalImage>
                {
                    new DentalImage 
                    { 
                        FingerprintCode = "N/A", // Ideally we parse this from the query analysis
                        UploadedAt = DateTime.UtcNow
                    }
                }
            };

            // Generate PDF
            var pdfBytes = await _reportService.GenerateMatchReportAsync(probe, SelectedCandidate);

            // Save to Reports folder
            string reportsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DentalID", "Reports");
            Directory.CreateDirectory(reportsDir);

            string fileName = $"MatchReport_{SelectedCandidate.Subject.SubjectId}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";
            string fullPath = Path.Combine(reportsDir, fileName);

            await _fileService.WriteAllBytesAsync(fullPath, pdfBytes);
            
            // Open the folder
            _fileService.LaunchFile(fullPath);
        }, successMessage: "Forensic Report exported successfully");
    }

    private bool CanExportReport() => SelectedCandidate != null;
    public void Dispose() { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(this); }}



