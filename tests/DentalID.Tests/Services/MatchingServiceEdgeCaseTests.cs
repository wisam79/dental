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

    [Fact]
    public void FindMatches_ShouldCalibrateHighBaselineCosineScores()
    {
        // Probe vector with unit norm.
        float[] probeVector = { 1.0f, 0.0f };

        // Candidate A: cosine ~0.86 (can look high as raw cosine, should be down-calibrated).
        float[] baselineLikeVector = { 0.86f, 0.5103f };
        byte[] baselineLikeBytes = new byte[baselineLikeVector.Length * sizeof(float)];
        Buffer.BlockCopy(baselineLikeVector, 0, baselineLikeBytes, 0, baselineLikeBytes.Length);

        // Candidate B: perfect match.
        float[] perfectVector = { 1.0f, 0.0f };
        byte[] perfectBytes = new byte[perfectVector.Length * sizeof(float)];
        Buffer.BlockCopy(perfectVector, 0, perfectBytes, 0, perfectBytes.Length);

        var nearBaseline = new Subject { SubjectId = "A", FeatureVector = baselineLikeBytes };
        var perfect = new Subject { SubjectId = "B", FeatureVector = perfectBytes };
        var probe = new DentalFingerprint { FeatureVector = probeVector };

        var result = _service.FindMatches(probe, new[] { nearBaseline, perfect });

        result.Should().HaveCount(2);
        result[0].Subject.SubjectId.Should().Be("B");
        result[0].Score.Should().BeApproximately(1.0, 0.0001);
        result[1].Score.Should().BeLessThan(0.2);
    }

    [Fact]
    public void FindMatches_ShouldDeflateCompressedNearPerfectVectorSpace()
    {
        const int dim = 128;
        var probeVector = new float[dim];
        for (int i = 0; i < dim; i++)
        {
            probeVector[i] = 10.0f
                + (float)Math.Sin((i + 1) * 0.013) * 0.15f
                + (float)Math.Cos((i + 3) * 0.017) * 0.10f;
        }

        var candidates = new List<Subject>();
        for (int c = 0; c < 12; c++)
        {
            float[] vector;
            if (c == 0)
            {
                vector = probeVector.ToArray();
            }
            else
            {
                vector = new float[dim];
                for (int i = 0; i < dim; i++)
                {
                    vector[i] = 10.0f
                        + (float)Math.Sin((i + 1) * (c + 1) * 0.013) * 0.15f
                        + (float)Math.Cos((i + 3) * (c + 2) * 0.017) * 0.10f;
                }
            }

            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

            candidates.Add(new Subject
            {
                SubjectId = c == 0 ? "EXACT" : $"C{c:D2}",
                FeatureVector = bytes
            });
        }

        var probe = new DentalFingerprint { FeatureVector = probeVector };
        var results = _service.FindMatches(probe, candidates);
        results.Should().NotBeEmpty();

        var scoreById = results.ToDictionary(r => r.Subject.SubjectId, r => r.Score);

        foreach (var candidate in candidates.Where(c => c.SubjectId != "EXACT"))
        {
            candidate.FeatureVector.Should().NotBeNull();
            var decoded = new float[candidate.FeatureVector!.Length / sizeof(float)];
            Buffer.BlockCopy(candidate.FeatureVector, 0, decoded, 0, candidate.FeatureVector.Length);

            var raw = _service.CalculateCosineSimilarity(probeVector, decoded);
            raw.Should().BeGreaterThan(0.99);

            var finalScore = scoreById.TryGetValue(candidate.SubjectId, out var s) ? s : 0.0;
            finalScore.Should().BeLessThan(0.95);
        }

        scoreById["EXACT"].Should().BeGreaterThan(0.95);
    }

    [Fact]
    public void FindMatches_ShouldIgnoreMalformedAnalysisJson_AndFallbackToSubjectVector()
    {
        float[] probeVector = { 1.0f, 0.0f, 0.0f };
        byte[] candidateBytes = new byte[probeVector.Length * sizeof(float)];
        Buffer.BlockCopy(probeVector, 0, candidateBytes, 0, candidateBytes.Length);

        var candidate = new Subject
        {
            SubjectId = "SUB-MALFORMED",
            FeatureVector = candidateBytes,
            DentalImages = new List<DentalImage>
            {
                new DentalImage
                {
                    AnalysisResults = "{ malformed json",
                    FingerprintCode = null
                }
            }
        };

        var probe = new DentalFingerprint { FeatureVector = probeVector };

        var results = _service.FindMatches(probe, new[] { candidate });

        results.Should().HaveCount(1);
        results[0].Subject.Should().Be(candidate);
    }
}
