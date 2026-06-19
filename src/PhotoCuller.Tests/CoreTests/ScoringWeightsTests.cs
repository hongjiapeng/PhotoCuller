using FluentAssertions;
using PhotoCuller.Core.Scoring;

namespace PhotoCuller.Tests.CoreTests;

public sealed class ScoringWeightsTests
{
    [Fact]
    public void Default_SumsTo100()
    {
        ScoringWeights.Default.Total.Should().Be(100);
        ScoringWeights.Default.IsValid().Should().BeTrue();
    }

    [Fact]
    public void Custom_ValidWhenSumsTo100()
    {
        var w = new ScoringWeights
        {
            SharpnessPercent   = 50,
            ExposurePercent    = 25,
            ContrastPercent    = 25,
        };
        w.Total.Should().Be(100);
        w.IsValid().Should().BeTrue();
    }

    [Fact]
    public void Custom_InvalidWhenNotSumsTo100()
    {
        var w = new ScoringWeights
        {
            SharpnessPercent = 40,
            ExposurePercent  = 40,
            // total = 80
        };
        w.IsValid().Should().BeFalse();
        w.Total.Should().Be(80);
    }

    [Fact]
    public void AllZero_IsInvalid()
    {
        var w = new ScoringWeights();
        w.IsValid().Should().BeFalse();
        w.Total.Should().Be(0);
    }

    [Fact]
    public void ToString_ContainsAllDimensions()
    {
        var s = ScoringWeights.Default.ToString();
        s.Should().Contain("Sharpness=");
        s.Should().Contain("Exposure=");
        s.Should().Contain("Total=100");
    }

    [Fact]
    public void ValueEquality_SameValues_AreEqual()
    {
        var a = ScoringWeights.Default;
        var b = ScoringWeights.Default;
        a.Should().Be(b);
    }
}
