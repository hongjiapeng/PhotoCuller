using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>File operation log repository (supports undo queries).</summary>
public interface IFileOperationLogRepository
{
    Task AddAsync(FileOperationLog log, CancellationToken ct = default);
    Task<IReadOnlyList<FileOperationLog>> GetRecentAsync(int count = 50, CancellationToken ct = default);
    Task<FileOperationLog?> GetLastUndoableAsync(CancellationToken ct = default);
    Task MarkAsUndoneAsync(Guid id, CancellationToken ct = default);
}
