using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DentalID.Desktop.Services;
using DentalID.Desktop.ViewModels;
using DentalID.Desktop.Views;
using DentalID.Desktop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using DentalID.Core.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using AppConfig = DentalID.Application.Configuration;

namespace DentalID.Desktop;

public partial class App : Avalonia.Application
{
    private Bootstrapper _bootstrapper = null!;
    private IServiceProvider _serviceProvider = null!;

    public static ThemeService? ThemeService { get; private set; }
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 1. Setup Global Exception Handler FIRST
            GlobalExceptionHandler.Setup();

            // 2. Load Configuration
            var settings = AppSettings.Load();
            ThemeService = new ThemeService(this, settings);
            var aiSettings = LoadAiSettings();

            // 3. Initialize Bootstrapper
            _bootstrapper = new Bootstrapper();
            _serviceProvider = _bootstrapper.ConfigureServices(settings, aiSettings);
            Services = _serviceProvider;

            // 4. Get Logger
            var logger = _serviceProvider.GetRequiredService<ILoggerService>();
            logger.LogInformation("App.Initialize() - Bootstrapper Configured");

            // 5. Create Startup View
            var startupVm = _serviceProvider.GetRequiredService<StartupViewModel>();
            
            // Create MainWindow (Shell)
            var mainWindow = new MainWindow();
            
            var mainVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = mainVm;

            // Initialize in Secure Boot Mode (Shell Hidden, Startup View Active)
            mainVm.CurrentView = startupVm; 
            mainVm.IsShellVisible = false;

            desktop.MainWindow = mainWindow;
            
            // 6. Run Secure Boot (Background)
            logger.LogInformation("Starting Background Secure Boot Task...");
            
            var bootstrapper = _bootstrapper; 
            var provider = _serviceProvider;

            Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("Running SecureBootAsync...");
                    await bootstrapper.RunSecureBootAsync(startupVm, provider);
                    
                    logger.LogInformation("SecureBoot Async Completed. Scheduling UI switch to Login...");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try 
                        {
                            var navigation = provider.GetRequiredService<INavigationService>();
                            
                            // Navigate to Login first
                            var loginVm = navigation.NavigateTo<LoginViewModel>(); 
                            
                            if (loginVm != null)
                            {
                                mainVm.IsShellVisible = true; 
                                logger.LogInformation("MainView Switched to Login.");
                            }
                            else
                            {
                                throw new Exception("Failed to navigate to Login View.");
                            }
                        }
                            catch (Exception innerEx)
                            {
                                 Console.WriteLine($"[CRITICAL ERROR UI SWITCH]: {innerEx}");
                                 logger.LogError(innerEx, "Error during UI switch");
                                 throw;
                            }
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL ERROR SECURE BOOT]: {ex}");
                    logger.LogError(ex, "Secure Boot or Task Failed");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        startupVm.StatusMessage = $"CRITICAL FAILURE:\n{ex.Message}\n\nReview logs";
                        startupVm.ProgressValue = 0;
                        desktop.Shutdown(-1);
                    });
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private AppConfig.AiSettings LoadAiSettings()
    {
        var settings = new AppConfig.AiSettings();
        try
        {
            var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(appSettingsPath))
            {
                var json = File.ReadAllText(appSettingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("AiSettings", out var element))
                {
                    settings = System.Text.Json.JsonSerializer.Deserialize<AppConfig.AiSettings>(
                        element.GetRawText(), 
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AiSettings Load Error: {ex.Message}");
        }
        return settings;
    }
}


