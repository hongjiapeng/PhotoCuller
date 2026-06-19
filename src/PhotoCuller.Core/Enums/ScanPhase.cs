namespace PhotoCuller.Core.Enums;

/// <summary>Scan phase.</summary>
public enum ScanPhase
{
    /// <summary>Idle.</summary>
    Idle = 0,

    /// <summary>Enumerating directory files.</summary>
    Enumerating = 1,

    /// <summary>Reading image metadata and EXIF.</summary>
    ReadingMetadata = 2,

    /// <summary>Generating thumbnails.</summary>
    GeneratingThumbnails = 3,

    /// <summary>Computing hash values.</summary>
    ComputingHashes = 4,

    /// <summary>Scoring image quality.</summary>
    Scoring = 5,

    /// <summary>Grouping duplicates and similar photos.</summary>
    Grouping = 6,

    /// <summary>Completed.</summary>
    Completed = 7,

    /// <summary>Cancelled.</summary>
    Cancelled = 8,

    /// <summary>Failed.</summary>
    Failed = 9,
}
