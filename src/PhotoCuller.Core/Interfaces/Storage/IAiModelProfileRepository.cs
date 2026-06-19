using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>AI model configuration profile repository.</summary>
public interface IAiModelProfileRepository
{
    Task<IReadOnlyList<AiModelProfile>> GetAllAsync(CancellationToken ct = default);
    Task<AiModelProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(AiModelProfile profile, CancellationToken ct = default);
    Task UpdateAsync(AiModelProfile profile, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
