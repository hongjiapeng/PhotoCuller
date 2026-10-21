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
    private readonly ILocalFeatureMatcher _localFeatureMatcher;
    private readonly DuplicateGroupingService _sut;

    public DuplicateGroupingServiceTests()
    {
        _pHash = Substitute.For<IPerceptualHashService>();

        // Default: Hamming distance = 0 (identical hashes)
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(0);
        _pHash.ComputeDHashAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ulong?>(0));
        _localFeatureMatcher = Substitute.For<ILocalFeatureMatcher>();
        _localFeatureMatcher.IsSameSceneAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var settings = new AppSettings
        {
            SimilarityThresholdHigh     = 8,
            SimilarityThresholdPossible = 15,
            BurstTimeWindowSeconds      = 10,
            UseCaptureTimeForGrouping   = true,
        };

        _sut = new DuplicateGroupingService(
            _pHash,
            _localFeatureMatcher,
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

    [Fact]
    public async Task BuildGroups_ManualDeleteHighestScore_RecommendsOtherPhotoWithoutOverwritingDecision()
    {
        var rejected = new PhotoItem { FilePath = "rejected.jpg", Sha256Hash = "same",
            Width = 2000, Height = 1000, OverallScore = 99, RecommendationStatus = RecommendationStatus.ManualDelete };
        var other = new PhotoItem { FilePath = "other.jpg", Sha256Hash = "same",
            Width = 2000, Height = 1000, OverallScore = 70 };

        var groups = await _sut.BuildGroupsAsync([rejected, other]);

        groups.Should().ContainSingle().Which.RecommendedPhotoId.Should().Be(other.Id);
        rejected.RecommendationStatus.Should().Be(RecommendationStatus.ManualDelete);
        other.RecommendationStatus.Should().Be(RecommendationStatus.Keep);
    }

    [Fact]
    public async Task BuildGroups_AllManuallyRejected_DoesNotRecommendRejectedPhoto()
    {
        var photos = Enumerable.Range(0, 2).Select(i => new PhotoItem
        {
            FilePath = $"{i}.jpg", Sha256Hash = "same", Width = 2000, Height = 1000,
            OverallScore = 90, RecommendationStatus = RecommendationStatus.ManualDelete,
        }).ToList();

        var groups = await _sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle().Which.RecommendedPhotoId.Should().BeNull();
        photos.Should().OnlyContain(p => p.RecommendationStatus == RecommendationStatus.ManualDelete);
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
                ExifTakenTime = baseTime.AddSeconds(1),
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
                ExifTakenTime = baseTime.AddSeconds(121), // outside Normal's 120s window
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
        var pHash = new CountingPHash()
            .WithDifferenceHash("original.jpg", 0)
            .WithDifferenceHash("resized.jpg", (1UL << 5) - 1);
        var sut = CreateService(SimilarityPreset.Loose, pHash);
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
            Photo("landscape.jpg", (1UL << 39) - 1), // distance 39; Loose limit is 38
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
    }

    [Fact]
    public async Task BuildGroups_NormalPreset_UsesBkTreeInsteadOfPairwiseScan()
    {
        var pHash = new CountingPHash()
            .WithDifferenceHash("photo-0.jpg", 0)
            .WithDifferenceHash("photo-1.jpg", 0);
        var sut = CreateService(
            SimilarityPreset.Normal,
            pHash,
            possibleThreshold: 5,
            highThreshold: 5,
            useAdvanced: true);
        var photos = Enumerable.Range(0, 1000)
            .Select(i => Photo($"photo-{i}.jpg", (ulong)i << 16))
            .ToList();

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().NotBeEmpty();
        pHash.DistanceCalls.Should().BeLessThan(1000L * 999 / 2);
    }

    [Theory]
    [InlineData(SimilarityPreset.Strict, 8, 12, 20, 18, 30, 1)]
    [InlineData(SimilarityPreset.Normal, 12, 16, 30, 26, 120, 1)]
    [InlineData(SimilarityPreset.Loose, 16, 20, 32, 30, 300, 1)]
    public void FromSettings_BasicPreset_UsesCompleteProfile(
        SimilarityPreset preset,
        int expectedGlobal,
        int expectedGlobalDifference,
        int expectedSequence,
        int expectedSequenceDifference,
        int expectedWindow,
        int expectedNumberGap)
    {
        var settings = new AppSettings
        {
            SimilarityPreset = preset,
            SimilarityThresholdHigh = 1,
            SimilarityThresholdPossible = 1,
            BurstTimeWindowSeconds = 1,
            UseCaptureTimeForGrouping = true,
        };

        var profile = SimilarityThresholds.FromSettings(settings);

        profile.GlobalThreshold.Should().Be(expectedGlobal);
        profile.GlobalDifferenceThreshold.Should().Be(expectedGlobalDifference);
        profile.SequenceThreshold.Should().Be(expectedSequence);
        profile.SequenceDifferenceThreshold.Should().Be(expectedSequenceDifference);
        profile.SequenceWindowSeconds.Should().Be(expectedWindow);
        profile.MaxSequenceNumberGap.Should().Be(expectedNumberGap);
        profile.BurstMaxIntervalSeconds.Should().Be(2);
        profile.UseCaptureSequence.Should().BeTrue();
    }

    [Fact]
    public async Task BuildGroups_NormalPreset_NearbySequentialPhotosStayTogetherAsSimilar()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\Photos\DSC02127.JPG", 0)
            .WithDifferenceHash(@"D:\Photos\DSC02128.JPG", (1UL << 10) - 1)
            .WithDifferenceHash(@"D:\Photos\DSC02129.JPG", (1UL << 12) - 1);
        var sut = CreateService(SimilarityPreset.Normal, pHash);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\Photos\DSC02127.JPG", 0, baseTime),
            SequencePhoto(@"D:\Photos\DSC02128.JPG", (1UL << 26) - 1,
                baseTime.AddSeconds(21)),
            SequencePhoto(@"D:\Photos\DSC02129.JPG", ((1UL << 30) - 1) << 10,
                baseTime.AddSeconds(50)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle();
        groups[0].GroupType.Should().Be(GroupType.Similar);
        groups[0].Photos.Select(p => p.FileName).Should().BeEquivalentTo(
            "DSC02127.JPG", "DSC02128.JPG", "DSC02129.JPG");
    }

    [Fact]
    public async Task BuildGroups_NormalPreset_SimilarHashOutsideSequenceDoesNotFalsePositive()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var sut = CreateService(SimilarityPreset.Normal, new CountingPHash());
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\Photos\DSC02001.JPG", 0, baseTime),
            SequencePhoto(@"D:\Photos\DSC02002.JPG", (1UL << 20) - 1,
                baseTime.AddMinutes(5)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
    }

    [Fact]
    public async Task BuildGroups_SequenceSimilarityChain_DoesNotCreateIncoherentGroup()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\Photos\DSC03001.JPG", 0)
            .WithDifferenceHash(@"D:\Photos\DSC03002.JPG", (1UL << 10) - 1)
            .WithDifferenceHash(@"D:\Photos\DSC03003.JPG", (1UL << 20) - 1);
        var sut = CreateService(SimilarityPreset.Normal, pHash);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\Photos\DSC03001.JPG", 0, baseTime),
            SequencePhoto(@"D:\Photos\DSC03002.JPG", (1UL << 20) - 1,
                baseTime.AddSeconds(10)),
            SequencePhoto(@"D:\Photos\DSC03003.JPG", (1UL << 40) - 1,
                baseTime.AddSeconds(20)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle();
        groups[0].Photos.Select(p => p.FileName).Should().BeEquivalentTo(
            "DSC03001.JPG", "DSC03002.JPG");
        photos.Single(p => p.FileName == "DSC03003.JPG").GroupId.Should().BeNull();
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_SubjectChangeSplitsAdjacentPhotosIntoSceneGroups()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        const ulong low12 = (1UL << 12) - 1;
        const ulong high33 = ((1UL << 33) - 1) << 31;
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\Photos\DSC02102.JPG", 0)
            .WithDifferenceHash(@"D:\Photos\DSC02103.JPG", (1UL << 8) - 1)
            .WithDifferenceHash(@"D:\Photos\DSC02104.JPG", (1UL << 10) - 1)
            .WithDifferenceHash(@"D:\Photos\DSC02105.JPG", (1UL << 31) - 1)
            .WithDifferenceHash(@"D:\Photos\DSC02106.JPG", (1UL << 31) - 1 ^ 0b11UL);
        var sut = CreateService(SimilarityPreset.Loose, pHash);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\Photos\DSC02102.JPG", 0, baseTime),
            SequencePhoto(@"D:\Photos\DSC02103.JPG", (1UL << 10) - 1,
                baseTime.AddSeconds(10)),
            SequencePhoto(@"D:\Photos\DSC02104.JPG", low12,
                baseTime.AddSeconds(20)),
            SequencePhoto(@"D:\Photos\DSC02105.JPG", low12 | high33,
                baseTime.AddSeconds(30)),
            SequencePhoto(@"D:\Photos\DSC02106.JPG", (low12 | high33) ^ 0b11UL,
                baseTime.AddSeconds(40)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().HaveCount(2);
        groups.Should().OnlyContain(g => g.GroupType == GroupType.Similar);
        groups.Select(g => g.Photos.Select(p => p.FileName)).Should().BeEquivalentTo(
            new[]
            {
                new[] { "DSC02102.JPG", "DSC02103.JPG", "DSC02104.JPG" },
                new[] { "DSC02105.JPG", "DSC02106.JPG" },
            });
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_TimeAdjacentButDHashDifferent_DoesNotGroup()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\Photos\DSC04001.JPG", 0)
            .WithDifferenceHash(@"D:\Photos\DSC04002.JPG", (1UL << 31) - 1);
        var sut = CreateService(SimilarityPreset.Loose, pHash);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\Photos\DSC04001.JPG", 0, baseTime),
            SequencePhoto(@"D:\Photos\DSC04002.JPG", (1UL << 31) - 1,
                baseTime.AddSeconds(1)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_HashCompatibleSceneChange_LocalFeaturesRejectPair()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        const string firstPath = @"D:\Photos\DSC02152.JPG";
        const string secondPath = @"D:\Photos\DSC02153.JPG";
        var pHash = new CountingPHash()
            .WithDifferenceHash(firstPath, 0)
            .WithDifferenceHash(secondPath, (1UL << 20) - 1);
        var localFeatureMatcher = Substitute.For<ILocalFeatureMatcher>();
        localFeatureMatcher.IsSameSceneAsync(
                firstPath, secondPath, Arg.Any<CancellationToken>())
            .Returns(false);
        var sut = CreateService(
            SimilarityPreset.Loose, pHash, localFeatureMatcher);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(firstPath, 0, baseTime),
            SequencePhoto(secondPath, (1UL << 24) - 1, baseTime.AddSeconds(17)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
        await localFeatureMatcher.Received(1).IsSameSceneAsync(
            firstPath, secondPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_HashCompatibleSameScene_LocalFeaturesKeepPair()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        const string firstPath = @"D:\Photos\DSC02153.JPG";
        const string secondPath = @"D:\Photos\DSC02154.JPG";
        var pHash = new CountingPHash()
            .WithDifferenceHash(firstPath, 0)
            .WithDifferenceHash(secondPath, (1UL << 18) - 1);
        var localFeatureMatcher = Substitute.For<ILocalFeatureMatcher>();
        localFeatureMatcher.IsSameSceneAsync(
                firstPath, secondPath, Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = CreateService(
            SimilarityPreset.Loose, pHash, localFeatureMatcher);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(firstPath, 0, baseTime),
            SequencePhoto(secondPath, (1UL << 22) - 1, baseTime.AddSeconds(2)),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle();
        groups[0].Photos.Select(p => p.FileName).Should().Equal(
            "DSC02153.JPG", "DSC02154.JPG");
        groups[0].GroupType.Should().Be(GroupType.Similar);
        await localFeatureMatcher.Received(1).IsSameSceneAsync(
            firstPath, secondPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_PHashCollisionAcrossSessions_DHashRejectsFalsePositive()
    {
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\A\DSC02117.JPG", 0)
            .WithDifferenceHash(@"D:\B\DSC02155.JPG", (1UL << 30) - 1);
        var sut = CreateService(SimilarityPreset.Loose, pHash);
        var photos = new List<PhotoItem>
        {
            Photo(@"D:\A\DSC02117.JPG", 0),
            Photo(@"D:\B\DSC02155.JPG", (1UL << 16) - 1),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty();
        photos.Should().OnlyContain(p => p.GroupId == null);
    }

    [Fact]
    public async Task BuildGroups_LoosePreset_GlobalHashCollision_LocalFeaturesRejectPair()
    {
        const string firstPath = @"D:\Photos\DSC08542.JPG";
        const string secondPath = @"D:\Photos\DSC08528.JPG";
        var pHash = new CountingPHash()
            .WithDifferenceHash(firstPath, 0)
            .WithDifferenceHash(secondPath, (1UL << 13) - 1);
        var localFeatureMatcher = Substitute.For<ILocalFeatureMatcher>();
        localFeatureMatcher.IsSameSceneAsync(
                firstPath, secondPath, Arg.Any<CancellationToken>())
            .Returns(false);
        var sut = CreateService(
            SimilarityPreset.Loose, pHash, localFeatureMatcher);
        var photos = new List<PhotoItem>
        {
            Photo(firstPath, 0),
            Photo(secondPath, (1UL << 10) - 1),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().BeEmpty(
            "similar framing and background must not override a different subject");
        photos.Should().OnlyContain(photo => photo.GroupId == null);
        await localFeatureMatcher.Received(1).IsSameSceneAsync(
            firstPath, secondPath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildGroups_AdvancedOverride_AllowsExplicitGlobalThreshold()
    {
        var baseTime = new DateTime(2025, 8, 30, 10, 0, 0, DateTimeKind.Utc);
        var pHash = new CountingPHash()
            .WithDifferenceHash(@"D:\A\one.jpg", 0)
            .WithDifferenceHash(@"D:\B\two.jpg", (1UL << 10) - 1);
        var sut = CreateService(
            SimilarityPreset.Strict,
            pHash,
            possibleThreshold: 24,
            highThreshold: 20,
            useAdvanced: true);
        var photos = new List<PhotoItem>
        {
            SequencePhoto(@"D:\A\one.jpg", 0, baseTime),
            SequencePhoto(@"D:\B\two.jpg", (1UL << 20) - 1, baseTime),
        };

        var groups = await sut.BuildGroupsAsync(photos);

        groups.Should().ContainSingle();
        groups[0].GroupType.Should().Be(GroupType.Similar);
    }

    private static DuplicateGroupingService CreateService(
        SimilarityPreset preset,
        IPerceptualHashService pHash,
        ILocalFeatureMatcher? localFeatureMatcher = null,
        int possibleThreshold = 32,
        int highThreshold = 12,
        bool useAdvanced = false)
    {
        var settings = new AppSettings
        {
            SimilarityPreset = preset,
            SimilarityThresholdHigh = highThreshold,
            SimilarityThresholdPossible = possibleThreshold,
            BurstTimeWindowSeconds = 120,
            UseCaptureTimeForGrouping = true,
            UseAdvancedSimilaritySettings = useAdvanced,
        };

        return new DuplicateGroupingService(
            pHash,
            localFeatureMatcher ?? AlwaysMatchingLocalFeatures(),
            new InMemorySettingsRepository(settings),
            NullLogger<DuplicateGroupingService>.Instance);
    }

    private static ILocalFeatureMatcher AlwaysMatchingLocalFeatures()
    {
        var matcher = Substitute.For<ILocalFeatureMatcher>();
        matcher.IsSameSceneAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        return matcher;
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

    private static PhotoItem SequencePhoto(string path, ulong pHash, DateTime takenTime)
    {
        var photo = Photo(path, pHash);
        photo.FileName = Path.GetFileName(path);
        photo.ExifTakenTime = takenTime;
        return photo;
    }

    private sealed class CountingPHash : IPerceptualHashService
    {
        private readonly Dictionary<string, ulong> _differenceHashes =
            new(StringComparer.OrdinalIgnoreCase);

        public long DistanceCalls { get; private set; }

        public CountingPHash WithDifferenceHash(string filePath, ulong hash)
        {
            _differenceHashes[filePath] = hash;
            return this;
        }

        public Task<ulong?> ComputePHashAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult<ulong?>(null);

        public Task<ulong?> ComputeDHashAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(
                _differenceHashes.TryGetValue(filePath, out var hash)
                    ? (ulong?)hash
                    : null);

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
