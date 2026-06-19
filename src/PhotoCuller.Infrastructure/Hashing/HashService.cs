using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Hashing;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Computes SHA-256 file hashes for exact-duplicate detection.
/// Reads the file in 64 KB chunks to keep memory flat on large files.
/// </summary>
public sealed class HashService : IHashService
{
    private const int BufferSize = 64 * 1024; // 64 KB

    private readonly ILogger<HashService> _logger;

    public HashService(ILogger<HashService> logger) => _logger = logger;

    public async Task<string?> ComputeSha256Async(
        string filePath,
        CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(async () =>
            {
                using var sha = SHA256.Create();
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: BufferSize,
                    useAsync: true);

                var hash = await sha.ComputeHashAsync(stream, ct);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SHA-256 failed for {Path}", filePath);
            return null;
        }
    }
}
