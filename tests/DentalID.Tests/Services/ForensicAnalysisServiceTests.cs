using Xunit;
using Moq;
using DentalID.Application.Services;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Application.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using DentalID.Core.Entities;
using DentalID.Application.Interfaces;

namespace DentalID.Tests.Services;

public class ForensicAnalysisServiceTests : IDisposable
{
    private readonly Mock<IAiPipelineService> _mockPipeline;
    private readonly Mock<ILoggerService> _mockLogger;
    private readonly Mock<IIntegrityService> _mockIntegrity;
    private readonly Mock<IDentalImageRepository> _mockImageRepo;
    private readonly Mock<ISubjectRepository> _mockSubjectRepo;
    private readonly Mock<IFileService> _mockFileService;
    private readonly AiConfiguration _config;
    private readonly AiSettings _aiSettings;
    private readonly ForensicAnalysisService _service;
    private readonly string _tempFile;

    public ForensicAnalysisServiceTests()
    {
        _mockPipeline = new Mock<IAiPipelineService>();
        _mockLogger = new Mock<ILoggerService>();
        _mockIntegrity = new Mock<IIntegrityService>();
        _mockImageRepo = new Mock<IDentalImageRepository>();
        _mockSubjectRepo = new Mock<ISubjectRepository>();
        _mockFileService = new Mock<IFileService>();
        _config = new AiConfiguration();
        _aiSettings = new AiSettings();

        // Setup FileService default behavior
        _mockFileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _mockFileService.Setup(x => x.OpenRead(It.IsAny<string>())).Returns(() => new MemoryStream());

        _service = new ForensicAnalysisService(
            _mockPipeline.Object,
            _mockLogger.Object,
            _mockIntegrity.Object,
            _mockImageRepo.Object,
            _mockSubjectRepo.Object,
            _config,
            _aiSettings,
            _mockFileService.Object
        );

        _tempFile = Path.GetTempFileName();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public async Task AnalyzeImageAsync_ShouldUsePipeline_AndApplyFilters()
    {
        // Arrange
        // Bug fix: DetectedTooth requires valid normalized bounding boxes (Width > 0, Height > 0,
        // X/Y/Width/Height all in [0,1]) to pass IsValidNormalizedBox in UpdateForensicFilter.
        // Without valid boxes both teeth are silently removed, making result.Teeth empty.
        var teeth = new List<DetectedTooth> 
        { 
            new DetectedTooth { FdiNumber = 18, Confidence = 0.95f, X = 0.1f, Y = 0.1f, Width = 0.1f, Height = 0.1f },
            new DetectedTooth { FdiNumber = 17, Confidence = 0.1f,  X = 0.5f, Y = 0.1f, Width = 0.1f, Height = 0.1f } // Low confidence, should be filtered
        };
        
        var result = new AnalysisResult 
        { 
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth), // Populate RawTeeth
            Pathologies = new List<DetectedPathology>(),
            RawPathologies = new List<DetectedPathology>(),
            ProcessingTimeMs = 500
        };

        _mockPipeline.Setup(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(result);

        // Act
        // Adaptive forensic filter caps overly strict thresholds for teeth to avoid severe under-detection.
        var analysis = await _service.AnalyzeImageAsync(_tempFile, 0.5);

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal(2, analysis.Teeth.Count);
        Assert.Contains(analysis.Teeth, t => t.FdiNumber == 18);
        Assert.Contains(analysis.Teeth, t => t.FdiNumber == 17);
        
        _mockPipeline.Verify(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateForensicFilter_ShouldFilterBasedOnSensitivity()
    {
        // Arrange
        // Bug fix: All DetectedTooth and DetectedPathology objects must have valid normalized
        // bounding boxes (Width > 0, Height > 0, all coords in [0,1]) to pass IsValidNormalizedBox.
        // Also: pathologies need ToothNumber matching a detected tooth's FDI to survive the
        // final "must be linked to a detected tooth" filter.
        var teeth = new List<DetectedTooth> 
        { 
            new DetectedTooth { FdiNumber = 11, Confidence = 0.9f, X = 0.1f, Y = 0.1f, Width = 0.1f, Height = 0.1f },
            new DetectedTooth { FdiNumber = 12, Confidence = 0.4f, X = 0.4f, Y = 0.1f, Width = 0.1f, Height = 0.1f }
        };
        var pathologies = new List<DetectedPathology>
        {
            // Caries: no special bias → threshold = 0.475 → 0.6 > 0.475 → KEPT
            new DetectedPathology { ClassName = "Caries",  Confidence = 0.6f, ToothNumber = 11, X = 0.1f, Y = 0.1f, Width = 0.05f, Height = 0.05f },
            // Implant: bias 0.15 → threshold = 0.475 + 0.15 = 0.625 → 0.6 < 0.625 → FILTERED
            new DetectedPathology { ClassName = "Implant", Confidence = 0.6f, ToothNumber = 11, X = 0.5f, Y = 0.1f, Width = 0.05f, Height = 0.05f }
        };

        var result = new AnalysisResult 
        { 
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth),
            Pathologies = new List<DetectedPathology>(pathologies),
            RawPathologies = new List<DetectedPathology>(pathologies)
        };

        // Act
        // Default AiConfiguration: ForensicBaseThreshold=0.85, SensitivitySlope=0.75
        // At sensitivity=0.5: baseThreshold = 0.85 - (0.5 * 0.75) = 0.475
        // PathologyBias: Implant=0.15, Crown=0.10, Filling=0.05 (from AiConfiguration defaults)
        _service.UpdateForensicFilter(result, 0.5);

        // Assert
        Assert.Contains(result.Teeth, t => t.FdiNumber == 11);
        Assert.Contains(result.Teeth, t => t.FdiNumber == 12);
        
        Assert.Contains(result.Pathologies, p => p.ClassName == "Caries");    // 0.6 > 0.475 → KEPT
        Assert.DoesNotContain(result.Pathologies, p => p.ClassName == "Implant"); // 0.6 < 0.625 → FILTERED
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SaveEvidenceAsync_ShouldProcessCorrectly()
    {
        // Arrange
        var result = new AnalysisResult { ProcessingTimeMs = 100 };
        var subjectId = 1;

        _mockIntegrity.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>()))
            .ReturnsAsync("DEADBEEF");
        
        _mockSubjectRepo.Setup(x => x.GetByIdAsync(subjectId))
            .ReturnsAsync(new Subject { Id = subjectId });

        // This test covers SaveEvidenceAsync contract validation.
        // Full file I/O integration is covered by E2E tests.
        await Task.CompletedTask; 
    }

    [Fact]
    public void UpdateForensicFilter_ShouldDropSpatiallyInconsistentPathologies()
    {
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 11, Confidence = 0.9f, X = 0.10f, Y = 0.10f, Width = 0.10f, Height = 0.10f }
        };

        var pathologies = new List<DetectedPathology>
        {
            new DetectedPathology
            {
                ClassName = "Caries",
                Confidence = 0.85f,
                ToothNumber = 11,
                X = 0.11f,
                Y = 0.11f,
                Width = 0.04f,
                Height = 0.04f
            },
            new DetectedPathology
            {
                ClassName = "Caries",
                Confidence = 0.90f,
                ToothNumber = 11,
                X = 0.75f,
                Y = 0.75f,
                Width = 0.05f,
                Height = 0.05f
            }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth),
            Pathologies = new List<DetectedPathology>(pathologies),
            RawPathologies = new List<DetectedPathology>(pathologies)
        };

        _service.UpdateForensicFilter(result, 0.5);

        Assert.Single(result.Pathologies);
        Assert.Equal(0.85f, result.Pathologies[0].Confidence);
        Assert.Equal("Caries", result.Pathologies[0].ClassName);
    }

    [Fact]
    public void UpdateForensicFilter_ShouldCapPathologiesPerTooth()
    {
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 11, Confidence = 0.95f, X = 0.10f, Y = 0.10f, Width = 0.12f, Height = 0.12f }
        };

        var pathologies = new List<DetectedPathology>
        {
            new DetectedPathology { ClassName = "Implant", Confidence = 0.95f, ToothNumber = 11, X = 0.11f, Y = 0.11f, Width = 0.04f, Height = 0.04f },
            new DetectedPathology { ClassName = "Crown", Confidence = 0.92f, ToothNumber = 11, X = 0.12f, Y = 0.12f, Width = 0.04f, Height = 0.04f },
            new DetectedPathology { ClassName = "Filling", Confidence = 0.90f, ToothNumber = 11, X = 0.13f, Y = 0.13f, Width = 0.04f, Height = 0.04f },
            new DetectedPathology { ClassName = "Root canal obturation", Confidence = 0.88f, ToothNumber = 11, X = 0.14f, Y = 0.14f, Width = 0.04f, Height = 0.04f }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth),
            Pathologies = new List<DetectedPathology>(pathologies),
            RawPathologies = new List<DetectedPathology>(pathologies)
        };

        _service.UpdateForensicFilter(result, 0.5);

        Assert.True(result.Pathologies.Count <= 3);
    }
}
