using Microsoft.EntityFrameworkCore;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Database;

namespace PhotoCuller.Infrastructure.Repositories;

public sealed class FileOperationLogRepository : IFileOperationLogRepository
{
    private readonly PhotoCullerDbContext _db;

    public FileOperationLogRepository(PhotoCullerDbContext db) => _db = db;

    public async Task AddAsync(FileOperationLog log, CancellationToken ct = default)
    {
        _db.FileOperationLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FileOperationLog>> GetRecentAsync(
        int count = 50, CancellationToken ct = default)
        => await _db.FileOperationLogs
            .OrderByDescending(l => l.OperationTime)
            .Take(count)
            .ToListAsync(ct);

    public async Task<FileOperationLog?> GetLastUndoableAsync(CancellationToken ct = default)
        => await _db.FileOperationLogs
            .Where(l => l.IsUndoable && l.Status == OperationStatus.Success)
            .OrderByDescending(l => l.OperationTime)
            .FirstOrDefaultAsync(ct);

    public async Task MarkAsUndoneAsync(Guid id, CancellationToken ct = default)
        => await _db.FileOperationLogs
            .Where(l => l.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(
                l => l.Status, OperationStatus.Undone), ct);
}
