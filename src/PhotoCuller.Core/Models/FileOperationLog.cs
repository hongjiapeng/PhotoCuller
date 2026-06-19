using PhotoCuller.Core.Enums;

namespace PhotoCuller.Core.Models;

/// <summary>File operation log; supports undoing the most recent move operation.</summary>
public class FileOperationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public OperationType OperationType { get; set; }

    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;

    public DateTime OperationTime { get; set; } = DateTime.UtcNow;

    /// <summary>Whether this operation can be undone (files moved during the current run are undoable).</summary>
    public bool IsUndoable { get; set; } = true;

    public OperationStatus Status { get; set; } = OperationStatus.Success;

    public string? ErrorMessage { get; set; }
}
