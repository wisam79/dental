using System.Diagnostics;
using DentalID.Application.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace DentalID.Benchmark;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("==============================================");
        Console.WriteLine("   DentalID AI Benchmark Tool (v2.0)          ");
        Console.WriteLine("==============================================");

        // Parse command line arguments
        string targetPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "data", "images");
        bool useParallel = args.Contains("--parallel") || args.Contains("-p");
        int maxConcurrency = 2; // Match SemaphoreSlim limit in OnnxInferenceService

        bool isSingleFile = File.Exists(targetPath) && !Directory.Exists(targetPath);

        string[] files;
        if (isSingleFile)
        {
             files = new[] { targetPath };
             Console.WriteLine($"Target File: {targetPath}");
        }
        else if (Directory.Exists(targetPath))
        {
            Console.WriteLine($"Target Folder: {targetPath}");
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".dcm", ".tif", ".tiff" };
            files = Directory.GetFiles(targetPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                 .ToArray();
        }
        else
        {
            Console.WriteLine($"Error: Path not found: {targetPath}");
            return;
        }

        // Initialize AI Service
        Console.WriteLine("Initializing AI Models...");
        
        // MOCK dependencies for Benchmark
        var config = new DentalID.Application.Configuration.AiConfiguration(); 
        var aiSettings = new DentalID.Application.Configuration.AiSettings();
        // Simple console logger for benchmark
        // Simple console logger for benchmark
        var logger = new DentalID.Infrastructure.Services.LogService(); 
        var bioService = new BiometricService();
        var intelligenceService = new DentalIntelligenceService();
        
        // Cache Service
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var cacheService = new DentalID.Infrastructure.Services.CacheService(memoryCache);
        // Extracted Services
        var fdiService = new FdiSpatialService();
        var heuristicsService = new ForensicHeuristicsService();
        var yoloParser = new YoloDetectionParser(config, aiSettings, fdiService);
        var tensorPrep = new TensorPreparationService();
        var sessionManager = new DentalID.Application.Services.OnnxSessionManager(config, aiSettings, logger);
        var teethSvc   = new DentalID.Application.Services.TeethDetectionService(sessionManager, yoloParser, fdiService, heuristicsService, tensorPrep, config, aiSettings);
        var pathSvc    = new DentalID.Application.Services.PathologyDetectionService(sessionManager, yoloParser, tensorPrep, config, aiSettings);
        var encoderSvc = new DentalID.Application.Services.FeatureEncoderService(sessionManager, tensorPrep, config, logger);
        var samSvc     = new DentalID.Application.Services.SamSegmentationService(
            sessionManager,
            NullLogger<DentalID.Application.Services.SamSegmentationService>.Instance);

        using var aiService = new OnnxInferenceService(
            sessionManager,
            teethSvc,
            pathSvc,
            encoderSvc,
            samSvc,
            yoloParser,
            heuristicsService,
            intelligenceService,
            bioService,
            cacheService,
            logger);
        
        string modelsPath = Path.Combine(AppContext.BaseDirectory, "models");
        
        // If the directory doesn't exist OR it's empty (e.g. MSBuild created it but didn't copy the large files)
        if (!Directory.Exists(modelsPath) || !File.Exists(Path.Combine(modelsPath, "teeth_detect.onnx")))
        {
             var projectRoot = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.Parent?.Parent?.FullName;
             if (projectRoot != null)
             {
                 // Try the repository root "models" folder
                 var rootModels = Path.Combine(projectRoot, "models");
                 if (Directory.Exists(rootModels) && File.Exists(Path.Combine(rootModels, "teeth_detect.onnx"))) 
                 {
                     modelsPath = rootModels;
                 }
                 else
                 {
                     var desktopModels = Path.Combine(projectRoot, "src", "DentalID.Desktop", "Models");
                     if (Directory.Exists(desktopModels)) modelsPath = desktopModels;
                 }
             }
        }

        if (!Directory.Exists(modelsPath))
        {
             Console.WriteLine($"CRITICAL: Models directory not found at {modelsPath}");
             return;
        }

        await aiService.InitializeAsync(modelsPath);

        if (!aiService.IsReady)
        {
             Console.WriteLine("CRITICAL: AI Service failed to initialize.");
             return;
        }

        Console.WriteLine($"Found {files.Length} images to process.");
        Console.WriteLine($"Processing Mode: {(useParallel ? $"Parallel (max {maxConcurrency} concurrent)" : "Sequential")}");
        Console.WriteLine("----------------------------------------------");

        var sw = Stopwatch.StartNew();
        var results = new ConcurrentBag<(string file, AnalysisResult result, Exception? error)>();
        int successCount = 0;
        int failureCount = 0;
        double totalProcessingTime = 0;

        if (useParallel)
        {
            // Parallel processing with concurrency limit
            var semaphore = new SemaphoreSlim(maxConcurrency);
            
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var stream = File.OpenRead(file);
                    var result = await aiService.AnalyzeImageAsync(stream, Path.GetFileName(file));
                    results.Add((file, result, null));
                }
                catch (Exception ex)
                {
                    results.Add((file, new AnalysisResult { Error = ex.Message }, ex));
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(tasks);
        }
        else
        {
            // Sequential processing (original behavior)
            foreach (var file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var result = await aiService.AnalyzeImageAsync(stream, Path.GetFileName(file));
                    results.Add((file, result, null));
                }
                catch (Exception ex)
                {
                    results.Add((file, new AnalysisResult { Error = ex.Message }, ex));
                }
            }
        }

        sw.Stop();

        // Display results in sorted order
        foreach (var (file, result, error) in results.OrderBy(r => r.file))
        {
            Console.WriteLine($"Processing {Path.GetFileName(file)}...");
            
            if (error != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {error.Message}");
                Console.ResetColor();
                failureCount++;
            }
            else if (result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] {result.ProcessingTimeMs}ms | {result.Teeth.Count} Teeth | {result.Pathologies.Count} Pathologies");
                Console.ResetColor();
                
                if (result.Pathologies.Count > 0)
                {
                    Console.WriteLine("   Detections:");
                    foreach (var p in result.Pathologies)
                    {
                        Console.WriteLine($"   - {p.ClassName} ({p.Confidence:P1}) on Tooth #{p.ToothNumber}");
                    }
                }
                else
                {
                    Console.WriteLine("   No pathologies detected.");
                }
                
                successCount++;
                totalProcessingTime += result.ProcessingTimeMs;
                Console.WriteLine();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[FAIL] {result.Error}");
                Console.ResetColor();
                failureCount++;
            }
        }

        // Display summary statistics
        Console.WriteLine("----------------------------------------------");
        Console.WriteLine("Summary Statistics:");
        Console.WriteLine($"  Total Images: {files.Length}");
        Console.WriteLine($"  Successful: {successCount}");
        Console.WriteLine($"  Failed: {failureCount}");
        Console.WriteLine($"  Total Wall Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Total Processing Time: {totalProcessingTime}ms");
        if (successCount > 0)
        {
            Console.WriteLine($"  Average Processing Time: {totalProcessingTime / successCount}ms");
            Console.WriteLine($"  Throughput: {successCount / (sw.ElapsedMilliseconds / 1000.0):F2} images/second");
        }
        Console.WriteLine("Done.");
    }
}
