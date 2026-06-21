using PhotoCuller.Core.Scoring;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.Core.Models;

/// <summary>
/// Application-wide settings.
/// Persisted to %AppData%\PhotoCuller\settings.json (not the database).
/// </summary>
public class AppSettings
{
    // ── General ──────────────────────────────────────────────────────
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "zh-CN";
    public string? LastSelectedDirectory { get; set; }
    public bool RestoreLastDirectoryOnStartup { get; set; } = true;

    // ── Scanning ─────────────────────────────────────────────────────
    public bool IsRecursiveScanEnabled { get; set; } = true;

    public List<string> SupportedExtensions { get; set; } =
    [
        ".jpg", ".jpeg", ".png", ".heic", ".heif",
        ".webp", ".bmp", ".tiff", ".tif",
    ];

    /// <summary>Maximum file size to scan in MB; files larger than this are skipped.</summary>
    public int MaxFileSizeMb { get; set; } = 500;

    public bool ReadExif { get; set; } = true;
    public bool GenerateThumbnails { get; set; } = true;

    /// <summary>Number of scan threads (0 = automatic = CPU core count - 1).</summary>
    public int ScanThreadCount { get; set; }

    public bool SkipHiddenFolders { get; set; } = true;
    public bool SkipSystemFolders { get; set; } = true;

    // ── Scoring weights (should sum to 100) ──────────────────────────
    public ScoringWeights ScoringWeights { get; set; } = ScoringWeights.Default;

    // ── Similar photos ───────────────────────────────────────────────
    /// <summary>pHash high-similarity threshold (Hamming distance ≤ this value is considered highly similar).</summary>
    public int SimilarityThresholdHigh { get; set; } = 8;

    /// <summary>pHash possible-similarity threshold (Hamming distance ≤ this value is considered possibly similar).</summary>
    public int SimilarityThresholdPossible { get; set; } = 15;

    /// <summary>Preset used for similar-photo grouping. Normal is conservative for 64-bit pHash.</summary>
    public SimilarityPreset SimilarityPreset { get; set; } = SimilarityPreset.Normal;

    /// <summary>Burst-shot time window in seconds.</summary>
    public int BurstTimeWindowSeconds { get; set; } = 10;

    public bool UseCaptureTimeForGrouping { get; set; } = true;

    // ── File management ──────────────────────────────────────────────
    public string ToDeleteFolderName { get; set; } = "PhotoCuller_ToDelete";
    public bool PreserveFolderStructure { get; set; } = true;
    public bool ConfirmBeforeMove { get; set; } = true;
    public bool GenerateOperationLog { get; set; } = true;

    // ── AI models ────────────────────────────────────────────────────
    public bool EnableCloudAi { get; set; } = false;
    public bool EnableLocalAi { get; set; } = false;
    public Guid? SelectedModelProfileId { get; set; }

    // ── Cache ────────────────────────────────────────────────────────
    public string? ThumbnailCacheDirectory { get; set; }
    public string? DatabasePath { get; set; }

    /// <summary>Maximum thumbnail cache size in MB.</summary>
    public int MaxCacheSizeMb { get; set; } = 2048;

    // ── Privacy ──────────────────────────────────────────────────────
    public bool AllowCloudImageAnalysis { get; set; }
    public bool CompressBeforeUpload { get; set; } = true;
    public int UploadMaxEdgePx { get; set; } = 1024;
    public bool UploadThumbnailOnly { get; set; } = true;

    // ── Logging ──────────────────────────────────────────────────────
    public string LogLevel { get; set; } = "Information";
}
