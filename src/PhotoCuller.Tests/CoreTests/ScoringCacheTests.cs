using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;
using PhotoCuller.Infrastructure.ImageProcessing;
using PhotoCuller.Infrastructure.ImageProcessing.Scorers;

namespace PhotoCuller.Tests.CoreTests;

public sealed class ScoringCacheTests
{
    private readonly ISharpnessAnalyzer _sharpness = Substitute.For<ISharpnessAnalyzer>();
    private readonly IExposureAnalyzer _exposure = Substitute.For<IExposureAnalyzer>();
    private readonly IContrastAnalyzer _contrast = Substitute.For<IContrastAnalyzer>();
    private readonly INoiseAnalyzer _noise = Substitute.For<INoiseAnalyzer>();
    private readonly ICompositionAnalyzer _composition = Substitute.For<ICompositionAnalyzer>();
    private readonly IColorAnalyzer _color = Substitute.For<IColorAnalyzer>();
    private readonly ILocalAiModelProvider _ai = Substitute.For<ILocalAiModelProvider>();
    private readonly IAiModelProfileRepository _profiles = Substitute.For<IAiModelProfileRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly AppSettings _configuration = new();
    private readonly PhotoItem _photo = new() { FilePath = "photo.jpg", FileSize = 1000,
        ModifiedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Width = 2000, Height = 1000 };

    public ScoringCacheTests()
    {
        _sharpness.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
        _exposure.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(60f);
        _contrast.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
        _noise.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(70f);
        _composition.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(65f);
        _color.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(_configuration);
    }

    [Fact]
    public async Task ScoreRangeAsync_UnchangedInput_ReusesScoreWithoutDecodingAgain()
    {
        var sut = CreateSut();
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        var score = _photo.OverallScore;
        var cached = _photo.CreateSnapshot();

        await CreateSut().ScoreRangeAsync([cached], ScoringWeights.Default);

        cached.OverallScore.Should().Be(score);
        cached.ScoringSignature.Should().NotBeNullOrEmpty();
        await _sharpness.Received(1).AnalyzeAsync(_photo.FilePath, Arg.Any<CancellationToken>());
        await _exposure.Received(1).AnalyzeAsync(_photo.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreRangeAsync_ChangedWeights_RecomputesScore()
    {
        var sut = CreateSut();
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        var signature = _photo.ScoringSignature;

        await sut.ScoreRangeAsync([_photo], new ScoringWeights { ExposurePercent = 100 });

        _photo.OverallScore.Should().Be(60f);
        _photo.ScoringSignature.Should().NotBe(signature);
        await _sharpness.Received(2).AnalyzeAsync(_photo.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreRangeAsync_ChangedFileSignature_RecomputesScore()
    {
        var sut = CreateSut();
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        _photo.FileSize++;
        _sharpness.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(20f);

        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);

        _photo.SharpnessScore.Should().Be(20f);
        await _sharpness.Received(2).AnalyzeAsync(_photo.FilePath, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("preset", 59.375f)]
    [InlineData("blend", 46.25f)]
    public async Task ScoreRangeAsync_ChangedModelSetting_InvalidatesCachedScore(string setting, float expected)
    {
        var profile = EnableAi();
        var sut = CreateSut();
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        var initial = _photo.ScoringSignature;
        if (setting == "preset") profile.LocalModelPreset = "clip-aesthetic-1-10";
        else _configuration.AestheticBlendPercent = 50;
        _ai.ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>()).Returns(0.2f);

        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);

        _photo.AestheticScore.Should().Be(20f);
        _photo.OverallScore.Should().BeApproximately(expected, 0.01f);
        _photo.ScoringSignature.Should().NotBe(initial);
        await _ai.Received(2).ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreRangeAsync_FailedInference_RetriesRatherThanCachingFallback()
    {
        var profile = EnableAi();
        var sut = CreateSut();
        _ai.ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>()).Returns((float?)null);
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        _photo.ScoringSignature.Should().BeNull();
        _photo.AestheticScore.Should().BeNull();
        _photo.OverallScore.Should().BeApproximately(72.5f, 0.01f);
        _ai.ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>()).Returns(0.4f);

        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);

        _photo.AestheticScore.Should().Be(40f);
        _photo.ScoringSignature.Should().NotBeNullOrEmpty();
        await _ai.Received(2).ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScoreRangeAsync_ModelReplacedAtSamePath_InvalidatesCachedScore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PhotoCullerModel_{Guid.NewGuid():N}.onnx");
        try
        {
            await File.WriteAllTextAsync(path, "first model");
            var profile = EnableAi();
            profile.LocalModelPath = path;
            var sut = CreateSut();
            await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
            var oldSignature = _photo.ScoringSignature;
            var timestamp = File.GetLastWriteTimeUtc(path);
            await File.WriteAllTextAsync(path, "replacement model with different size");
            File.SetLastWriteTimeUtc(path, timestamp);

            await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);

            _photo.ScoringSignature.Should().NotBe(oldSignature);
            await _ai.Received(2).ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ScoreRangeAsync_FailedDimension_RetriesAndRecoversScore()
    {
        var sut = CreateSut();
        _exposure.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((float?)null);
        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);
        _photo.ScoringSignature.Should().BeNull();
        _exposure.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(60f);

        await sut.ScoreRangeAsync([_photo], ScoringWeights.Default);

        _photo.ExposureScore.Should().Be(60f);
        _photo.OverallScore.Should().BeApproximately(72.5f, 0.01f);
        _photo.ScoringSignature.Should().NotBeNullOrEmpty();
        await _exposure.Received(2).AnalyzeAsync(_photo.FilePath, Arg.Any<CancellationToken>());
    }

    private AiModelProfile EnableAi()
    {
        var profile = new AiModelProfile { Name = "test model", ProviderType = ProviderType.Local,
            ModelType = ModelType.Aesthetic, LocalModelPath = "test-model.onnx" };
        _configuration.EnableLocalAi = true;
        _profiles.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { profile });
        _ai.TestConnectionAsync(profile, Arg.Any<CancellationToken>()).Returns(true);
        _ai.ScoreAsync(profile, _photo.FilePath, Arg.Any<CancellationToken>()).Returns(0.4f);
        return profile;
    }

    private PhotoScoringService CreateSut() => new(_sharpness, _exposure, _contrast, _noise,
        _composition, _color, new ResolutionAnalyzer(NullLogger<ResolutionAnalyzer>.Instance),
        _ai, _profiles, _settings, NullLogger<PhotoScoringService>.Instance);
}
