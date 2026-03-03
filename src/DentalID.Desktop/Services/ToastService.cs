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
    void Show(string title, string message, ToastType type = ToastType.Info, Action? onUndo = null);
    void Success(string code, string message); // For quick localization lookups later
    void Error(string code, string message); 
}

public partial class ToastViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private ToastType _type;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private double _progress = 100;
    [ObservableProperty] private bool _hasUndo;

    private readonly Action<ToastViewModel> _onClose;
    private readonly Action? _onUndo;
    private DispatcherTimer? _timer;
    private int _ticks = 0;
    private const int MaxTicks = 100;

    public ToastViewModel(string title, string message, ToastType type, Action<ToastViewModel> onClose, Action? onUndo = null)
    {
        Title = title;
        Message = message;
        Type = type;
        _onClose = onClose;
        _onUndo = onUndo;
        HasUndo = onUndo != null;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _ticks++;
        Progress = 100.0 - (_ticks / (double)MaxTicks * 100.0);
        if (_ticks >= MaxTicks)
        {
            Close();
        }
    }

    [RelayCommand]
    public void Undo()
    {
        _onUndo?.Invoke();
        Close();
    }

    [RelayCommand]
    public void Close()
    {
        IsVisible = false;
        Dispose();
        _onClose(this);
    }

    public void Dispose()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }
}

public class ToastService : IToastService
{
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    public void Show(string title, string message, ToastType type = ToastType.Info, Action? onUndo = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastViewModel(title, message, type, RemoveToast, onUndo);
            Toasts.Add(toast);
        });
    }

    private void RemoveToast(ToastViewModel toast)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Toasts.Contains(toast))
            {
                toast.Dispose();
                Toasts.Remove(toast);
            }
        });
    }

    public void Success(string title, string message) => Show(title, message, ToastType.Success);
    public void Error(string title, string message) => Show(title, message, ToastType.Error);
}
