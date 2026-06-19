using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.FileOps;
using PhotoCuller.Core.Interfaces.Storage;

namespace PhotoCuller.Infrastructure.FileSystem;

/// <summary>
/// Undoes the most recent file-move operation for the current run.
/// Restores the file from TargetPath back to SourcePath, then marks
/// the log entry as Undone.
///
/// Only operations with IsUndoable = true and Status = Success can be undone.
/// </summary>
public sealed class UndoService : IUndoService
{
    private readonly IFileOperationLogRepository _logRepo;
    private readonly ILogger<UndoService> _logger;

    public UndoService(
        IFileOperationLogRepository logRepo,
        ILogger<UndoService> logger)
    {
        _logRepo = logRepo;
        _logger  = logger;
    }

    public bool CanUndo
    {
        get
        {
            // Synchronous check: query DB on a background thread would be
            // awkward here; use the async path in practice.
            // Returns true optimistically; UndoLastAsync does the real check.
            return true;
        }
    }

    public async Task<bool> UndoLastAsync(CancellationToken ct = default)
    {
        var log = await _logRepo.GetLastUndoableAsync(ct);
        if (log is null)
        {
            _logger.LogInformation("Nothing to undo.");
            return false;
        }

        try
        {
            if (!File.Exists(log.TargetPath))
            {
                _logger.LogWarning(
                    "Undo failed: target file not found at {Path}", log.TargetPath);
                return false;
            }

            var destDir = Path.GetDirectoryName(log.SourcePath);
            if (destDir is not null)
                Directory.CreateDirectory(destDir);

            File.Move(log.TargetPath, log.SourcePath);

            await _logRepo.MarkAsUndoneAsync(log.Id, ct);

            _logger.LogInformation(
                "Undone: {Target} → {Source}", log.TargetPath, log.SourcePath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Undo failed for log entry {Id}", log.Id);
            return false;
        }
    }
}
