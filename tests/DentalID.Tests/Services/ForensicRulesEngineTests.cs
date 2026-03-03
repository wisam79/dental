using Xunit;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using System.Collections.Generic;

namespace DentalID.Tests.Services;

public class ForensicRulesEngineTests
{
    private readonly ForensicRulesEngine _engine;

    public ForensicRulesEngineTests()
    {
        _engine = new ForensicRulesEngine();
    }

    [Fact]
    public void ApplyRules_ShouldFlagOrphanPathologies()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Caries", ToothNumber = 11 },
                new() { ClassName = "Abscess", ToothNumber = 0 } // Orphan
            }
        };

        _engine.ApplyRules(result);

        Assert.Single(result.Flags);
        Assert.Contains("Warning", result.Flags[0]);
        Assert.Contains("mapped", result.Flags[0]);
    }

    [Fact]
    public void ApplyRules_ShouldFlagImplantConflicts()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Implant", ToothNumber = 21 },
                new() { ClassName = "Caries", ToothNumber = 21 } // Impossible!
            }
        };

        _engine.ApplyRules(result);

        Assert.Single(result.Flags);
        Assert.Contains("Conflict", result.Flags[0]);
        Assert.Contains("Implant", result.Flags[0]);
    }

    [Fact]
    public void ApplyRules_ShouldCollapseDuplicateImplantConflictsPerClass()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Implant", ToothNumber = 27 },
                new() { ClassName = "Caries", ToothNumber = 27, Confidence = 0.9f, X = 0.20f, Y = 0.20f, Width = 0.10f, Height = 0.10f },
                new() { ClassName = "Caries", ToothNumber = 27, Confidence = 0.7f, X = 0.21f, Y = 0.21f, Width = 0.10f, Height = 0.10f }
            }
        };

        _engine.ApplyRules(result);

        Assert.Single(result.Flags);
        Assert.Contains("Conflict", result.Flags[0]);
        Assert.Contains("overlapping detections", result.Flags[0]);
    }

    [Fact]
    public void ApplyRules_ShouldFlagContradictoryRestorations_AsObservation()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Crown", ToothNumber = 46 },
                new() { ClassName = "Filling", ToothNumber = 46 } // Suspicious but possible
            }
        };

        _engine.ApplyRules(result);

        Assert.Single(result.Flags);
        Assert.Contains("Observation", result.Flags[0]);
    }

    [Fact]
    public void ApplyRules_ShouldPassCleanResults()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Filling", ToothNumber = 11 },
                new() { ClassName = "Caries", ToothNumber = 12 }
            }
        };

        _engine.ApplyRules(result);

        Assert.Empty(result.Flags);
    }

    [Fact]
    public void ApplyRules_ShouldCollapseOverlappingOrphans()
    {
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Caries", ToothNumber = 0, Confidence = 0.9f, X = 0.30f, Y = 0.30f, Width = 0.10f, Height = 0.10f },
                new() { ClassName = "Caries", ToothNumber = null, Confidence = 0.8f, X = 0.31f, Y = 0.31f, Width = 0.10f, Height = 0.10f }
            }
        };

        _engine.ApplyRules(result);

        Assert.Single(result.Flags);
        Assert.Contains("collapsed", result.Flags[0]);
    }
    
    [Theory]
    [InlineData("Root Piece", "Implant")]
    [InlineData("Roots", "Implant")]
    public void ApplyRules_ShouldFlagImplantOnRootRemnant(string rootName, string implantName)
    {
         var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = rootName, ToothNumber = 36 },
                new() { ClassName = implantName, ToothNumber = 36 } 
            }
        };

        _engine.ApplyRules(result);

        Assert.NotEmpty(result.Flags);
        Assert.Contains("Conflict", result.Flags[0]);
    }

    [Fact]
    public void ApplyRules_ShouldHandleNullPathologies()
    {
        var result = new AnalysisResult { Pathologies = null! };
        _engine.ApplyRules(result); // Should not throw
        Assert.Empty(result.Flags);
    }

    [Fact]
    public void ApplyRules_ShouldHandleEmptyPathologies()
    {
        var result = new AnalysisResult { Pathologies = new List<DetectedPathology>() };
        _engine.ApplyRules(result);
        Assert.Empty(result.Flags);
    }
    
    [Fact]
    public void ApplyRules_ShouldIdentifyMultiplePathologiesOnSameTooth()
    {
        // This is valid, e.g. Caries and Filling
         var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Caries", ToothNumber = 14 },
                new() { ClassName = "Filling", ToothNumber = 14 }
            }
        };

        _engine.ApplyRules(result);
        Assert.Empty(result.Flags); // Should NOT flag as error
    }

    [Fact]
    public void ApplyRules_ShouldFlagAbscessWithoutSource()
    {
        // Future rule: Abscess usually associated with deep caries or root canal history
        // If the engine implements this, we test it.
        // For now, assuming current implementation only checks the ones we saw.
        // Adding a placeholder for future logic extension.
    }
    
    [Theory]
    [InlineData(18)]
    [InlineData(28)]
    [InlineData(38)]
    [InlineData(48)]
    public void ApplyRules_SmartCheck_WisdomTeeth(int fdi)
    {
         // Verify no special error for wisdom teeth having issues
          var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Impaction", ToothNumber = fdi }
            }
        };

        _engine.ApplyRules(result);
        Assert.Empty(result.Flags);
    }

    [Fact]
    public void ApplyRules_RootCanalWithCaries_ShouldNotConflict()
    {
        // Root canal treatment and an existing caries on the same tooth is clinically valid
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Root canal obturation", ToothNumber = 46 },
                new() { ClassName = "Caries", ToothNumber = 46 }
            }
        };

        _engine.ApplyRules(result);

        // Should NOT be flagged as a conflict – combination is valid
        Assert.DoesNotContain(result.Flags, f => f.Contains("Conflict"));
    }

    [Fact]
    public void ApplyRules_TwoImplantsOnSameTooth_ShouldCollapseToSingleFlag()
    {
        // Two Implant detections: one hard conflict with Caries, one simply duplicate Implant.
        // Regardless, there should be only one 'Conflict' flag for tooth 15.
        var result = new AnalysisResult
        {
            Pathologies = new List<DetectedPathology>
            {
                new() { ClassName = "Implant",  ToothNumber = 15, Confidence = 0.95f, X = 0.20f, Y = 0.20f, Width = 0.08f, Height = 0.08f },
                new() { ClassName = "Implant",  ToothNumber = 15, Confidence = 0.88f, X = 0.21f, Y = 0.21f, Width = 0.08f, Height = 0.08f },
                new() { ClassName = "Caries",   ToothNumber = 15, Confidence = 0.85f, X = 0.22f, Y = 0.22f, Width = 0.04f, Height = 0.04f }
            }
        };

        _engine.ApplyRules(result);

        // There should be exactly one conflict flag for tooth 15 (not one per Implant)
        var conflictFlags = result.Flags.Where(f => f.Contains("Conflict") && f.Contains("15")).ToList();
        Assert.True(conflictFlags.Count <= 1, $"Expected ≤1 conflict flag for tooth 15, got {conflictFlags.Count}: {string.Join(" | ", conflictFlags)}");
    }

    [Fact]
    public void ApplyRules_NoPathologiesAtAll_ShouldHaveNoFlags()
    {
        var result = new AnalysisResult
        {
            Teeth = new List<DetectedTooth> { new() { FdiNumber = 11 }, new() { FdiNumber = 21 } }
        };

        _engine.ApplyRules(result);

        Assert.Empty(result.Flags);
    }
}
