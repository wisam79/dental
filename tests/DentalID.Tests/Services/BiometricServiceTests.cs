using Xunit;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using System.Collections.Generic;

namespace DentalID.Tests.Services;

public class BiometricServiceTests
{
    private readonly BiometricService _service = new();

    [Fact]
    public void GenerateFingerprint_ShouldCalculateScoreCorrectly()
    {
        // Arrange
        var teeth = new List<DetectedTooth>
        {
            new() { FdiNumber = 11 }, // Healthy
            new() { FdiNumber = 21 }  // Healthy
        };
        var pathologies = new List<DetectedPathology>
        {
            new() { ClassName = "Implant", ToothNumber = 11 }, // Base 100
            new() { ClassName = "Caries", ToothNumber = 21 }   // Base 5
        };

        // Act
        var fingerprint = _service.GenerateFingerprint(teeth, pathologies);

        // Assert
        Assert.Contains("11:I", fingerprint.Code);
        Assert.Contains("21:K", fingerprint.Code);
        Assert.Equal(100, fingerprint.UniquenessScore); // Cap is 100
    }

    [Fact]
    public void GenerateFingerprint_ShouldHandleEmptyInputs()
    {
        var fingerprint = _service.GenerateFingerprint(new List<DetectedTooth>(), new List<DetectedPathology>());
        
        Assert.Equal(0, fingerprint.UniquenessScore);
        Assert.Contains("11:U", fingerprint.Code); // Default Unknown (was Healthy)
    }
    
    [Fact]
    public void ParseFingerprintCode_ShouldReconstructFingerprint()
    {
        string code = "11:I-21:K";
        
        var fingerprint = _service.ParseFingerprintCode(code);
        
        Assert.True(fingerprint.ToothMap.ContainsKey(11));
        Assert.Equal("I", fingerprint.ToothMap[11]);
        Assert.Equal("K", fingerprint.ToothMap[21]);
        Assert.True(fingerprint.UniquenessScore > 0);
    }
    
    [Fact]
    public void CalculateSimilarity_ShouldReturnOneForIdenticalFingerprints()
    {
        var fp1 = _service.GenerateFingerprint(
            new List<DetectedTooth> { new() { FdiNumber = 16 } },
            new List<DetectedPathology> { new() { ClassName = "Filling", ToothNumber = 16 } }
        );
        var fp2 = _service.GenerateFingerprint(
            new List<DetectedTooth> { new() { FdiNumber = 16 } },
            new List<DetectedPathology> { new() { ClassName = "Filling", ToothNumber = 16 } }
        );

        var similarity = _service.CalculateSimilarity(fp1, fp2);
        
        Assert.Equal(1.0, similarity, 4);
    }

    [Fact]
    public void CalculateSimilarity_ShouldReturnZeroForDisjointFingerprints()
    {
        // Different teeth entirely (implied by different codes if intersection check is used based on FDI keys)
        // Service uses Intersection of Keys.
        // So we need SAME keys but Orthogonal values? 
        // Or DIFFERENT keys means intersection is empty -> 0.
        
        var fp1 = _service.ParseFingerprintCode("11:I");
        var fp2 = _service.ParseFingerprintCode("12:I");
        
        var similarity = _service.CalculateSimilarity(fp1, fp2);
        
        Assert.Equal(0.0, similarity);
    }
    
    [Fact]
    public void CalculateSimilarity_ShouldReturnValuesBetweenZeroAndOne()
    {
        var fp1 = _service.ParseFingerprintCode("11:I-12:H");
        var fp2 = _service.ParseFingerprintCode("11:H-12:H");
        
        var similarity = _service.CalculateSimilarity(fp1, fp2);
        
        Assert.InRange(similarity, 0.0, 1.0);
    }
}
