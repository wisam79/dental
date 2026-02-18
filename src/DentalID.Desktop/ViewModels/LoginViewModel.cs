using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DentalID.Core.Interfaces;
using DentalID.Application.Interfaces;
using DentalID.Desktop.Services;
using System.Threading.Tasks;
using System;
using System.ComponentModel.DataAnnotations;

namespace DentalID.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    [Required(ErrorMessage = "Username is required")]
    private string _username = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character (@$!%*?&)")]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showPassword;

    public LoginViewModel(IAuthService authService, INavigationService navigationService, ILoggerService logger)
    {
        _authService = authService;
        _navigationService = navigationService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter both username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authService.LoginAsync(Username, Password);
            if (result.Success)
            {
                _logger.LogInformation($"Login successful for user: {Username}");
                // Navigate to Subjects
                _navigationService.NavigateTo<SubjectsViewModel>();
                // Clear credentials for security
                Password = string.Empty;
                Username = string.Empty;
            }
            else
            {
                ErrorMessage = result.Error ?? "Login failed.";
                Password = string.Empty; // Clear password on failed attempt
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "An unexpected error occurred. Please try again.";
            _logger.LogError(ex, "LoginViewModel LoginAsync error");
            Password = string.Empty; // Clear password on exception
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ForgotPassword()
    {
        // TODO: Implement forgot password functionality
        ErrorMessage = "Forgot password functionality coming soon.";
    }

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        ShowPassword = !ShowPassword;
    }

    partial void OnUsernameChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ErrorMessage = string.Empty;
        }
    }

    partial void OnPasswordChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ErrorMessage = string.Empty;
        }
    }
}

