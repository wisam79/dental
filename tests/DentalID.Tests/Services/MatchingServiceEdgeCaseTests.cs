using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

public class MatchingServiceEdgeCaseTests
{
    private readonly MatchingService _service;
    private readonly Mock<IBiometricService> _mockBiometric;

    public MatchingServiceEdgeCaseTests()
    {
        _mockBiometric = new Mock<IBiometricService>();
        _service = new MatchingService(_mockBiometric.Object);
    }

    [Fact]
    public void CalculateCosineSimilarity_ShouldReturnOne_ForIdenticalVectors()
    {
        float[] v1 = { 1.0f, 0.0f, 0.5f };
        float[] v2 = { 1.0f, 0.0f, 0.5f };

        var score = _service.CalculateCosineSimilarity(v1, v2);
        score.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void CalculateCosineSimilarity_ShouldReturnZero_ForOrthogonalVectors()
    {
        float[] v1 = { 1.0f, 0.0f };
        float[] v2 = { 0.0f, 1.0f };

        var score = _service.CalculateCosineSimilarity(v1, v2);
        score.Should().BeApproximately(0.0, 0.0001);
    }
    
    [Fact]
    public void CalculateCosineSimilarity_ShouldReturnMinusOne_ForOppositeVectors()
    {
        float[] v1 = { 1.0f, 2.0f };
        float[] v2 = { -1.0f, -2.0f };

        var score = _service.CalculateCosineSimilarity(v1, v2);
        score.Should().BeApproximately(-1.0, 0.0001);
    }

    [Fact]
    public void CalculateCosineSimilarity_ShouldThrow_ForLengthMismatch()
    {
        float[] v1 = { 1.0f };
        float[] v2 = { 1.0f, 2.0f };

        Assert.Throws<ArgumentException>(() => _service.CalculateCosineSimilarity(v1, v2));
    }
    
    [Fact]
    public void CalculateCosineSimilarity_ShouldReturnZero_ForZeroVector()
    {
         float[] v1 = { 0.0f, 0.0f };
         float[] v2 = { 1.0f, 1.0f };
         
         var score = _service.CalculateCosineSimilarity(v1, v2);
         score.Should().Be(0);
    }

    [Fact]
    public void FindMatches_ShouldReturnEmpty_WhenCandidatesEmpty()
    {
        var probe = new DentalFingerprint { FeatureVector = new float[32] }; // float[]
        var candidates = new List<Subject>();

        var result = _service.FindMatches(probe, candidates);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindMatches_ShouldFilterByAge()
    {
        var probe = new DentalFingerprint();
        var subjectYoung = new Subject { DateOfBirth = DateTime.UtcNow.AddYears(-20), DentalImages = new List<DentalImage> { new DentalImage() } };
        var subjectOld = new Subject { DateOfBirth = DateTime.UtcNow.AddYears(-60), DentalImages = new List<DentalImage> { new DentalImage() } };
        
        // Mock candidates to have matching capability so we only test filter
        // We can verify filter works, we can check that even if young subject is identical, it's not returned.
        // We need real vectors.
        float[] vec = { 1.0f, 0.0f }; // Normalized length 1
        byte[] vecBytes = new byte[vec.Length * sizeof(float)];
        Buffer.BlockCopy(vec, 0, vecBytes, 0, vecBytes.Length);
        
        probe.FeatureVector = vec; // float[]
        subjectYoung.FeatureVector = vecBytes; // Perfect match
        subjectOld.FeatureVector = vecBytes; // Perfect match
        
        // Old Logic:
        // Candidates filtered. Filtered list has only Old.
        // Matches calculated for Old -> Score 1.0.
        // Result has 1 match (Old).

        probe.FeatureVector = vec; // float[]
        subjectYoung.FeatureVector = vecBytes; // Perfect match
        subjectOld.FeatureVector = vecBytes; // Perfect match
        
        var candidates = new List<Subject> { subjectYoung, subjectOld };
        var criteria = new MatchingCriteria { MinAge = 50 }; // Only old allowed

        var result = _service.FindMatches(probe, candidates, criteria);
        
        result.Should().HaveCount(1);
        result[0].Subject.Should().Be(subjectOld);
    }
}
