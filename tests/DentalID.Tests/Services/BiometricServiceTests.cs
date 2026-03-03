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

    [Fact]
    public void CalculateSimilarity_ShouldPenalizeMissingTeethInRecord()
    {
        // Represents Bug #16 fix: Union-based comparison instead of Intersect.
        // A fingerprint with 5 teeth vs 1 tooth should not be a perfect match 1.0.
        var fp1 = _service.ParseFingerprintCode("11:H-12:H-13:H-14:H-15:H");
        var fp2 = _service.ParseFingerprintCode("11:H");
        
        var similarity = _service.CalculateSimilarity(fp1, fp2);
        
        // With Union logic, Cosine SIM = 4 / (sqrt(20) * sqrt(4)) = 0.447
        // If it were Intersect logic, Cosine SIM would be 1.0
        Assert.True(similarity < 0.5, $"Expected similarity < 0.5, but was {similarity}");
    }

    [Fact]
    public void GenerateFingerprint_RootCanal_ShouldContributeToUniquenessScore()
    {
        // Root canal obturation is a unique, high-scoring dental feature.
        var teeth = new List<DetectedTooth> { new() { FdiNumber = 36 } };
        var pathologies = new List<DetectedPathology>
        {
            new() { ClassName = "Root canal obturation", ToothNumber = 36 }
        };

        var fp = _service.GenerateFingerprint(teeth, pathologies);

        Assert.Contains("36:R", fp.Code);
        Assert.True(fp.UniquenessScore > 0, "Root canal should contribute uniqueness");
    }

    [Fact]
    public void GenerateFingerprint_UnknownTooth_ShouldNotCrash()
    {
        // A tooth with FDI=0 or unknown should be handled safely.
        var teeth = new List<DetectedTooth>
        {
            new() { FdiNumber = 0 }, // unknown / out of range
            new() { FdiNumber = 11 } // normal
        };
        var pathologies = new List<DetectedPathology>();

        // Should not throw
        var fp = _service.GenerateFingerprint(teeth, pathologies);

        Assert.NotNull(fp);
        Assert.NotNull(fp.Code);
    }

    [Fact]
    public void ParseFingerprintCode_EmptyString_ShouldReturnEmptyFingerprint()
    {
        var fp = _service.ParseFingerprintCode(string.Empty);

        Assert.NotNull(fp);
        Assert.Equal(0, fp.UniquenessScore);
    }
}
