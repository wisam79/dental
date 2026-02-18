using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using Moq;
using Xunit;

namespace DentalID.Tests.Scenarios;

/// <summary>
/// Limited test scenarios for application limits and capabilities
/// This file avoids dependencies on problematic files
/// </summary>
public class LimitedApplicationLimitScenariosTests
{
    private readonly Mock<IAiPipelineService> _aiPipelineMock;
    private readonly Mock<IFileService> _fileServiceMock;
    private readonly Mock<ILoggerService> _loggerMock;
    private readonly Mock<IIntegrityService> _integrityServiceMock;
    private readonly Mock<IDentalImageRepository> _imageRepoMock;
    private readonly Mock<ISubjectRepository> _subjectRepoMock;
    private readonly AiConfiguration _config;
    private readonly AiSettings _aiSettings;
    private readonly ForensicAnalysisService _service;

    public LimitedApplicationLimitScenariosTests()
    {
        _aiPipelineMock = new Mock<IAiPipelineService>();
        _fileServiceMock = new Mock<IFileService>();
        _loggerMock = new Mock<ILoggerService>();
        _integrityServiceMock = new Mock<IIntegrityService>();
        _imageRepoMock = new Mock<IDentalImageRepository>();
        _subjectRepoMock = new Mock<ISubjectRepository>();
        
        _config = new AiConfiguration();
        _aiSettings = new AiSettings { SealingKey = "TestSecureKey123!@#" };
        
        // Setup default file service behavior
        _fileServiceMock.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);

        _service = new ForensicAnalysisService(
            _aiPipelineMock.Object,
            _loggerMock.Object,
            _integrityServiceMock.Object,
            _imageRepoMock.Object,
            _subjectRepoMock.Object,
            _config,
            _aiSettings,
            _fileServiceMock.Object);
    }

    [Fact]
    public async Task Scenario1_BatchProcessing_SmallVolume_ShouldHandle()
    {
        var imageCount = 5;
        var images = new List<string>();
        
        for (int i = 0; i < imageCount; i++)
        {
            var imagePath = $"image_{i}.jpg";
            images.Add(imagePath);
            
            _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
            _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(new AnalysisResult
                {
                    Teeth = new List<DetectedTooth> { new() { FdiNumber = 11, Confidence = 0.9f } },
                    Pathologies = new List<DetectedPathology>()
                });
        }

        foreach (var imagePath in images)
        {
            var result = await _service.AnalyzeImageAsync(imagePath, 0.5);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Teeth);
        }

        _loggerMock.Verify(x => x.LogAudit(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), 
            Times.AtLeast(imageCount));
    }

    [Fact]
    public async Task Scenario2_SensitivityRange_ValidValues_ShouldWork()
    {
        var imagePath = "test_image.jpg";
        var expectedResult = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            },
            Pathologies = new List<DetectedPathology>()
        };

        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(expectedResult);

        var result1 = await _service.AnalyzeImageAsync(imagePath, 0.0);
        var teethCountStrict = result1.Teeth.Count;

        var result2 = await _service.AnalyzeImageAsync(imagePath, 1.0);
        var teethCountLenient = result2.Teeth.Count;

        Assert.True(teethCountLenient >= teethCountStrict);
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
    }

    [Fact]
    public void Scenario2_SensitivityRange_InvalidValues_ShouldThrow()
    {
        var imagePath = "test_image.jpg";

        Assert.Throws<AggregateException>(() => _service.AnalyzeImageAsync(imagePath, -0.1).Wait());
        Assert.Throws<AggregateException>(() => _service.AnalyzeImageAsync(imagePath, 1.1).Wait());
    }

    [Fact]
    public async Task Scenario3_PoorQualityImage_LowConfidence_ShouldFlag()
    {
        var imagePath = "blurry_image.jpg";
        var lowConfidenceResult = new AnalysisResult
        {
            Teeth = new List<DetectedTooth> 
            { 
                new() { FdiNumber = 11, Confidence = 0.5f },
                new() { FdiNumber = 12, Confidence = 0.6f }
            },
            Pathologies = new List<DetectedPathology>(),
            Flags = new List<string> { "Low Confidence - Enhancement Recommended" }
        };

        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(lowConfidenceResult);

        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        Assert.True(result.IsSuccess);
        Assert.Contains("Low Confidence", string.Join(",", result.Flags));
        Assert.True(result.Teeth.Count > 0);
    }

    [Fact]
    public async Task Scenario3_NoDetections_ShouldFlagSuspicious()
    {
        var imagePath = "blank_image.jpg";
        var noDetectionResult = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(),
            Pathologies = new List<DetectedPathology>(),
            Flags = new List<string> { "Suspicious - No detections" }
        };

        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(noDetectionResult);

        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        Assert.True(result.IsSuccess);
        Assert.Contains("Suspicious", string.Join(",", result.Flags));
        Assert.Empty(result.Teeth);
        Assert.Empty(result.Pathologies);
    }

    [Fact]
    public async Task Scenario9_AiFailure_ShouldFallback()
    {
        var imagePath = "test_image.jpg";
        
        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("AI Model failed"));

        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        Assert.False(result.IsSuccess);
        Assert.Contains("System Error", result.Error);
        _loggerMock.Verify(x => x.LogError(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
    }
}
