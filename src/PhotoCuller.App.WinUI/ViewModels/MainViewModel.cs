using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.FileOps;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Platform;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using System.Collections.ObjectModel;

namespace PhotoCuller.App.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPhotoScanner _scanner;
    private readonly IPhotoScoringService _scoring;
    private readonly IDuplicateGroupingService _grouping;
    private readonly IPhotoRepository _photoRepo;
    private readonly IPhotoGroupRepository _groupRepo;
    private readonly IThumbnailService _thumbnails;
    private readonly IHashService _hash;
    private readonly IPerceptualHashService _pHash;
    private readonly ISettingsRepository _settings;
    private readonly IFileMoveService _fileMover;
    private readonly IUndoService _undo;
    private readonly IFileDialogService _fileDialog;
    private readonly INotificationService _notifications;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _scanCts;

    public MainViewModel(
        IPhotoScanner scanner,
        IPhotoScoringService scoring,
        IDuplicateGroupingService grouping,
        IPhotoRepository photoRepo,
        IPhotoGroupRepository groupRepo,
        IThumbnailService thumbnails,
        IHashService hash,
        IPerceptualHashService pHash,
        ISettingsRepository settings,
        IFileMoveService fileMover,
        IUndoService undo,
        IFileDialogService fileDialog,
        INotificationService notifications,
        ILogger<MainViewModel> logger)
    {
        _scanner       = scanner;
        _scoring       = scoring;
        _grouping      = grouping;
        _photoRepo     = photoRepo;
        _groupRepo     = groupRepo;
        _thumbnails    = thumbnails;
        _hash          = hash;
        _pHash         = pHash;
        _settings      = settings;
        _fileMover     = fileMover;
        _undo          = undo;
        _fileDialog    = fileDialog;
        _notifications = notifications;
        _logger        = logger;
    }

    // ── State ────────────────────────────────────────────────────────────────

#pragma warning disable MVVMTK0045
    [ObservableProperty] private string _selectedDirectory = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private ScanProgress _scanProgress = ScanProgress.Idle;
    [ObservableProperty] private int _totalPhotoCount;
    [ObservableProperty] private int _groupCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private string _scanDuration = "—";
    [ObservableProperty] private string _lastScanTime = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPhoto))]
    private PhotoItemViewModel? _selectedPhoto;
#pragma warning restore MVVMTK0045

    public bool HasSelectedPhoto => SelectedPhoto is not null;

    public ObservableCollection<PhotoGroupViewModel> Groups    { get; } = [];
    public ObservableCollection<PhotoItemViewModel>  AllPhotos { get; } = [];

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PickDirectoryAsync()
    {
        var path = await _fileDialog.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
            SelectedDirectory = path;
    }

    // ── Load from DB (called on startup or page navigation) ───────────────────

    /// <summary>
    /// Loads photos and groups already persisted in the database.
    /// Called by SimilarGroupsPage.OnNavigatedTo and on app start-up
    /// so that data survives app restarts.
    /// </summary>
    public async Task LoadFromDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            var dbGroups = await _groupRepo.GetAllAsync(ct);
            if (dbGroups.Count == 0) return;

            AllPhotos.Clear();
            Groups.Clear();

            // Load all photos for each group
            foreach (var group in dbGroups.OrderByDescending(g => g.PhotoCount))
            {
                var photos = await _photoRepo.GetByGroupAsync(group.Id, ct);
                var vmList = photos.Select(p => new PhotoItemViewModel(p)).ToList();

                foreach (var vm in vmList)
                    AllPhotos.Add(vm);

                Groups.Add(new PhotoGroupViewModel(group, vmList));
            }

            TotalPhotoCount = await _photoRepo.GetTotalCountAsync(ct);
            GroupCount      = dbGroups.Count;
            PendingCount    = dbGroups
                .SelectMany(g => g.Photos)
                .Count(p => p.RecommendationStatus == RecommendationStatus.Review);

            _logger.LogInformation(
                "Loaded {Photos} photos in {Groups} groups from DB",
                TotalPhotoCount, GroupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadFromDatabase failed");
        }
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (string.IsNullOrEmpty(SelectedDirectory)) return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        var started = DateTime.Now;
        var allPhotos = new List<PhotoItem>();

        try
        {
            AllPhotos.Clear();
            Groups.Clear();

            // ── Phase 1: scan + metadata + hashes ────────────────────────────
            ScanProgress = new Core.Models.ScanProgress(ScanPhase.Enumerating, 0, 0,
                Message: "扫描目录...");

            await foreach (var photo in _scanner.ScanAsync(SelectedDirectory, _scanCts.Token))
            {
                ScanProgress = new Core.Models.ScanProgress(ScanPhase.ReadingMetadata,
                    allPhotos.Count, 0, photo.FileName);

                photo.ThumbnailPath  = await _thumbnails
                    .GetOrCreateAsync(photo, cancellationToken: _scanCts.Token);
                photo.Sha256Hash     = await _hash
                    .ComputeSha256Async(photo.FilePath, _scanCts.Token);
                photo.PerceptualHash = await _pHash
                    .ComputePHashAsync(photo.FilePath, _scanCts.Token);

                allPhotos.Add(photo);
                AllPhotos.Add(new PhotoItemViewModel(photo));
            }

            TotalPhotoCount = allPhotos.Count;

            int withHash = allPhotos.Count(p => p.PerceptualHash.HasValue);
            _logger.LogInformation(
                "Scan phase done: {Total} photos, {WithHash} with pHash",
                allPhotos.Count, withHash);

            // ── Phase 2: scoring ──────────────────────────────────────────────
            ScanProgress = new Core.Models.ScanProgress(ScanPhase.Scoring, 0, allPhotos.Count);
            var appSettings = await _settings.LoadAsync(_scanCts.Token);
            await _scoring.ScoreRangeAsync(allPhotos, appSettings.ScoringWeights,
                new Progress<int>(n => ScanProgress =
                    new Core.Models.ScanProgress(ScanPhase.Scoring, n, allPhotos.Count)),
                _scanCts.Token);

            // ── Phase 3: grouping ─────────────────────────────────────────────
            ScanProgress = new Core.Models.ScanProgress(ScanPhase.Grouping, 0, 0,
                Message: "分组中...");

            var photoGroups = await _grouping.BuildGroupsAsync(allPhotos,
                ct: _scanCts.Token);

            _logger.LogInformation(
                "Grouping done: {GroupCount} groups from {PhotoCount} photos",
                photoGroups.Count, allPhotos.Count);

            // ── Phase 4: persist (groups FIRST, then photos with GroupId) ─────
            //
            // Order matters for FK integrity:
            //   1. Insert PhotoGroups (no FK dependencies)
            //   2. Detach navigation Photos from groups before inserting groups
            //      (EF Core would try to re-insert already-tracked entities)
            //   3. Insert PhotoItems (with GroupId set, FK satisfied)
            //
            if (photoGroups.Count > 0)
            {
                // Snapshot group shells (no navigation photos) for DB insert
                var groupShells = photoGroups.Select(g => new PhotoGroup
                {
                    Id               = g.Id,
                    GroupType        = g.GroupType,
                    Name             = g.Name,
                    RecommendedPhotoId = g.RecommendedPhotoId,
                    PhotoCount       = g.PhotoCount,
                    SimilarityScore  = g.SimilarityScore,
                    CreatedTime      = g.CreatedTime,
                }).ToList();

                await _groupRepo.DeleteAllAsync(_scanCts.Token); // clear stale groups
                await _groupRepo.AddRangeAsync(groupShells, _scanCts.Token);

                _logger.LogInformation("Persisted {Count} groups to DB", groupShells.Count);
            }

            // Now persist photos (GroupId already set by grouping service)
            await _photoRepo.AddRangeAsync(allPhotos, _scanCts.Token);

            _logger.LogInformation(
                "Persisted {Count} photos to DB (grouped: {Grouped})",
                allPhotos.Count,
                allPhotos.Count(p => p.GroupId.HasValue));

            // ── Phase 5: build view-models ────────────────────────────────────
            var photoVmMap = AllPhotos.ToDictionary(vm => vm.Id);
            Groups.Clear();

            foreach (var group in photoGroups.OrderByDescending(g => g.PhotoCount))
            {
                var memberVms = group.Photos
                    .Select(p => photoVmMap.TryGetValue(p.Id, out var vm) ? vm : null)
                    .OfType<PhotoItemViewModel>();
                Groups.Add(new PhotoGroupViewModel(group, memberVms));
            }

            GroupCount   = photoGroups.Count;
            PendingCount = allPhotos.Count(p =>
                p.RecommendationStatus == RecommendationStatus.Review);
            LastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ScanDuration = $"{(DateTime.Now - started).TotalSeconds:F0}s";

            // Persist last-used directory
            appSettings.LastSelectedDirectory = SelectedDirectory;
            await _settings.SaveAsync(appSettings, _scanCts.Token);

            ScanProgress = Core.Models.ScanProgress.Completed(
                allPhotos.Count, started.ToUniversalTime());

            _notifications.ShowSuccess(
                $"扫描完成：{allPhotos.Count} 张照片" +
                (photoGroups.Count > 0 ? $"，{photoGroups.Count} 个相似组" : "，未发现相似照片"));
        }
        catch (OperationCanceledException)
        {
            ScanProgress = Core.Models.ScanProgress.Cancelled(allPhotos.Count, allPhotos.Count);
            _notifications.ShowWarning("扫描已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed");
            _notifications.ShowError($"扫描失败：{ex.Message}");
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanStartScan()
        => !IsScanning && !string.IsNullOrEmpty(SelectedDirectory);

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void StopScan() => _scanCts?.Cancel();

    partial void OnIsScanningChanged(bool value)
    {
        StartScanCommand.NotifyCanExecuteChanged();
        StopScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDirectoryChanged(string value)
        => StartScanCommand.NotifyCanExecuteChanged();

    // ── File operations ───────────────────────────────────────────────────────

    [RelayCommand]
    public async Task MoveMarkedToTrashAsync(CancellationToken ct = default)
    {
        var appSettings = await _settings.LoadAsync(ct);
        var toMove = AllPhotos
            .Where(vm => vm.Status is RecommendationStatus.ToDelete
                                   or RecommendationStatus.ManualDelete)
            .Select(vm => vm.Model)
            .ToList();

        if (toMove.Count == 0) return;

        var trashDir = string.IsNullOrEmpty(SelectedDirectory)
            ? Path.Combine(Path.GetTempPath(), "PhotoCuller_ToDelete")
            : Path.Combine(
                Path.GetDirectoryName(SelectedDirectory) ?? SelectedDirectory,
                appSettings.ToDeleteFolderName);

        try
        {
            var logs = await _fileMover.MoveManyToTrashAsync(
                toMove, trashDir, appSettings.PreserveFolderStructure,
                new Progress<int>(n =>
                    _notifications.ShowSuccess($"已移动 {n}/{toMove.Count} 张照片")),
                ct);

            int ok   = logs.Count(l => l.Status == Core.Enums.OperationStatus.Success);
            int fail = logs.Count(l => l.Status == Core.Enums.OperationStatus.Failed);

            if (fail > 0)
                _notifications.ShowWarning($"移动完成：{ok} 成功，{fail} 失败");
            else
                _notifications.ShowSuccess($"已将 {ok} 张照片移动到待删除目录：{trashDir}");

            PendingCount = AllPhotos.Count(vm =>
                vm.Status is RecommendationStatus.Review or RecommendationStatus.ToDelete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveMarkedToTrash failed");
            _notifications.ShowError($"移动失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public async Task UndoLastMoveAsync(CancellationToken ct = default)
    {
        bool ok = await _undo.UndoLastAsync(ct);
        if (ok) _notifications.ShowSuccess("已撤销上一次移动操作");
        else    _notifications.ShowWarning("没有可撤销的操作");
    }
}
