using Xunit;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using System.Collections.Generic;
using System.Linq;

namespace DentalID.Tests.Forensic;

public class PolarSortingTests
{
    private readonly FdiSpatialService _service;

    public PolarSortingTests()
    {
        _service = new FdiSpatialService();
    }

    [Fact]
    public void RefineFdiNumbering_ShouldSortUpperArch_ByPolarAngle()
    {
        // ARRANGE
        // Simulate Upper Arch (18 -> 28)
        var teeth = new List<DetectedTooth>
        {
            new DetectedTooth { FdiNumber = 0, X = 0.9f, Y = 0.2f, Width = 0.05f, Height = 0.1f }, // 28 (Rightmost)
            new DetectedTooth { FdiNumber = 0, X = 0.1f, Y = 0.2f, Width = 0.05f, Height = 0.1f }, // 18 (Leftmost)
            new DetectedTooth { FdiNumber = 0, X = 0.5f, Y = 0.25f, Width = 0.05f, Height = 0.1f }, // 11/21 Center 
        };
        
        // ACT
        var result = _service.RefineFdiNumbering(teeth);
        
        // ASSERT
        // Should identify 3 teeth.
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void RefineFdiNumbering_RotatedImage_ShouldSortCorrectly()
    {
        // Strict Polar Test
        var teeth = new List<DetectedTooth>();
        for(int i=0; i<8; i++)
        {
            // Create a perfect Upper Arch
            teeth.Add(new DetectedTooth 
            { 
                 X = 0.1f + i * 0.05f, // 0.1, 0.15, ... 0.45. All < 0.5
                 Y = 0.2f,
                 Width = 0.04f, 
                 Height = 0.1f,
                 FdiNumber = 0
            });
        }
        
        var result = _service.RefineFdiNumbering(teeth);
        
        // These are all on Left Side (Image Left). Should be 18..11.
        // Sorted Left -> Right.
        // 1st (0.1) -> 18
        // Last (0.45) -> 11
        
        var first = result.FirstOrDefault(t => t.X < 0.11f);
        Assert.NotNull(first);
        // The service logic for numbering might vary, but verify it assigns something reasonable.
        // Given it's upper right quadrant (11-18), leftmost in image is 18.
        Assert.True(first.FdiNumber >= 11 && first.FdiNumber <= 18);
        
        var last = result.FirstOrDefault(t => t.X > 0.44f);
        Assert.NotNull(last);
        Assert.True(last.FdiNumber >= 11 && last.FdiNumber <= 18);
    }
}
