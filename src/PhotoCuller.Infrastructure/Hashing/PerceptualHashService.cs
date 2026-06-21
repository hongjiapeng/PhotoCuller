using System.Numerics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Hashing;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Perceptual hash implementation using OpenCvSharp.
///
/// pHash (DCT-based):
///   1. Resize to 32×32 grayscale
///   2. Apply DCT → 32×32 frequency coefficients
///   3. Take top-left 8×8 block (low-frequency components)
///   4. Compute mean of the 64 values (excluding DC component at [0,0])
///   5. Each bit = 1 if value > mean, else 0
///   Result: 64-bit hash stored as ulong
///
/// dHash (difference hash):
///   1. Resize to 9×8 grayscale
///   2. For each row, compare adjacent pixels: bit = left > right
///   Result: 64-bit hash stored as ulong
///
/// Hamming distance via BitOperations.PopCount(a ^ b) — O(1) CPU instruction.
/// </summary>
public sealed class PerceptualHashService : IPerceptualHashService
{
    private const string PHashAlgorithm = "pHash";
    private const string DHashAlgorithm = "dHash";
    private static readonly PerceptualHashCache Cache = new();

    private readonly ILogger<PerceptualHashService> _logger;

    public PerceptualHashService(ILogger<PerceptualHashService> logger)
        => _logger = logger;

    public Task<ulong?> ComputePHashAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ComputeWithCache(filePath, PHashAlgorithm, ComputePHash), ct);

    public Task<ulong?> ComputeDHashAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ComputeWithCache(filePath, DHashAlgorithm, ComputeDHash), ct);

    public int HammingDistance(ulong hashA, ulong hashB)
        => BitOperations.PopCount(hashA ^ hashB);

    private ulong? ComputeWithCache(
        string filePath,
        string hashAlgorithm,
        Func<string, ulong?> compute)
    {
        try
        {
            if (Cache.TryGet(filePath, hashAlgorithm, SimilarityThresholds.HashSize, out var cached))
                return cached;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hash cache lookup failed for {Path}", filePath);
        }

        var hash = compute(filePath);
        if (!hash.HasValue)
            return null;

        try
        {
            Cache.Set(filePath, hashAlgorithm, SimilarityThresholds.HashSize, hash.Value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hash cache write failed for {Path}", filePath);
        }

        return hash;
    }

    // ── pHash ────────────────────────────────────────────────────────────────

    private ulong? ComputePHash(string filePath)
    {
        try
        {
            using var src = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (src.Empty()) return null;

            // Step 1: resize to 32×32
            using var small = new Mat();
            Cv2.Resize(src, small, new Size(32, 32), interpolation: InterpolationFlags.Area);

            // Step 2: convert to float32 and apply DCT
            using var floatMat = new Mat();
            small.ConvertTo(floatMat, MatType.CV_32F);

            using var dct = new Mat();
            Cv2.Dct(floatMat, dct);

            // Step 3: take top-left 8×8 block
            var roi = new Rect(0, 0, 8, 8);
            using var block = new Mat(dct, roi);

            // Step 4: compute mean (skip DC component at [0,0])
            double sum = 0;
            int count = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (r == 0 && c == 0) continue; // skip DC
                    sum += block.At<float>(r, c);
                    count++;
                }
            }
            double mean = sum / count;

            // Step 5: build 64-bit hash
            ulong hash = 0;
            int bit = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (block.At<float>(r, c) > mean)
                        hash |= (1UL << bit);
                    bit++;
                }
            }

            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "pHash failed for {Path}", filePath);
            return null;
        }
    }

    // ── dHash ────────────────────────────────────────────────────────────────

    private ulong? ComputeDHash(string filePath)
    {
        try
        {
            using var src = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (src.Empty()) return null;

            // Resize to 9×8 (9 wide so we can compare 8 adjacent pairs per row)
            using var small = new Mat();
            Cv2.Resize(src, small, new Size(9, 8), interpolation: InterpolationFlags.Area);

            ulong hash = 0;
            int bit = 0;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    // bit = 1 if left pixel > right pixel
                    if (small.At<byte>(r, c) > small.At<byte>(r, c + 1))
                        hash |= (1UL << bit);
                    bit++;
                }
            }

            return hash;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "dHash failed for {Path}", filePath);
            return null;
        }
    }
}
