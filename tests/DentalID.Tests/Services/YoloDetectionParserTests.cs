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
}
