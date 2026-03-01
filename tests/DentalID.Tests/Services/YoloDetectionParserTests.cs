using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

public class YoloDetectionParserTests
{
    private readonly Mock<IFdiSpatialService> _mockFdiService;
    private readonly AiConfiguration _config;
    private readonly AiSettings _aiSettings;
    private readonly YoloDetectionParser _parser;

    public YoloDetectionParserTests()
    {
        _mockFdiService = new Mock<IFdiSpatialService>();
        _config = new AiConfiguration
        {
            FdiMapping = new FdiMappingSettings { ClassMap = new int[] { 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 42, 43 } },
            Thresholds = new ThresholdSettings 
            { 
                ProximityThreshold = 0.1f,
                PathologyThresholds = new Dictionary<string, float> { { "Caries", 0.5f } }
            }
        };
        _aiSettings = new AiSettings { IouThreshold = 0.5f };
        
        _parser = new YoloDetectionParser(_config, _aiSettings, _mockFdiService.Object);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependenciesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new YoloDetectionParser(null!, _aiSettings, _mockFdiService.Object));
        Assert.Throws<ArgumentNullException>(() => new YoloDetectionParser(_config, null!, _mockFdiService.Object));
        Assert.Throws<ArgumentNullException>(() => new YoloDetectionParser(_config, _aiSettings, null!));
    }

    [Fact]
    public void ParseTeethDetections_ShouldReturnEmpty_WhenOutputInvalid()
    {
        var output = new DenseTensor<float>(new[] { 1, 40 }); // Wrong dims
        var result = _parser.ParseTeethDetections(output, 640, 1, 0, 0, 0.5f);
        result.Should().BeEmpty();
    }

    private Tensor<float> CreateYoloOutput(int numPredictions, int numClasses, List<(int cls, float conf, float x, float y, float w, float h)> detections)
    {
        // YOLO output: [1, 4 + numClasses, numPredictions]
        // Channels: x, y, w, h, class0_conf, class1_conf...
        var tensor = new DenseTensor<float>(new[] { 1, 4 + numClasses, numPredictions });
        
        for (int i = 0; i < numPredictions; i++)
        {
            if (i < detections.Count)
            {
                var (cls, conf, x, y, w, h) = detections[i];
                tensor[0, 0, i] = x;
                tensor[0, 1, i] = y;
                tensor[0, 2, i] = w;
                tensor[0, 3, i] = h;
                
                // Set confidence for the class
                if (cls < numClasses)
                {
                   tensor[0, 4 + cls, i] = conf;
                }
            }
        }
        return tensor;
    }

    [Fact]
    public void ParseTeethDetections_ShouldParseAndFilter_Correctly()
    {
        // Setup mock FDI to pass through
        _mockFdiService.Setup(s => s.RefineFdiNumbering(It.IsAny<List<DetectedTooth>>()))
            .Returns((List<DetectedTooth> list) => list);

        // 2 classes (11, 12). 2 predictions.
        // Pred 1: Class 0 (11), Conf 0.9, Box...
        var detections = new List<(int, float, float, float, float, float)>
        {
            (0, 0.9f, 320, 320, 50, 50), // Center
            (1, 0.2f, 100, 100, 50, 50)  // Low confidence
        };
        
        var tensor = CreateYoloOutput(2, 2, detections);
        
        var result = _parser.ParseTeethDetections(tensor, 640, 1, 0, 0, 0.5f);

        result.Should().HaveCount(1);
        result[0].FdiNumber.Should().Be(11);
        result[0].Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void ApplyNms_ShouldSuppressOverlappingDetections()
    {
        var overlappingTeeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 11, Confidence = 0.9f, X = 0.5f, Y = 0.5f, Width = 0.1f, Height = 0.1f },
            new DetectedTooth { FdiNumber = 11, Confidence = 0.8f, X = 0.51f, Y = 0.51f, Width = 0.1f, Height = 0.1f } // High overlap
        };

        var result = _parser.ApplyNms(overlappingTeeth, 0.5f);

        result.Should().HaveCount(1);
        result[0].Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void MapPathologiesToTeeth_ShouldLinkBasedOnIoU()
    {
        var tooth = new DetectedTooth { FdiNumber = 11, X = 100, Y = 100, Width = 50, Height = 50 };
        var pathology = new DetectedPathology { ClassName = "Caries", X = 110, Y = 110, Width = 20, Height = 20 }; // Inside tooth

        var teeth = new List<DetectedTooth> { tooth };
        var pathologies = new List<DetectedPathology> { pathology };

        _parser.MapPathologiesToTeeth(teeth, pathologies);

        pathology.ToothNumber.Should().Be(11);
    }

    [Fact]
    public void MapPathologiesToTeeth_ShouldNotMapCaries_WhenOnlyFarProximity()
    {
        var tooth = new DetectedTooth { FdiNumber = 11, X = 0.40f, Y = 0.40f, Width = 0.10f, Height = 0.10f };
        var pathology = new DetectedPathology
        {
            ClassName = "Caries",
            X = 0.54f,
            Y = 0.40f,
            Width = 0.05f,
            Height = 0.05f
        };

        var teeth = new List<DetectedTooth> { tooth };
        var pathologies = new List<DetectedPathology> { pathology };

        _parser.MapPathologiesToTeeth(teeth, pathologies);

        pathology.ToothNumber.Should().BeNull();
    }

    [Fact]
    public void MapPathologiesToTeeth_ShouldAllowMissingTeethProximityFallback_WhenClose()
    {
        var tooth = new DetectedTooth { FdiNumber = 36, X = 0.45f, Y = 0.55f, Width = 0.08f, Height = 0.10f };
        var pathology = new DetectedPathology
        {
            ClassName = "Missing teeth",
            X = 0.52f,
            Y = 0.60f,
            Width = 0.05f,
            Height = 0.06f
        };

        var teeth = new List<DetectedTooth> { tooth };
        var pathologies = new List<DetectedPathology> { pathology };

        _parser.MapPathologiesToTeeth(teeth, pathologies);

        pathology.ToothNumber.Should().Be(36);
    }

    [Fact]
    public void ParseTeethDetections_ShouldCapResultsTo32()
    {
        var config = new AiConfiguration
        {
            FdiMapping = new FdiMappingSettings { ClassMap = Enumerable.Range(11, 40).ToArray() },
            Thresholds = new ThresholdSettings { TeethThreshold = 0.4f }
        };
        var parser = new YoloDetectionParser(config, new AiSettings { IouThreshold = 0.5f }, _mockFdiService.Object);
        _mockFdiService.Setup(s => s.RefineFdiNumbering(It.IsAny<List<DetectedTooth>>()))
            .Returns((List<DetectedTooth> list) => list);

        var detections = Enumerable.Range(0, 40)
            .Select(i => (i, 0.9f, 20f + (i * 15f), 300f, 12f, 30f))
            .ToList();

        var tensor = CreateYoloOutput(40, 40, detections);
        var result = parser.ParseTeethDetections(tensor, 640, 1, 0, 0, 0.5f);

        result.Should().HaveCount(32);
    }

    [Fact]
    public void ParseTeethDetections_ShouldKeepHighestConfidencePerFdi()
    {
        var config = new AiConfiguration
        {
            FdiMapping = new FdiMappingSettings { ClassMap = new[] { 11 } },
            Thresholds = new ThresholdSettings { TeethThreshold = 0.4f }
        };
        var parser = new YoloDetectionParser(config, new AiSettings { IouThreshold = 0.5f }, _mockFdiService.Object);
        _mockFdiService.Setup(s => s.RefineFdiNumbering(It.IsAny<List<DetectedTooth>>()))
            .Returns((List<DetectedTooth> list) => list);

        var detections = new List<(int, float, float, float, float, float)>
        {
            (0, 0.9f, 120f, 300f, 24f, 40f),
            (0, 0.7f, 500f, 300f, 24f, 40f)
        };

        var tensor = CreateYoloOutput(2, 1, detections);
        var result = parser.ParseTeethDetections(tensor, 640, 1, 0, 0, 0.5f);

        result.Should().HaveCount(1);
        result[0].FdiNumber.Should().Be(11);
        result[0].Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void ParseTeethDetections_ShouldUseSpatialRescue_WhenDirectCoverageIsSparse()
    {
        var rescuedFdi = new[] { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24 };
        _mockFdiService.Setup(s => s.RefineFdiNumbering(It.IsAny<List<DetectedTooth>>()))
            .Returns((List<DetectedTooth> list) =>
            {
                return list.Select((t, i) => new DetectedTooth
                {
                    FdiNumber = rescuedFdi[Math.Min(i, rescuedFdi.Length - 1)],
                    Confidence = t.Confidence,
                    X = t.X,
                    Y = t.Y,
                    Width = t.Width,
                    Height = t.Height
                }).ToList();
            });

        var detections = Enumerable.Range(0, 12)
            .Select(i => (0, 0.86f, 60f + (i * 44f), 320f, 24f, 40f))
            .ToList();

        var tensor = CreateYoloOutput(12, 12, detections);
        var result = _parser.ParseTeethDetections(tensor, 640, 1, 0, 0, 0.5f);

        result.Should().HaveCount(12);
        result.Select(t => t.FdiNumber).Distinct().Should().HaveCount(12);
    }

    [Fact]
    public void ParseTeethDetections_ShouldSupplementMissingFdi_WhenCoverageAlreadyHigh()
    {
        var config = new AiConfiguration
        {
            FdiMapping = new FdiMappingSettings
            {
                ClassMap = new[]
                {
                    11, 12, 13, 14, 15, 16, 17, 18,
                    21, 22, 23, 24, 25, 26, 27, 28,
                    31, 32, 33, 34, 35, 36, 37, 38,
                    41, 42, 43, 44, 45, 46, 47, 48
                }
            },
            Thresholds = new ThresholdSettings { TeethThreshold = 0.35f }
        };
        var parser = new YoloDetectionParser(config, new AiSettings { IouThreshold = 0.5f }, _mockFdiService.Object);
        _mockFdiService.Setup(s => s.RefineFdiNumbering(It.IsAny<List<DetectedTooth>>()))
            .Returns((List<DetectedTooth> list) => list);

        // Build 30 strong unique classes (0..29), so we are already above coverage target (28)
        // but still missing classes 30->47 and 31->48.
        var detections = Enumerable.Range(0, 30)
            .Select(i => (i, 0.9f, 20f + (i * 20f), 300f, 14f, 32f))
            .ToList();

        var tensor = CreateYoloOutput(30, 32, detections);

        // Inject moderate evidence for missing classes on existing anchors.
        // These are below main threshold 0.35 (thus not direct best-class picks),
        // but above dense coverage supplement floor 0.20.
        tensor[0, 4 + 30, 0] = 0.24f; // class 30 => FDI 47
        tensor[0, 4 + 31, 1] = 0.22f; // class 31 => FDI 48

        var result = parser.ParseTeethDetections(tensor, 640, 1, 0, 0, 0.5f);

        result.Should().Contain(t => t.FdiNumber == 47);
        result.Should().Contain(t => t.FdiNumber == 48);
        result.Select(t => t.FdiNumber).Distinct().Should().HaveCount(32);
    }

    [Fact]
    public void ParsePathologyDetections_ShouldNormalizeLogitsToProbabilities()
    {
        var tensor = new DenseTensor<float>(new[] { 1, 5, 1 }); // [1,4+1,1]
        tensor[0, 0, 0] = 320f;
        tensor[0, 1, 0] = 320f;
        tensor[0, 2, 0] = 50f;
        tensor[0, 3, 0] = 50f;
        tensor[0, 4, 0] = 2.2f; // logit (outside [0,1])

        var result = _parser.ParsePathologyDetections(
            tensor,
            640,
            1,
            0,
            0,
            0.5f,
            new[] { "Caries" });

        result.Should().HaveCount(1);
        result[0].Confidence.Should().BeGreaterThan(0.5f);
        result[0].Confidence.Should().BeLessThanOrEqualTo(1.0f);
    }

    [Fact]
    public void ParsePathologyDetections_ShouldRejectAmbiguousClassMargin_WhenConfidenceNotHigh()
    {
        var tensor = new DenseTensor<float>(new[] { 1, 6, 1 }); // [1,4+2,1]
        tensor[0, 0, 0] = 320f;
        tensor[0, 1, 0] = 320f;
        tensor[0, 2, 0] = 50f;
        tensor[0, 3, 0] = 50f;
        tensor[0, 4, 0] = 0.61f; // class 0
        tensor[0, 5, 0] = 0.58f; // class 1 (close -> ambiguous)

        var result = _parser.ParsePathologyDetections(
            tensor,
            640,
            1,
            0,
            0,
            0.5f,
            new[] { "Caries", "Crown" });

        result.Should().BeEmpty();
    }
}
