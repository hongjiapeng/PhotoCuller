using FluentAssertions;
using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;
using PhotoCuller.Infrastructure.AiProviders.LocalModels;
using PhotoCuller.Infrastructure.ImageProcessing;

namespace PhotoCuller.Tests.CoreTests;

public sealed class AestheticScoringTests
{
    [Theory]
    [InlineData(0.75f, OnnxAestheticModelProvider.PresetZeroToOne, 0.75f)]
    [InlineData(0.0f, OnnxAestheticModelProvider.PresetMinusOneToOne, 0.5f)]
    [InlineData(5.5f, OnnxAestheticModelProvider.PresetOneToTen, 0.5f)]
    [InlineData(20f, OnnxAestheticModelProvider.PresetOneToTen, 1f)]
    public void NormalizeOutput_MapsSupportedRanges(
        float raw,
        string preset,
        float expected)
    {
        OnnxAestheticModelProvider.NormalizeOutput(raw, preset)
            .Should().BeApproximately(expected, 0.0001f);
    }

    [Fact]
    public void ComputeOverall_BlendsAestheticWithTechnicalScore()
    {
        var photo = new PhotoItem
        {
            SharpnessScore = 80,
            ExposureScore = 80,
            ContrastScore = 80,
            NoiseScore = 80,
            ResolutionScore = 80,
            CompositionScore = 80,
            ColorScore = 80,
            AestheticScore = 40,
        };

        var score = PhotoScoringService.ComputeOverall(
            photo,
            ScoringWeights.Default,
            aestheticBlendPercent: 25);

        score.Should().BeApproximately(70f, 0.001f);
    }

    [Fact]
    public void ComputeOverall_IgnoresBlendWhenModelScoreMissing()
    {
        var photo = new PhotoItem
        {
            SharpnessScore = 80,
            ExposureScore = 80,
            ContrastScore = 80,
            NoiseScore = 80,
            ResolutionScore = 80,
            CompositionScore = 80,
            ColorScore = 80,
        };

        var score = PhotoScoringService.ComputeOverall(
            photo,
            ScoringWeights.Default,
            aestheticBlendPercent: 25);

        score.Should().BeApproximately(80f, 0.001f);
    }
}
