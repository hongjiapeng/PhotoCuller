using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCuller.Application.Analysis;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;
using PhotoCuller.Infrastructure.Hashing;
using PhotoCuller.Infrastructure.ImageProcessing;
using PhotoCuller.Infrastructure.ImageProcessing.Scorers;
using PhotoCuller.Infrastructure.Repositories;
using PhotoCuller.Infrastructure.Scanning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PhotoCuller.Tests.ApplicationTests;

public sealed class IncrementalAnalysisTests
{
    [Fact]
    public async Task AnalyzeAndSave_RepeatedScans_ReusesWorkPreservesDecisionAndFindsNewDuplicate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PhotoCullerIncremental_{Guid.NewGuid():N}");
        var source = Path.Combine(root, "photos");
        Directory.CreateDirectory(source);
        try
        {
            var firstPath = Path.Combine(source, "first.png");
            var secondPath = Path.Combine(source, "second.png");
            using (var image = new Image<Rgb24>(100, 70, new Rgb24(80, 120, 200)))
                await image.SaveAsPngAsync(firstPath);
            using (var image = new Image<Rgb24>(220, 160, new Rgb24(160, 20, 100)))
                await image.SaveAsPngAsync(secondPath);
            new FileInfo(firstPath).Length.Should().NotBe(new FileInfo(secondPath).Length);

            var options = new DbContextOptionsBuilder<PhotoCullerDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "library.db")};Pooling=False").Options;
            await using var db = new PhotoCullerDbContext(options);
            await db.Database.MigrateAsync();
            var repository = new PhotoRepository(db, NullLogger<PhotoRepository>.Instance);
            var snapshots = new AnalysisResultRepository(options);
            var settings = Substitute.For<ISettingsRepository>();
            settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
            var metadata = Substitute.For<IPhotoMetadataReader>();
            var reader = new PhotoMetadataReader(NullLogger<PhotoMetadataReader>.Instance);
            metadata.ReadAsync(Arg.Any<PhotoItem>(), Arg.Any<CancellationToken>())
                .Returns(call => reader.ReadAsync(call.Arg<PhotoItem>(), call.Arg<CancellationToken>()));
            var hash = Substitute.For<IHashService>();
            hash.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("duplicate-content");
            var perceptual = Substitute.For<IPerceptualHashService>();
            perceptual.ComputePHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1UL);
            perceptual.ComputeDHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1UL);
            var matcher = Substitute.For<ILocalFeatureMatcher>();
            matcher.IsSameSceneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
            var sharp = Substitute.For<ISharpnessAnalyzer>();
            var exposure = Substitute.For<IExposureAnalyzer>();
            var contrast = Substitute.For<IContrastAnalyzer>();
            var noise = Substitute.For<INoiseAnalyzer>();
            var composition = Substitute.For<ICompositionAnalyzer>();
            var color = Substitute.For<IColorAnalyzer>();
            sharp.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
            exposure.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
            contrast.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
            noise.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(70f);
            composition.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(65f);
            color.AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(80f);
            var scoring = new PhotoScoringService(sharp, exposure, contrast, noise, composition, color,
                new ResolutionAnalyzer(NullLogger<ResolutionAnalyzer>.Instance),
                Substitute.For<ILocalAiModelProvider>(), Substitute.For<IAiModelProfileRepository>(), settings,
                NullLogger<PhotoScoringService>.Instance);
            var progress = new ScanProgressService();
            var engine = new PhotoAnalysisEngine(
                new PhotoScanner(metadata, repository, progress, Options.Create(new AppSettings()), NullLogger<PhotoScanner>.Instance),
                Substitute.For<IThumbnailService>(), hash, perceptual, scoring,
                new DuplicateGroupingService(perceptual, matcher, settings, NullLogger<DuplicateGroupingService>.Instance),
                progress, NullLogger<PhotoAnalysisEngine>.Instance);
            var analysisOptions = new PhotoAnalysisOptions { ComputeAllFileHashes = false, GenerateThumbnails = false };

            var initial = await engine.AnalyzeAsync(source, analysisOptions);
            await snapshots.ReplaceAsync(initial.Photos, initial.Groups);
            var chosenId = initial.Photos.Single(p => p.FilePath == firstPath).Id;
            await repository.UpdateRecommendationAsync(chosenId, RecommendationStatus.ManualKeep);
            var duplicatePath = Path.Combine(source, "copy.png");
            File.Copy(firstPath, duplicatePath);

            var updated = await engine.AnalyzeAsync(source, analysisOptions);
            await snapshots.ReplaceAsync(updated.Photos, updated.Groups);
            var unchanged = await engine.AnalyzeAsync(source, analysisOptions);

            unchanged.Photos.Should().HaveCount(3);
            var group = unchanged.Groups.Should().ContainSingle().Which;
            group.GroupType.Should().Be(GroupType.ExactDuplicate);
            group.RecommendedPhotoId.Should().Be(chosenId);
            group.Photos.Select(p => p.FilePath).Should().BeEquivalentTo(firstPath, duplicatePath);
            unchanged.Photos.Single(p => p.Id == chosenId).RecommendationStatus.Should().Be(RecommendationStatus.ManualKeep);
            await metadata.Received(3).ReadAsync(Arg.Any<PhotoItem>(), Arg.Any<CancellationToken>());
            await sharp.Received(3).AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await hash.Received(2).ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>());

            File.Delete(secondPath);
            var reconciled = await engine.AnalyzeAsync(source, analysisOptions);
            await snapshots.ReplaceAsync(reconciled.Photos, reconciled.Groups);
            (await repository.GetAllAsync()).Select(p => p.FilePath)
                .Should().BeEquivalentTo(firstPath, duplicatePath);
            await sharp.Received(3).AnalyzeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
