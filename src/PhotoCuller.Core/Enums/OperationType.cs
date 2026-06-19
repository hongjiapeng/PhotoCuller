namespace PhotoCuller.Core.Enums;

/// <summary>File operation type.</summary>
public enum OperationType
{
    /// <summary>Move to the pending-delete folder.</summary>
    MoveToTrash = 0,

    /// <summary>Restore from the pending-delete folder.</summary>
    RestoreFromTrash = 1,
}
