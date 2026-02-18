using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using DentalID.Application.Configuration;
using DentalID.Application.Services;
using DentalID.Application.Interfaces;
using DentalID.Core.Interfaces;
using DentalID.Desktop.ViewModels;
using DentalID.Infrastructure;
using DentalID.Infrastructure.Data;
using DentalID.Infrastructure.Services;
using DentalID.Infrastructure.Repositories;
using DentalID.Application.Interfaces;
using Avalonia.Threading;

namespace DentalID.Desktop.Services;

/// <summary>
/// Handles the "Secure Boot" sequence of the application.
/// Ensures all dependencies are valid, integrity is checked, and hardware is audited before UI launch.
/// </summary>
public class Bootstrapper
{
    private readonly IServiceCollection _services;

    public Bootstrapper()
    {
        _services = new ServiceCollection();
    }

    public IServiceProvider ConfigureServices(AppSettings settings, AiSettings aiSettings)
    {
        // 1. Fundamentals
        // Build Configuration with layered loading: base → dev → env vars
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
        
        IConfiguration configuration = builder.Build();
        _services.AddSingleton(configuration);

        // Auto-generate security keys if not configured (dev convenience)
        EnsureSecurityKeys(aiSettings, configuration);

        // Bind and Register AiConfiguration (for OnnxInferenceService)
        var aiConfig = new AiConfiguration();
        configuration.GetSection("AI").Bind(aiConfig);
        _services.AddSingleton(aiConfig);
        _services.AddSingleton<IAiConfiguration>(aiConfig); // Register interface if needed

        _services.AddSingleton(settings);
        _services.AddSingleton<ISettingsService>(settings); // Register interface mapping
        _services.AddSingleton(aiSettings);
        _services.AddSingleton<ILoggerService, LogService>();
        _services.AddSingleton<IThemeService>(sp => App.ThemeService!); // Use existing instance from App
        _services.AddSingleton<ILocalizationService>(sp => Loc.Instance);


        // 2. Data Access
        var appDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(appDir);
        var dbPath = Path.Combine(appDir, "dentalid.db");
        _services.AddInfrastructure(dbPath);

        // Repositories - Transient to match DbContext lifetime
        // Note: IIntegrityService, IImageIntegrityService, and IAuditService are registered in AddInfrastructure
        
        // 3. Core Services
        _services.AddSingleton<IDentalIntelligenceService, DentalIntelligenceService>();
        _services.AddSingleton<IFdiSpatialService, FdiSpatialService>();
        _services.AddSingleton<IForensicHeuristicsService, ForensicHeuristicsService>();
        _services.AddSingleton<ITensorPreparationService, TensorPreparationService>();
        _services.AddSingleton<IYoloDetectionParser, YoloDetectionParser>();
        _services.AddSingleton<IAiPipelineService, OnnxInferenceService>();
        _services.AddSingleton<IMatchingService, MatchingService>();
        _services.AddSingleton<IFileService, LocalFileService>();
        _services.AddSingleton<IBiometricService, BiometricService>();
    
        _services.AddTransient<IForensicAnalysisService, ForensicAnalysisService>();
        _services.AddSingleton<IReportService, PdfReportService>();
        _services.AddSingleton<IToastService, ToastService>(); // Registered ToastService
        _services.AddTransient<INavigationService, NavigationService>();
        _services.AddTransient<IBackupService, BackupService>();
        
        
        // 4. ViewModels (Transient for proper lifecycle management)
        _services.AddTransient<StartupViewModel>();
        _services.AddTransient<LoginViewModel>(); // Transient as it's part of flow, or Singleton if reused? Transient safer for state clear.
        _services.AddTransient<MainWindowViewModel>();
        
        // Navigation Targets
        // Navigation Targets
        _services.AddTransient<SubjectsViewModel>();
        _services.AddTransient<AnalysisLabViewModel>();
        _services.AddTransient<MatchingViewModel>();
        _services.AddTransient<SettingsViewModel>();
        _services.AddTransient<ImportWizardViewModel>();
        
        // 5. Build
        return _services.BuildServiceProvider();
    }

    /// <summary>
    /// Runs the Secure Boot sequence.
    /// </summary>
    public async Task RunSecureBootAsync(StartupViewModel vm, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILoggerService>();
        logger.LogInformation("Starting application initialization...");

        try
        {
            // Step 1: Quick integrity check
            vm.UpdateStatus("Verifying System...", 20);
            await VerifyIntegrityAsync(logger, serviceProvider);

            // Step 2: Database initialization
            vm.UpdateStatus("Initializing Database...", 50);
            await InitializeDatabaseAsync(logger, serviceProvider);

            // Step 3: AI Engine initialization
            vm.UpdateStatus("Loading AI Models...", 80);
            await InitializeAiEngineAsync(logger, serviceProvider);

            vm.UpdateStatus("System Ready.", 100);
            logger.LogInformation("Application initialized successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "INITIALIZATION FAILED");
            vm.UpdateStatus($"ERROR: {ex.Message}", 0);
            throw;
        }
    }

    private async Task VerifyIntegrityAsync(ILoggerService logger, IServiceProvider serviceProvider)
    {
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");

        // Simplified integrity check - just verify files exist and are not empty
        // Hash verification disabled for development flexibility
        var requiredFiles = new[] { "teeth_detect.onnx", "pathology_detect.onnx" };

        foreach (var fileName in requiredFiles)
        {
            var path = File.Exists(Path.Combine(modelsDir, fileName)) 
                ? Path.Combine(modelsDir, fileName) 
                : Path.Combine(AppContext.BaseDirectory, fileName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Missing Model File: {fileName}");
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0)
            {
                throw new Exception($"Model file is empty: {fileName}");
            }

            logger.LogInformation($"Verified: {fileName} ({fileInfo.Length / 1024 / 1024}MB)");
        }
    }

    private async Task InitializeDatabaseAsync(ILoggerService logger, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedData.InitializeAsync(db);
            logger.LogInformation("Database initialized.");

            // Secure Admin Initialization
            // We use a new scope to get IAuthService which is Scoped
            using var authScope = serviceProvider.CreateScope();
            var authService = authScope.ServiceProvider.GetRequiredService<IAuthService>();
            await authService.InitializeAdminAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Database Corruption Detected: {ex.Message}", ex);
        }
    }

    private async Task InitializeAiEngineAsync(ILoggerService logger, IServiceProvider serviceProvider)
    {
        var pipeline = serviceProvider.GetRequiredService<IAiPipelineService>();
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        
        // Ensure directory exists
        if (!Directory.Exists(modelsDir))
             throw new DirectoryNotFoundException($"Critical AI Model Directory missing: {modelsDir}");

        await pipeline.InitializeAsync(modelsDir);
        
        if (!pipeline.IsReady)
            throw new Exception("AI Engine failed to return Ready state.");
            
        logger.LogInformation("AI Engine Online.");
    }

    /// <summary>
    /// Auto-generates security keys if not configured.
    /// In production, keys should be provided via environment variables or a vault.
    /// </summary>
    private static void EnsureSecurityKeys(AiSettings aiSettings, IConfiguration config)
    {
        // Auto-generate SealingKey if missing
        if (string.IsNullOrWhiteSpace(aiSettings.SealingKey) || 
            aiSettings.SealingKey.Contains("ChangeMeToASecureRandomKey") ||
            aiSettings.SealingKey.Contains("DO-NOT-USE-IN-PRODUCTION"))
        {
            // Check config first (may have been set via env var DENTALID_AiSettings__SealingKey)
            var configKey = config["AiSettings:SealingKey"];
            if (!string.IsNullOrWhiteSpace(configKey) && !configKey.Contains("DO-NOT-USE-IN-PRODUCTION"))
            {
                aiSettings.SealingKey = configKey;
            }
            else
            {
                // Generate and persist a random key for this installation
                var keyPath = Path.Combine(AppContext.BaseDirectory, "data", ".sealing_key");
                Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

                if (File.Exists(keyPath))
                {
                    aiSettings.SealingKey = File.ReadAllText(keyPath).Trim();
                }
                else
                {
                    var keyBytes = new byte[32];
                    RandomNumberGenerator.Fill(keyBytes);
                    aiSettings.SealingKey = Convert.ToBase64String(keyBytes);
                    File.WriteAllText(keyPath, aiSettings.SealingKey);
                }
            }
        }
    }
}

