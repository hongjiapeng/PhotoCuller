using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Hashing;

namespace PhotoCuller.Tests.CoreTests;

public sealed class DuplicateGroupingServiceTests
{
    private readonly IPerceptualHashService _pHash;
    private readonly DuplicateGroupingService _sut;

    public DuplicateGroupingServiceTests()
    {
        _pHash = Substitute.For<IPerceptualHashService>();

        // Default: Hamming distance = 0 (identical hashes)
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(0);

        var opts = Options.Create(new AppSettings
        {
            SimilarityThresholdHigh     = 8,
            SimilarityThresholdPossible = 15,
            BurstTimeWindowSeconds      = 10,
            UseCaptureTimeForGrouping   = true,
        });

        _sut = new DuplicateGroupingService(
            _pHash,
            opts,
            NullLogger<DuplicateGroupingService>.Instance);
    }

    // ── Exact duplicates ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuildGroups_ExactDuplicates_GroupedTogether()
    {
        const string hash = "deadbeef";
        var photos = new List<PhotoItem>
        {
            new() { FilePath = "a.jpg", Sha256Hash = hash },
            new() { FilePath = "b.jpg", Sha256Hash = hash },
            new() { FilePath = "c.jpg", Sha256Hash = hash },
        };

        var groups = await _sut.BuildGroupsAsync(photos);

        groups.Should().HaveCount(1);
        groups[0].GroupType.Should().Be(GroupType.ExactDuplicate);
        groups[0].PhotoCount.Should().Be(3);
        groups[0].Photos.Should().HaveCount(3);
    }

    [Fact]
    public async Task BuildGroups_NoDuplicates_NoGroups()
    {
        var photos = new List<PhotoItem>
        {
            new() { FilePath = "a.jpg", Sha256Hash = "aaa" },
            new() { FilePath = "b.jpg", Sha256Hash = "bbb" },
        };

        // Make Hamming distance large so no similarity group either
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(20);

        var groups = await _sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
    }

    // ── Burst shots ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildGroups_BurstWithinWindow_GroupedAsBurst()
    {
        var baseTime = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var photos = new List<PhotoItem>
        {
            new()
            {
                FilePath = @"D:\Photos\a.jpg",
                ExifTakenTime = baseTime,
                PerceptualHash = 0xFFFF_FFFF_FFFF_FFFFUL,
            },
            new()
            {
                FilePath = @"D:\Photos\b.jpg",
                ExifTakenTime = baseTime.AddSeconds(3),   // within 10s window
                PerceptualHash = 0xFFFF_FFFF_FFFF_FFFFUL,
            },
        };

        // distance = 0 (identical hashes)
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(0);

        var groups = await _sut.BuildGroupsAsync(photos);

        groups.Should().HaveCount(1);
        groups[0].GroupType.Should().Be(GroupType.Burst);
    }

    [Fact]
    public async Task BuildGroups_BurstOutsideWindow_NotGrouped()
    {
        var baseTime = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var photos = new List<PhotoItem>
        {
            new()
            {
                FilePath = @"D:\Photos\a.jpg",
                ExifTakenTime = baseTime,
                PerceptualHash = 0xABCDUL,
            },
            new()
            {
                FilePath = @"D:\Photos\b.jpg",
                ExifTakenTime = baseTime.AddSeconds(60), // outside 10s window
                PerceptualHash = 0xABCDUL,
            },
        };

        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(0);

        // Outside window → not burst; distance 0 → similar (but different dir check
        // won't apply here since both in same dir, and outside window is the guard)
        var groups = await _sut.BuildGroupsAsync(photos);

        // Should be grouped as Similar (union-find), not Burst
        groups.Should().HaveCount(1);
        groups[0].GroupType.Should().NotBe(GroupType.Burst);
    }

    // ── Recommendation ────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildGroups_RecommendedPhotoIsHighestScore()
    {
        const string hash = "samehash";
        var photos = new List<PhotoItem>
        {
            new() { FilePath = "low.jpg",  Sha256Hash = hash, OverallScore = 60f,
                    Width = 1000, Height = 1000 },
            new() { FilePath = "high.jpg", Sha256Hash = hash, OverallScore = 92f,
                    Width = 4000, Height = 3000 },
            new() { FilePath = "mid.jpg",  Sha256Hash = hash, OverallScore = 75f,
                    Width = 2000, Height = 2000 },
        };

        var groups = await _sut.BuildGroupsAsync(photos);

        groups.Should().HaveCount(1);
        var recommended = photos.First(p => p.Id == groups[0].RecommendedPhotoId);
        recommended.FilePath.Should().Be("high.jpg");
        recommended.RecommendationStatus.Should().Be(RecommendationStatus.Keep);
    }

    [Fact]
    public async Task BuildGroups_ManualKeepTakesPriority()
    {
        const string hash = "samehash";
        var photos = new List<PhotoItem>
        {
            new()
            {
                FilePath   = "manual.jpg",
                Sha256Hash = hash,
                OverallScore = 50f,
                Width = 100, Height = 100,
                RecommendationStatus = RecommendationStatus.ManualKeep,
            },
            new()
            {
                FilePath   = "high.jpg",
                Sha256Hash = hash,
                OverallScore = 99f,
                Width = 6000, Height = 4000,
            },
        };

        var groups = await _sut.BuildGroupsAsync(photos);

        groups[0].RecommendedPhotoId
            .Should().Be(photos.First(p => p.FilePath == "manual.jpg").Id);
    }
}
