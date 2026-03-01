using Xunit;
using DentalID.Application.Services;
using DentalID.Application.Interfaces;
using System.Reflection;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using Moq;
using System.Collections.Generic;

namespace DentalID.Tests.Services;

/// <summary>
/// Tests the CalculateIoU shim retained on the facade, plus related math invariants.
/// IoU logic now lives in <see cref="ForensicHeuristicsService.CalculateIoU"/> (static),
/// so we call the instance shim via reflection to stay backward compatible with existing tests.
/// </summary>
public class MathTests
{
    private readonly OnnxInferenceService _service;

    public MathTests()
    {
        var mockSession      = new Mock<IOnnxSessionManager>();
        var mockTeeth        = new Mock<ITeethDetectionService>();
        var mockPath         = new Mock<IPathologyDetectionService>();
        var mockEncoder      = new Mock<IFeatureEncoderService>();
        var mockYolo         = new Mock<IYoloDetectionParser>();
        var mockHeuristics   = new Mock<IForensicHeuristicsService>();
        var mockIntelligence = new Mock<IDentalIntelligenceService>();
        var mockBiometric    = new Mock<IBiometricService>();
        var mockCache        = new Mock<ICacheService>();
        var mockLogger       = new Mock<ILoggerService>();

        mockSession.Setup(s => s.InferenceLock).Returns(new SemaphoreSlim(1, 1));
        mockSession.Setup(s => s.IsReady).Returns(false);
        mockTeeth.Setup(s => s.DetectTeeth(It.IsAny<SkiaSharp.SKBitmap>()))
                 .Returns(new List<DetectedTooth>());
        mockPath.Setup(s => s.DetectPathologies(It.IsAny<SkiaSharp.SKBitmap>()))
                .Returns(new List<DetectedPathology>());
        mockBiometric.Setup(s => s.GenerateFingerprint(It.IsAny<List<DetectedTooth>>(), It.IsAny<List<DetectedPathology>>()))
                     .Returns(new DentalFingerprint());

        _service = new OnnxInferenceService(
            mockSession.Object, mockTeeth.Object, mockPath.Object, mockEncoder.Object,
            mockYolo.Object, mockHeuristics.Object, mockIntelligence.Object,
            mockBiometric.Object, mockCache.Object, mockLogger.Object);
    }

    private float CallCalculateIoU(float x1, float y1, float w1, float h1,
                                   float x2, float y2, float w2, float h2)
    {
        var method = typeof(OnnxInferenceService).GetMethod(
            "CalculateIoU",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (float)method!.Invoke(_service, new object[] { x1, y1, w1, h1, x2, y2, w2, h2 })!;
    }

    [Fact]
    public void CalculateIoU_BoxInsideBox_ReturnsCorrectRatio()
    {
        // Big Box: 0,0 10x10 (Area 100); Small: 0,0 5x5 (Area 25)
        // Intersection 25, Union 100 → IoU 0.25
        float iou = CallCalculateIoU(0, 0, 10, 10, 0, 0, 5, 5);
        Assert.Equal(0.25f, iou);
    }

    [Fact]
    public void CalculateIoU_TouchingEdges_ReturnsZero()
    {
        // Box1 ends at x=10; Box2 starts at x=10 → Intersection width 0
        float iou = CallCalculateIoU(0, 0, 10, 10, 10, 0, 10, 10);
        Assert.Equal(0f, iou);
    }

    [Fact]
    public void CalculateIoU_PartialOverlap_ReturnsCorrectRatio()
    {
        // Box1: 0,0 2x2 (4); Box2: 1,0 2x2 (4). Intersection 1x2=2. Union 6. IoU 1/3.
        float iou = CallCalculateIoU(0, 0, 2, 2, 1, 0, 2, 2);
        Assert.Equal(1f / 3f, iou, precision: 4);
    }
}
