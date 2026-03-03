using System;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Desktop.Services;
using DentalID.Application.Interfaces;
using DentalID.Desktop.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace DentalID.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private int _selectedThemeIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    private readonly IThemeService _themeService;
    private readonly IBulkOperationsService _bulkService;
    private readonly ISettingsService _settingsService;

    // Design-time constructor
    public SettingsViewModel() 
    {
         _themeService = null!; // Designer only
         _bulkService = null!;
         _settingsService = null!;
    }

    public SettingsViewModel(IThemeService themeService, IBulkOperationsService bulkService, ISettingsService settingsService)
    {
        _themeService = themeService;
        _bulkService = bulkService;
        _settingsService = settingsService;
        Title = "Settings";

        // Set initial indices from current state
        _selectedThemeIndex = _themeService.CurrentThemeName switch
        {
            "Light" => 1,
            "HighContrast" => 2,
            _ => 0
        };

        _selectedLanguageIndex = Loc.Instance.CurrentLanguage == "ar" ? 1 : 0;
    }
    
    // ... commands ...

    [RelayCommand]
    private async Task ExportSubjects()
    {
        await SafeExecuteAsync(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export Subjects",
                    DefaultExtension = "csv",
                    SuggestedFileName = $"Subjects_Export_{DateTime.UtcNow:yyyyMMdd}.csv",
                    FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } }
                });

                if (file != null)
                {
                    var path = file.TryGetLocalPath();
                    if (path != null)
                    {
                        var result = await _bulkService.ExportSubjectsToCsvAsync(path);
                        if (result.Success)
                        {
                            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Export Successful", $"Exported {result.RecordsExported} subjects.", ToastType.Success));
                        }
                        else
                        {
                            throw new System.Exception(string.Join(", ", result.Errors));
                        }
                    }
                }
            }
        });
    }

    [RelayCommand]
    private async Task ExportCases()
    {
         await SafeExecuteAsync(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export Cases",
                    DefaultExtension = "csv",
                    SuggestedFileName = $"Cases_Export_{DateTime.UtcNow:yyyyMMdd}.csv",
                    FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } }
                });

                if (file != null)
                {
                    var path = file.TryGetLocalPath();
                    if (path != null)
                    {
                        var result = await _bulkService.ExportCasesToCsvAsync(path);
                        if (result.Success)
                        {
                            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Export Successful", $"Exported {result.RecordsExported} cases.", ToastType.Success));
                        }
                        else
                        {
                            throw new System.Exception(string.Join(", ", result.Errors));
                        }
                    }
                }
            }
        });
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        var themeName = value switch
        {
            1 => "Light",
            2 => "HighContrast",
            _ => "Dark"
        };
        _themeService?.ApplyTheme(themeName);
        
        // Save settings via service
        if (_settingsService != null)
        {
            _settingsService.Theme = themeName;
            _settingsService.Save();
        }
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var lang = value == 1 ? "ar" : "en";
        Loc.Instance.SwitchLanguage(lang);
        
        // Save settings via service
        if (_settingsService != null)
        {
            _settingsService.Language = lang;
            _settingsService.Save();
        }
    }
    public void Dispose() { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(this); }}



