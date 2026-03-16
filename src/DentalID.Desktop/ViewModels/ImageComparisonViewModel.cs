using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Core.DTOs;
using DentalID.Application.Interfaces;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Services;
using DentalID.Desktop.Messages;

namespace DentalID.Desktop.ViewModels;

public partial class ImageComparisonViewModel : ViewModelBase
{
    private readonly IFileService? _fileService;
    private readonly IAiPipelineService? _aiService;
    private readonly IComparisonService? _comparisonService;

    public ImageComparisonViewModel()
    {
        Title = "Image Comparison";
    }

    public ImageComparisonViewModel(
        IFileService fileService, 
        IAiPipelineService aiService,
        IComparisonService comparisonService) : this()
    {
        _fileService = fileService;
        _aiService = aiService;
        _comparisonService = comparisonService;
    }

    [ObservableProperty]
    private string? _image1Path;

    [ObservableProperty]
    private string? _image2Path;

    [ObservableProperty]
    private string? _image1Info;

    [ObservableProperty]
    private string? _image2Info;

    [ObservableProperty]
    private bool _isOverlayMode;

    [ObservableProperty]
    private double _overlayOpacity = 0.5;

    // AI Analysis Results
    [ObservableProperty] private AnalysisResult? _image1Analysis;
    [ObservableProperty] private AnalysisResult? _image2Analysis;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private bool _showDetections = true;

    // Comparison Results
    [ObservableProperty] private ComparisonResult? _comparisonResults;
    [ObservableProperty] private double _similarityScore;
    [ObservableProperty] private ObservableCollection<string> _differenceHighlights = new();
    [ObservableProperty] private bool _syncZoom = true;

    [RelayCommand]
    private async Task LoadImage1()
    {
        var path = await BrowseForImageAsync("Select Evidence Image");
        if (path != null)
        {
            Image1Path = path;
            Image1Info = GetFileInfo(path);
            Image1Analysis = null;
            ResetComparison();
        }
    }

    [RelayCommand]
    private async Task LoadImage2()
    {
        var path = await BrowseForImageAsync("Select Record Image");
        if (path != null)
        {
            Image2Path = path;
            Image2Info = GetFileInfo(path);
            Image2Analysis = null;
            ResetComparison();
        }
    }

    [RelayCommand]
    private void SwapImages()
    {
        // Swap paths
        var tempPath = Image1Path;
        Image1Path = Image2Path;
        Image2Path = tempPath;

        // Swap Infos
        var tempInfo = Image1Info;
        Image1Info = Image2Info;
        Image2Info = tempInfo;

        // Swap Analysis
        var tempAnalysis = Image1Analysis;
        Image1Analysis = Image2Analysis;
        Image2Analysis = tempAnalysis;

        // Optionally rerun comparison if we want
        if (Image1Analysis != null && Image2Analysis != null)
        {
            RunComparisonCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task RunComparisonAsync()
    {
        if (string.IsNullOrEmpty(Image1Path) || string.IsNullOrEmpty(Image2Path))
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Warning", "Please select two images to compare.", ToastType.Warning));
            return;
        }

        if (_aiService == null || _comparisonService == null) return;

        IsAnalyzing = true;
        ResetComparison();

        try
        {
            if (Image1Analysis == null)
            {
                using var stream1 = File.OpenRead(Image1Path);
                Image1Analysis = await _aiService.AnalyzeImageAsync(stream1, Path.GetFileName(Image1Path));
            }

            if (Image2Analysis == null)
            {
                using var stream2 = File.OpenRead(Image2Path);
                Image2Analysis = await _aiService.AnalyzeImageAsync(stream2, Path.GetFileName(Image2Path));
            }

            if (Image1Analysis.IsSuccess && Image2Analysis.IsSuccess)
            {
                ComparisonResults = _comparisonService.CompareAnalyses(Image1Analysis, Image2Analysis);
                SimilarityScore = ComparisonResults.SimilarityScore;
                
                DifferenceHighlights.Clear();
                foreach (var diff in ComparisonResults.Differences)
                {
                    DifferenceHighlights.Add(diff);
                }

                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Success", "Comparison completed.", ToastType.Success));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", "Failed to analyze one or both images.", ToastType.Error));
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", $"Comparison failed: {ex.Message}", ToastType.Error));
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private void ToggleDetections()
    {
        ShowDetections = !ShowDetections;
    }

    [RelayCommand]
    private void ResetView()
    {
        Image1Path = null;
        Image2Path = null;
        Image1Info = null;
        Image2Info = null;
        IsOverlayMode = false;
        OverlayOpacity = 0.5;
        Image1Analysis = null;
        Image2Analysis = null;
        ResetComparison();
    }

    private void ResetComparison()
    {
        ComparisonResults = null;
        SimilarityScore = 0;
        DifferenceHighlights.Clear();
    }

    private string GetFileInfo(string path)
    {
        if (_fileService == null || !_fileService.Exists(path))
            return "Unknown";

        try
        {
            var fileInfo = new FileInfo(path);
            var sizeMb = fileInfo.Length / 1024.0 / 1024.0;
            return $"{fileInfo.Extension.ToUpper()} • {sizeMb:F2} MB";
        }
        catch
        {
            return "Unknown";
        }
    }

    private async Task<string?> BrowseForImageAsync(string title)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window == null) return null;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.dcm" } } }
            });

            if (files.Count > 0)
            {
                return files[0].TryGetLocalPath();
            }
        }
        return null;
    }
}
