using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>Similar-photo group repository.</summary>
public interface IPhotoGroupRepository
{
    Task<IReadOnlyList<PhotoGroup>> GetAllAsync(CancellationToken ct = default);
    Task<PhotoGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PhotoGroup group, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<PhotoGroup> groups, CancellationToken ct = default);
    Task UpdateAsync(PhotoGroup group, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}
