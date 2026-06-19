using Microsoft.EntityFrameworkCore;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;

namespace PhotoCuller.Infrastructure.Repositories;

public sealed class PhotoGroupRepository : IPhotoGroupRepository
{
    private readonly PhotoCullerDbContext _db;

    public PhotoGroupRepository(PhotoCullerDbContext db) => _db = db;

    public async Task<IReadOnlyList<PhotoGroup>> GetAllAsync(CancellationToken ct = default)
        => await _db.PhotoGroups
            .OrderByDescending(g => g.CreatedTime)
            .ToListAsync(ct);

    public async Task<PhotoGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.PhotoGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task AddAsync(PhotoGroup group, CancellationToken ct = default)
    {
        _db.PhotoGroups.Add(group);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(
        IEnumerable<PhotoGroup> groups, CancellationToken ct = default)
    {
        _db.PhotoGroups.AddRange(groups);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PhotoGroup group, CancellationToken ct = default)
    {
        _db.PhotoGroups.Update(group);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
        => await _db.PhotoGroups.ExecuteDeleteAsync(ct);
}
