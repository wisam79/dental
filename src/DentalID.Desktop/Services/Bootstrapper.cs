using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
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
using Avalonia.Threading;
using System.Text;
using System.Globalization;

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
        AddPrefixedEnvironmentVariables(builder, "DENTALID_");
        
        IConfiguration configuration = builder.Build();
        _services.AddSingleton(configuration);

        // Bind from layered config (appsettings + development + env vars) so runtime tuning is centralized.
        configuration.GetSection("AiSettings").Bind(aiSettings);

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
        _services.AddLogging(); // Enable generic ILogger<T>
        _services.AddSingleton<ILoggerService, LogService>();
        _services.AddSingleton<IThemeService>(sp => App.ThemeService ?? new ThemeService(Avalonia.Application.Current, sp.GetRequiredService<AppSettings>()));
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
        // AI sub-services extracted from OnnxInferenceService (Phase 5 refactor)
        _services.AddSingleton<IOnnxSessionManager, OnnxSessionManager>();
        _services.AddSingleton<ITeethDetectionService, TeethDetectionService>();
        _services.AddSingleton<IPathologyDetectionService, PathologyDetectionService>();
        _services.AddSingleton<IFeatureEncoderService, FeatureEncoderService>();
        _services.AddSingleton<ISamSegmentationService, SamSegmentationService>();
        _services.AddSingleton<IAiPipelineService, OnnxInferenceService>();
        _services.AddSingleton<IMatchingService, MatchingService>();
        _services.AddSingleton<IFileService, LocalFileService>();
        _services.AddSingleton<IBiometricService, BiometricService>();
        _services.AddTransient<IForensicRulesEngine, ForensicRulesEngine>();
        _services.AddTransient<IForensicAnalysisService, ForensicAnalysisService>();
        _services.AddSingleton<IReportService, PdfReportService>();
        _services.AddSingleton<IToastService, ToastService>(); // Registered ToastService
        // Must be singleton so all ViewModels share the same navigation state/event stream.
        _services.AddSingleton<INavigationService, NavigationService>();
        _services.AddTransient<IBackupService, BackupService>();
        
        
        // 4. ViewModels (Transient for proper lifecycle management)
        _services.AddTransient<StartupViewModel>();
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
        var localization = serviceProvider.GetService<ILocalizationService>();
        logger.LogInformation("Starting application initialization...");

        try
        {
            await RunOnUiThreadAsync(vm.ResetModelStates).ConfigureAwait(false);
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_Booting", "Booting Secure Runtime..."), 8).ConfigureAwait(false);

            // Step 1: Quick integrity check
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_Verifying", "Verifying System..."), 20).ConfigureAwait(false);
            await VerifyIntegrityAsync(logger, serviceProvider).ConfigureAwait(false);
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_Verified", "Integrity Verified."), 35).ConfigureAwait(false);

            // Step 2: Database initialization
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_InitDatabase", "Initializing Database..."), 50).ConfigureAwait(false);
            await InitializeDatabaseAsync(logger, serviceProvider).ConfigureAwait(false);
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_DatabaseReady", "Database Ready."), 68).ConfigureAwait(false);

            // Step 3: AI Engine initialization
            await UpdateStartupAsync(vm, T(localization, "Startup_Status_LoadingModels", "Loading AI Models..."), 80).ConfigureAwait(false);
            await InitializeAiEngineAsync(logger, serviceProvider, vm, localization).ConfigureAwait(false);

            await UpdateStartupAsync(vm, T(localization, "Startup_Status_SystemReady", "System Ready."), 100).ConfigureAwait(false);
            logger.LogInformation("Application initialized successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "INITIALIZATION FAILED");
            await UpdateStartupAsync(vm, $"ERROR: {ex.Message}", 0).ConfigureAwait(false);
            throw;
        }
    }

    private async Task VerifyIntegrityAsync(ILoggerService logger, IServiceProvider serviceProvider)
    {
        var aiSettings = serviceProvider.GetRequiredService<AiSettings>();
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        var requiredFiles = new[] { "teeth_detect.onnx", "pathology_detect.onnx", "encoder.onnx" };
        var optionalFiles = new[] { "genderage.onnx", "sam_encoder.onnx", "sam_decoder.onnx" };
        var filesToVerify = new List<string>(requiredFiles);
        var optionalModelsDetected = new List<string>();

        foreach (var optionalFile in optionalFiles)
        {
            var optionalPath = Path.Combine(modelsDir, optionalFile);
            if (File.Exists(optionalPath))
            {
                filesToVerify.Add(optionalFile);
                optionalModelsDetected.Add(optionalFile);
            }
        }

        var computedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in filesToVerify)
        {
            var path = Path.Combine(modelsDir, fileName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Missing Model File: {fileName}");
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0)
            {
                throw new Exception($"Model file is empty: {fileName}");
            }

            computedHashes[fileName] = await ComputeSha256Async(path).ConfigureAwait(false);
            logger.LogInformation($"Verified: {fileName} ({fileInfo.Length / 1024 / 1024}MB)");
        }

        if (optionalModelsDetected.Count > 0)
        {
            logger.LogInformation(
                $"Optional models included in integrity validation: {string.Join(", ", optionalModelsDetected)}");
        }

        if (!aiSettings.EnableModelIntegrity)
        {
            logger.LogWarning("Model integrity hash validation is disabled by configuration (AiSettings:EnableModelIntegrity=false).");
            return;
        }

        var manifestPath = ResolveManifestPath(aiSettings.ModelIntegrityManifestPath);
        if (File.Exists(manifestPath))
        {
            var manifest = await LoadManifestAsync(manifestPath).ConfigureAwait(false);
            foreach (var requiredFile in filesToVerify)
            {
                if (!manifest.Models.TryGetValue(requiredFile, out var expectedHash))
                {
                    throw new Exception($"Model integrity manifest is missing entry for '{requiredFile}'.");
                }

                var actualHash = computedHashes[requiredFile];
                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"Model integrity mismatch for '{requiredFile}'. " +
                        $"Expected '{expectedHash}', actual '{actualHash}'.");
                }
            }

            logger.LogInformation($"Model integrity manifest verified: {manifestPath}");
            return;
        }

        if (!aiSettings.AllowIntegrityBaselineCreation)
        {
            throw new Exception(
                $"Model integrity manifest not found at '{manifestPath}' and baseline creation is disabled.");
        }

        var baselineManifest = new ModelIntegrityManifest
        {
            Version = 1,
            CreatedUtc = DateTime.UtcNow,
            Models = computedHashes
        };

        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await SaveManifestAsync(manifestPath, baselineManifest).ConfigureAwait(false);
        logger.LogWarning($"Model integrity baseline created at '{manifestPath}'. Review and commit this file intentionally.");
    }

    private async Task InitializeDatabaseAsync(ILoggerService logger, IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedData.InitializeAsync(db).ConfigureAwait(false);
            logger.LogInformation("Database initialized.");
            logger.LogInformation("Runtime auth is disabled (no-login mode). Admin initialization skipped.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Database Corruption Detected: {ex.Message}", ex);
        }
    }

    private async Task InitializeAiEngineAsync(
        ILoggerService logger,
        IServiceProvider serviceProvider,
        StartupViewModel vm,
        ILocalizationService? localization)
    {
        var pipeline = serviceProvider.GetRequiredService<IAiPipelineService>();
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "models");
        var requiredModels = new[]
        {
            (Key: StartupViewModel.TeethModelKey, File: "teeth_detect.onnx", Label: T(localization, "Startup_Model_Teeth", "Teeth detector"), Progress: 83.0),
            (Key: StartupViewModel.PathologyModelKey, File: "pathology_detect.onnx", Label: T(localization, "Startup_Model_Pathology", "Pathology detector"), Progress: 87.0),
            (Key: StartupViewModel.EncoderModelKey, File: "encoder.onnx", Label: T(localization, "Startup_Model_Encoder", "Feature encoder"), Progress: 91.0)
        };
        
        // Ensure directory exists
        if (!Directory.Exists(modelsDir))
        {
            await MarkAllModelsAsync(vm, StartupViewModel.StateError, 100).ConfigureAwait(false);
            throw new DirectoryNotFoundException($"Critical AI Model Directory missing: {modelsDir}");
        }

        foreach (var model in requiredModels)
        {
            await UpdateModelAsync(vm, model.Key, StartupViewModel.StateValidating, 25).ConfigureAwait(false);
            await UpdateStartupAsync(
                vm,
                TF(localization, "Startup_Status_ValidatingModel", "Validating {0}...", model.Label),
                model.Progress).ConfigureAwait(false);

            var modelPath = Path.Combine(modelsDir, model.File);
            if (!File.Exists(modelPath))
            {
                await UpdateModelAsync(vm, model.Key, StartupViewModel.StateError, 100).ConfigureAwait(false);
                throw new FileNotFoundException($"Missing Model File: {model.File}", modelPath);
            }

            var fileInfo = new FileInfo(modelPath);
            if (fileInfo.Length == 0)
            {
                await UpdateModelAsync(vm, model.Key, StartupViewModel.StateError, 100).ConfigureAwait(false);
                throw new Exception($"Model file is empty: {model.File}");
            }
            
            try 
            {
                using var testSession = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath);
            }
            catch (Exception ex)
            {
                await UpdateModelAsync(vm, model.Key, StartupViewModel.StateError, 100).ConfigureAwait(false);
                throw new Exception($"Model file is corrupted: {model.File}", ex);
            }

            logger.LogInformation($"Validated model: {model.File} ({fileInfo.Length / 1024 / 1024}MB)");
            await UpdateModelAsync(vm, model.Key, StartupViewModel.StateVerified, 55).ConfigureAwait(false);
        }

        await UpdateStartupAsync(
            vm,
            T(localization, "Startup_Status_LoadingSessions", "Loading ONNX runtime sessions..."),
            94).ConfigureAwait(false);
        foreach (var model in requiredModels)
        {
            await UpdateModelAsync(vm, model.Key, StartupViewModel.StateLoading, 82).ConfigureAwait(false);
        }

        try
        {
            await pipeline.InitializeAsync(modelsDir).ConfigureAwait(false);
        }
        catch
        {
            await MarkAllModelsAsync(vm, StartupViewModel.StateError, 100).ConfigureAwait(false);
            throw;
        }
        
        if (!pipeline.IsReady)
        {
            await MarkAllModelsAsync(vm, StartupViewModel.StateError, 100).ConfigureAwait(false);
            throw new Exception("AI Engine failed to return Ready state.");
        }

        foreach (var model in requiredModels)
        {
            await UpdateModelAsync(vm, model.Key, StartupViewModel.StateReady, 100).ConfigureAwait(false);
        }

        await UpdateStartupAsync(
            vm,
            T(localization, "Startup_Status_ModelsLoaded", "AI models loaded successfully."),
            98).ConfigureAwait(false);
            
        logger.LogInformation("AI Engine Online.");
    }

    private static async Task UpdateStartupAsync(StartupViewModel vm, string message, double progress)
    {
        await RunOnUiThreadAsync(() => vm.UpdateStatus(message, progress)).ConfigureAwait(false);
    }

    private static async Task UpdateModelAsync(StartupViewModel vm, string modelKey, string state, double progress)
    {
        await RunOnUiThreadAsync(() => vm.UpdateModelStatus(modelKey, state, progress)).ConfigureAwait(false);
    }

    private static async Task MarkAllModelsAsync(StartupViewModel vm, string state, double progress)
    {
        await UpdateModelAsync(vm, StartupViewModel.TeethModelKey, state, progress).ConfigureAwait(false);
        await UpdateModelAsync(vm, StartupViewModel.PathologyModelKey, state, progress).ConfigureAwait(false);
        await UpdateModelAsync(vm, StartupViewModel.EncoderModelKey, state, progress).ConfigureAwait(false);
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    /// <summary>
    /// Auto-generates security keys if not configured.
    /// Priority: Environment Variable → Existing persisted key → Generate new key.
    /// In production, always set DENTALID_SEALING_KEY environment variable.
    /// </summary>
    private static void EnsureSecurityKeys(AiSettings aiSettings, IConfiguration config)
    {
        // 1. Check environment variable first (highest priority)
        var envKey = Environment.GetEnvironmentVariable("DENTALID_SEALING_KEY");
        if (!string.IsNullOrWhiteSpace(envKey) && !IsDefaultInsecureKey(envKey))
        {
            aiSettings.SealingKey = envKey;
            return;
        }

        // 2. Check if already configured with a non-default value
        if (!string.IsNullOrWhiteSpace(aiSettings.SealingKey) && !IsDefaultInsecureKey(aiSettings.SealingKey))
        {
            return; // Already has a valid key
        }

        // 3. Check config (may have been set via env var DENTALID_AiSettings__SealingKey)
        var configKey = config["AiSettings:SealingKey"];
        if (!string.IsNullOrWhiteSpace(configKey) && !IsDefaultInsecureKey(configKey))
        {
            aiSettings.SealingKey = configKey;
            return;
        }

        // 4. Generate and persist a random key for this installation
        var keyPath = Path.Combine(AppContext.BaseDirectory, "data", ".sealing_key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        if (File.Exists(keyPath))
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(keyPath);
                string persistedKey = string.Empty;

                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    try
                    {
                        var unp = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);
                        persistedKey = Encoding.UTF8.GetString(unp);
                    }
                    catch
                    {
                        // Removed insecure fallback to plaintext reading
                        throw new CryptographicException("Failed to unprotect sealing key. Ensure DPAPI is functioning or delete the corrupted key file.");
                    }
                }
                else
                {
                    persistedKey = Encoding.UTF8.GetString(fileBytes).Trim();
                }

                if (!string.IsNullOrWhiteSpace(persistedKey) && !IsDefaultInsecureKey(persistedKey))
                {
                    aiSettings.SealingKey = persistedKey;
                    return;
                }
            }
            catch { /* fallback to generate */ }
        }

        // Generate new cryptographically secure key
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        aiSettings.SealingKey = Convert.ToBase64String(keyBytes);

        try
        {
            var dataToWrite = Encoding.UTF8.GetBytes(aiSettings.SealingKey);
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                dataToWrite = ProtectedData.Protect(dataToWrite, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyPath, dataToWrite);
            }
            else
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using var fs = new FileStream(keyPath, options);
                fs.Write(dataToWrite);
            }
        }
        catch
        {
            // Key stays in memory; warn on next startup when file is missing
        }
    }

    /// <summary>
    /// Returns true if the key string is a known insecure default placeholder.
    /// </summary>
    private static bool IsDefaultInsecureKey(string key) =>
        key.Contains("CHANGE-THIS", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("DO-NOT-USE", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("ChangeMeToASecureRandomKey", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("DO-NOT-USE-IN-PRODUCTION", StringComparison.OrdinalIgnoreCase) ||
        key.Length < 16; // Too short to be a real key

    private static string ResolveManifestPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(AppContext.BaseDirectory, "data", "model_integrity.json");
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static async Task<ModelIntegrityManifest> LoadManifestAsync(string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<ModelIntegrityManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest == null)
        {
            throw new Exception($"Unable to parse model integrity manifest: {manifestPath}");
        }

        manifest.Models ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return manifest;
    }

    private static async Task SaveManifestAsync(string manifestPath, ModelIntegrityManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(manifestPath, json).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void AddPrefixedEnvironmentVariables(IConfigurationBuilder builder, string prefix)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var rawKey = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(rawKey) ||
                !rawKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var configKey = rawKey[prefix.Length..].Replace("__", ":", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(configKey))
            {
                continue;
            }

            values[configKey] = entry.Value?.ToString();
        }

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }
    }

    private static string T(ILocalizationService? localization, string key, string fallback)
    {
        if (localization == null)
            return fallback;

        var value = localization[key];
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, $"[{key}]", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private static string TF(ILocalizationService? localization, string key, string fallback, params object[] args)
    {
        var format = T(localization, key, fallback);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }

    private sealed class ModelIntegrityManifest
    {
        public int Version { get; set; } = 1;
        public DateTime CreatedUtc { get; set; }
        public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
