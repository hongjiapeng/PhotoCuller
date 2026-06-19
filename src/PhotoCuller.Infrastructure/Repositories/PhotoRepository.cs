using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;
using PhotoCuller.Infrastructure.Logging;

namespace PhotoCuller.Infrastructure.Repositories;

public sealed class PhotoRepository : IPhotoRepository
{
    private readonly PhotoCullerDbContext _db;
    private readonly ILogger<PhotoRepository> _logger;

    public PhotoRepository(PhotoCullerDbContext db, ILogger<PhotoRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PhotoItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Photos.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PhotoItem?> GetByPathAsync(string filePath, CancellationToken ct = default)
        => await _db.Photos.FirstOrDefaultAsync(p => p.FilePath == filePath, ct);

    public async Task<IReadOnlyList<PhotoItem>> GetPagedAsync(
        int skip, int take, CancellationToken ct = default)
        => await _db.Photos
            .OrderBy(p => p.FileName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PhotoItem>> GetByGroupAsync(
        Guid groupId, CancellationToken ct = default)
        => await _db.Photos
            .Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.OverallScore)
            .ToListAsync(ct);

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
        => await _db.Photos.CountAsync(ct);

    public async Task AddAsync(PhotoItem photo, CancellationToken ct = default)
    {
        _db.Photos.Add(photo);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bulk insert in batches of 50 to avoid excessive memory usage.
    /// Each batch is committed in its own transaction.
    /// </summary>
    public async Task AddRangeAsync(
        IEnumerable<PhotoItem> photos, CancellationToken ct = default)
    {
        const int BatchSize = 50;
        var batch = new List<PhotoItem>(BatchSize);

        foreach (var photo in photos)
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(photo);

            if (batch.Count < BatchSize)
                continue;

            await FlushBatchAsync(batch, ct);
            batch.Clear();
        }

        if (batch.Count > 0)
            await FlushBatchAsync(batch, ct);
    }

    private async Task FlushBatchAsync(List<PhotoItem> batch, CancellationToken ct)
    {
        try
        {
            // Detach navigation properties so EF Core does not try to
            // re-insert already-persisted PhotoGroup entities
            foreach (var p in batch)
                p.Group = null;

            _db.Photos.AddRange(batch);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
        catch (Exception ex)
        {
            Log.BatchFlushFailed(_logger, ex, batch.Count);
            throw;
        }
    }

    public async Task UpdateAsync(PhotoItem photo, CancellationToken ct = default)
    {
        _db.Photos.Update(photo);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _db.Photos
            .Where(p => p.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
        => await _db.Photos.ExecuteDeleteAsync(ct);

    /// <summary>
    /// Raw SQL escape hatch: returns paths that need rescanning
    /// (file not in DB, or ModifiedTime is newer than last scan).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetPathsNeedingRescanAsync(
        IEnumerable<(string Path, DateTime ModifiedTime)> candidates,
        CancellationToken ct = default)
    {
        // Load existing scan times into a dictionary for O(1) lookup
        var existing = await _db.Photos
            .Select(p => new { p.FilePath, p.ModifiedTime, p.ScanTime })
            .ToDictionaryAsync(p => p.FilePath, ct);

        var result = new List<string>();

        foreach (var (path, modifiedTime) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!existing.TryGetValue(path, out var record))
            {
                // Not yet in DB
                result.Add(path);
                continue;
            }

            // Re-scan if the file was modified after the last scan
            if (record.ScanTime is null || modifiedTime > record.ScanTime)
                result.Add(path);
        }

        return result;
    }
}
