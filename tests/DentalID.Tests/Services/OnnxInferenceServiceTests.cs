using Xunit;
using DentalID.Application.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.IO;
using SkiaSharp;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;

namespace DentalID.Tests.Services;

public class OnnxInferenceServiceTests
{
    private readonly Mock<ILoggerService>             _mockLogger;
    private readonly Mock<IBiometricService>          _mockBiometric;
    private readonly Mock<IDentalIntelligenceService> _mockIntelligence;
    private readonly Mock<ICacheService>              _mockCache;
    private readonly Mock<IImageIntegrityService>     _mockIntegrity;
    private readonly Mock<IOnnxSessionManager>        _mockSession;
    private readonly Mock<ITeethDetectionService>     _mockTeeth;
    private readonly Mock<IPathologyDetectionService> _mockPath;
    private readonly Mock<IFeatureEncoderService>     _mockEncoder;
    private readonly Mock<IYoloDetectionParser>       _mockYolo;
    private readonly Mock<IForensicHeuristicsService> _mockHeuristics;

    public OnnxInferenceServiceTests()
    {
        _mockLogger       = new Mock<ILoggerService>();
        _mockBiometric    = new Mock<IBiometricService>();
        _mockIntelligence = new Mock<IDentalIntelligenceService>();
        _mockCache        = new Mock<ICacheService>();
        _mockIntegrity    = new Mock<IImageIntegrityService>();
        _mockSession      = new Mock<IOnnxSessionManager>();
        _mockTeeth        = new Mock<ITeethDetectionService>();
        _mockPath         = new Mock<IPathologyDetectionService>();
        _mockEncoder      = new Mock<IFeatureEncoderService>();
        _mockYolo         = new Mock<IYoloDetectionParser>();
        _mockHeuristics   = new Mock<IForensicHeuristicsService>();

        // Default plumbing
        _mockSession.Setup(s => s.InferenceLock).Returns(new SemaphoreSlim(1, 1));
        _mockSession.Setup(s => s.IsReady).Returns(false);
        _mockTeeth.Setup(s => s.DetectTeeth(It.IsAny<SkiaSharp.SKBitmap>()))
                  .Returns(new List<DetectedTooth>());
        _mockPath.Setup(s => s.DetectPathologies(It.IsAny<SkiaSharp.SKBitmap>()))
                 .Returns(new List<DetectedPathology>());
        _mockBiometric.Setup(s => s.GenerateFingerprint(It.IsAny<List<DetectedTooth>>(), It.IsAny<List<DetectedPathology>>()))
                      .Returns(new DentalFingerprint());
    }

    private OnnxInferenceService CreateService() => new(
        _mockSession.Object,
        _mockTeeth.Object,
        _mockPath.Object,
        _mockEncoder.Object,
        _mockYolo.Object,
        _mockHeuristics.Object,
        _mockIntelligence.Object,
        _mockBiometric.Object,
        _mockCache.Object,
        _mockLogger.Object,
        _mockIntegrity.Object
    );

    [Fact]
    public void Constructor_Throws_WhenSessionManagerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new OnnxInferenceService(
            null!,
            _mockTeeth.Object,
            _mockPath.Object,
            _mockEncoder.Object,
            _mockYolo.Object,
            _mockHeuristics.Object,
            _mockIntelligence.Object,
            _mockBiometric.Object,
            _mockCache.Object,
            _mockLogger.Object
        ));
    }

    [Fact]
    public void Constructor_Throws_WhenLoggerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new OnnxInferenceService(
            _mockSession.Object,
            _mockTeeth.Object,
            _mockPath.Object,
            _mockEncoder.Object,
            _mockYolo.Object,
            _mockHeuristics.Object,
            _mockIntelligence.Object,
            _mockBiometric.Object,
            _mockCache.Object,
            null!
        ));
    }

    [Fact]
    public async Task AnalyzeImageAsync_ReturnsCachedResult_WhenAvailable()
    {
        var service = CreateService();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var cachedResult = new AnalysisResult
        {
            Teeth = new List<DetectedTooth> { new DetectedTooth { FdiNumber = 11 } }
        };

        _mockIntegrity.Setup(x => x.ComputeHash(It.IsAny<Stream>())).Returns("dummy_hash");
        _mockCache.Setup(x => x.Exists("analysis_dummy_hash")).Returns(true);
        _mockCache.Setup(x => x.Get<AnalysisResult>("analysis_dummy_hash")).Returns(cachedResult);

        var result = await service.AnalyzeImageAsync(stream);

        Assert.Same(cachedResult, result);
        _mockCache.Verify(x => x.Get<AnalysisResult>("analysis_dummy_hash"), Times.Once);
        Assert.Equal(0, result.ProcessingTimeMs);
    }

    [Fact]
    public async Task AnalyzeImageAsync_ThrowsOrReturnsError_WhenNotInitialized()
    {
        var service = CreateService();
        _mockSession.Setup(s => s.IsReady).Returns(false);

        // No IntegrityService → skip cache check, go straight to lock + ready check
        var noIntegrityService = new OnnxInferenceService(
            _mockSession.Object, _mockTeeth.Object, _mockPath.Object, _mockEncoder.Object,
            _mockYolo.Object, _mockHeuristics.Object, _mockIntelligence.Object,
            _mockBiometric.Object, _mockCache.Object, _mockLogger.Object);

        using var stream = CreateTestBitmapStream();
        var result = await noIntegrityService.AnalyzeImageAsync(stream);

        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    private static MemoryStream CreateTestBitmapStream()
    {
        using var bmp    = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        var ms = new MemoryStream();
        bmp.Encode(ms, SKEncodedImageFormat.Png, 100);
        ms.Position = 0;
        return ms;
    }
}
