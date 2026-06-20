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
    private readonly IScanProgressService _scanProgressService;
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
        IScanProgressService scanProgressService,
        ILogger<MainViewModel> logger)
    {
        _scanner              = scanner;
        _scoring              = scoring;
        _grouping             = grouping;
        _photoRepo            = photoRepo;
        _groupRepo            = groupRepo;
        _thumbnails           = thumbnails;
        _hash                 = hash;
        _pHash                = pHash;
        _settings             = settings;
        _fileMover            = fileMover;
        _undo                 = undo;
        _fileDialog           = fileDialog;
        _notifications        = notifications;
        _scanProgressService  = scanProgressService;
        _logger               = logger;
    }

    // ── State ────────────────────────────────────────────────────────────────

#pragma warning disable MVVMTK0045
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoryDisplayName))]
    private string _selectedDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanToggleButtonContent))]
    private bool _isScanning;
    [ObservableProperty] private ScanProgress _scanProgress = ScanProgress.Idle;
    [ObservableProperty] private int _totalPhotoCount;
    [ObservableProperty] private int _groupCount;
    [ObservableProperty] private int _keepCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _trashCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private string _scanDuration = "—";
    [ObservableProperty] private string _lastScanTime = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPhoto))]
    private PhotoItemViewModel? _selectedPhoto;
#pragma warning restore MVVMTK0045

    public bool HasSelectedPhoto => SelectedPhoto is not null;

    public string ScanToggleButtonContent =>
        IsScanning ? "■  停止扫描" : "▶  开始扫描";

    /// <summary>
    /// Short display name for the selected directory shown in the nav panel.
    /// Shows just the folder name (e.g. "2024旅行") rather than the full path.
    /// </summary>
    public string DirectoryDisplayName =>
        string.IsNullOrEmpty(SelectedDirectory)
            ? "未选择目录"
            : Path.GetFileName(SelectedDirectory) is { Length: > 0 } name
                ? name
                : SelectedDirectory;

    public ObservableCollection<PhotoGroupViewModel> Groups    { get; } = [];
    public ObservableCollection<PhotoItemViewModel>  AllPhotos { get; } = [];

    // ── Filtered views (computed from AllPhotos) ──────────────────────────────

    public IEnumerable<PhotoItemViewModel> KeepPhotos =>
        AllPhotos.Where(p => p.Status is RecommendationStatus.Keep
                                      or RecommendationStatus.ManualKeep);

    public IEnumerable<PhotoItemViewModel> PendingPhotos =>
        AllPhotos.Where(p => p.Status == RecommendationStatus.Review);

    public IEnumerable<PhotoItemViewModel> TrashPhotos =>
        AllPhotos.Where(p => p.Status is RecommendationStatus.ToDelete
                                      or RecommendationStatus.ManualDelete
                              || p.IsMovedToTrash);

    // ── RefreshCounts (call after any status change) ──────────────────────────

    public void RefreshCounts()
    {
        KeepCount      = AllPhotos.Count(p => p.Status is
            RecommendationStatus.Keep or RecommendationStatus.ManualKeep);
        PendingCount   = AllPhotos.Count(p => p.Status == RecommendationStatus.Review);
        TrashCount     = AllPhotos.Count(p => p.Status is
            RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete
            || p.IsMovedToTrash);
        DuplicateCount = Groups.Count(g => g.GroupType == GroupType.ExactDuplicate);
    }

    // ── Mark commands (used by detail panel buttons and context menus) ────────

    [RelayCommand]
    public void MarkKeep(PhotoItemViewModel? vm)
    {
        if (vm is null) return;
        vm.SetStatus(RecommendationStatus.ManualKeep);
        RefreshCounts();
    }

    [RelayCommand]
    public void MarkDelete(PhotoItemViewModel? vm)
    {
        if (vm is null) return;
        vm.SetStatus(RecommendationStatus.ManualDelete);
        RefreshCounts();
    }

    [RelayCommand]
    public void MarkReview(PhotoItemViewModel? vm)
    {
        if (vm is null) return;
        vm.SetStatus(RecommendationStatus.Review);
        RefreshCounts();
    }

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

            // Single query for all grouped photos, then group in memory
            // (avoids N+1: one GetByGroupAsync call per group)
            var allGroupIds = dbGroups.Select(g => g.Id).ToHashSet();

            // Load all photos that belong to any group in one pass per group
            // Use a dictionary for O(1) group lookup
            var groupMap = dbGroups.ToDictionary(g => g.Id);
            var photosByGroup = new Dictionary<Guid, List<PhotoItemViewModel>>();

            foreach (var group in dbGroups)
            {
                var photos = await _photoRepo.GetByGroupAsync(group.Id, ct);
                var vmList = photos.Select(p => new PhotoItemViewModel(p)).ToList();

                foreach (var vm in vmList)
                    AllPhotos.Add(vm);

                photosByGroup[group.Id] = vmList;
            }

            foreach (var group in dbGroups.OrderByDescending(g => g.PhotoCount))
            {
                var vmList = photosByGroup.TryGetValue(group.Id, out var list)
                    ? list : [];
                Groups.Add(new PhotoGroupViewModel(group, vmList));
            }

            TotalPhotoCount = await _photoRepo.GetTotalCountAsync(ct);
            GroupCount      = dbGroups.Count;
            PendingCount    = AllPhotos.Count(vm =>
                vm.Status == RecommendationStatus.Review);

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

    [RelayCommand(CanExecute = nameof(CanToggleScan))]
    private async Task ToggleScanAsync()
    {
        if (IsScanning)
        {
            StopScan();
            return;
        }

        await StartScanAsync();
    }

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

            // Reset progress so the previous scan's InfoBar is dismissed
            ScanProgress = ScanProgress.Idle;

            // Clear DB so the incremental-scan filter starts fresh.
            await _groupRepo.DeleteAllAsync(_scanCts.Token);
            await _photoRepo.DeleteAllAsync(_scanCts.Token);

            // ── Phase 1: scan + metadata + hashes ────────────────────────────
            // Use a separate CTS for the progress listener so we can cancel it
            // independently when the scanner finishes (otherwise ReadAllAsync
            // blocks forever waiting for more items in the channel).
            using var progressCts = CancellationTokenSource
                .CreateLinkedTokenSource(_scanCts.Token);

            var progressTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var p in _scanProgressService
                        .ReadAllAsync(progressCts.Token))
                    {
                        ScanProgress = p;
                    }
                }
                catch (OperationCanceledException) { /* expected on cancel */ }
            }, progressCts.Token);

            await foreach (var photo in _scanner.ScanAsync(SelectedDirectory, _scanCts.Token))
            {
                photo.ThumbnailPath  = await _thumbnails
                    .GetOrCreateAsync(photo, cancellationToken: _scanCts.Token);
                photo.Sha256Hash     = await _hash
                    .ComputeSha256Async(photo.FilePath, _scanCts.Token);
                photo.PerceptualHash = await _pHash
                    .ComputePHashAsync(photo.FilePath, _scanCts.Token);

                allPhotos.Add(photo);
                AllPhotos.Add(new PhotoItemViewModel(photo));
            }

            // Stop the progress listener now that the scanner is done
            await progressCts.CancelAsync();

            // Stop listening to progress (scanner is done)
            _scanProgressService.Report(ScanProgress.Completed(allPhotos.Count, started.ToUniversalTime()));

            TotalPhotoCount = allPhotos.Count;

            _logger.LogInformation(
                "Scan phase done: {Total} photos, {WithHash} with pHash",
                allPhotos.Count,
                allPhotos.Count(p => p.PerceptualHash.HasValue));

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
            LastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ScanDuration = $"{(DateTime.Now - started).TotalSeconds:F0}s";

            // Refresh all nav counts
            RefreshCounts();

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

    private bool CanToggleScan()
        => IsScanning || CanStartScan();

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void StopScan() => _scanCts?.Cancel();

    partial void OnIsScanningChanged(bool value)
    {
        StartScanCommand.NotifyCanExecuteChanged();
        StopScanCommand.NotifyCanExecuteChanged();
        ToggleScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDirectoryChanged(string value)
    {
        StartScanCommand.NotifyCanExecuteChanged();
        ToggleScanCommand.NotifyCanExecuteChanged();
    }

    // ── File operations ───────────────────────────────────────────────────────

    [RelayCommand]
    public async Task MoveMarkedToTrashAsync(CancellationToken ct = default)
    {
        var appSettings = await _settings.LoadAsync(ct);
        var toMoveVms = AllPhotos
            .Where(vm => vm.Status is RecommendationStatus.ToDelete
                                   or RecommendationStatus.ManualDelete)
            .ToList();
        var toMove = toMoveVms.Select(vm => vm.Model).ToList();

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

            foreach (var vm in toMoveVms)
                vm.NotifyStatusChanged();

            RefreshCounts();
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
