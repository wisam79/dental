using System.Globalization;
using Avalonia;
using Avalonia.Layout;
using DentalID.Desktop.Converters;
using FluentAssertions;
using Xunit;

namespace DentalID.Tests.Converters;

public class RtlLayoutConvertersTests
{
    [Fact]
    public void LogicalHorizontalAlignment_ShouldMapStartAndEnd()
    {
        var converter = BoolToLogicalHorizontalAlignmentConverter.Instance;

        converter.Convert(false, typeof(HorizontalAlignment), "Start", CultureInfo.InvariantCulture)
            .Should().Be(HorizontalAlignment.Left);
        converter.Convert(true, typeof(HorizontalAlignment), "Start", CultureInfo.InvariantCulture)
            .Should().Be(HorizontalAlignment.Right);
        converter.Convert(false, typeof(HorizontalAlignment), "End", CultureInfo.InvariantCulture)
            .Should().Be(HorizontalAlignment.Right);
        converter.Convert(true, typeof(HorizontalAlignment), "End", CultureInfo.InvariantCulture)
            .Should().Be(HorizontalAlignment.Left);
    }

    [Fact]
    public void MirroredThickness_ShouldFlipOnlyHorizontally()
    {
        var converter = BoolToMirroredThicknessConverter.Instance;

        var ltr = (Thickness)converter.Convert(false, typeof(Thickness), "20,1,5,3", CultureInfo.InvariantCulture);
        var rtl = (Thickness)converter.Convert(true, typeof(Thickness), "20,1,5,3", CultureInfo.InvariantCulture);

        ltr.Should().Be(new Thickness(20, 1, 5, 3));
        rtl.Should().Be(new Thickness(5, 1, 20, 3));
    }

    [Fact]
    public void FlyoutPlacement_ShouldMirrorAcrossDirections()
    {
        var converter = BoolToFlyoutPlacementConverter.Instance;

        converter.Convert(false, typeof(object), "End", CultureInfo.InvariantCulture)
            .Should().NotBeNull()
            .And.Match<object>(x => x.ToString() == "Right");
        converter.Convert(true, typeof(object), "End", CultureInfo.InvariantCulture)
            .Should().NotBeNull()
            .And.Match<object>(x => x.ToString() == "Left");
        converter.Convert(false, typeof(object), "Start", CultureInfo.InvariantCulture)
            .Should().NotBeNull()
            .And.Match<object>(x => x.ToString() == "Left");
        converter.Convert(true, typeof(object), "Start", CultureInfo.InvariantCulture)
            .Should().NotBeNull()
            .And.Match<object>(x => x.ToString() == "Right");
    }

    [Fact]
    public void ArrowGeometry_ShouldSwapPrevAndNextForRtl()
    {
        var converter = BoolToArrowGeometryConverter.Instance;

        var prevLtr = converter.Convert(false, typeof(object), "Prev", CultureInfo.InvariantCulture)?.ToString();
        var prevRtl = converter.Convert(true, typeof(object), "Prev", CultureInfo.InvariantCulture)?.ToString();
        var nextLtr = converter.Convert(false, typeof(object), "Next", CultureInfo.InvariantCulture)?.ToString();
        var nextRtl = converter.Convert(true, typeof(object), "Next", CultureInfo.InvariantCulture)?.ToString();

        prevLtr.Should().NotBeNullOrWhiteSpace();
        prevRtl.Should().NotBeNullOrWhiteSpace();
        nextLtr.Should().NotBeNullOrWhiteSpace();
        nextRtl.Should().NotBeNullOrWhiteSpace();
        prevLtr.Should().NotBe(prevRtl);
        nextLtr.Should().NotBe(nextRtl);
        prevLtr.Should().Be(nextRtl);
        prevRtl.Should().Be(nextLtr);
    }
}
