using System;
using System.Collections.Generic;
using Xunit;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using Moq;
using DentalID.Core.Interfaces;

namespace DentalID.Tests.Services
{
    public class BiometricTests
    {
        [Fact]
        public void CosineSimilarity_ShouldReturnOne_ForIdenticalVectors()
        {
            // Arrange
            var matcher = new MatchingService(new Mock<IBiometricService>().Object);
            float[] v1 = { 1.0f, 0.0f, 0.5f };
            float[] v2 = { 1.0f, 0.0f, 0.5f };

            // Act
            double score = matcher.CalculateCosineSimilarity(v1, v2);

            // Assert
            Assert.Equal(1.0, score, 4);
        }

        [Fact]
        public void CosineSimilarity_ShouldReturnZero_ForOrthogonalVectors()
        {
            // Arrange
            var matcher = new MatchingService(new Mock<IBiometricService>().Object);
            float[] v1 = { 1.0f, 0.0f };
            float[] v2 = { 0.0f, 1.0f };

            // Act
            double score = matcher.CalculateCosineSimilarity(v1, v2);

            // Assert
            Assert.Equal(0.0, score, 4);
        }

        [Fact]
        public void FindMatches_ShouldPrioritizeVectorMatch()
        {
            // Arrange
            var matcher = new MatchingService(new Mock<IBiometricService>().Object);
            
            var probe = new DentalFingerprint 
            { 
                FeatureVector = new float[] { 1.0f, 1.0f } 
            };

            var candidate1 = new Subject { Id = 1, SubjectId = "c1", FullName = "Match" };
            var candidate2 = new Subject { Id = 2, SubjectId = "c2", FullName = "NoMatch" };
            
            // Create byte array for { 1.0f, 1.0f }
            var matchBytes = new byte[8];
            Buffer.BlockCopy(new float[] { 1.0f, 1.0f }, 0, matchBytes, 0, 8);
            candidate1.FeatureVector = matchBytes;
             // Add dummy image to trigger matching logic
             candidate1.DentalImages = new List<DentalImage> { new DentalImage { Id = 1 } };

            // Create byte array for { -1.0f, -1.0f } (Opposite)
            var noMatchBytes = new byte[8];
            Buffer.BlockCopy(new float[] { -1.0f, -1.0f }, 0, noMatchBytes, 0, 8);
            candidate2.FeatureVector = noMatchBytes;
             candidate2.DentalImages = new List<DentalImage> { new DentalImage { Id = 2 } };

            var candidates = new List<Subject> { candidate1, candidate2 };

            // Act
            var matches = matcher.FindMatches(probe, candidates);

            // Assert
            Assert.NotEmpty(matches);
            Assert.Equal("c1", matches[0].Subject.SubjectId);
            Assert.Equal(1.0, matches[0].Score, 1); // Should be close to 1
        }
    }
}
