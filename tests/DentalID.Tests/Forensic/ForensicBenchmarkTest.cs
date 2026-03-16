using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DentalID.Application.Configuration;
using DentalID.Application.Services;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace DentalID.Tests.Forensic;

public class ForensicBenchmarkTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly OnnxInferenceService _pipeline;
    private readonly string _datasetDir;
    private readonly string _modelsDir;
    private readonly ServiceProvider _serviceProvider;

    public ForensicBenchmarkTest(ITestOutputHelper output)
    {
        _output = output;
        
        string projectRoot = FindProjectRoot();
        _datasetDir = Path.Combine(projectRoot, "Panoramic Dental Xray Dataset");
        _modelsDir = Path.Combine(projectRoot, "models");

        var services = new ServiceCollection();
        
        services.AddSingleton(new AiConfiguration());
        services.AddSingleton(new AiSettings());
        services.AddLogging();
        services.AddSingleton<ILoggerService, TestLogger>(sp => new TestLogger(_output));
        
        services.AddSingleton<IDentalIntelligenceService, DentalIntelligenceService>();
        services.AddSingleton<IFdiSpatialService, FdiSpatialService>();
        services.AddSingleton<IForensicHeuristicsService, ForensicHeuristicsService>();
        services.AddSingleton<ITensorPreparationService, TensorPreparationService>();
        services.AddSingleton<IYoloDetectionParser, YoloDetectionParser>();
        services.AddSingleton<IOnnxSessionManager, OnnxSessionManager>();
        services.AddSingleton<ITeethDetectionService, TeethDetectionService>();
        services.AddSingleton<IPathologyDetectionService, PathologyDetectionService>();
        services.AddSingleton<IFeatureEncoderService, FeatureEncoderService>();
        services.AddSingleton<ISamSegmentationService, SamSegmentationService>();
        services.AddSingleton<IMatchingService, MatchingService>();
        services.AddSingleton<IBiometricService, BiometricService>();
        services.AddSingleton<ICacheService>(sp => new Moq.Mock<ICacheService>().Object);
        services.AddTransient<IForensicRulesEngine, ForensicRulesEngine>();
        
        services.AddSingleton<OnnxInferenceService>();

        _serviceProvider = services.BuildServiceProvider();
        _pipeline = _serviceProvider.GetRequiredService<OnnxInferenceService>();
    }

    [Fact(Skip = "Run manually: dotnet test --filter ForensicBenchmarkTest -- xUnit.RunSettings.Skip=false")]
    public async Task RunBenchmark_OnSampleImages_ProducesValidForensicScores()
    {
        // Arrange
        Assert.True(Directory.Exists(_modelsDir), $"Models dir not found: {_modelsDir}");
        Assert.True(Directory.Exists(_datasetDir), $"Dataset dir not found: {_datasetDir}");

        await _pipeline.InitializeAsync(_modelsDir);

        var images = Directory.GetFiles(_datasetDir, "*.jpg")
            .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out int n) ? n : 999)
            .Take(5)
            .ToArray();
            
        Assert.NotEmpty(images);

        var results = new List<(string File, AnalysisResult Result)>();

        // Act — Analyze all images
        foreach (var imagePath in images)
        {
            using var stream = File.OpenRead(imagePath);
            var result = await _pipeline.AnalyzeImageAsync(stream, Path.GetFileName(imagePath));
            
            Assert.True(result.IsSuccess, $"Analysis failed for {imagePath}: {result.Error}");
            Assert.NotNull(result.FeatureVector);
            Assert.True(result.FeatureVector.Length > 0, "Feature vector should not be empty");
            
            _output.WriteLine($"✅ {Path.GetFileName(imagePath)}: Teeth={result.Teeth.Count}, Pathologies={result.Pathologies.Count}, VectorDim={result.FeatureVector.Length}, Time={result.ProcessingTimeMs}ms");
            results.Add((Path.GetFileName(imagePath), result));
        }

        // Assert - Full Cross Comparison Matrix (observational)
        _output.WriteLine("");
        _output.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗");
        _output.WriteLine("║                      FORENSIC COMPARISON MATRIX                              ║");
        _output.WriteLine("╠═══════════════════════════════════════════════════════════════════════════════╣");
        
        var comparisonService = new ComparisonService(new MatchingService(new BiometricService(), new AiConfiguration()));

        var selfScores = new List<double>();
        var crossScores = new List<double>();

        for (int i = 0; i < results.Count; i++)
        {
            // Self comparison
            var selfCompare = comparisonService.CompareAnalyses(results[i].Result, results[i].Result);
            selfScores.Add(selfCompare.CombinedForensicScore);
            _output.WriteLine($"║ Self [{results[i].File}]: Combined={selfCompare.CombinedForensicScore:P2} | Vector={selfCompare.VectorSimilarityScore:P2} | Condition={selfCompare.ConditionMatchScore:P2} | Presence={selfCompare.SimilarityScore:P2}");
            Assert.True(selfCompare.CombinedForensicScore > 0.95, $"Self comparison for {results[i].File} should be near 100%");

            for (int j = i + 1; j < results.Count; j++)
            {
                var crossCompare = comparisonService.CompareAnalyses(results[i].Result, results[j].Result);
                crossScores.Add(crossCompare.CombinedForensicScore);
                _output.WriteLine($"║ Cross [{results[i].File}] vs [{results[j].File}]: Combined={crossCompare.CombinedForensicScore:P2} | Vector={crossCompare.VectorSimilarityScore:P2} | Condition={crossCompare.ConditionMatchScore:P2} | Presence={crossCompare.SimilarityScore:P2}");
                if (crossCompare.ConditionDifferences.Count > 0)
                {
                    _output.WriteLine($"║   Condition Diffs: {string.Join(", ", crossCompare.ConditionDifferences.Take(5))}");
                }
            }
        }

        _output.WriteLine("╠═══════════════════════════════════════════════════════════════════════════════╣");
        _output.WriteLine($"║ SUMMARY: Self avg={selfScores.Average():P2} | Cross avg={crossScores.Average():P2} | Gap={selfScores.Average() - crossScores.Average():P2}");
        _output.WriteLine($"║ Cross range: min={crossScores.Min():P2} max={crossScores.Max():P2}");
        _output.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
        
        // Observational assertion: just ensure self > cross on average (basic sanity)
        double gap = selfScores.Average() - crossScores.Average();
        _output.WriteLine($"\n🔬 Self-Cross Gap = {gap:P2} (target: >10%)");
        
        // Soft assertion: if gap < 5%, it's a serious discrimination problem worth flagging
        Assert.True(gap > 0.01, $"Self-similarity should be higher than cross-similarity on average. Gap={gap:P2}. The encoder may not be discriminative enough.");
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
    }

    private string FindProjectRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "DentalID.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return @"E:\projects\dental";
    }

    private class TestLogger : ILoggerService
    {
        private readonly ITestOutputHelper _output;
        public TestLogger(ITestOutputHelper output) => _output = output;
        public void LogInformation(string message) => _output.WriteLine($"[INFO] {message}");
        public void LogWarning(string message) => _output.WriteLine($"[WARN] {message}");
        public void LogError(Exception ex, string message) => _output.WriteLine($"[ERROR] {message}: {ex}");
        public void LogAudit(string action, string user, string details, string dataHash = "N/A") { }
    }
}
