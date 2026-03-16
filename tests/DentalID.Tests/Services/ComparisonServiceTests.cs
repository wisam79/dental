using System;
using System.Collections.Generic;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using Xunit;

namespace DentalID.Tests.Services;

public class ComparisonServiceTests
{
    private class FakeMatchingService : IMatchingService
    {
        public double SimilarityToReturn { get; set; } = 1.0;
        
        public double CalculateCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
        {
            return SimilarityToReturn;
        }

        public List<MatchCandidate> FindMatches(DentalFingerprint probe, IEnumerable<Subject> candidates, MatchingCriteria? criteria = null)
        {
            throw new NotImplementedException();
        }
    }

    private readonly FakeMatchingService _fakeMatchingService;
    private readonly ComparisonService _comparisonService;

    public ComparisonServiceTests()
    {
        _fakeMatchingService = new FakeMatchingService();
        _comparisonService = new ComparisonService(_fakeMatchingService);
    }

    [Fact]
    public void CompareAnalyses_IdenticalImages_ReturnsPerfectMatch()
    {
        // Arrange
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 11 },
            new DetectedTooth { FdiNumber = 12 }
        };

        var pathologies = new List<DetectedPathology>
        {
            new DetectedPathology { ToothNumber = 11, ClassName = "Crown" }
        };

        var image1 = new AnalysisResult { Teeth = teeth, Pathologies = pathologies, FeatureVector = new float[] { 0.1f, 0.2f } };
        var image2 = new AnalysisResult { Teeth = teeth, Pathologies = pathologies, FeatureVector = new float[] { 0.1f, 0.2f } };

        _fakeMatchingService.SimilarityToReturn = 1.0;

        // Act
        var result = _comparisonService.CompareAnalyses(image1, image2);

        // Assert
        Assert.Equal(1.0, result.SimilarityScore);
        Assert.Equal(1.0, result.ConditionMatchScore);
        Assert.Equal(1.0, result.VectorSimilarityScore);
        Assert.Equal(1.0, result.CombinedForensicScore);
        Assert.Empty(result.ConditionDifferences);
    }

    [Fact]
    public void CompareAnalyses_DifferentPathologies_LowersConditionScore()
    {
        // Arrange
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 11 },
            new DetectedTooth { FdiNumber = 12 }
        };

        var image1 = new AnalysisResult 
        { 
            Teeth = teeth, 
            Pathologies = new List<DetectedPathology> { new DetectedPathology { ToothNumber = 11, ClassName = "Crown" } },
            FeatureVector = new float[] { 0.1f }
        };

        var image2 = new AnalysisResult 
        { 
            Teeth = teeth, 
            Pathologies = new List<DetectedPathology> { new DetectedPathology { ToothNumber = 11, ClassName = "Filling" } },
            FeatureVector = new float[] { 0.1f }
        };

        _fakeMatchingService.SimilarityToReturn = 0.8;

        // Act
        var result = _comparisonService.CompareAnalyses(image1, image2);

        // Assert
        Assert.Equal(1.0, result.SimilarityScore); // All teeth present
        Assert.Equal(0.5, result.ConditionMatchScore); // 1 out of 2 teeth have matching condition
        Assert.Single(result.ConditionDifferences); // Difference on tooth 11
        // Vector * 0.5 + Condition * 0.3 + Presence * 0.2
        // 0.8 * 0.5 + 0.5 * 0.3 + 1.0 * 0.2 = 0.40 + 0.15 + 0.20 = 0.75
        Assert.Equal(0.75, result.CombinedForensicScore); 
    }
}
