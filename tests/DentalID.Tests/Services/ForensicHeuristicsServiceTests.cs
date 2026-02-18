using System.Collections.Generic;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace DentalID.Tests.Services;

public class ForensicHeuristicsServiceTests
{
    private readonly ForensicHeuristicsService _service;

    public ForensicHeuristicsServiceTests()
    {
        _service = new ForensicHeuristicsService();
    }

    [Fact]
    public void CalculateIoU_ShouldReturnCorrectValue()
    {
        // No overlap
        ForensicHeuristicsService.CalculateIoU(0, 0, 10, 10, 20, 20, 10, 10).Should().Be(0);

        // Full overlap
        ForensicHeuristicsService.CalculateIoU(0, 0, 10, 10, 0, 0, 10, 10).Should().Be(1);

        // 50% intersection of area (e.g. two 10x10 squares sharing 5x10 half)
        // Box1: 0,0 10x10 (Area 100)
        // Box2: 5,0 10x10 (Area 100)
        // Intersect: 5,0 w=5, h=10 (Area 50)
        // Union: 100 + 100 - 50 = 150
        // IoU: 50/150 = 0.333...
        ForensicHeuristicsService.CalculateIoU(0, 0, 10, 10, 5, 0, 10, 10).Should().BeApproximately(0.333f, 0.01f);
    }

    [Fact]
    public void ApplyChecks_ShouldFlagUnusualToothCount()
    {
        var result = new AnalysisResult { RawTeeth = new List<DetectedTooth>(new DetectedTooth[41]) }; // 41 teeth
        
        _service.ApplyChecks(result);

        result.Flags.Should().ContainMatch("*Unusual tooth count*");
    }

    [Fact]
    public void ApplyChecks_ShouldFlagSevereAsymmetry()
    {
        var teeth = new List<DetectedTooth>();
        // Add 10 teeth to Quadrant 1 (Right Upper) -> Fdi 11..18 (8 teeth) + extras? 
        // Logic checks FdiNumber/10 == 1 or 4 vs 2 or 3.
        
        // Right side (1x, 4x): 15 teeth
        for(int i=0; i<15; i++) teeth.Add(new DetectedTooth { FdiNumber = 11 }); 
        
        // Left side (2x, 3x): 2 teeth
        for(int i=0; i<2; i++) teeth.Add(new DetectedTooth { FdiNumber = 21 });

        // Total > 10. Diff (13) > 6.
        
        var result = new AnalysisResult { RawTeeth = teeth };
        
        _service.ApplyChecks(result);

        result.Flags.Should().ContainMatch("*Severe bilateral asymmetry*");
    }

    [Fact]
    public void ApplyChecks_ShouldFlagHighOverlapDensity()
    {
        var teeth = new List<DetectedTooth>();
        // Add 5 teeth perfectly overlapping each other
        for(int i=0; i<5; i++) 
        {
            teeth.Add(new DetectedTooth { X=0.5f, Y=0.5f, Width=0.1f, Height=0.1f, Confidence=0.9f });
        }

        var result = new AnalysisResult { RawTeeth = teeth };
        
        _service.ApplyChecks(result);

        result.Flags.Should().ContainMatch("*high-density overlaps*");
    }
}
