using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;

namespace DentalID.Desktop.Services;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public interface IToastService
{
    ObservableCollection<ToastViewModel> Toasts { get; }
    void Show(string title, string message, ToastType type = ToastType.Info);
    void Success(string code, string message); // For quick localization lookups later
    void Error(string code, string message); 
}

public partial class ToastViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private ToastType _type;
    [ObservableProperty] private bool _isVisible = true;

    private readonly Action<ToastViewModel> _onClose;

    public ToastViewModel(string title, string message, ToastType type, Action<ToastViewModel> onClose)
    {
        Title = title;
        Message = message;
        Type = type;
        _onClose = onClose;

        // Auto-close after 5 seconds
        DispatcherTimer.RunOnce(() => Close(), TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    public void Close()
    {
        IsVisible = false;
        // Allow animation to play before removing? For now, simplistic.
        _onClose(this);
    }
}

public class ToastService : IToastService
{
    // Simple event based for now, or direct manipulation if we inject MainViewModel (circular dependency risk).
    // Better pattern: Messenger or specific ToastManager.
    // For simplicity in this codebase, let's use a singleton-like access or Messenger.
    // Actually, let's expose an ObservableCollection that MainViewModel can bind to.
    
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    public void Show(string title, string message, ToastType type = ToastType.Info)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var toast = new ToastViewModel(title, message, type, RemoveToast);
            Toasts.Add(toast);
        });
    }

    private void RemoveToast(ToastViewModel toast)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (Toasts.Contains(toast))
                Toasts.Remove(toast);
        });
    }

    public void Success(string title, string message) => Show(title, message, ToastType.Success);
    public void Error(string title, string message) => Show(title, message, ToastType.Error);
}
