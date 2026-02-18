using Xunit;
using DentalID.Infrastructure.Services;
using DentalID.Core.Entities;
using DentalID.Core.DTOs;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.IO;

namespace DentalID.Tests.Services;

public class PdfReportServiceTests
{
    private readonly PdfReportService _service;

    public PdfReportServiceTests()
    {
        _service = new PdfReportService();
    }

    private void AssertIsValidPdf(byte[] data)
    {
        Assert.NotNull(data);
        Assert.NotEmpty(data);
        // PDF magic number: %PDF
        Assert.True(data.Length > 4);
        Assert.Equal((byte)'%', data[0]);
        Assert.Equal((byte)'P', data[1]);
        Assert.Equal((byte)'D', data[2]);
        Assert.Equal((byte)'F', data[3]);
    }

    [Fact]
    public async Task GenerateSubjectReport_ShouldCreateValidPdf()
    {
        // Arrange
        var subject = new Subject 
        { 
            FullName = "John Doe", 
            SubjectId = "SUB-123",
            NationalId = "123456789",
            DateOfBirth = new DateTime(1980, 1, 1),
            // LatestDentalCode is computed from images
            DentalImages = new List<DentalImage>
            {
                new DentalImage { UploadedAt = DateTime.Now, FingerprintCode = "FP-001" }
            }
        };

        // Act
        var pdf = await _service.GenerateSubjectReportAsync(subject);

        // Assert
        AssertIsValidPdf(pdf);
    }

    [Fact]
    public async Task GenerateLabReport_ShouldCreateValidPdf()
    {
        // Arrange
        var subject = new Subject { FullName = "Jane Doe", SubjectId = "SUB-456" };
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth> 
            { 
                new DetectedTooth { FdiNumber = 18, Confidence = 0.95f, Width=10, Height=10 }
            },
            Pathologies = new List<DetectedPathology>
            {
                new DetectedPathology { ToothNumber = 18, ClassName = "Caries", Confidence = 0.8f, Width=5, Height=5 }
            },
            ProcessingTimeMs = 1500,
            EstimatedAge = 35,
            EstimatedGender = "F"
        };
        
        string imagePath = "nonexistent.jpg";

        // Act
        var pdf = await _service.GenerateLabReportAsync(result, subject, imagePath);

        // Assert
        AssertIsValidPdf(pdf);
    }

    [Fact]
    public async Task GenerateMatchReport_ShouldCreateValidPdf()
    {
        // Arrange
        var probe = new Subject 
        { 
            FullName = "Unknown", 
            DentalImages = new List<DentalImage> { new DentalImage { FingerprintCode = "XYZ" } }
        };
        var candidate = new MatchCandidate 
        { 
            Subject = new Subject 
            { 
                FullName = "Known Person", 
                SubjectId = "SUB-999", 
                DentalImages = new List<DentalImage> { new DentalImage { FingerprintCode = "XYZ" } }
            },
            Score = 0.98f,
            MatchMethod = "Dental Code",
            MatchDetails = new Dictionary<string, double> { { "MethodConfidence", 0.99 } }
        };

        // Act
        var pdf = await _service.GenerateMatchReportAsync(probe, candidate);

        // Assert
        AssertIsValidPdf(pdf);
    }
}
