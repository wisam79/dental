using System.Collections.Generic;
using DentalID.Application.Services;
using DentalID.Core.DTOs;
using FluentAssertions;
using Xunit;

namespace DentalID.Tests.Services;

/// <summary>
/// Tests for DentalAgeEstimator static class – a heuristic rules engine that
/// estimates chronological age from detected FDI tooth numbers.
/// </summary>
public class DentalAgeEstimatorTests
{
    // ────── Helper ──────────────────────────────────────────────────────────

    private static DetectedTooth Tooth(int fdi) => new() { FdiNumber = fdi };

    private static List<DetectedTooth> Teeth(params int[] fdis)
    {
        var list = new List<DetectedTooth>();
        foreach (var fdi in fdis) list.Add(Tooth(fdi));
        return list;
    }

    // ────── Empty / Invalid input ───────────────────────────────────────────

    [Fact]
    public void EstimateAgeRange_EmptyList_ShouldReturnUnknown()
    {
        var (range, median) = DentalAgeEstimator.EstimateAgeRange(new List<DetectedTooth>());

        range.Should().Contain("Unknown");
        median.Should().BeNull();
    }

    [Fact]
    public void EstimateAgeRange_OnlyOutOfRangeFdi_ShouldReturnUnknown()
    {
        // FDI 90 and 99 are outside valid range (10..90)
        var (range, median) = DentalAgeEstimator.EstimateAgeRange(Teeth(90, 0, 9));

        range.Should().Contain("Unknown");
        median.Should().BeNull();
    }

    // ────── Primary / Deciduous dentition ───────────────────────────────────

    [Fact]
    public void EstimateAgeRange_DeciduousOnly_ShouldBeUnder6()
    {
        // FDI 51-55, 61-65, 71-75, 81-85 are deciduous (50..85)
        var teeth = Teeth(51, 52, 61, 62, 71, 72, 81, 82);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("Under 6");
        median.Should().Be(5);
    }

    [Fact]
    public void EstimateAgeRange_MixedDentition_ShouldBe6to12()
    {
        // Mix of deciduous (5x) AND permanent (1x-4x)
        var teeth = Teeth(51, 52, 61, 11, 21, 31, 41); // deciduous + some permanent

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("6");
        range.Should().Contain("12");
        median.Should().Be(9);
    }

    // ────── Adult: wisdom teeth ──────────────────────────────────────────────

    [Fact]
    public void EstimateAgeRange_AllFourWisdomTeeth_ShouldBeOver21()
    {
        // 18, 28, 38, 48 = all four wisdom teeth
        var teeth = Teeth(18, 28, 38, 48, 11, 21, 31, 41);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("21");
        median.Should().Be(25);
    }

    [Fact]
    public void EstimateAgeRange_OneWisdomTooth_ShouldBeLateAdolescence()
    {
        // Only 18 present from wisdom teeth group
        var teeth = Teeth(18, 11, 12, 21, 22, 31, 32, 41, 42);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("18");
        range.Should().Contain("21");
        median.Should().Be(20);
    }

    [Fact]
    public void EstimateAgeRange_TwoWisdomTeeth_ShouldBeLateAdolescence()
    {
        var teeth = Teeth(18, 28, 11, 21, 31, 41);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("18");
        range.Should().Contain("21");
        median.Should().Be(20);
    }

    // ────── Early adolescence: second molars ─────────────────────────────────

    [Fact]
    public void EstimateAgeRange_AllSecondMolars_NoWisdomTeeth_ShouldBe12to15()
    {
        // 17, 27, 37, 47 = all second molars. No wisdom teeth.
        var teeth = Teeth(17, 27, 37, 47, 11, 21, 31, 41, 12, 22, 32, 42);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("12");
        range.Should().Contain("15");
        median.Should().Be(14);
    }

    // ────── Late childhood: canines + premolars ──────────────────────────────

    [Fact]
    public void EstimateAgeRange_CaninesAndPremolars_NoSecondMolars_ShouldBe9to12()
    {
        // 13=canine, 14=first premolar. No second molars (17, 27, 37, 47 absent).
        var teeth = Teeth(13, 14, 23, 24, 11, 21, 31, 41);

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("9");
        range.Should().Contain("12");
        median.Should().Be(11);
    }

    // ────── Assumed adult (many permanent teeth, no wisdom) ──────────────────

    [Fact]
    public void EstimateAgeRange_24OrMoreTeeth_NoWisdom_ShouldBeAssumedAdult()
    {
        // 24 permanent teeth without wisdom teeth = assumed adult
        var teeth = Teeth(
            11, 12, 13, 14, 15, 16,
            21, 22, 23, 24, 25, 26,
            31, 32, 33, 34, 35, 36,
            41, 42, 43, 44, 45, 46
        );

        var (range, median) = DentalAgeEstimator.EstimateAgeRange(teeth);

        range.Should().Contain("18");
        median.Should().Be(25);
    }

    // ────── Theory: every distinct "path" returns a non-null string ───────────

    [Theory]
    [InlineData(new[] { 51, 61, 71, 81 }, "Under 6")]               // pure deciduous
    [InlineData(new[] { 51, 11, 21, 31, 41 }, "6")]                  // mixed dentition
    [InlineData(new[] { 18, 28, 38, 48, 11, 21 }, "21")]             // all wisdom teeth
    [InlineData(new[] { 18, 11, 21, 31, 41 }, "18")]                 // one wisdom tooth
    [InlineData(new[] { 17, 27, 37, 47, 11, 21, 31, 41, 12, 22 }, "12")] // all 2nd molars
    public void EstimateAgeRange_Theory_ShouldContainExpectedText(int[] fdis, string expectedSubstring)
    {
        var (range, _) = DentalAgeEstimator.EstimateAgeRange(Teeth(fdis));
        range.Should().Contain(expectedSubstring);
    }
}
