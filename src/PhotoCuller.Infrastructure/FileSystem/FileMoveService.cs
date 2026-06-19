using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.FileOps;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.FileSystem;

/// <summary>
/// Safely moves photos to a pending-delete folder.
/// Never permanently deletes originals.
///
/// Target path rules:
///   preserveStructure = true  →  {toDeleteDir}\{relative path from source root}
///   preserveStructure = false →  {toDeleteDir}\{filename}
///
/// If a file with the same name already exists in the target, a numeric
/// suffix is appended: photo(1).jpg, photo(2).jpg, etc.
/// </summary>
public sealed class FileMoveService : IFileMoveService
{
    private readonly IFileOperationLogRepository _logRepo;
    private readonly ILogger<FileMoveService> _logger;

    public FileMoveService(
        IFileOperationLogRepository logRepo,
        ILogger<FileMoveService> logger)
    {
        _logRepo = logRepo;
        _logger  = logger;
    }

    public async Task<FileOperationLog> MoveToTrashAsync(
        PhotoItem photo,
        string toDeleteDirectory,
        bool preserveStructure = true,
        CancellationToken ct = default)
    {
        var log = new FileOperationLog
        {
            OperationType = OperationType.MoveToTrash,
            SourcePath    = photo.FilePath,
            Status        = OperationStatus.Success,
            IsUndoable    = true,
        };

        try
        {
            var dest = ResolveDestination(photo.FilePath, toDeleteDirectory, preserveStructure);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(photo.FilePath, dest);

            log.TargetPath = dest;
            photo.IsMovedToTrash = true;
            photo.RecommendationStatus = RecommendationStatus.ToDelete;

            _logger.LogInformation(
                "Moved {Src} → {Dst}", photo.FilePath, dest);
        }
        catch (Exception ex)
        {
            log.Status       = OperationStatus.Failed;
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to move {Path}", photo.FilePath);
        }

        await _logRepo.AddAsync(log, ct);
        return log;
    }

    public async Task<IReadOnlyList<FileOperationLog>> MoveManyToTrashAsync(
        IReadOnlyList<PhotoItem> photos,
        string toDeleteDirectory,
        bool preserveStructure = true,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<FileOperationLog>(photos.Count);
        int done = 0;

        foreach (var photo in photos)
        {
            ct.ThrowIfCancellationRequested();
            var log = await MoveToTrashAsync(
                photo, toDeleteDirectory, preserveStructure, ct);
            results.Add(log);
            progress?.Report(++done);
        }

        return results;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ResolveDestination(
        string sourcePath,
        string toDeleteDirectory,
        bool preserveStructure)
    {
        string candidate;

        if (preserveStructure)
        {
            // Keep relative path under the trash dir
            var root = Path.GetPathRoot(sourcePath) ?? string.Empty;
            var relative = Path.GetRelativePath(root, sourcePath);
            candidate = Path.Combine(toDeleteDirectory, relative);
        }
        else
        {
            candidate = Path.Combine(toDeleteDirectory,
                Path.GetFileName(sourcePath));
        }

        // Avoid overwriting an existing file
        if (!File.Exists(candidate))
            return candidate;

        var dir   = Path.GetDirectoryName(candidate)!;
        var stem  = Path.GetFileNameWithoutExtension(candidate);
        var ext   = Path.GetExtension(candidate);
        int n     = 1;

        string unique;
        do
        {
            unique = Path.Combine(dir, $"{stem}({n}){ext}");
            n++;
        } while (File.Exists(unique));

        return unique;
    }
}
