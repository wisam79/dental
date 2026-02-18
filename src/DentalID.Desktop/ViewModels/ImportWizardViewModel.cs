using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Core.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Desktop.Services;
using DentalID.Desktop.Messages;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;

namespace DentalID.Desktop.ViewModels;

public partial class ImportWizardViewModel : ViewModelBase
{
    private readonly IDataImportService _importService;

    [ObservableProperty] private int _currentStep = 1;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string? _selectedFilePath;
    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _importProgress;

    [ObservableProperty]
    private string _importStatus = string.Empty;
    [ObservableProperty] private string? _importLog;
    
    // Step 2: Preview & Mapping
    [ObservableProperty] private ObservableCollection<Dictionary<string, string>> _previewData = new();
    [ObservableProperty] private ObservableCollection<ColumnMappingViewModel> _mappings = new();
    
    private List<Dictionary<string, string>> _allRecords = new();

    public ImportWizardViewModel(IDataImportService importService)
    {
        _importService = importService;
        StatusMessage = "Select a CSV file to begin.";
    }

    // Default Constructor for Designer
    public ImportWizardViewModel() 
    {
        _importService = null!;
    }

    [RelayCommand]
    private async Task SelectFile()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == null) return;

            var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open CSV File",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } }
            });

            if (files.Any())
            {
                SelectedFilePath = files[0].Path.LocalPath;
                await ParseFile();
            }
        }
    }

    private async Task ParseFile()
    {
        await SafeExecuteAsync(async () =>
        {
            if (string.IsNullOrEmpty(SelectedFilePath))
            {
                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Selection Error", "No file selected.", ToastType.Warning));
                return;
            }

            using var stream = File.OpenRead(SelectedFilePath);
            _allRecords = await _importService.ParseCsvAsync(stream);
            
            if (_allRecords.Any())
            {
                // Generate Preview (First 5 rows)
                PreviewData = new ObservableCollection<Dictionary<string, string>>(_allRecords.Take(5));
                
                // Generate Mappings
                var csvHeaders = _allRecords.First().Keys.ToList();
                Mappings = new ObservableCollection<ColumnMappingViewModel>
                {
                    new("Full Name", "FullName", csvHeaders, true), // Required
                    new("Gender (M/F)", "Gender", csvHeaders),
                    new("Date of Birth", "DateOfBirth", csvHeaders),
                    new("National ID", "NationalId", csvHeaders),
                    new("Contact Info", "ContactInfo", csvHeaders),
                    new("Notes", "Notes", csvHeaders)
                };

                // Auto-Map (Smart Guessing)
                foreach(var map in Mappings) map.AutoMap();

                CurrentStep = 2; // Move to mapping step
                // Success message handled by SafeExecuteAsync or manually here if we want a status update without a toast
                StatusMessage = $"Loaded {_allRecords.Count} records. Please map columns.";
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Empty File", "The selected file is empty.", ToastType.Warning));
            }
        });
    }

    [RelayCommand]
    private async Task ExecuteImport()
    {
        if (Mappings == null || !_allRecords.Any()) return;

        ImportProgress = 0;
        ImportStatus = "Preparing to import...";

        await SafeExecuteAsync(async () =>
        {
            // Run mapping and import on background thread to prevent UI freeze
            var result = await Task.Run(async () => 
            {
                var subjects = new List<Subject>();
                int totalRecords = _allRecords.Count;
                
                // This loop can be slow for 10k+ records
                for (int i = 0; i < _allRecords.Count; i++)
                {
                    var row = _allRecords[i];
                    var s = new Subject();
                    bool isValid = true;

                    foreach (var map in Mappings.Where(m => !string.IsNullOrEmpty(m.SelectedSourceColumn)))
                    {
                        if (row.TryGetValue(map.SelectedSourceColumn, out var val))
                        {
                            if (map.TargetProperty == "FullName")
                            {
                                if (string.IsNullOrWhiteSpace(val)) isValid = false;
                                s.FullName = val;
                            }
                            else if (map.TargetProperty == "Gender")
                            {
                                s.Gender = val.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "Male" : 
                                               val.StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "Female" : "Unknown";
                            }
                            else if (map.TargetProperty == "DateOfBirth")
                            {
                                if (DateTime.TryParse(val, out var dob)) s.DateOfBirth = dob;
                            }
                            else if (map.TargetProperty == "ContactInfo") s.ContactInfo = val;
                            else if (map.TargetProperty == "NationalId") s.NationalId = val;
                            else if (map.TargetProperty == "Notes") s.Notes = val;
                        }
                    }

                    if (isValid) subjects.Add(s);

                    // Update progress
                    ImportProgress = (int)((i + 1) / (double)totalRecords * 80);
                    ImportStatus = $"Processing record {i + 1} of {totalRecords}...";
                }

                ImportStatus = "Importing to database...";
                ImportProgress = 85;

                return await _importService.ImportSubjectsAsync(subjects);
            });
            
            ImportProgress = 100;
            ImportStatus = "Import completed";

            ImportLog = $"Import Completed.\nSuccess: {result.SuccessCount}\nErrors: {result.ErrorCount}\n\nErrors:\n{string.Join("\n", result.Errors)}";
            CurrentStep = 3;
            // Additional specific success message
        }, successMessage: "Data Import Completed Successfully");
    }
    
     [RelayCommand]
     private void Reset()
     {
         CurrentStep = 1;
         SelectedFilePath = null;
         Mappings = new ObservableCollection<ColumnMappingViewModel>();
         PreviewData = new ObservableCollection<Dictionary<string, string>>();
         StatusMessage = "Select a CSV file to begin.";
         ImportLog = string.Empty;
     }
}

public partial class ColumnMappingViewModel : ObservableObject
{
    public string DisplayName { get; }
    public string TargetProperty { get; }
    public List<string> SourceColumns { get; }
    public bool IsRequired { get; }

    [ObservableProperty] private string _selectedSourceColumn = string.Empty;

    public ColumnMappingViewModel(string display, string target, List<string> sources, bool required = false)
    {
        DisplayName = display;
        TargetProperty = target;
        SourceColumns = new List<string> { "" }; // Empty option
        SourceColumns.AddRange(sources);
        IsRequired = required;
    }

    public void AutoMap()
    {
        // Simple fuzzy match
        var match = SourceColumns.FirstOrDefault(c => 
            c.Replace("_", "").Replace(" ", "").Equals(TargetProperty, StringComparison.OrdinalIgnoreCase) ||
            (TargetProperty == "FullName" && (c.ToLower().Contains("name") || c.ToLower().Contains("patient"))) ||
            (TargetProperty == "DateOfBirth" && (c.ToLower().Contains("dob") || c.ToLower().Contains("birth")))
        );

        if (match != null) SelectedSourceColumn = match;
    }
}
