using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Controls;
using DentalID.Desktop.Messages;
using DentalID.Desktop.Services;
using DentalID.Core.Interfaces;

namespace DentalID.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IToastService _toastService;
    private readonly ILoggerService? _logger;
    private string _currentPageLocKey = "Nav_Subjects";

    public ObservableCollection<ToastViewModel> Toasts => _toastService.Toasts;


    [ObservableProperty]
    private ViewModelBase _currentView;
    
    private bool _suppressNavSelectionHandling;

    private int _selectedNavIndex;
    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            if (SetProperty(ref _selectedNavIndex, value))
            {
                if (_suppressNavSelectionHandling)
                    return;

                _logger?.LogInformation($"[NAV] SelectedNavIndex SET to value={value}, _navigation={_navigation != null}");
                OnSelectedNavIndexChanged(value);
            }
        }
    }

    [ObservableProperty]
    private string _currentPageTitle = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;
    
    [ObservableProperty]
    private string _currentUserName = "Unknown";

    [ObservableProperty]
    private bool _isShellVisible;

    [ObservableProperty]
    private bool _isFocusMode = false;

    [RelayCommand]
    private void ToggleFocusMode()
    {
        IsFocusMode = !IsFocusMode;
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        var next = Loc.Instance.CurrentLanguage == "ar" ? "en" : "ar";
        Loc.Instance.SwitchLanguage(next);

        var title = next == "ar" ? "اللغة" : "Language";
        var message = next == "ar" ? "تم التبديل إلى العربية." : "Switched to English.";
        _toastService.Show(title, message, ToastType.Info);
    }

    [RelayCommand]
    private void NavigateToSubjects()
    {
        SelectedNavIndex = 0;
    }

    [RelayCommand]
    private void NavigateToAnalysisLab()
    {
        _logger?.LogInformation("[CMD] NavigateToAnalysisLab command executed");
        SelectedNavIndex = 1;
    }

    [RelayCommand]
    private void NavigateToMatching()
    {
        SelectedNavIndex = 2;
    }



    public MainWindowViewModel()
    {
        // Design-time constructor - navigation is not available
        _navigation = null!;
        _toastService = null!;
        _logger = null;

        // Create a simple view for design-time preview (use safe null handling)
        _currentView = new SubjectsViewModel(
            null!, 
            null!, 
            null!
        );
    }

    public MainWindowViewModel(INavigationService navigation, IToastService toastService, ILoggerService logger)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _navigation.CurrentViewChanged += OnCurrentViewChanged;
        
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        UpdateStrings();

        // Research Mode: Default to Research User (admin status should be determined by authentication)
        CurrentUserName = "Researcher";
        IsAdmin = false; // Default to non-admin; should be set by authentication

        // Navigate to Subjects by default (Data Management)
        _navigation.NavigateTo<SubjectsViewModel>();
        _currentView = _navigation.CurrentView;
        SyncShellForCurrentView(_currentView);

        // Register for global toast messages
        WeakReferenceMessenger.Default.Register<ShowToastMessage>(this, (r, m) =>
        {
            _toastService.Show(m.Value.Title, m.Value.Message, m.Value.Type);
        });

        // Register for busy state
        WeakReferenceMessenger.Default.Register<SetBusyMessage>(this, (r, m) =>
        {
            IsBusy = m.Value;
            BusyMessage = m.Message;
        });
    }

    [ObservableProperty]
    private string _busyMessage = "Ready";

    private void OnLanguageChanged(object? sender, string e)
    {
        UpdateStrings();
    }

    private void UpdateStrings()
    {
        Title = Loc.Instance["App_TitleFull"];
        SetCurrentPageFromLocalizationKey(_currentPageLocKey);
    }

    private void OnCurrentViewChanged(object? sender, ViewModelBase viewModel)
    {
        CurrentView = viewModel;
        SyncShellForCurrentView(viewModel);
    }

    private void OnSelectedNavIndexChanged(int value)
    {
        _logger?.LogInformation($"[NAV] OnSelectedNavIndexChanged: value={value}, _navigation={_navigation != null}");
        
        if (_navigation == null)
        {
            _logger?.LogWarning("[NAV] Navigation is NULL! Skipping navigation.");
            return;
        }
        
        // If main nav is selected (0-2), deselect settings
        if (value >= 0 && value <= 2)
        {
            SettingsNavIndex = -1; 
        }

        try
        {
            switch (value)
            {
                case 0:
                    _navigation.NavigateTo<SubjectsViewModel>();
                    SetCurrentPageFromLocalizationKey("Nav_Subjects");
                    _logger?.LogInformation("[NAV] Navigated to Subjects");
                    break;
                case 1:
                    _logger?.LogInformation("[NAV] Navigating to AnalysisLabViewModel...");
                    _navigation.NavigateTo<AnalysisLabViewModel>();
                    SetCurrentPageFromLocalizationKey("Nav_AnalysisLab");
                    _logger?.LogInformation("[NAV] Navigated to Analysis Lab");
                    break;
                case 2:
                    _navigation.NavigateTo<MatchingViewModel>();
                    SetCurrentPageFromLocalizationKey("Nav_Matching");
                    _logger?.LogInformation("[NAV] Navigated to Matching");
                    break;
                default:
                    // Invalid index or Settings selected (handled elsewhere)
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[NAV] Navigation failed");
        }
    }

    [ObservableProperty]
    private int _settingsNavIndex = 0;

    partial void OnSettingsNavIndexChanged(int value)
    {
        if (value == 0) // Settings Selected
        {
            _navigation.NavigateTo<SettingsViewModel>();
            SetCurrentPageFromLocalizationKey("Nav_Settings");
            SelectedNavIndex = -1; // Deselect main nav
        }
    }

    private void SyncShellForCurrentView(ViewModelBase viewModel)
    {
        switch (viewModel)
        {
            case SubjectsViewModel:
                SetCurrentPageFromLocalizationKey("Nav_Subjects");
                SetSelectedNavIndexSilently(0);
                SettingsNavIndex = -1;
                break;
            case AnalysisLabViewModel:
                SetCurrentPageFromLocalizationKey("Nav_AnalysisLab");
                SetSelectedNavIndexSilently(1);
                SettingsNavIndex = -1;
                break;
            case MatchingViewModel:
                SetCurrentPageFromLocalizationKey("Nav_Matching");
                SetSelectedNavIndexSilently(2);
                SettingsNavIndex = -1;
                break;
            case SettingsViewModel:
                SetCurrentPageFromLocalizationKey("Nav_Settings");
                SetSelectedNavIndexSilently(-1);
                if (SettingsNavIndex != 0)
                    SettingsNavIndex = 0;
                break;
        }
    }

    private void SetCurrentPageFromLocalizationKey(string key)
    {
        _currentPageLocKey = key;
        CurrentPageTitle = Loc.Instance[key];
    }

    private void SetSelectedNavIndexSilently(int index)
    {
        _suppressNavSelectionHandling = true;
        SelectedNavIndex = index;
        _suppressNavSelectionHandling = false;
    }
    [RelayCommand]
    private void Minimize()
    {
        if (GetMainWindow() is Window w)
        {
            w.WindowState = WindowState.Minimized;
        }
    }

    [RelayCommand]
    private void Maximize()
    {
        if (GetMainWindow() is Window w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (GetMainWindow() is Window w)
        {
            w.Close();
        }
    }

    private Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}

