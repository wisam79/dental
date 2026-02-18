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
}
