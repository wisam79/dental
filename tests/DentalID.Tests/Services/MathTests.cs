using Xunit;
using DentalID.Application.Services;
using System.Reflection;
using DentalID.Application.Configuration;
using DentalID.Core.Interfaces;
using DentalID.Application.Interfaces;
using Moq;

namespace DentalID.Tests.Services;

public class MathTests
{
    private readonly OnnxInferenceService _service;

    public MathTests()
    {
        var config = new AiConfiguration();
        var aiSettings = new AiSettings();
        var logger = new Mock<ILoggerService>();
        var bio = new BiometricService();
        var cache = new Mock<ICacheService>();
        var integrity = new Mock<IImageIntegrityService>();
        // Nulls for optional services (Integrity, Rules)
        var intelligence = new Mock<IDentalIntelligenceService>().Object;
        var fdiService = new FdiSpatialService();
        var heuristicsService = new ForensicHeuristicsService();
        var yoloParser = new YoloDetectionParser(config, aiSettings, fdiService);
        _service = new OnnxInferenceService(
            config, 
            aiSettings,
            logger.Object, 
            bio, 
            intelligence, 
            cache.Object, 
            yoloParser,
            fdiService,
            heuristicsService,
            new Mock<ITensorPreparationService>().Object,
            integrity.Object
        );
    }

    private float CallCalculateIoU(float x1, float y1, float w1, float h1, float x2, float y2, float w2, float h2)
    {
        var method = typeof(OnnxInferenceService).GetMethod(
            "CalculateIoU",
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        return (float)method!.Invoke(_service, new object[] { x1, y1, w1, h1, x2, y2, w2, h2 })!;
    }

    [Fact]
    public void CalculateIoU_BoxInsideBox_ReturnsCorrectRatio()
    {
        // Big Box: 0,0 10x10 (Area 100)
        // Small Box: 0,0 5x5 (Area 25)
        // Intersection: 25
        // Union: 100
        // IoU: 0.25
        
        float iou = CallCalculateIoU(0, 0, 10, 10, 0, 0, 5, 5);
        Assert.Equal(0.25f, iou);
    }

    [Fact]
    public void CalculateIoU_TouchingEdges_ReturnsZero()
    {
        // Box 1: 0,0 10x10 (Ends at x=10)
        // Box 2: 10,0 10x10 (Starts at x=10)
        // Intersection width = 0
        
        float iou = CallCalculateIoU(0, 0, 10, 10, 10, 0, 10, 10);
        Assert.Equal(0f, iou);
    }

    [Fact]
    public void CalculateIoU_PartialOverlap_ReturnsCorrectRatio()
    {
        // Box 1: 0,0 2x2 (Area 4)
        // Box 2: 1,0 2x2 (Area 4)
        // Intersection: 1x2 = 2
        // Union: 4 + 4 - 2 = 6
        // IoU: 2/6 = 0.333...
        
        float iou = CallCalculateIoU(0, 0, 2, 2, 1, 0, 2, 2);
        Assert.Equal(1f/3f, iou, precision: 4);
    }
}
