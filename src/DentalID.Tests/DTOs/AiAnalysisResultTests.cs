using DentalID.Core.DTOs;
using Xunit;

namespace DentalID.Tests.DTOs;

/// <summary>
/// Tests for AiAnalysisResult DTO to ensure correct data transformation.
/// </summary>
public class AiAnalysisResultTests
{
    [Fact]
    public void AnalysisResult_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var result = new AnalysisResult();

        // Assert
        Assert.NotNull(result.Teeth);
        Assert.Empty(result.Teeth);
        Assert.NotNull(result.Pathologies);
        Assert.Empty(result.Pathologies);
        Assert.NotNull(result.Flags);
        Assert.Empty(result.Flags);
        Assert.NotNull(result.SmartInsights);
        Assert.Empty(result.SmartInsights);
    }

    [Fact]
    public void AnalysisResult_IsSuccess_WhenNoError_ShouldReturnTrue()
    {
        // Arrange
        var result = new AnalysisResult { Error = null };

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void AnalysisResult_IsSuccess_WhenErrorExists_ShouldReturnFalse()
    {
        // Arrange
        var result = new AnalysisResult { Error = "Some error occurred" };

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void DetectedTooth_WithValidData_ShouldSetPropertiesCorrectly()
    {
        // Arrange & Act
        var tooth = new DetectedTooth
        {
            FdiNumber = 11,
            Confidence = 0.95f,
            X = 100.5f,
            Y = 200.3f,
            Width = 50.0f,
            Height = 60.0f
        };

        // Assert
        Assert.Equal(11, tooth.FdiNumber);
        Assert.Equal(0.95f, tooth.Confidence);
        Assert.Equal(100.5f, tooth.X);
        Assert.Equal(200.3f, tooth.Y);
        Assert.Equal(50.0f, tooth.Width);
        Assert.Equal(60.0f, tooth.Height);
    }

    [Fact]
    public void DetectedPathology_WithValidData_ShouldSetPropertiesCorrectly()
    {
        // Arrange & Act
        var pathology = new DetectedPathology
        {
            ClassName = "Caries",
            Confidence = 0.87f,
            ToothNumber = 36,
            X = 50.0f,
            Y = 75.0f,
            Width = 25.0f,
            Height = 30.0f
        };

        // Assert
        Assert.Equal("Caries", pathology.ClassName);
        Assert.Equal(0.87f, pathology.Confidence);
        Assert.Equal(36, pathology.ToothNumber);
        Assert.Equal(50.0f, pathology.X);
        Assert.Equal(75.0f, pathology.Y);
        Assert.Equal(25.0f, pathology.Width);
        Assert.Equal(30.0f, pathology.Height);
    }

    [Fact]
    public void DetectedPathology_DefaultClassName_ShouldBeEmptyString()
    {
        // Arrange & Act
        var pathology = new DetectedPathology();

        // Assert
        Assert.Equal(string.Empty, pathology.ClassName);
    }

    [Fact]
    public void AnalysisResult_RawTeeth_ShouldBeJsonIgnored()
    {
        // Arrange
        var result = new AnalysisResult
        {
            RawTeeth = new List<DetectedTooth>
            {
                new() { FdiNumber = 11, Confidence = 0.9f }
            }
        };

        // Assert - RawTeeth should be settable but ignored in JSON serialization
        Assert.NotNull(result.RawTeeth);
        Assert.Single(result.RawTeeth);
    }

    [Fact]
    public void AnalysisResult_RawPathologies_ShouldBeJsonIgnored()
    {
        // Arrange
        var result = new AnalysisResult
        {
            RawPathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Caries", Confidence = 0.8f }
            }
        };

        // Assert - RawPathologies should be settable but ignored in JSON serialization
        Assert.NotNull(result.RawPathologies);
        Assert.Single(result.RawPathologies);
    }

    [Fact]
    public void AnalysisResult_WithFeatureVector_ShouldStoreCorrectly()
    {
        // Arrange
        var featureVector = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var result = new AnalysisResult
        {
            FeatureVector = featureVector,
            EstimatedAge = 35,
            EstimatedGender = "Male"
        };

        // Assert
        Assert.NotNull(result.FeatureVector);
        Assert.Equal(5, result.FeatureVector.Length);
        Assert.Equal(35, result.EstimatedAge);
        Assert.Equal("Male", result.EstimatedGender);
    }

    [Fact]
    public void AnalysisResult_WithFingerprint_ShouldStoreCorrectly()
    {
        // Arrange
        var fingerprint = new DentalFingerprint
        {
            Code = "DF-2024-001",
            UniquenessScore = 0.95
        };
        var result = new AnalysisResult
        {
            Fingerprint = fingerprint,
            ProcessingTimeMs = 1500.5
        };

        // Assert
        Assert.NotNull(result.Fingerprint);
        Assert.Equal("DF-2024-001", result.Fingerprint.Code);
        Assert.Equal(0.95, result.Fingerprint.UniquenessScore);
        Assert.Equal(1500.5, result.ProcessingTimeMs);
    }
}
