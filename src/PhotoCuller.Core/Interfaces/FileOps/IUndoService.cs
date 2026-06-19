using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.FileOps;

/// <summary>Undoes file move operations (valid for the current run only).</summary>
public interface IUndoService
{
    /// <summary>Whether there is an operation available to undo.</summary>
    bool CanUndo { get; }

    /// <summary>Undoes the most recent move, restoring the file to its original path.</summary>
    Task<bool> UndoLastAsync(CancellationToken ct = default);
}
