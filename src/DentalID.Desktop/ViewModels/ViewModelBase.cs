using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using DentalID.Desktop.Messages;
using DentalID.Desktop.Services;

namespace DentalID.Desktop.ViewModels;

/// <summary>
/// Base class for all ViewModels. Provides INotifyPropertyChanged via ObservableValidator (for validation).
/// </summary>
public abstract partial class ViewModelBase : ObservableValidator
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Safely executes an async action with error handling and busy state management.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <param name="errorMessage">Optional custom error message.</param>
    /// <param name="successMessage">Optional success message to show on completion.</param>
    protected async Task SafeExecuteAsync(Func<Task> action, string? errorMessage = null, string? successMessage = null)
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Processing...";
        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                WeakReferenceMessenger.Default.Send(new ShowToastMessage("Success", successMessage, ToastType.Success));
                StatusMessage = successMessage;
            }
        }
        catch (Exception ex)
        {
            var msg = errorMessage ?? ex.Message;
            StatusMessage = $"Error: {msg}";
            WeakReferenceMessenger.Default.Send(new ShowToastMessage("Error", msg, ToastType.Error));
            System.Diagnostics.Debug.WriteLine($"Error in ViewModel operation: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
