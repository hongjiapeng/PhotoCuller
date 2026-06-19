using System.Numerics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoCuller.Infrastructure.Hashing;

namespace PhotoCuller.Tests.CoreTests;

public sealed class PerceptualHashServiceTests
{
    private readonly PerceptualHashService _sut;

    public PerceptualHashServiceTests()
        => _sut = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

    // ── HammingDistance ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0b0000UL, 0b0000UL, 0)]   // identical
    [InlineData(0b0001UL, 0b0000UL, 1)]   // differ by 1 bit
    [InlineData(0b1111UL, 0b0000UL, 4)]   // differ by 4 bits
    [InlineData(ulong.MaxValue, 0UL, 64)]  // all bits differ
    public void HammingDistance_KnownPairs(ulong a, ulong b, int expected)
    {
        _sut.HammingDistance(a, b).Should().Be(expected);
    }

    [Fact]
    public void HammingDistance_IsSymmetric()
    {
        const ulong x = 0xDEAD_BEEF_CAFE_BABEul;
        const ulong y = 0x1234_5678_9ABC_DEF0ul;
        _sut.HammingDistance(x, y).Should().Be(_sut.HammingDistance(y, x));
    }

    [Fact]
    public void HammingDistance_MaxIs64()
    {
        _sut.HammingDistance(ulong.MaxValue, 0UL).Should().Be(64);
    }

    [Fact]
    public void HammingDistance_MatchesPopCount()
    {
        const ulong a = 0xF0F0_F0F0_F0F0_F0F0ul;
        const ulong b = 0x0F0F_0F0F_0F0F_0F0Ful;
        int expected = BitOperations.PopCount(a ^ b);
        _sut.HammingDistance(a, b).Should().Be(expected);
    }

    // ── ComputePHash (file-based) — skipped in unit tests since files needed ──
    // Integration tests for pHash with real image files go in InfrastructureTests.
}
