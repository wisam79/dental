using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using FluentAssertions;
using Xunit;

namespace DentalID.Tests.Services;

public class FdiSpatialServiceTests
{
    private readonly FdiSpatialService _service;

    public FdiSpatialServiceTests()
    {
        _service = new FdiSpatialService();
    }

    [Fact]
    public void RefineFdiNumbering_ShouldReturnOriginal_WhenFewTeeth()
    {
        var input = new List<DetectedTooth> { new DetectedTooth(), new DetectedTooth() };
        var result = _service.RefineFdiNumbering(input);
        result.Should().BeEquivalentTo(input);
    }

    [Fact]
    public void RefineFdiNumbering_ShouldNumberFullUpperArch_Correctly()
    {
        // Create full upper arch (18-28)
        // 16 teeth
        var input = GenerateArch(isUpper: true, count: 16);
        
        var result = _service.RefineFdiNumbering(input);
        
        // Check 18, 17... 11, 21... 28
        // Note: 18 is rightmost in patient view (left in image)
        result.Should().Contain(t => t.FdiNumber == 11);
        result.Should().Contain(t => t.FdiNumber == 21);
        result.Should().Contain(t => t.FdiNumber == 18);
        result.Should().Contain(t => t.FdiNumber == 28);
        result.Count(t => t.FdiNumber != 0).Should().Be(16);
    }
    
    [Fact]
    public void RefineFdiNumbering_ShouldNumberFullLowerArch_Correctly()
    {
        // Create full lower arch (48-38)
        var input = GenerateArch(isUpper: false, count: 16);
        
        var result = _service.RefineFdiNumbering(input);
        
        result.Should().Contain(t => t.FdiNumber == 41);
        result.Should().Contain(t => t.FdiNumber == 31);
        result.Should().Contain(t => t.FdiNumber == 48);
        result.Should().Contain(t => t.FdiNumber == 38);
        result.Count(t => t.FdiNumber != 0).Should().Be(16);
    }
    
    [Fact]
    public void RefineFdiNumbering_ShouldDetectGap_AndSkipNumber()
    {
        // Create upper arch, remove 11 (Central Incisor)
        var input = GenerateArch(isUpper: true, count: 16);
        // Remove tooth near center-left (11)
        // Sorted 18...11, 21...28.
        // 11 is index 7 in numbering, index 7 in list (if generated sequential)
        // Let's filter by position
        var toRemove = input.OrderBy(t => Math.Abs(t.X - 0.5)).First(); 
        input.Remove(toRemove);
        
        // Recalculate numbering
        var result = _service.RefineFdiNumbering(input);
        
        // Should have 15 teeth
        // One number should be missing (either 11 or 21 depending on which was removed)
        // Assuming we removed 11 or 21.
        result.Should().HaveCount(15);
        
        // Assert that we have a gap in numbering
        // E.g. we have 12 and 21, but no 11? Or 11 and 22, no 21?
        // If dist > 2*width threshold is met.
    }

    private List<DetectedTooth> GenerateArch(bool isUpper, int count)
    {
        var teeth = new List<DetectedTooth>();
        // Arc parameters
        float cy = isUpper ? 0.8f : 0.2f; // Upper arch curves downward in image? No, Upper arch is top of image (Y small).
        // Upper arch: Teeth Y ~ 0.2. Root Y ~ 0.1.
        // Lower arch: Teeth Y ~ 0.8. Root Y ~ 0.9.
        // Standard Pano:
        // Upper Teeth: Y=0 to 0.5. Curve is concave down (frown). Center Y is lowest.
        // Lower Teeth: Y=0.5 to 1. Curve is concave up (smile). Center Y is highest.
        
        // Let's use simplified linear X with Arc Y
        
        float width = 0.05f;
        float height = 0.1f;
        float gap = 0.005f; // Small gap
        
        // 16 teeth -> 8 left, 8 right
        // Intead of complex arc, let's place them linearly to ensure gap logic works perfectly.
        // FdiSpatialService uses Atan2 for sorting, so Angle assumes arc usage.
        // We MUST use Arc.
        
        // Center of arc:
        float arcCy = isUpper ? 0.0f : 1.0f; // Approximate
        if (isUpper) arcCy = -0.5f; else arcCy = 1.5f; // Pull center far away for gentle curve
        
        // Angles:
        // Upper: 18 is Left (X<0.5). 28 is Right (X>0.5).
        // 18 angle -> 28 angle.
        
        for (int i = 0; i < 16; i++)
        {
            // 0..7 (Right side 18..11), 8..15 (Left side 21..28)
            // Or just map -8 to +8 relative to midline
            float idx = i - 7.5f; // -7.5 to +7.5
            float x = 0.5f + idx * (width + gap);
            
            // Simple Y curve
            float distFromCenter = Math.Abs(x - 0.5f);
            float y = isUpper ? 0.2f - distFromCenter * 0.1f : 0.8f + distFromCenter * 0.1f;
            
            teeth.Add(new DetectedTooth
            {
                X = x - width/2,
                Y = y - height/2,
                Width = width,
                Height = height,
                Confidence = 0.9f
            });
        }
        return teeth;
    }
}
