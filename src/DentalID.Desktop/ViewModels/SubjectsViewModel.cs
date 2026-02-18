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
    [ObservableProperty] private Subject? _selectedSubject;
    [ObservableProperty] private bool _isAddEditDialogOpen;
    [ObservableProperty] private bool _isEditing;

    // Form Properties
    [ObservableProperty] [NotifyDataErrorInfo] [Required] private string _formFullName = string.Empty;
    [ObservableProperty] private string _formGender = string.Empty;
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
    public string PageInfo => $"Page {CurrentPage} of {Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize))}";

    // Partial method hooks for property changes if needed
    partial void OnCurrentPageChanged(int value) => RefreshPagination();
    partial void OnTotalCountChanged(int value) => RefreshPagination();

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
        FormGender = SelectedSubject.Gender ?? "";
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
        ValidateAllProperties();
        if (HasErrors)
        {
            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(GetErrors(nameof(FormFullName)).FirstOrDefault()?.ErrorMessage))
                errors.Add("Full name is required");
            if (!string.IsNullOrWhiteSpace(GetErrors(nameof(FormDateOfBirth)).FirstOrDefault()?.ErrorMessage))
                errors.Add("Valid date of birth is required");
            if (!string.IsNullOrWhiteSpace(GetErrors(nameof(FormGender)).FirstOrDefault()?.ErrorMessage))
                errors.Add("Gender is required");
            if (!string.IsNullOrWhiteSpace(GetErrors(nameof(FormNationalId)).FirstOrDefault()?.ErrorMessage))
                errors.Add("Valid national ID is required");
            if (!string.IsNullOrWhiteSpace(GetErrors(nameof(FormContactInfo)).FirstOrDefault()?.ErrorMessage))
                errors.Add("Valid contact info is required");

            var errorMessage = string.Join("\n", errors);
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Validation Error", errorMessage, ToastType.Warning));
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            // Bug #30 Fix: Capture IsEditing BEFORE any await/async operations.
            // After 'await LoadAsyncCore()' the UI thread may process other events and 
            // IsEditing could be stale or changed. Using a local copy ensures the correct
            // value is used in the Toast notification.
            bool wasEditing = IsEditing;

            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISubjectRepository>();

            // Check for duplicate national ID
            if (!string.IsNullOrWhiteSpace(FormNationalId))
            {
                var existingSubject = await repo.GetByNationalIdAsync(FormNationalId);
                if (existingSubject != null && (!IsEditing || existingSubject.Id != SelectedSubject?.Id))
                {
                    WeakReferenceMessenger.Default.Send(new ShowToastMessage(
                        "Duplicate National ID", 
                        "A subject with this national ID already exists.", 
                        ToastType.Error));
                    return;
                }
            }

            if (IsEditing)
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
                    subjectToUpdate.FullName = FormFullName;
                    subjectToUpdate.Gender = string.IsNullOrWhiteSpace(FormGender) ? null : FormGender;
                    subjectToUpdate.DateOfBirth = FormDateOfBirth?.DateTime;
                    subjectToUpdate.NationalId = string.IsNullOrWhiteSpace(FormNationalId) ? null : FormNationalId;
                    subjectToUpdate.ContactInfo = string.IsNullOrWhiteSpace(FormContactInfo) ? null : FormContactInfo;
                    subjectToUpdate.Notes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes;
                    
                    await repo.UpdateAsync(subjectToUpdate);
                    _logger?.LogInformation($"Subject updated: {subjectToUpdate.FullName} ({subjectToUpdate.SubjectId})");
                }
            }
            else
            {
                var subject = new Subject
                {
                    SubjectId = $"SUB-{Guid.NewGuid():N}",
                    FullName = FormFullName,
                    Gender = string.IsNullOrWhiteSpace(FormGender) ? null : FormGender,
                    DateOfBirth = FormDateOfBirth?.DateTime,
                    NationalId = string.IsNullOrWhiteSpace(FormNationalId) ? null : FormNationalId,
                    ContactInfo = string.IsNullOrWhiteSpace(FormContactInfo) ? null : FormContactInfo,
                    Notes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes,
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
         // Use the Read Repo (Transient but likely shared for view lifecycle or just used for reading)
         List<Subject> list;
         if (string.IsNullOrWhiteSpace(SearchQuery))
         {
             list = await _readRepo.GetAllAsync(CurrentPage, PageSize);
             TotalCount = await _readRepo.GetCountAsync();
         }
         else
         {
             list = await _readRepo.SearchAsync(SearchQuery, CurrentPage, PageSize);
             TotalCount = await _readRepo.GetSearchCountAsync(SearchQuery);
         }
         
         Subjects = new ObservableCollection<Subject>(list);

         OnPropertyChanged(nameof(HasPreviousPage));
         OnPropertyChanged(nameof(HasNextPage));
         OnPropertyChanged(nameof(PageInfo));
     }

    private void ClearForm()
    {
        FormFullName = string.Empty;
        FormGender = string.Empty;
        FormDateOfBirth = null;
        FormNationalId = string.Empty;
        FormContactInfo = string.Empty;
        FormNotes = string.Empty;
    }
}

