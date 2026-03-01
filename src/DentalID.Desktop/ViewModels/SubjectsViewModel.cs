using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Desktop.Messages;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.ViewModels;

public partial class SubjectsViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISubjectRepository _readRepo;
    private readonly ILoggerService _logger;

    [ObservableProperty] private ObservableCollection<Subject> _subjects = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSubject))]
    private Subject? _selectedSubject;
    [ObservableProperty] private ObservableCollection<DentalImage> _selectedSubjectAnalyses = new();
    [ObservableProperty] private bool _isAddEditDialogOpen;
    [ObservableProperty] private bool _isEditing;

    // Form Properties
    [ObservableProperty] [NotifyDataErrorInfo] [Required] private string _formFullName = string.Empty;
    [ObservableProperty] private string _formGender = "Unknown";
    [ObservableProperty] private int _formGenderIndex = 2;
    [ObservableProperty] private DateTimeOffset? _formDateOfBirth;
    [ObservableProperty] private string _formNationalId = string.Empty;
    [ObservableProperty] private string _formContactInfo = string.Empty;
    [ObservableProperty] private string _formNotes = string.Empty;

    // Pagination & Search
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _pageSize = 20;
    [ObservableProperty] private int _totalCount;

    public bool HasNextPage => CurrentPage * PageSize < TotalCount;
    public bool HasPreviousPage => CurrentPage > 1;
    public string PageInfo => $"{CurrentPage} / {Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize))}";
    public bool HasSelectedSubject => SelectedSubject != null;
    public int SelectedSubjectAnalysisCount => SelectedSubjectAnalyses.Count;
    public bool HasSubjectAnalyses => SelectedSubjectAnalysisCount > 0;
    public bool ShowAnalysesEmptyState => HasSelectedSubject && !HasSubjectAnalyses;

    // Partial method hooks for property changes if needed
    partial void OnCurrentPageChanged(int value) => RefreshPagination();
    partial void OnTotalCountChanged(int value) => RefreshPagination();
    partial void OnSelectedSubjectChanged(Subject? value)
    {
        UpdateSelectedSubjectAnalyses(value);
        OnPropertyChanged(nameof(ShowAnalysesEmptyState));
    }
    partial void OnSelectedSubjectAnalysesChanged(ObservableCollection<DentalImage> value)
    {
        OnPropertyChanged(nameof(SelectedSubjectAnalysisCount));
        OnPropertyChanged(nameof(HasSubjectAnalyses));
        OnPropertyChanged(nameof(ShowAnalysesEmptyState));
    }
    partial void OnFormGenderChanged(string value) => FormGenderIndex = ToGenderIndex(value);
    partial void OnFormGenderIndexChanged(int value) => FormGender = value switch
    {
        0 => "Male",
        1 => "Female",
        _ => "Unknown"
    };

    private void RefreshPagination()
    {
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(PageInfo));
    }

    public SubjectsViewModel(ISubjectRepository readRepo, IServiceScopeFactory scopeFactory, ILoggerService logger)
    {
        Title = "Subjects";
        _readRepo = readRepo;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadAsync();
    }

    public async Task FocusSubjectByCodeAsync(string subjectCode)
    {
        var normalized = subjectCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            await LoadAsync();
            return;
        }

        SearchQuery = normalized;
        CurrentPage = 1;

        await SafeExecuteAsync(async () =>
        {
            var exact = await _readRepo.GetBySubjectIdAsync(normalized);
            if (exact != null)
            {
                Subjects = new ObservableCollection<Subject> { exact };
                TotalCount = 1;
                SelectedSubject = exact;
                _logger.LogInformation($"[SUBJECTS] Focused exact subject by SubjectId='{normalized}'");
                RefreshPagination();
                return;
            }

            await LoadAsyncCore();

            if (Subjects.Count == 1 && SelectedSubject == null)
            {
                SelectedSubject = Subjects[0];
            }

            _logger.LogWarning($"[SUBJECTS] Exact SubjectId='{normalized}' not found. Fallback search returned {Subjects.Count} rows.");
        });
    }

    private async Task LoadAsync()
    {
        await SafeExecuteAsync(LoadAsyncCore);
    }

    [RelayCommand]
    private async Task Search()
    {
        await SafeExecuteAsync(async () =>
        {
            CurrentPage = 1;
            await LoadAsyncCore();
        });
    }

    [RelayCommand]
    private async Task NextPage()
    {
        if (HasNextPage)
        {
            CurrentPage++;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPage()
    {
        if (HasPreviousPage)
        {
            CurrentPage--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        SearchQuery = string.Empty;
        CurrentPage = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenAddDialog()
    {
        IsEditing = false;
        ClearForm();
        IsAddEditDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditDialog()
    {
        if (SelectedSubject == null) return;
        IsEditing = true;
        FormFullName = SelectedSubject.FullName;
        FormGender = Subject.NormalizeGenderCode(SelectedSubject.Gender);
        FormDateOfBirth = SelectedSubject.DateOfBirth.HasValue
            ? new DateTimeOffset(SelectedSubject.DateOfBirth.Value, TimeSpan.Zero)
            : null;
        FormNationalId = SelectedSubject.NationalId ?? "";
        FormContactInfo = SelectedSubject.ContactInfo ?? "";
        FormNotes = SelectedSubject.Notes ?? "";
        IsAddEditDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsAddEditDialogOpen = false;
    }

    [RelayCommand]
    private async Task SaveSubject()
    {
        FormFullName = FormFullName?.Trim() ?? string.Empty;
        ValidateProperty(FormFullName, nameof(FormFullName));
        if (HasErrors || string.IsNullOrWhiteSpace(FormFullName))
        {
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Validation Error", "Full name is required", ToastType.Warning));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            // Bug #30 Fix: Capture IsEditing BEFORE any await/async operations.
            // After 'await LoadAsyncCore()' the UI thread may process other events and 
            // IsEditing could be stale or changed. Using a local copy ensures the correct
            // value is used in the Toast notification.
            bool wasEditing = IsEditing;
            var normalizedNationalId = NormalizeOptionalField(FormNationalId);
            var normalizedContactInfo = NormalizeOptionalField(FormContactInfo);
            var normalizedNotes = NormalizeOptionalField(FormNotes);

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();

            // Check for duplicate national ID
            if (!string.IsNullOrWhiteSpace(normalizedNationalId))
            {
                var existingSubject = await repo.GetByNationalIdAsync(normalizedNationalId);
                if (existingSubject != null && (!wasEditing || existingSubject.Id != SelectedSubject?.Id))
                {
                    WeakReferenceMessenger.Default.Send(new ShowToastMessage(
                        "Duplicate National ID", 
                        "A subject with this national ID already exists.", 
                        ToastType.Error));
                    return;
                }
            }

            if (wasEditing)
            {
                if (SelectedSubject == null)
                {
                     WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", "No subject selected for editing.", ToastType.Error));
                     return;
                }

                // Fetch fresh entity to avoid modifying the read-only UI tracking one
                var subjectToUpdate = await repo.GetByIdAsync(SelectedSubject.Id);
                if (subjectToUpdate != null)
                {
                    var normalizedGender = ToGenderCode(FormGenderIndex);
                    subjectToUpdate.FullName = FormFullName;
                    subjectToUpdate.Gender = normalizedGender;
                    subjectToUpdate.DateOfBirth = FormDateOfBirth?.DateTime;
                    subjectToUpdate.NationalId = normalizedNationalId;
                    subjectToUpdate.ContactInfo = normalizedContactInfo;
                    subjectToUpdate.Notes = normalizedNotes;
                    
                    await repo.UpdateAsync(subjectToUpdate);
                    _logger?.LogInformation($"Subject updated: {subjectToUpdate.FullName} ({subjectToUpdate.SubjectId})");
                }
            }
            else
            {
                var normalizedGender = ToGenderCode(FormGenderIndex);
                var subject = new Subject
                {
                    SubjectId = $"SUB-{Guid.NewGuid():N}",
                    FullName = FormFullName,
                    Gender = normalizedGender,
                    DateOfBirth = FormDateOfBirth?.DateTime,
                    NationalId = normalizedNationalId,
                    ContactInfo = normalizedContactInfo,
                    Notes = normalizedNotes,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await repo.AddAsync(subject);
                _logger?.LogInformation($"Subject created: {subject.FullName} ({subject.SubjectId})");
            }

            IsAddEditDialogOpen = false;
            // Reload list to refresh UI with latest state from DB
            try
            {
                await LoadAsyncCore();
            }
            catch (Exception ex)
            {
                // Log error but don't crash - the save was successful
                _logger?.LogError(ex, "Failed to refresh subjects list after save");
                WeakReferenceMessenger.Default.Send(new ShowToastMessage(
                    "Warning", 
                    "Subject saved but list refresh failed. Please refresh manually.", 
                    ToastType.Warning));
            }
            WeakReferenceMessenger.Default.Send(new ShowToastMessage(
                Loc.Instance["Action_Save"], 
                wasEditing ? Loc.Instance["Msg_SubjUpdated"] : Loc.Instance["Msg_SubjCreated"], // Bug #30: use captured wasEditing
                ToastType.Success));
        }, "Failed to save subject");
    }

    [RelayCommand]
    private async Task DeleteSubject()
    {
        if (SelectedSubject == null) return;
        
        await SafeExecuteAsync(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();

            await repo.DeleteAsync(SelectedSubject.Id);
            SelectedSubject = null;
            await LoadAsyncCore();
            WeakReferenceMessenger.Default.Send(new ShowToastMessage(
                Loc.Instance["Action_Delete"], 
                Loc.Instance["Msg_SubjDeleted"], 
                ToastType.Success));
        });
    }

     private async Task LoadAsyncCore()
     {
         var selectedId = SelectedSubject?.Id;
         var query = SearchQuery?.Trim() ?? string.Empty;
         List<Subject> list;
         if (string.IsNullOrWhiteSpace(query))
         {
             list = await _readRepo.GetAllAsync(CurrentPage, PageSize);
             TotalCount = await _readRepo.GetCountAsync();
         }
         else
         {
             list = await _readRepo.SearchAsync(query, CurrentPage, PageSize);
             TotalCount = await _readRepo.GetSearchCountAsync(query);

             // Defensive fallback: if count says records exist but the page is empty,
             // attempt exact SubjectId lookup so the grid is not left blank.
             if (list.Count == 0 && TotalCount > 0)
             {
                 var exact = await _readRepo.GetBySubjectIdAsync(query);
                 if (exact != null)
                 {
                     list = new List<Subject> { exact };
                     TotalCount = 1;
                     _logger.LogWarning($"[SUBJECTS] Search mismatch for query='{query}'. Using exact SubjectId fallback.");
                 }
             }
         }

         Subjects = new ObservableCollection<Subject>(list);
         if (selectedId.HasValue)
         {
             SelectedSubject = Subjects.FirstOrDefault(s => s.Id == selectedId.Value);
         }
         else if (Subjects.Count > 0)
         {
             SelectedSubject = Subjects[0];
         }
         else
         {
             SelectedSubject = null;
         }

         _logger.LogInformation($"[SUBJECTS] Load completed. Query='{query}', Page={CurrentPage}, Rows={Subjects.Count}, Total={TotalCount}");
         OnPropertyChanged(nameof(HasPreviousPage));
         OnPropertyChanged(nameof(HasNextPage));
         OnPropertyChanged(nameof(PageInfo));
     }

    private void UpdateSelectedSubjectAnalyses(Subject? subject)
    {
        var analyses = subject?.DentalImages?
            .OrderByDescending(i => i.UploadedAt)
            .ToList() ?? new List<DentalImage>();

        SelectedSubjectAnalyses = new ObservableCollection<DentalImage>(analyses);
        OnPropertyChanged(nameof(HasSelectedSubject));
        OnPropertyChanged(nameof(SelectedSubjectAnalysisCount));
        OnPropertyChanged(nameof(HasSubjectAnalyses));
        OnPropertyChanged(nameof(ShowAnalysesEmptyState));
    }

    private void ClearForm()
    {
        FormFullName = string.Empty;
        FormGender = "Unknown";
        FormDateOfBirth = null;
        FormNationalId = string.Empty;
        FormContactInfo = string.Empty;
        FormNotes = string.Empty;
    }

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

    private static string? NormalizeOptionalField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}

