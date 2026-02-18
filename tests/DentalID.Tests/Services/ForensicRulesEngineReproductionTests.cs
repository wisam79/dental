using System.Collections.Generic;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using Xunit;

namespace DentalID.Tests.Services;

public class ForensicRulesEngineReproductionTests
{
    [Fact]
    public void CorrectSequence_ShouldPreserveGaps_WhenSpacingIsCorrect()
    {
        // Arrange
        var engine = new ForensicRulesEngine();
        var result = new AnalysisResult();

        // Q4 Case: 48->41 (Descending FDI, Increasing X) for Lower Arch
        // To avoid midY issues (single line of teeth = lower arch typically in this logic), use 4x series.
        
        var t44 = new DetectedTooth { FdiNumber = 44, X = 10, Width = 10, Height = 10, Y = 10, Confidence = 0.9f };
        var t42 = new DetectedTooth { FdiNumber = 42, X = 30, Width = 10, Height = 10, Y = 10, Confidence = 0.9f }; // Gap of 10
        var t41 = new DetectedTooth { FdiNumber = 41, X = 40, Width = 10, Height = 10, Y = 10, Confidence = 0.9f };

        result.Teeth = new List<DetectedTooth> { t44, t42, t41 }; 

        // Act
        engine.ApplyRules(result);

        // Assert
        Assert.Equal(44, t44.FdiNumber);
        Assert.Equal(42, t42.FdiNumber);
        Assert.Equal(41, t41.FdiNumber);
    }

    [Fact]
    public void CorrectSequence_ShouldCorrectBlindLabeling_WhenGapIsMissing()
    {
        // Arrange
        var engine = new ForensicRulesEngine();
        var result = new AnalysisResult();

        // Scenario: Model detected 44, 42, 41 but they are tightly packed (no gap).
        // 44: X=10
        // 42: X=20 (Should be 43 if tight)
        // 41: X=30 (Should be 42 if tight)

        var t44 = new DetectedTooth { FdiNumber = 44, X = 10, Width = 10, Height = 10, Y = 10, Confidence = 0.9f };
        var t42 = new DetectedTooth { FdiNumber = 42, X = 20, Width = 10, Height = 10, Y = 10, Confidence = 0.9f }; 
        var t41 = new DetectedTooth { FdiNumber = 41, X = 30, Width = 10, Height = 10, Y = 10, Confidence = 0.9f };

        result.Teeth = new List<DetectedTooth> { t44, t42, t41 };

        // Act
        engine.ApplyRules(result);

        // Assert
        // Should rename 42 -> 43, 41 -> 42
        Assert.Equal(44, t44.FdiNumber);
        Assert.Equal(43, t42.FdiNumber); 
        Assert.Equal(42, t41.FdiNumber);
    }
}
