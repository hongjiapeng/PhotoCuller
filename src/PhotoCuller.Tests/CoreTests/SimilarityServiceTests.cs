using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Hashing;
using Microsoft.Extensions.Options;

namespace PhotoCuller.Tests.CoreTests;

public sealed class SimilarityServiceTests
{
    private readonly IPerceptualHashService _pHash;
    private readonly SimilarityService _sut;

    public SimilarityServiceTests()
    {
        _pHash = Substitute.For<IPerceptualHashService>();
        var opts = Options.Create(new AppSettings
        {
            SimilarityThresholdHigh     = 8,
            SimilarityThresholdPossible = 15,
        });
        _sut = new SimilarityService(opts, _pHash);
    }

    // ── IsExactDuplicate ──────────────────────────────────────────────────────

    [Fact]
    public void IsExactDuplicate_SameHash_ReturnsTrue()
    {
        var a = new PhotoItem { Sha256Hash = "abc123" };
        var b = new PhotoItem { Sha256Hash = "abc123" };
        _sut.IsExactDuplicate(a, b).Should().BeTrue();
    }

    [Fact]
    public void IsExactDuplicate_DifferentHash_ReturnsFalse()
    {
        var a = new PhotoItem { Sha256Hash = "abc123" };
        var b = new PhotoItem { Sha256Hash = "xyz789" };
        _sut.IsExactDuplicate(a, b).Should().BeFalse();
    }

    [Fact]
    public void IsExactDuplicate_CaseInsensitive()
    {
        var a = new PhotoItem { Sha256Hash = "ABC123" };
        var b = new PhotoItem { Sha256Hash = "abc123" };
        _sut.IsExactDuplicate(a, b).Should().BeTrue();
    }

    [Fact]
    public void IsExactDuplicate_NullHash_ReturnsFalse()
    {
        var a = new PhotoItem { Sha256Hash = null };
        var b = new PhotoItem { Sha256Hash = "abc123" };
        _sut.IsExactDuplicate(a, b).Should().BeFalse();
    }

    // ── IsHighlySimilar ───────────────────────────────────────────────────────

    [Fact]
    public void IsHighlySimilar_DistanceAtThreshold_ReturnsTrue()
    {
        const ulong hashA = 0b_1111_1111_0000_0000UL; // arbitrary
        const ulong hashB = 0b_1111_1110_0000_0000UL; // distance 1
        _pHash.HammingDistance(hashA, hashB).Returns(1);

        var a = new PhotoItem { PerceptualHash = hashA };
        var b = new PhotoItem { PerceptualHash = hashB };
        _sut.IsHighlySimilar(a, b).Should().BeTrue();
    }

    [Fact]
    public void IsHighlySimilar_DistanceBeyondThreshold_ReturnsFalse()
    {
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(9); // > 8

        var a = new PhotoItem { PerceptualHash = 0UL };
        var b = new PhotoItem { PerceptualHash = 0UL };
        _sut.IsHighlySimilar(a, b).Should().BeFalse();
    }

    [Fact]
    public void IsHighlySimilar_NullHash_ReturnsFalse()
    {
        var a = new PhotoItem { PerceptualHash = null };
        var b = new PhotoItem { PerceptualHash = 42UL };
        _sut.IsHighlySimilar(a, b).Should().BeFalse();
    }

    // ── IsPossiblySimilar ─────────────────────────────────────────────────────

    [Fact]
    public void IsPossiblySimilar_DistanceAtPossibleThreshold_ReturnsTrue()
    {
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(15);

        var a = new PhotoItem { PerceptualHash = 0UL };
        var b = new PhotoItem { PerceptualHash = 0UL };
        _sut.IsPossiblySimilar(a, b).Should().BeTrue();
    }

    [Fact]
    public void IsPossiblySimilar_DistanceBeyondPossible_ReturnsFalse()
    {
        _pHash.HammingDistance(Arg.Any<ulong>(), Arg.Any<ulong>()).Returns(16);

        var a = new PhotoItem { PerceptualHash = 0UL };
        var b = new PhotoItem { PerceptualHash = 0UL };
        _sut.IsPossiblySimilar(a, b).Should().BeFalse();
    }
}
