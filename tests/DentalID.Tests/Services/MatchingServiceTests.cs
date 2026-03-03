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
    public void CalculateCosineSimilarity_ShouldReturnCorrectScore()
    {
        // Arrange
        float[] v1 = { 1, 0, 0 };
        float[] v2 = { 0, 1, 0 };
        float[] v3 = { 1, 0, 0 };

        // Act
        var scoreOrthogonal = _matchingService.CalculateCosineSimilarity(v1, v2);
        var scoreIdentical = _matchingService.CalculateCosineSimilarity(v1, v3);

        // Assert
        Assert.Equal(0, scoreOrthogonal, 0.001);
        Assert.Equal(1.0, scoreIdentical, 0.001);
    }

}
