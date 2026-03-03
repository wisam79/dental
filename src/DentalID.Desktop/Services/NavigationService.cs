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
    private IServiceScope? _currentScope;

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
            
            // Memory Leak Fix: Dispose the previous scope and its Transient ViewModels
            _currentScope?.Dispose();
            
            // Create a new scope for the incoming View
            _currentScope = _serviceProvider.CreateScope();
            
            var viewModel = _currentScope.ServiceProvider.GetRequiredService<TViewModel>();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] ViewModel resolved: {viewModel?.GetType().Name ?? "NULL"}");
            var logger = _serviceProvider.GetService<ILoggerService>();
            logger?.LogInformation($"[NAV] NavigateTo: Resolved {typeof(TViewModel).Name} in new scope");
            
            CurrentView = viewModel!;
            return viewModel;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] NavigationService.NavigateTo<{typeof(TViewModel).Name}>() failed: {ex.Message}");
            var logger = _serviceProvider.GetService<ILoggerService>();
            logger?.LogError(ex, $"[NAV] NavigateTo<{typeof(TViewModel).Name}>() failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ERROR] Full exception: {ex}");
            throw; // Fixed silent failure bug
        }
    }
}
