using System;
using Microsoft.Extensions.DependencyInjection;
using DentalID.Core.Interfaces;

namespace DentalID.Desktop.Services;

/// <summary>
/// Navigation service that manages view switching in the main content area.
/// </summary>
public interface INavigationService
{
    ViewModels.ViewModelBase CurrentView { get; }
    event EventHandler<ViewModels.ViewModelBase> CurrentViewChanged;
    TViewModel? NavigateTo<TViewModel>() where TViewModel : ViewModels.ViewModelBase;
}

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private ViewModels.ViewModelBase _currentView = null!;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModels.ViewModelBase CurrentView
    {
        get => _currentView;
        private set
        {
            if (_currentView != value)
            {
                // Memory Leak Fix: Dispose the old view if it implements IDisposable
                if (_currentView is IDisposable disposable)
                {
                    disposable.Dispose();
                    var logger = _serviceProvider.GetService<ILoggerService>();
                    logger?.LogInformation($"[NAV] Disposed resources for {_currentView.GetType().Name}");
                }

                _currentView = value;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] NavigationService: CurrentView changed to {value?.GetType().Name}");
                var loggerSvc = _serviceProvider.GetService<ILoggerService>();
                loggerSvc?.LogInformation($"[NAV] CurrentView changed to {value?.GetType().Name}");
                if (value != null)
                {
                    CurrentViewChanged?.Invoke(this, value);
                }
            }
        }
    }

    public event EventHandler<ViewModels.ViewModelBase>? CurrentViewChanged;

    public TViewModel? NavigateTo<TViewModel>() where TViewModel : ViewModels.ViewModelBase
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] NavigationService.NavigateTo<{typeof(TViewModel).Name}>()");
            var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] ViewModel resolved: {viewModel?.GetType().Name ?? "NULL"}");
            var logger = _serviceProvider.GetService<ILoggerService>();
            logger?.LogInformation($"[NAV] NavigateTo: Resolved {typeof(TViewModel).Name}");
            CurrentView = viewModel!;
            return viewModel;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] NavigationService.NavigateTo<{typeof(TViewModel).Name}>() failed: {ex.Message}");
            var logger = _serviceProvider.GetService<ILoggerService>();
            logger?.LogError(ex, $"[NAV] NavigateTo<{typeof(TViewModel).Name}>() failed: {ex.Message}");
            // Do not rethrow to prevent crash
            System.Diagnostics.Debug.WriteLine($"[ERROR] Full exception: {ex}");
            return null;
        }
    }
}
