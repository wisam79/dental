using Xunit;
using DentalID.Application.Services;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Application.Interfaces;
using Moq;

namespace DentalID.Tests.Services;

public class PerformanceTests
{
    [Fact]
    public async Task InferenceService_ShouldLimitConcurrency()
    {
        // Arrange
        // We can't easily mock the internal SemaphoreSlim without exposing it or using reflection,
        // but we can test the behavior by firing multiple tasks and checking if they finish in batches.
        // However, since AnalyzeImageAsync does heavy work, a unit test might be slow.
        // Instead, let's verify the Lock field exists and has correct initial count via reflection.
        
        var config = new AiConfiguration();
        var logger = new Mock<ILoggerService>();
        var bio = new BiometricService();
        var intelligence = new Mock<IDentalIntelligenceService>().Object;
        var cache = new Mock<ICacheService>();
        var integrity = new Mock<IImageIntegrityService>();
        var aiSettings = new AiSettings();
        var fdiService = new FdiSpatialService();
        var heuristicsService = new ForensicHeuristicsService();
        var yoloParser = new YoloDetectionParser(config, aiSettings, fdiService);
        var service = new OnnxInferenceService(config, aiSettings, logger.Object, bio, intelligence, cache.Object, yoloParser, fdiService, heuristicsService, new Mock<ITensorPreparationService>().Object, integrity.Object);
        var field = typeof(OnnxInferenceService).GetField("_inferenceLock", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var semaphore = field?.GetValue(service) as System.Threading.SemaphoreSlim;

        // Assert
        Assert.NotNull(semaphore);
        Assert.Equal(1, semaphore.CurrentCount); // Hardened service allows 1 concurrent request
        await Task.CompletedTask;
    }

    [Fact]
    public void MatchingService_ShouldHandleLargeBatchesWithoutException()
    {
        // Arrange
        var mockBio = new Mock<IBiometricService>();
        var service = new MatchingService(mockBio.Object);
        int vectorSize = 2048;
        int batchSize = 10000;
        
        var queryVector = new float[vectorSize];
        Array.Fill(queryVector, 0.5f);

        var databaseVectors = new List<float[]>();
        for (int i = 0; i < batchSize; i++)
        {
            var v = new float[vectorSize];
            Array.Fill(v, 0.5f);
            databaseVectors.Add(v);
        }

        // Act & Assert
        // Should execute within reasonable time (e.g. < 1s for 10k items)
        var sw = Stopwatch.StartNew();
        
        foreach(var target in databaseVectors)
        {
            _ = service.CalculateCosineSimilarity(queryVector, target);
        }
        
        sw.Stop();
        
        // 10,000 matches of 2048-dim vectors should be blazing fast with SIMD
        // Usually < 100ms on modern CPU. Let's be generous with 1000ms for CI environments.
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Matching too slow: {sw.ElapsedMilliseconds}ms");
    }
}
