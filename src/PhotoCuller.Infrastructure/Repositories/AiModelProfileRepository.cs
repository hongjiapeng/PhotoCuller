using Microsoft.EntityFrameworkCore;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;

namespace PhotoCuller.Infrastructure.Repositories;

public sealed class AiModelProfileRepository : IAiModelProfileRepository
{
    private readonly PhotoCullerDbContext _db;

    public AiModelProfileRepository(PhotoCullerDbContext db) => _db = db;

    public async Task<IReadOnlyList<AiModelProfile>> GetAllAsync(CancellationToken ct = default)
        => await _db.AiModelProfiles.OrderBy(m => m.Name).ToListAsync(ct);

    public async Task<AiModelProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.AiModelProfiles.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task AddAsync(AiModelProfile profile, CancellationToken ct = default)
    {
        _db.AiModelProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AiModelProfile profile, CancellationToken ct = default)
    {
        profile.UpdatedTime = DateTime.UtcNow;
        _db.AiModelProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        => await _db.AiModelProfiles
            .Where(m => m.Id == id)
            .ExecuteDeleteAsync(ct);
}
