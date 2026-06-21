using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Hashing;
using System.Numerics;

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

        var settings = new AppSettings
        {
            SimilarityThresholdHigh     = 8,
            SimilarityThresholdPossible = 15,
            BurstTimeWindowSeconds      = 10,
            UseCaptureTimeForGrouping   = true,
        };

        _sut = new DuplicateGroupingService(
            _pHash,
            new InMemorySettingsRepository(settings),
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

    [Fact]
    public async Task BuildGroups_LoosePreset_GroupsResizeOrCompressedVariant()
    {
        var sut = CreateService(SimilarityPreset.Loose, new CountingPHash());
        var photos = new List<PhotoItem>
        {
            Photo("original.jpg",  0b0000_0000UL, score: 88f),
            Photo("resized.jpg",   0b0111_1111UL, score: 82f), // distance 7
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle();
        groups[0].GroupType.Should().Be(GroupType.Similar);
        groups[0].Photos.Select(p => p.FilePath).Should()
            .BeEquivalentTo("original.jpg", "resized.jpg");
        groups[0].RecommendedPhotoId.Should().Be(photos[0].Id);
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_DoesNotGroupBeyondLooseThreshold()
    {
        var sut = CreateService(SimilarityPreset.Loose, new CountingPHash());
        var photos = new List<PhotoItem>
        {
            Photo("building.jpg",  0b0000_0000UL),
            Photo("landscape.jpg", 0b0111_1111_1111_1111_1111UL), // distance 19
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
    }

    [Fact]
    public async Task BuildGroups_NormalPreset_UsesBkTreeInsteadOfPairwiseScan()
    {
        var pHash = new CountingPHash();
        var sut = CreateService(SimilarityPreset.Normal, pHash, possibleThreshold: 5);
        var photos = Enumerable.Range(0, 1000)
            .Select(i => Photo($"photo-{i}.jpg", (ulong)i << 16))
            .ToList();

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().NotBeEmpty();
        pHash.DistanceCalls.Should().BeLessThan(1000L * 999 / 2);
    }

    private static DuplicateGroupingService CreateService(
        SimilarityPreset preset,
        IPerceptualHashService pHash,
        int possibleThreshold = 15)
    {
        var settings = new AppSettings
        {
            SimilarityPreset = preset,
            SimilarityThresholdHigh = 8,
            SimilarityThresholdPossible = possibleThreshold,
            BurstTimeWindowSeconds = 10,
            UseCaptureTimeForGrouping = false,
        };

        return new DuplicateGroupingService(
            pHash,
            new InMemorySettingsRepository(settings),
            NullLogger<DuplicateGroupingService>.Instance);
    }

    private static PhotoItem Photo(string path, ulong pHash, float score = 50f)
        => new()
        {
            FilePath = path,
            FileName = path,
            PerceptualHash = pHash,
            OverallScore = score,
            Width = 4000,
            Height = 3000,
        };

    private sealed class CountingPHash : IPerceptualHashService
    {
        public long DistanceCalls { get; private set; }

        public Task<ulong?> ComputePHashAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult<ulong?>(null);

        public Task<ulong?> ComputeDHashAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult<ulong?>(null);

        public int HammingDistance(ulong hashA, ulong hashB)
        {
            DistanceCalls++;
            return BitOperations.PopCount(hashA ^ hashB);
        }
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly AppSettings _settings;

        public InMemorySettingsRepository(AppSettings settings)
            => _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
