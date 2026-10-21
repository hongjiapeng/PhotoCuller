using Microsoft.EntityFrameworkCore;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;

namespace PhotoCuller.Infrastructure.Repositories;

public sealed class AnalysisResultRepository(DbContextOptions<PhotoCullerDbContext> options)
    : IAnalysisResultRepository
{
    public async Task ReplaceAsync(
        IReadOnlyList<PhotoItem> photos,
        IReadOnlyList<PhotoGroup> groups,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // A private context avoids attaching UI objects or clearing another repository's tracker.
        await using var db = new PhotoCullerDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var paths = photos.Select(p => p.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retainedTrash = (await db.Photos.Where(p => p.IsMovedToTrash).ToListAsync(ct))
            .Where(p => !paths.Contains(p.FilePath)).ToList();
        foreach (var photo in retainedTrash)
            photo.GroupId = null;

        await db.Photos.ExecuteDeleteAsync(ct);
        await db.PhotoGroups.ExecuteDeleteAsync(ct);
        db.PhotoGroups.AddRange(groups.Select(g => new PhotoGroup
        {
            Id = g.Id,
            GroupType = g.GroupType,
            Name = g.Name,
            RecommendedPhotoId = g.RecommendedPhotoId,
            PhotoCount = g.PhotoCount,
            SimilarityScore = g.SimilarityScore,
            CreatedTime = g.CreatedTime,
        }));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        foreach (var batch in photos.Concat(retainedTrash).Chunk(100))
        {
            db.Photos.AddRange(batch.Select(p => p.CreateSnapshot()));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(ct);
    }
}
