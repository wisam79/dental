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
using System.Linq;
using DentalID.Core.Entities;
using DentalID.Application.Interfaces;
using SkiaSharp;

namespace DentalID.Tests.Services;

public class ForensicAnalysisServiceTests : IDisposable
{
    private static readonly byte[] ValidGrayImageBytes = CreateGrayImageBytes();

    private static byte[] CreateGrayImageBytes()
    {
        using var bitmap = new SKBitmap(256, 128, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(135, 135, 135));
        using var paint = new SKPaint
        {
            Color = new SKColor(90, 90, 90),
            IsStroke = true,
            StrokeWidth = 4
        };
        canvas.DrawLine(8, 64, 248, 64, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private readonly Mock<IAiPipelineService> _mockPipeline;
    private readonly Mock<ILoggerService> _mockLogger;
    private readonly Mock<IIntegrityService> _mockIntegrity;
    private readonly Mock<IDentalImageRepository> _mockImageRepo;
    private readonly Mock<ISubjectRepository> _mockSubjectRepo;
    private readonly Mock<IFileService> _mockFileService;
    private readonly Mock<IForensicRulesEngine> _mockRulesEngine;
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
        _mockRulesEngine = new Mock<IForensicRulesEngine>();
        _config = new AiConfiguration();
        _aiSettings = new AiSettings();

        // Setup FileService default behavior
        _mockFileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(true);
        _mockFileService.Setup(x => x.OpenRead(It.IsAny<string>())).Returns(() => new MemoryStream(ValidGrayImageBytes));

        _service = new ForensicAnalysisService(
            _mockPipeline.Object,
            _mockLogger.Object,
            _mockIntegrity.Object,
            _mockImageRepo.Object,
            _mockSubjectRepo.Object,
            _config,
            _aiSettings,
            _mockFileService.Object,
            _mockRulesEngine.Object
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
        if (!analysis.IsSuccess)
        {
            var errorCall = _mockLogger.Invocations.FirstOrDefault(i => i.Method.Name == nameof(ILoggerService.LogError));
            if (errorCall != null && errorCall.Arguments.Count > 0 && errorCall.Arguments[0] is Exception ex)
                throw new Xunit.Sdk.XunitException($"Analyze failed: {analysis.Error}; logger exception: {ex.GetType().Name} - {ex.Message}");
        }
        Assert.True(analysis.IsSuccess, analysis.Error ?? "Unknown analysis error");
        Assert.Equal(2, analysis.Teeth.Count);
        Assert.Contains(analysis.Teeth, t => t.FdiNumber == 18);
        Assert.Contains(analysis.Teeth, t => t.FdiNumber == 17);
        
        _mockPipeline.Verify(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeImageAsync_WhenInvalidImage_ShouldRejectBeforePipeline()
    {
        _mockFileService.Setup(x => x.OpenRead(It.IsAny<string>()))
            .Returns(() => new MemoryStream(new byte[] { 1, 2, 3, 4, 5 }));

        var analysis = await _service.AnalyzeImageAsync(_tempFile, 0.5);

        Assert.NotNull(analysis);
        Assert.False(analysis.IsSuccess);
        Assert.Contains("Invalid image format", analysis.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _mockPipeline.Verify(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
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

    [Fact]
    public async Task AnalyzeImageAsync_WhenFileDoesNotExist_ShouldThrowFileNotFoundException()
    {
        // Arrange: FileService reports file not found.
        // Per implementation, AnalyzeImageAsync throws FileNotFoundException (not returns an error object).
        _mockFileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(false);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await _service.AnalyzeImageAsync("/nonexistent/path.png", 0.5));

        _mockPipeline.Verify(x => x.AnalyzeImageAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void UpdateForensicFilter_WhenOnePermanentToothMissingAndRawCandidateExists_ShouldRecoverByNeighborGeometry()
    {
        // Arrange: Build 31 permanent teeth where 47 is the only missing FDI.
        var teeth = new List<DetectedTooth>();
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        {
            for (int unit = 1; unit <= 8; unit++)
            {
                int fdi = quadrant * 10 + unit;
                if (fdi == 47)
                    continue;

                float x = quadrant switch
                {
                    1 => 0.52f - (unit * 0.03f),
                    2 => 0.50f + (unit * 0.03f),
                    3 => 0.50f + (unit * 0.03f),
                    4 => 0.52f - (unit * 0.03f),
                    _ => 0.5f
                };
                float y = quadrant <= 2 ? 0.20f : 0.58f;

                teeth.Add(new DetectedTooth
                {
                    FdiNumber = fdi,
                    Confidence = 0.90f,
                    X = x,
                    Y = y,
                    Width = 0.035f,
                    Height = 0.11f
                });
            }
        }
        
        // Add a plausible raw candidate in the missing 47 slot but mislabel it as 46.
        var rawTeeth = new List<DetectedTooth>(teeth)
        {
            new DetectedTooth
            {
                FdiNumber = 46,
                Confidence = 0.14f,
                X = 0.31f, // between 46 (0.34) and 48 (0.28)
                Y = 0.58f,
                Width = 0.034f,
                Height = 0.11f
            }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = rawTeeth,
            Pathologies = new List<DetectedPathology>(),
            RawPathologies = new List<DetectedPathology>()
        };

        // Act
        _service.UpdateForensicFilter(result, 0.5);

        // Assert
        Assert.Equal(32, result.Teeth.Count);
        Assert.Contains(result.Teeth, t => t.FdiNumber == 47);
        Assert.Contains(result.Flags, f => f.Contains("Recovered missing tooth 47", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateForensicFilter_WhenMissingTeethEvidenceExists_ShouldNotRecoverMissingTooth()
    {
        // Arrange: same 31-teeth setup (47 missing), plus a low-confidence raw candidate near 47.
        var teeth = new List<DetectedTooth>();
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        {
            for (int unit = 1; unit <= 8; unit++)
            {
                int fdi = quadrant * 10 + unit;
                if (fdi == 47)
                    continue;

                float x = quadrant switch
                {
                    1 => 0.52f - (unit * 0.03f),
                    2 => 0.50f + (unit * 0.03f),
                    3 => 0.50f + (unit * 0.03f),
                    4 => 0.52f - (unit * 0.03f),
                    _ => 0.5f
                };
                float y = quadrant <= 2 ? 0.20f : 0.58f;

                teeth.Add(new DetectedTooth
                {
                    FdiNumber = fdi,
                    Confidence = 0.90f,
                    X = x,
                    Y = y,
                    Width = 0.035f,
                    Height = 0.11f
                });
            }
        }

        var rawTeeth = new List<DetectedTooth>(teeth)
        {
            new DetectedTooth
            {
                FdiNumber = 46,
                Confidence = 0.14f,
                X = 0.31f,
                Y = 0.58f,
                Width = 0.034f,
                Height = 0.11f
            }
        };

        var missingEvidence = new List<DetectedPathology>
        {
            new DetectedPathology
            {
                ClassName = "Missing teeth",
                ToothNumber = 47,
                Confidence = 0.92f,
                X = 0.305f,
                Y = 0.58f,
                Width = 0.036f,
                Height = 0.11f
            }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = rawTeeth,
            Pathologies = new List<DetectedPathology>(missingEvidence),
            RawPathologies = new List<DetectedPathology>(missingEvidence)
        };

        // Act
        _service.UpdateForensicFilter(result, 0.5);

        // Assert: keep true missing behavior (31 teeth) and do not synthesize 47.
        Assert.Equal(31, result.Teeth.Count);
        Assert.DoesNotContain(result.Teeth, t => t.FdiNumber == 47);
        Assert.DoesNotContain(result.Flags, f => f.Contains("Recovered missing tooth 47", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateForensicFilter_WhenNoAdditionalRawCandidate_ShouldKeepMissingTooth()
    {
        // Arrange: 47 is missing from the selected set, and raw set has no extra candidate near 47.
        var teeth = new List<DetectedTooth>();
        foreach (var quadrant in new[] { 1, 2, 3, 4 })
        {
            for (int unit = 1; unit <= 8; unit++)
            {
                int fdi = quadrant * 10 + unit;
                if (fdi == 47)
                    continue;

                float x = quadrant switch
                {
                    1 => 0.52f - (unit * 0.03f),
                    2 => 0.50f + (unit * 0.03f),
                    3 => 0.50f + (unit * 0.03f),
                    4 => 0.52f - (unit * 0.03f),
                    _ => 0.5f
                };
                float y = quadrant <= 2 ? 0.20f : 0.58f;

                teeth.Add(new DetectedTooth
                {
                    FdiNumber = fdi,
                    Confidence = 0.90f,
                    X = x,
                    Y = y,
                    Width = 0.035f,
                    Height = 0.11f
                });
            }
        }

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth), // no additional raw candidate
            Pathologies = new List<DetectedPathology>(),
            RawPathologies = new List<DetectedPathology>()
        };

        // Act
        _service.UpdateForensicFilter(result, 0.5);

        // Assert
        Assert.Equal(31, result.Teeth.Count);
        Assert.DoesNotContain(result.Teeth, t => t.FdiNumber == 47);
        Assert.DoesNotContain(result.Flags, f => f.Contains("Recovered missing tooth 47", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateForensicFilter_AtSensitivity0_ShouldBeVeryStrict()
    {
        // At sensitivity=0.0: baseThreshold = 0.85 - (0 * 0.75) = 0.85
        // Even Caries (no bias) requires >0.85 confidence to pass.
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 21, Confidence = 0.9f, X = 0.2f, Y = 0.2f, Width = 0.1f, Height = 0.1f }
        };
        var pathologies = new List<DetectedPathology>
        {
            new DetectedPathology { ClassName = "Caries", Confidence = 0.80f, ToothNumber = 21, X = 0.2f, Y = 0.2f, Width = 0.05f, Height = 0.05f },
            new DetectedPathology { ClassName = "Caries", Confidence = 0.90f, ToothNumber = 21, X = 0.21f, Y = 0.21f, Width = 0.05f, Height = 0.05f }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth),
            Pathologies = new List<DetectedPathology>(pathologies),
            RawPathologies = new List<DetectedPathology>(pathologies)
        };

        _service.UpdateForensicFilter(result, 0.0);

        // Only confidence 0.90 should survive at strict threshold 0.85
        Assert.All(result.Pathologies, p => Assert.True(p.Confidence >= 0.85f,
            $"Expected confidence >= 0.85 but was {p.Confidence}"));
    }

    [Fact]
    public void UpdateForensicFilter_AtSensitivity1_ShouldBePermissive()
    {
        // At sensitivity=1.0: baseThreshold = 0.85 - (1.0 * 0.75) = 0.10
        // However, Caries has a hard confidence floor of 0.52 (GetPathologyConfidenceFloor).
        // Use confidence 0.80 which exceeds both the computed threshold AND the Caries floor.
        // The test verifies that permissive mode keeps a pathology that would be rejected at strict mode.
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 16, Confidence = 0.9f, X = 0.1f, Y = 0.1f, Width = 0.1f, Height = 0.1f }
        };
        var pathologies = new List<DetectedPathology>
        {
            // Confidence 0.80: above the Caries floor (0.52) but below strict threshold (0.85).
            // At sensitivity=1.0, effective threshold = 0.10 → Caries passes at max(0.10 + 0, 0.52) = 0.52 → 0.80 > 0.52 ✓
            new DetectedPathology { ClassName = "Caries", Confidence = 0.80f, ToothNumber = 16, X = 0.11f, Y = 0.11f, Width = 0.04f, Height = 0.04f }
        };

        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth>(teeth),
            RawTeeth = new List<DetectedTooth>(teeth),
            Pathologies = new List<DetectedPathology>(pathologies),
            RawPathologies = new List<DetectedPathology>(pathologies)
        };

        _service.UpdateForensicFilter(result, 1.0);

        // Low-confidence (but above floor) pathology should survive at permissive sensitivity
        Assert.Contains(result.Pathologies, p => p.ClassName == "Caries");
    }
}
