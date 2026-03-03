using System;
using DentalID.Application.Services;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace DentalID.Tests.Services;

public class TensorPreparationServiceTests
{
    private readonly TensorPreparationService _service = new();

    [Fact]
    public void PrepareDetectionTensor_ShouldMaintainAspectRatio_AndPad()
    {
        // 100x50 image. Target 640.
        // Scale should be 640/100 = 6.4 (width dom) or 640/50 = 12.8 (height dom).
        // Min scale is 6.4.
        // New Width: 100 * 6.4 = 640.
        // New Height: 50 * 6.4 = 320.
        // PadX: (640 - 640)/2 = 0.
        // PadY: (640 - 320)/2 = 160.
        
        using var bmp = new SKBitmap(100, 50);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.White); // Fill white for potential value check

        var (tensor, scale, padX, padY) = _service.PrepareDetectionTensor(bmp, 640);

        scale.Should().Be(6.4f);
        padX.Should().Be(0);
        padY.Should().Be(160);
        
        tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
        
        // Check center pixel (should be white -> 1.0f)
        // Center of original is maintained in center of tensor?
        // Yes, padding centers it.
        // Y=320, X=320.
        // White is RGB 255,255,255. Normalized 0-1.
        // tensor[0, 0, 320, 320].Should().Be(1.0f);
    }
    
    [Fact]
    public void PrepareEncoderTensor_ShouldResizeToTarget_AndCreateHWC()
    {
        // 100x100 image. Target 1024.
        using var bmp = new SKBitmap(100, 100);
        
        var tensor = _service.PrepareEncoderTensor(bmp, 1024);
        
        // H, W, C
        tensor.Dimensions.ToArray().Should().Equal(1024, 1024, 3);
    }
    
    [Fact]
    public void PrepareAgeGenderTensor_ShouldCreateBGR_NCHW()
    {
        // 50x50. Target 96.
        using var bmp = new SKBitmap(50, 50);
        using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Red); } // R=255, G=0, B=0
        
        var tensor = _service.PrepareAgeGenderTensor(bmp, 96);
        
        tensor.Dimensions.ToArray().Should().Equal(1, 3, 96, 96);
        
        // BGR check
        // Channel 0 is B (should be 0)
        // Channel 1 is G (should be 0)
        // Channel 2 is R (should be 255)
        
        tensor[0, 0, 50, 50].Should().Be(0);
        tensor[0, 1, 50, 50].Should().Be(0);
        tensor[0, 2, 50, 50].Should().Be(255);
    }

    [Fact]
    public void PrepareDetectionTensor_SquareImage_ShouldHaveZeroPadding()
    {
        // A 640×640 input scaled to a 640 target should have no letterboxing at all.
        using var bmp = new SKBitmap(640, 640);

        var (tensor, scale, padX, padY) = _service.PrepareDetectionTensor(bmp, 640);

        scale.Should().Be(1.0f);
        padX.Should().Be(0);
        padY.Should().Be(0);
        tensor.Dimensions.ToArray().Should().Equal(1, 3, 640, 640);
    }

    [Fact]
    public void PrepareDetectionTensor_WhitePixel_ShouldBeNormalized_1()
    {
        // A pure-white image's pixels should be normalized to exactly 1.0.
        using var bmp = new SKBitmap(640, 640);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.White);

        var (tensor, _, _, _) = _service.PrepareDetectionTensor(bmp, 640);

        // Sample center pixel – all three RGB channels should be 1.0f
        tensor[0, 0, 320, 320].Should().Be(1.0f);
        tensor[0, 1, 320, 320].Should().Be(1.0f);
        tensor[0, 2, 320, 320].Should().Be(1.0f);
    }

    [Fact]
    public void PrepareDetectionTensor_BlackPixel_ShouldBeNormalized_0()
    {
        // A pure-black image's pixels should be normalized to exactly 0.0.
        using var bmp = new SKBitmap(640, 640);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.Black);

        var (tensor, _, _, _) = _service.PrepareDetectionTensor(bmp, 640);

        tensor[0, 0, 320, 320].Should().Be(0.0f);
        tensor[0, 1, 320, 320].Should().Be(0.0f);
        tensor[0, 2, 320, 320].Should().Be(0.0f);
    }
}
