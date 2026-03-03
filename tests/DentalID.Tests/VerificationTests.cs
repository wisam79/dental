using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using DentalID.Application.Services;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Application.Interfaces;

namespace DentalID.Tests
{
    public class VerificationTests
    {
        private readonly ITestOutputHelper _output;

        public VerificationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task VerifyImageAnalysis_Integration()
        {
            // 1. Locate Resources (Dynamic Path Finding)
            // We need to find "Panoramic Dental Xray Dataset/1.jpg"
            // Start from BaseDirectory and go up until we find the dataset folder.
            var searchDir = AppContext.BaseDirectory;
            string? projectRoot = null;
            for (int i = 0; i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(searchDir, "Panoramic Dental Xray Dataset")))
                {
                    projectRoot = searchDir;
                    break;
                }
                var parent = Directory.GetParent(searchDir);
                if (parent == null) break;
                searchDir = parent.FullName;
            }

            if (projectRoot == null)
            {
                _output.WriteLine("WARNING: Could not find 'Panoramic Dental Xray Dataset'. Integration test skipped.");
                return; // Skip if dataset missing (e.g. CI environment)
            }

            var imagePath = Path.Combine(projectRoot, "Panoramic Dental Xray Dataset", "1.jpg");
            var modelsDir = Path.Combine(projectRoot, "models");

            if (!File.Exists(imagePath))
            {
                _output.WriteLine($"Image not found at {imagePath}. Test Skipped.");
                return;
            }

            _output.WriteLine($"Using Image: {imagePath}");
            _output.WriteLine($"Using Models: {modelsDir}");

            // 2. Initialize Service with Real Rules Engine
            // Note: We use the real ForensicRulesEngine to verify the full stack
            var rulesEngine = new ForensicRulesEngine();
            var bio = new BiometricService();
            var cache = new DummyCacheService();
            // config, logger, biometric, intelligence, cache, integrity
            var intelligence = new DentalIntelligenceService();
            var aiConfig = new AiConfiguration();
            var aiSettings = new AiSettings();
            var fdiService = new FdiSpatialService();
            var heuristicsService = new ForensicHeuristicsService();
            var yoloParser = new YoloDetectionParser(aiConfig, aiSettings, fdiService);
            var tensorPrep = new TensorPreparationService();
            var sessionManager = new OnnxSessionManager(aiConfig, aiSettings, new Infrastructure.Services.LogService());
            var teethSvc = new TeethDetectionService(sessionManager, yoloParser, fdiService, heuristicsService, tensorPrep, aiConfig, aiSettings);
            var pathSvc  = new PathologyDetectionService(sessionManager, yoloParser, tensorPrep, aiConfig, aiSettings);
            var encoderSvc = new FeatureEncoderService(sessionManager, tensorPrep, aiConfig, new Infrastructure.Services.LogService());
            var samSvc = new SamSegmentationService(sessionManager, new Microsoft.Extensions.Logging.Abstractions.NullLogger<SamSegmentationService>());
            var service = new OnnxInferenceService(sessionManager, teethSvc, pathSvc, encoderSvc, samSvc, yoloParser, heuristicsService, intelligence, bio, cache, new Infrastructure.Services.LogService());
            
            await service.InitializeAsync(modelsDir);

            // 3. Run Analysis
            using var stream = File.OpenRead(imagePath);
            var result = await service.AnalyzeImageAsync(stream);

            // 4. Validate Results
            _output.WriteLine($"Processing Time: {result.ProcessingTimeMs} ms");
            
            if (!string.IsNullOrEmpty(result.Error))
            {
                _output.WriteLine($"ERROR: {result.Error}");
            }
            else
            {
                _output.WriteLine($"Success! Detected {result.Teeth?.Count ?? 0} teeth.");
                _output.WriteLine($"Rules Flags: {result.Flags.Count}");
                foreach(var flag in result.Flags) _output.WriteLine($" - {flag}");
            }

            // Assertions
            Assert.True(result.IsSuccess, $"Analysis failed with error: {result.Error}");
            Assert.NotNull(result.Teeth);
            Assert.NotNull(result.Pathologies);
        }

        private class DummyCacheService : ICacheService
        {
            public T? Get<T>(string key) where T : class => null;
            public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class { }
            public void Remove(string key) { }
            public bool Exists(string key) => false;
            public void Clear() { }
            public Task<T?> GetAsync<T>(string key, System.Threading.CancellationToken cancellationToken = default) where T : class
            {
                return Task.FromResult(Get<T>(key));
            }
            public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, System.Threading.CancellationToken cancellationToken = default) where T : class
            {
                Set(key, value, expiration);
                return Task.CompletedTask;
            }
            public Task RemoveAsync(string key, System.Threading.CancellationToken cancellationToken = default)
            {
                Remove(key);
                return Task.CompletedTask;
            }
            public Task<bool> ExistsAsync(string key, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Exists(key));
            }
            public Task ClearAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                Clear();
                return Task.CompletedTask;
            }
        }
    }
}
