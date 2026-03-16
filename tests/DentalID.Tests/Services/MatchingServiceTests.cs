using Xunit;
using Moq;
using DentalID.Application.Services;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using System.Collections.Generic;
using System.Linq;

namespace DentalID.Tests.Services;

public class MatchingServiceTests
{
    private readonly MatchingService _matchingService;
    private readonly Mock<IBiometricService> _mockBiometricService;

    public MatchingServiceTests()
    {
        _mockBiometricService = new Mock<IBiometricService>();
        _matchingService = new MatchingService(_mockBiometricService.Object);
    }

    [Fact]
    public void FindMatches_ShouldReturnBiometricMatches_WhenFingerprintExists()
    {
        // Arrange
        var probe = new DentalFingerprint { Code = "18:I" };
        var candidateSubject = new Subject { Id = 1, FullName = "John Doe" };
        var candidateImage = new DentalImage 
        { 
            SubjectId = 1, 
            FingerprintCode = "18:I",
            Subject = candidateSubject
        };
        candidateSubject.DentalImages.Add(candidateImage);

        var parsedFp = new DentalFingerprint { Code = "18:I" };

        _mockBiometricService.Setup(s => s.ParseFingerprintCode("18:I")).Returns(parsedFp);
        _mockBiometricService.Setup(s => s.CalculateSimilarity(probe, parsedFp)).Returns(1.0);

        // Act
        var results = _matchingService.FindMatches(probe, new[] { candidateSubject });

        // Assert
        Assert.Single(results);
        Assert.Equal(1.0, results[0].Score);
        Assert.Equal("Biometric Fingerprint", results[0].MatchMethod);
    }

    [Fact]
    public void CalculateCosineSimilarity_ShouldUseHybridWeighting_For1280LengthVectors()
    {
        // Arrange
        // v1: Perfect visual (1.0), imperfect spatial (0.5), imperfect SAM (0.5)
        // v2: Perfect visual (1.0), perfect spatial (1.0), perfect SAM (1.0)
        var v1 = new float[1280];
        var v2 = new float[1280];

        // Fill Visual (0-1023)
        for (int i = 0; i < 1024; i++) { v1[i] = 1.0f; v2[i] = 1.0f; }
        
        // Fill Spatial (1024-1183) -> v1 is orthogonal to v2 in this segment to get 0 similarity
        // but for simplicity let's just use known values.
        // Actually, let's make them identical except for one element to control similarity.
        for (int i = 1024; i < 1184; i++) { v1[i] = 0.0f; v2[i] = 0.0f; }
        v1[1024] = 1.0f; v2[1024] = 1.0f; // Identical in spatial part too for now

        // Wait, to test weighting, I need segments with different similarities.
        // Segment 1 (Deep): Identical -> Sim = 1.0
        // Segment 2 (Spatial): Orthogonal -> Sim = 0.0
        // Segment 3 (SAM): Orthogonal -> Sim = 0.0
        
        var vProbe = new float[1280];
        var vCandidate = new float[1280];
        
        // Deep: 1.0
        vProbe[0] = 1.0f; vCandidate[0] = 1.0f; 
        
        // Spatial: 0.0 (vProbe[1024]=1, vCandidate[1025]=1)
        vProbe[1024] = 1.0f; vCandidate[1025] = 1.0f;
        
        // SAM: 0.0 (vProbe[1184]=1, vCandidate[1185]=1)
        vProbe[1184] = 1.0f; vCandidate[1185] = 1.0f;

        // Expected Score = (1.0 * 0.70) + (0.0 * 0.20) + (0.0 * 0.10) = 0.70

        // Act
        var score = _matchingService.CalculateCosineSimilarity(vProbe, vCandidate);

        // Assert
        Assert.Equal(0.70, score, 0.001);
    }
}
