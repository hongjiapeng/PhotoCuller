using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Platform;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Core.Scoring;

namespace PhotoCuller.App.WinUI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _repo;
    private readonly IThemeService _theme;
    private readonly INotificationService _notifications;
    private readonly ILogger<SettingsViewModel> _logger;

    private AppSettings _settings = new();

    public SettingsViewModel(
        ISettingsRepository repo,
        IThemeService theme,
        INotificationService notifications,
        ILogger<SettingsViewModel> logger)
    {
        _repo          = repo;
        _theme         = theme;
        _notifications = notifications;
        _logger        = logger;
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        _settings = await _repo.LoadAsync();
        RefreshFromSettings();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        ApplyToSettings();
        await _repo.SaveAsync(_settings);
        _theme.ApplyTheme(_settings.Theme);
        _notifications.ShowSuccess("设置已保存");
        _logger.LogInformation("Settings saved.");
    }

    [RelayCommand]
    public async Task ResetWeightsAsync()
    {
        var def = ScoringWeights.Default;
        SharpnessPercent   = def.SharpnessPercent;
        ExposurePercent    = def.ExposurePercent;
        ContrastPercent    = def.ContrastPercent;
        NoisePercent       = def.NoisePercent;
        ResolutionPercent  = def.ResolutionPercent;
        CompositionPercent = def.CompositionPercent;
        ColorPercent       = def.ColorPercent;
        await SaveAsync();
    }

    // ── General ───────────────────────────────────────────────────────────────

#pragma warning disable MVVMTK0045
    [ObservableProperty] private string _themeValue     = "Dark";
    [ObservableProperty] private string _languageValue  = "zh-CN";
    [ObservableProperty] private bool   _restoreLastDir  = true;

    // ── Scan ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _recursiveScan     = true;
    [ObservableProperty] private int    _maxFileSizeMb     = 500;
    [ObservableProperty] private bool   _readExif          = true;
    [ObservableProperty] private bool   _generateThumbnails = true;
    [ObservableProperty] private int    _scanThreadCount    = 0;
    [ObservableProperty] private bool   _skipHiddenFolders  = true;
    [ObservableProperty] private bool   _skipSystemFolders  = true;

    // ── Scoring weights ───────────────────────────────────────────────────────
    [ObservableProperty] private int _sharpnessPercent   = 35;
    [ObservableProperty] private int _exposurePercent    = 20;
    [ObservableProperty] private int _contrastPercent    = 15;
    [ObservableProperty] private int _noisePercent       = 10;
    [ObservableProperty] private int _resolutionPercent  = 10;
    [ObservableProperty] private int _compositionPercent = 10;
    [ObservableProperty] private int _colorPercent       = 0;

    // ── Similar ───────────────────────────────────────────────────────────────
    [ObservableProperty] private int  _similarityThresholdHigh     = 8;
    [ObservableProperty] private int  _similarityThresholdPossible = 15;
    [ObservableProperty] private int  _burstTimeWindowSeconds       = 10;
    [ObservableProperty] private bool _useCaptureTimeForGrouping    = true;

    // ── File management ───────────────────────────────────────────────────────
    [ObservableProperty] private string _toDeleteFolderName     = "PhotoCuller_ToDelete";
    [ObservableProperty] private bool   _preserveFolderStructure = true;
    [ObservableProperty] private bool   _confirmBeforeMove       = true;
    [ObservableProperty] private bool   _generateOperationLog    = true;

    // ── Cache ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private string? _thumbnailCacheDirectory;
    [ObservableProperty] private string? _databasePath;
    [ObservableProperty] private int     _maxCacheSizeMb = 2048;

    // ── Privacy ───────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _allowCloudImageAnalysis = false;
    [ObservableProperty] private bool _compressBeforeUpload    = true;
    [ObservableProperty] private int  _uploadMaxEdgePx         = 1024;
    [ObservableProperty] private bool _uploadThumbnailOnly      = true;

    // ── Logging ───────────────────────────────────────────────────────────────
    [ObservableProperty] private string _logLevel = "Information";
#pragma warning restore MVVMTK0045

    // ── Computed ──────────────────────────────────────────────────────────────

    public int WeightTotal =>
        SharpnessPercent + ExposurePercent + ContrastPercent
        + NoisePercent + ResolutionPercent + CompositionPercent + ColorPercent;

    public bool WeightTotalValid => WeightTotal == 100;

    public string WeightTotalLabel => WeightTotal == 100
        ? "✓ 总计 100"
        : $"⚠ 总计 {WeightTotal}（需等于 100）";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshFromSettings()
    {
        ThemeValue    = _settings.Theme;
        LanguageValue = _settings.Language;
        RestoreLastDir = _settings.RestoreLastDirectoryOnStartup;

        RecursiveScan      = _settings.IsRecursiveScanEnabled;
        MaxFileSizeMb      = _settings.MaxFileSizeMb;
        ReadExif           = _settings.ReadExif;
        GenerateThumbnails = _settings.GenerateThumbnails;
        ScanThreadCount    = _settings.ScanThreadCount;
        SkipHiddenFolders  = _settings.SkipHiddenFolders;
        SkipSystemFolders  = _settings.SkipSystemFolders;

        SharpnessPercent   = _settings.ScoringWeights.SharpnessPercent;
        ExposurePercent    = _settings.ScoringWeights.ExposurePercent;
        ContrastPercent    = _settings.ScoringWeights.ContrastPercent;
        NoisePercent       = _settings.ScoringWeights.NoisePercent;
        ResolutionPercent  = _settings.ScoringWeights.ResolutionPercent;
        CompositionPercent = _settings.ScoringWeights.CompositionPercent;
        ColorPercent       = _settings.ScoringWeights.ColorPercent;

        SimilarityThresholdHigh     = _settings.SimilarityThresholdHigh;
        SimilarityThresholdPossible = _settings.SimilarityThresholdPossible;
        BurstTimeWindowSeconds      = _settings.BurstTimeWindowSeconds;
        UseCaptureTimeForGrouping   = _settings.UseCaptureTimeForGrouping;

        ToDeleteFolderName      = _settings.ToDeleteFolderName;
        PreserveFolderStructure = _settings.PreserveFolderStructure;
        ConfirmBeforeMove       = _settings.ConfirmBeforeMove;
        GenerateOperationLog    = _settings.GenerateOperationLog;

        ThumbnailCacheDirectory = _settings.ThumbnailCacheDirectory;
        DatabasePath            = _settings.DatabasePath;
        MaxCacheSizeMb          = _settings.MaxCacheSizeMb;

        AllowCloudImageAnalysis = _settings.AllowCloudImageAnalysis;
        CompressBeforeUpload    = _settings.CompressBeforeUpload;
        UploadMaxEdgePx         = _settings.UploadMaxEdgePx;
        UploadThumbnailOnly     = _settings.UploadThumbnailOnly;

        LogLevel = _settings.LogLevel;

        OnPropertyChanged(nameof(WeightTotal));
        OnPropertyChanged(nameof(WeightTotalValid));
        OnPropertyChanged(nameof(WeightTotalLabel));
    }

    private void ApplyToSettings()
    {
        _settings.Theme                         = ThemeValue;
        _settings.Language                      = LanguageValue;
        _settings.RestoreLastDirectoryOnStartup = RestoreLastDir;

        _settings.IsRecursiveScanEnabled = RecursiveScan;
        _settings.MaxFileSizeMb          = MaxFileSizeMb;
        _settings.ReadExif               = ReadExif;
        _settings.GenerateThumbnails     = GenerateThumbnails;
        _settings.ScanThreadCount        = ScanThreadCount;
        _settings.SkipHiddenFolders      = SkipHiddenFolders;
        _settings.SkipSystemFolders      = SkipSystemFolders;

        _settings.ScoringWeights = new ScoringWeights
        {
            SharpnessPercent   = SharpnessPercent,
            ExposurePercent    = ExposurePercent,
            ContrastPercent    = ContrastPercent,
            NoisePercent       = NoisePercent,
            ResolutionPercent  = ResolutionPercent,
            CompositionPercent = CompositionPercent,
            ColorPercent       = ColorPercent,
        };

        _settings.SimilarityThresholdHigh     = SimilarityThresholdHigh;
        _settings.SimilarityThresholdPossible = SimilarityThresholdPossible;
        _settings.BurstTimeWindowSeconds      = BurstTimeWindowSeconds;
        _settings.UseCaptureTimeForGrouping   = UseCaptureTimeForGrouping;

        _settings.ToDeleteFolderName      = ToDeleteFolderName;
        _settings.PreserveFolderStructure = PreserveFolderStructure;
        _settings.ConfirmBeforeMove       = ConfirmBeforeMove;
        _settings.GenerateOperationLog    = GenerateOperationLog;

        _settings.ThumbnailCacheDirectory = ThumbnailCacheDirectory;
        _settings.DatabasePath            = DatabasePath;
        _settings.MaxCacheSizeMb          = MaxCacheSizeMb;

        _settings.AllowCloudImageAnalysis = AllowCloudImageAnalysis;
        _settings.CompressBeforeUpload    = CompressBeforeUpload;
        _settings.UploadMaxEdgePx         = UploadMaxEdgePx;
        _settings.UploadThumbnailOnly     = UploadThumbnailOnly;

        _settings.LogLevel = LogLevel;
    }

    // Recompute weight total whenever any weight changes
    partial void OnSharpnessPercentChanged(int value)   => NotifyWeights();
    partial void OnExposurePercentChanged(int value)    => NotifyWeights();
    partial void OnContrastPercentChanged(int value)    => NotifyWeights();
    partial void OnNoisePercentChanged(int value)       => NotifyWeights();
    partial void OnResolutionPercentChanged(int value)  => NotifyWeights();
    partial void OnCompositionPercentChanged(int value) => NotifyWeights();
    partial void OnColorPercentChanged(int value)       => NotifyWeights();

    private void NotifyWeights()
    {
        OnPropertyChanged(nameof(WeightTotal));
        OnPropertyChanged(nameof(WeightTotalValid));
        OnPropertyChanged(nameof(WeightTotalLabel));
    }
}
