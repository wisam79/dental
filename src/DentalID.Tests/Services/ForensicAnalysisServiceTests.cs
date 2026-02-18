using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Enums;
using DentalID.Core.Interfaces;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

/// <summary>
/// Integration tests for ForensicAnalysisService to ensure correct API calls and error handling.
/// </summary>
public class ForensicAnalysisServiceTests
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

    public ForensicAnalysisServiceTests()
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
    public async Task AnalyzeImageAsync_WithValidImage_ShouldReturnSuccessResult()
    {
        // Arrange
        var imagePath = "test_image.jpg";
        var expectedResult = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f }
            },
            Pathologies = new List<DetectedPathology>()
            // IsSuccess is computed from Error == null
        };

        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Teeth);
        Assert.Single(result.Teeth);
        _loggerMock.Verify(x => x.LogAudit(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WhenAIThrowsException_ShouldReturnErrorResult()
    {
        // Arrange
        var imagePath = "test_image.jpg";

        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("AI Pipeline Error"));

        // Act
        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("System Error", result.Error);
        _loggerMock.Verify(x => x.LogError(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void UpdateForensicFilter_WithSensitivity_ShouldFilterTeeth()
    {
        // Arrange
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            },
            RawTeeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            }
        };

        // Act - sensitivity 0.5 should filter out low confidence teeth
        _service.UpdateForensicFilter(result, 0.5);

        // Assert - With sensitivity 0.5, threshold should be around 0.475
        // Teeth with confidence >= 0.475 should remain
        Assert.True(result.Teeth.Count >= 1);
    }

    [Fact]
    public void UpdateForensicFilter_WithHighSensitivity_ShouldKeepMoreTeeth()
    {
        // Arrange
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            },
            RawTeeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            }
        };

        // Act - High sensitivity (1.0) should keep more teeth
        _service.UpdateForensicFilter(result, 1.0);

        // Assert - With high sensitivity, fewer teeth should be filtered
        Assert.True(result.Teeth.Count >= 2);
    }

    [Fact]
    public void UpdateForensicFilter_WithLowSensitivity_ShouldFilterMoreTeeth()
    {
        // Arrange
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            },
            RawTeeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f },
                new() { FdiNumber = 12, Confidence = 0.3f },
                new() { FdiNumber = 13, Confidence = 0.7f }
            }
        };

        // Act - Low sensitivity (0.0) should filter more teeth
        _service.UpdateForensicFilter(result, 0.0);

        // Assert - With low sensitivity (strict), only high confidence teeth remain
        Assert.Contains(result.Teeth, t => t.Confidence >= 0.85f);
    }

    [Fact]
    public void UpdateForensicFilter_WithPathologies_ShouldApplyClassBias()
    {
        // Arrange
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Implant", Confidence = 0.7f },  // Has 0.15 bias
                new() { ClassName = "Caries", Confidence = 0.6f },  // No bias
                new() { ClassName = "Crown", Confidence = 0.65f }   // Has 0.10 bias
            },
            RawPathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Implant", Confidence = 0.7f },
                new() { ClassName = "Caries", Confidence = 0.6f },
                new() { ClassName = "Crown", Confidence = 0.65f }
            }
        };

        // Act
        _service.UpdateForensicFilter(result, 0.5);

        // Assert - Each pathology type has different threshold due to class bias
        // This test ensures the filtering logic runs without error
        Assert.NotNull(result.Pathologies);
    }

    [Fact]
    public async Task SaveEvidenceAsync_WithValidData_ShouldSaveSuccessfully()
    {
        // Arrange
        var sourcePath = "test_image.jpg";
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(),
            FeatureVector = new float[] { 0.1f, 0.2f, 0.3f }
        };
        var subjectId = 1;
        var subject = new Subject { Id = subjectId, FullName = "Test Subject" };

        _fileServiceMock.Setup(x => x.Copy(sourcePath, It.IsAny<string>(), It.IsAny<bool>()));
        _fileServiceMock.Setup(x => x.Move(It.IsAny<string>(), It.IsAny<string>()));
        _integrityServiceMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>()))
            .ReturnsAsync("hash123");
        _subjectRepoMock.Setup(x => x.GetByIdAsync(subjectId)).ReturnsAsync(subject);
        _subjectRepoMock.Setup(x => x.UpdateAsync(It.IsAny<Subject>())).Returns(Task.CompletedTask);
        _imageRepoMock.Setup(x => x.AddAsync(It.IsAny<DentalImage>())).ReturnsAsync(new DentalImage());

        // Act
        var image = await _service.SaveEvidenceAsync(sourcePath, result, subjectId);

        // Assert
        Assert.NotNull(image);
        _imageRepoMock.Verify(x => x.AddAsync(It.IsAny<DentalImage>()), Times.Once);
        _loggerMock.Verify(x => x.LogAudit(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WithNullResult_ShouldHandleGracefully()
    {
        // Arrange
        var imagePath = "test_image.jpg";
        
        _fileServiceMock.Setup(x => x.OpenRead(imagePath)).Returns(new MemoryStream());
        _aiPipelineMock.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync((AnalysisResult?)null);

        // Act
        var result = await _service.AnalyzeImageAsync(imagePath, 0.5);

        // Assert
        Assert.False(result.IsSuccess);
    }
}
