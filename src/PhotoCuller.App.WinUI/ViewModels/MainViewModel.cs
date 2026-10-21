using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoCuller.Application.Analysis;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.FileOps;
using PhotoCuller.Core.Interfaces.Platform;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using System.Collections.ObjectModel;

namespace PhotoCuller.App.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPhotoAnalysisEngine _analysisEngine;
    private readonly IPhotoRepository _photoRepo;
    private readonly IPhotoGroupRepository _groupRepo;
    private readonly IAnalysisResultRepository _analysisResults;
    private readonly ISettingsRepository _settings;
    private readonly IFileMoveService _fileMover;
    private readonly IUndoService _undo;
    private readonly IFileDialogService _fileDialog;
    private readonly INotificationService _notifications;
    private readonly ILogger<MainViewModel> _logger;

    private CancellationTokenSource? _scanCts;
    private readonly SemaphoreSlim _dataGate = new(1, 1);
    // Incremented whenever a scan replaces the in-memory result set. A
    // database load that started before that replacement must not clear the
    // newer scan results when its asynchronous queries finish.
    private int _dataVersion;

    public MainViewModel(
        IPhotoAnalysisEngine analysisEngine,
        IPhotoRepository photoRepo,
        IPhotoGroupRepository groupRepo,
        IAnalysisResultRepository analysisResults,
        ISettingsRepository settings,
        IFileMoveService fileMover,
        IUndoService undo,
        IFileDialogService fileDialog,
        INotificationService notifications,
        ILogger<MainViewModel> logger)
    {
        _analysisEngine       = analysisEngine;
        _photoRepo            = photoRepo;
        _groupRepo            = groupRepo;
        _analysisResults      = analysisResults;
        _settings             = settings;
        _fileMover            = fileMover;
        _undo                 = undo;
        _fileDialog           = fileDialog;
        _notifications        = notifications;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseMode))]
    [NotifyPropertyChangedFor(nameof(IsCompareMode))]
    [NotifyPropertyChangedFor(nameof(IsInspectMode))]
    private SimilarGroupViewMode _currentViewMode = SimilarGroupViewMode.Browse;

    [ObservableProperty] private PhotoGroupViewModel? _selectedGroup;
    [ObservableProperty] private PhotoItemViewModel? _recommendedPhoto;
    [ObservableProperty] private PhotoItemViewModel? _comparePhoto;
    [ObservableProperty] private PhotoItemViewModel? _inspectPhoto;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private bool _isFitToWindow = true;
#pragma warning restore MVVMTK0045

    public bool HasSelectedPhoto => SelectedPhoto is not null;
    public bool IsBrowseMode => CurrentViewMode == SimilarGroupViewMode.Browse;
    public bool IsCompareMode => CurrentViewMode == SimilarGroupViewMode.Compare;
    public bool IsInspectMode => CurrentViewMode == SimilarGroupViewMode.Inspect;

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

    public string CompareHeader =>
        SelectedGroup is null
            ? "请选择一个相似照片组"
            : $"相似照片组 > 组 {Groups.IndexOf(SelectedGroup) + 1} / {Groups.Count} > {RecommendedPhoto?.FileName ?? "—"}";

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

    private bool CanEditPhotos() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public Task MarkKeepAsync(PhotoItemViewModel? vm)
        => SaveDecisionAsync(vm, RecommendationStatus.ManualKeep);

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public Task MarkDeleteAsync(PhotoItemViewModel? vm)
        => SaveDecisionAsync(vm, RecommendationStatus.ManualDelete);

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public Task MarkReviewAsync(PhotoItemViewModel? vm)
        => SaveDecisionAsync(vm, RecommendationStatus.Review);

    private async Task SaveDecisionAsync(PhotoItemViewModel? vm, RecommendationStatus status)
    {
        if (vm is null || IsScanning) return;
        await _dataGate.WaitAsync();
        try
        {
            await _photoRepo.UpdateRecommendationAsync(vm.Id, status);
            vm.SetStatus(status);
            if (status == RecommendationStatus.ManualKeep && FindGroupForPhoto(vm) is { } group)
            {
                var previous = group.RecommendedPhoto;
                group.SetRecommendedPhoto(vm);
                await _groupRepo.UpdateAsync(group.Model);
                if (previous is not null && previous.Id != vm.Id
                    && previous.Status == RecommendationStatus.Keep)
                {
                    await _photoRepo.UpdateRecommendationAsync(previous.Id, RecommendationStatus.Review);
                    previous.SetStatus(RecommendationStatus.Review);
                }
                if (SelectedGroup == group) RecommendedPhoto = vm;
            }
            RefreshCounts();
            UpdatePhotoSelectionFlags();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save decision for {PhotoId}", vm.Id);
            _notifications.ShowError($"保存选片状态失败：{ex.Message}");
        }
        finally { _dataGate.Release(); }
    }

    [RelayCommand]
    public void EnterBrowseMode()
    {
        CurrentViewMode = SimilarGroupViewMode.Browse;
    }

    [RelayCommand]
    public void EnterCompareMode()
    {
        EnsureSelectedGroup();
        if (SelectedGroup is null) return;

        EnsureRecommendedAndCandidate();
        CurrentViewMode = SimilarGroupViewMode.Compare;
        IsFitToWindow = true;
        ZoomLevel = 1.0;
        SelectedPhoto = ComparePhoto ?? RecommendedPhoto;
    }

    [RelayCommand]
    public void EnterInspectMode()
    {
        EnsureSelectedGroup();
        if (SelectedGroup is null) return;

        EnsureRecommendedAndCandidate();
        InspectPhoto ??= ComparePhoto ?? RecommendedPhoto;
        SelectedPhoto = InspectPhoto;
        CurrentViewMode = SimilarGroupViewMode.Inspect;
    }

    [RelayCommand]
    public void SelectGroup(PhotoGroupViewModel? group)
    {
        if (group is null) return;

        SelectedGroup = group;
        EnsureRecommendedAndCandidate();
        SelectedPhoto = ComparePhoto ?? RecommendedPhoto;
    }

    [RelayCommand]
    public void SelectComparePhoto(PhotoItemViewModel? photo)
    {
        if (photo is null) return;

        var group = FindGroupForPhoto(photo);
        if (group is null) return;

        // The algorithm recommendation is already fixed in the left panel.
        // Clicking its green filmstrip card must not advance the blue
        // comparison selection to the following photo.
        if (group.RecommendedPhoto?.Id == photo.Id)
            return;

        // Clicking the already selected comparison photo is idempotent.
        if (ComparePhoto?.Id == photo.Id)
        {
            SelectedPhoto = photo;
            return;
        }

        if (group != SelectedGroup)
            SelectedGroup = group;

        EnsureRecommendedAndCandidate();

        ComparePhoto = photo;

        InspectPhoto = ComparePhoto;
        SelectedPhoto = ComparePhoto;
        UpdatePhotoSelectionFlags();
    }

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public async Task SetAsKeepAsync(CancellationToken ct = default)
    {
        if (IsScanning || SelectedGroup is null || ComparePhoto is null) return;

        var previousRecommended = RecommendedPhoto;
        await MarkKeepAsync(ComparePhoto);

        ComparePhoto = previousRecommended
            ?? GetNextPhotoInGroup(RecommendedPhoto, skipRecommended: true)
            ?? RecommendedPhoto;
        InspectPhoto = ComparePhoto;
        SelectedPhoto = ComparePhoto;
        RefreshCounts();
        UpdatePhotoSelectionFlags();
        OnPropertyChanged(nameof(CompareHeader));
    }

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public async Task MarkAsKeepAsync()
    {
        var photo = CurrentActionPhoto();
        if (photo is null) return;

        await MarkKeepAsync(photo);
    }

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public async Task MarkAsDeleteAsync()
    {
        var photo = CurrentActionPhoto();
        if (photo is null) return;

        await MarkDeleteAsync(photo);
    }

    [RelayCommand]
    public void ToggleZoom()
    {
        if (IsFitToWindow)
            SetActualSize();
        else
            SetFitToWindow();
    }

    [RelayCommand]
    public void SetFitToWindow()
    {
        IsFitToWindow = true;
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    public void SetActualSize()
    {
        IsFitToWindow = false;
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    public void ZoomIn()
    {
        IsFitToWindow = false;
        ZoomLevel = Math.Min(4.0, Math.Round(ZoomLevel + 0.25, 2));
    }

    [RelayCommand]
    public void ZoomOut()
    {
        IsFitToWindow = false;
        ZoomLevel = Math.Max(0.25, Math.Round(ZoomLevel - 0.25, 2));
    }

    [RelayCommand]
    public void NextPhoto()
    {
        EnsureSelectedGroup();
        if (SelectedGroup is null) return;

        if (IsInspectMode)
        {
            InspectPhoto = GetNextPhotoInGroup(InspectPhoto, skipRecommended: false);
            SelectedPhoto = InspectPhoto;
        }
        else
        {
            ComparePhoto = GetNextPhotoInGroup(ComparePhoto ?? RecommendedPhoto, skipRecommended: true);
            InspectPhoto = ComparePhoto;
            SelectedPhoto = ComparePhoto;
        }

        UpdatePhotoSelectionFlags();
    }

    [RelayCommand]
    public void PreviousPhoto()
    {
        EnsureSelectedGroup();
        if (SelectedGroup is null) return;

        if (IsInspectMode)
        {
            InspectPhoto = GetPreviousPhotoInGroup(InspectPhoto, skipRecommended: false);
            SelectedPhoto = InspectPhoto;
        }
        else
        {
            ComparePhoto = GetPreviousPhotoInGroup(ComparePhoto ?? RecommendedPhoto, skipRecommended: true);
            InspectPhoto = ComparePhoto;
            SelectedPhoto = ComparePhoto;
        }

        UpdatePhotoSelectionFlags();
    }

    private void EnsureSelectedGroup()
    {
        if (SelectedGroup is not null) return;

        SelectedGroup = SelectedPhoto is not null
            ? FindGroupForPhoto(SelectedPhoto)
            : Groups.FirstOrDefault();
    }

    private void EnsureRecommendedAndCandidate()
    {
        RecommendedPhoto = SelectedGroup?.RecommendedPhoto;
        ComparePhoto = ComparePhoto is not null && SelectedGroup?.Photos.Contains(ComparePhoto) == true
            ? ComparePhoto
            : GetNextPhotoInGroup(RecommendedPhoto, skipRecommended: true) ?? RecommendedPhoto;
        InspectPhoto = InspectPhoto is not null && SelectedGroup?.Photos.Contains(InspectPhoto) == true
            ? InspectPhoto
            : ComparePhoto ?? RecommendedPhoto;
        UpdatePhotoSelectionFlags();
        OnPropertyChanged(nameof(CompareHeader));
    }

    private PhotoGroupViewModel? FindGroupForPhoto(PhotoItemViewModel photo)
        => Groups.FirstOrDefault(g => g.Photos.Any(p => p.Id == photo.Id));

    private PhotoItemViewModel? GetNextPhotoInGroup(
        PhotoItemViewModel? current, bool skipRecommended)
        => GetAdjacentPhotoInGroup(current, skipRecommended, 1);

    private PhotoItemViewModel? GetPreviousPhotoInGroup(
        PhotoItemViewModel? current, bool skipRecommended)
        => GetAdjacentPhotoInGroup(current, skipRecommended, -1);

    private PhotoItemViewModel? GetAdjacentPhotoInGroup(
        PhotoItemViewModel? current, bool skipRecommended, int direction)
    {
        if (SelectedGroup is null || SelectedGroup.Photos.Count == 0)
            return null;

        var photos = SelectedGroup.Photos.ToList();
        var startIndex = current is null
            ? -1
            : Math.Max(0, photos.FindIndex(p => p.Id == current.Id));

        for (var offset = 1; offset <= photos.Count; offset++)
        {
            var index = (startIndex + direction * offset + photos.Count) % photos.Count;
            var candidate = photos[index];
            if (!skipRecommended || candidate != RecommendedPhoto)
                return candidate;
        }

        return RecommendedPhoto;
    }

    private PhotoItemViewModel? CurrentActionPhoto()
        => CurrentViewMode switch
        {
            SimilarGroupViewMode.Inspect => InspectPhoto ?? SelectedPhoto,
            SimilarGroupViewMode.Compare => ComparePhoto ?? SelectedPhoto,
            _                            => SelectedPhoto,
        };

    private void UpdatePhotoSelectionFlags()
    {
        var recommendedIds = Groups.Select(group => group.Model.RecommendedPhotoId).ToHashSet();
        foreach (var photo in AllPhotos)
        {
            var isAlgorithmRecommended = recommendedIds.Contains(photo.Id);
            photo.IsAlgorithmRecommended = isAlgorithmRecommended;
            photo.IsRecommendedInSelectedGroup =
                SelectedGroup is not null && photo.Id == SelectedGroup.Model.RecommendedPhotoId;
            photo.IsCurrentComparePhoto = ComparePhoto is not null && photo.Id == ComparePhoto.Id;
            photo.IsCurrentInspectPhoto = InspectPhoto is not null && photo.Id == InspectPhoto.Id;
            photo.IsCurrentBrowseAlternative =
                SelectedPhoto is not null
                && photo.Id == SelectedPhoto.Id
                && !isAlgorithmRecommended;
        }
    }

    private void EnsureDefaultPhotoSelection()
    {
        var hasValidGroup = SelectedGroup is not null && Groups.Contains(SelectedGroup);
        if (!hasValidGroup)
        {
            SelectedGroup = Groups.FirstOrDefault();
            SelectedPhoto = SelectedGroup?.RecommendedPhoto;
            return;
        }

        if (SelectedPhoto is null || SelectedGroup?.Photos.Contains(SelectedPhoto) != true)
            SelectedPhoto = SelectedGroup?.RecommendedPhoto;
    }

    partial void OnCurrentViewModeChanged(SimilarGroupViewMode value)
    {
        OnPropertyChanged(nameof(IsBrowseMode));
        OnPropertyChanged(nameof(IsCompareMode));
        OnPropertyChanged(nameof(IsInspectMode));
    }

    partial void OnSelectedGroupChanged(PhotoGroupViewModel? value)
    {
        ComparePhoto = null;
        InspectPhoto = null;
        EnsureRecommendedAndCandidate();
    }

    partial void OnRecommendedPhotoChanged(PhotoItemViewModel? value)
        => UpdatePhotoSelectionFlags();

    partial void OnSelectedPhotoChanged(PhotoItemViewModel? value)
        => UpdatePhotoSelectionFlags();

    partial void OnComparePhotoChanged(PhotoItemViewModel? value)
    {
        if (!IsInspectMode)
            SelectedPhoto = value ?? RecommendedPhoto;
        UpdatePhotoSelectionFlags();
    }

    partial void OnInspectPhotoChanged(PhotoItemViewModel? value)
    {
        if (IsInspectMode)
            SelectedPhoto = value ?? ComparePhoto ?? RecommendedPhoto;
        UpdatePhotoSelectionFlags();
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
        var loadVersion = Volatile.Read(ref _dataVersion);

        try
        {
            var dbGroups = await _groupRepo.GetAllAsync(ct);
            var dbPhotos = await _photoRepo.GetAllAsync(ct);

            // The page loads persisted data asynchronously during startup. If
            // the user starts a scan while those queries are in flight, the
            // scan owns the in-memory collections now. Applying this stale
            // snapshot would clear the freshly scanned photos and leave empty
            // group rows on screen.
            if (loadVersion != Volatile.Read(ref _dataVersion))
            {
                _logger.LogDebug(
                    "Skipping stale database load (version {LoadVersion}, current {CurrentVersion})",
                    loadVersion, Volatile.Read(ref _dataVersion));
                return;
            }

            AllPhotos.Clear();
            Groups.Clear();

            // Load every photo, including ungrouped photos, into the library.
            // Groups are rebuilt from the same in-memory set so the library
            // count and the group contents cannot diverge after app restart.
            var photoVmMap = dbPhotos
                .Select(p => new PhotoItemViewModel(p))
                .ToDictionary(vm => vm.Id);

            foreach (var photo in dbPhotos)
                AllPhotos.Add(photoVmMap[photo.Id]);

            var photosByGroup = dbPhotos.Where(p => p.GroupId.HasValue)
                .GroupBy(p => p.GroupId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(p => photoVmMap[p.Id]).ToList());

            foreach (var group in dbGroups.OrderByDescending(g => g.PhotoCount))
            {
                var vmList = photosByGroup.TryGetValue(group.Id, out var list)
                    ? list : [];
                Groups.Add(new PhotoGroupViewModel(group, vmList));
            }

            EnsureDefaultPhotoSelection();
            UpdatePhotoSelectionFlags();

            TotalPhotoCount = dbPhotos.Count;
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

    [RelayCommand(CanExecute = nameof(CanToggleScan), AllowConcurrentExecutions = true)]
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
        if (IsScanning || string.IsNullOrEmpty(SelectedDirectory)) return;

        Interlocked.Increment(ref _dataVersion);
        var sourceDirectory = SelectedDirectory;
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        var started = DateTime.Now;
        var allPhotos = new List<PhotoItem>();
        // Drain pending decision writes before taking the input snapshot.
        await _dataGate.WaitAsync();

        try
        {
            // Reset progress so the previous scan's InfoBar is dismissed
            ScanProgress = ScanProgress.Idle;

            var previousPhotos = await _photoRepo.GetAllAsync(_scanCts.Token);
            var appSettings = await _settings.LoadAsync(_scanCts.Token);
            var analysisRun = await _analysisEngine.AnalyzeAsync(
                sourceDirectory,
                PhotoAnalysisOptions.FromSettings(appSettings) with { ComputeAllFileHashes = false },
                new Progress<ScanProgress>(progress => ScanProgress = progress),
                _scanCts.Token);
            allPhotos.AddRange(analysisRun.Photos);
            var photoGroups = analysisRun.Groups;

            // Keep both the displayed and persisted snapshot until every analysis stage succeeds.
            await _analysisResults.ReplaceAsync(analysisRun.Photos, photoGroups, _scanCts.Token);
            Interlocked.Increment(ref _dataVersion);
            var activePaths = allPhotos.Select(p => p.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            allPhotos.AddRange(previousPhotos.Where(p => p.IsMovedToTrash && !activePaths.Contains(p.FilePath))
                .Select(p => { var copy = p.CreateSnapshot(); copy.GroupId = null; return copy; }));

            AllPhotos.Clear();
            Groups.Clear();
            SelectedGroup = null;
            SelectedPhoto = null;
            foreach (var photo in allPhotos)
                AllPhotos.Add(new PhotoItemViewModel(photo));

            TotalPhotoCount = allPhotos.Count;

            _logger.LogInformation(
                "Grouping done: {GroupCount} groups from {PhotoCount} photos",
                photoGroups.Count, allPhotos.Count);

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

            EnsureDefaultPhotoSelection();
            UpdatePhotoSelectionFlags();

            GroupCount   = photoGroups.Count;
            LastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ScanDuration = $"{(DateTime.Now - started).TotalSeconds:F0}s";

            // Refresh all nav counts
            RefreshCounts();

            // Persist last-used directory
            appSettings.LastSelectedDirectory = sourceDirectory;
            try { await _settings.SaveAsync(appSettings, CancellationToken.None); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not save last directory after scan"); }

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
            _dataGate.Release();
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
        MarkKeepCommand.NotifyCanExecuteChanged();
        MarkDeleteCommand.NotifyCanExecuteChanged();
        MarkReviewCommand.NotifyCanExecuteChanged();
        MarkAsKeepCommand.NotifyCanExecuteChanged();
        MarkAsDeleteCommand.NotifyCanExecuteChanged();
        SetAsKeepCommand.NotifyCanExecuteChanged();
        MoveMarkedToTrashCommand.NotifyCanExecuteChanged();
        UndoLastMoveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDirectoryChanged(string value)
    {
        StartScanCommand.NotifyCanExecuteChanged();
        ToggleScanCommand.NotifyCanExecuteChanged();
    }

    // ── File operations ───────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public async Task MoveMarkedToTrashAsync(CancellationToken ct = default)
    {
        if (IsScanning) return;
        await _dataGate.WaitAsync(ct);
        try
        {
            if (IsScanning) return;
            var appSettings = await _settings.LoadAsync(ct);
            var toMoveVms = AllPhotos
                .Where(vm => !vm.IsMovedToTrash && vm.Status is
                    RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete)
                .ToList();
            var toMove = toMoveVms.Select(vm => vm.Model).ToList();

            if (toMove.Count == 0) return;

            var trashDir = string.IsNullOrEmpty(SelectedDirectory)
                ? Path.Combine(Path.GetTempPath(), "PhotoCuller_ToDelete")
                : Path.Combine(
                    Path.GetDirectoryName(SelectedDirectory) ?? SelectedDirectory,
                    appSettings.ToDeleteFolderName);

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
            {
                await _photoRepo.UpdateAsync(vm.Model, ct);
                vm.NotifyStatusChanged();
            }

            RefreshCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveMarkedToTrash failed");
            _notifications.ShowError($"移动失败：{ex.Message}");
        }
        finally { _dataGate.Release(); }
    }

    [RelayCommand(CanExecute = nameof(CanEditPhotos))]
    public async Task UndoLastMoveAsync(CancellationToken ct = default)
    {
        if (IsScanning) return;
        await _dataGate.WaitAsync(ct);
        try
        {
            if (IsScanning) return;
            bool ok = await _undo.UndoLastAsync(ct);
            if (ok)
            {
                foreach (var photo in AllPhotos.Where(p => p.IsMovedToTrash && File.Exists(p.FilePath)))
                {
                    photo.Model.IsMovedToTrash = false;
                    photo.SetStatus(RecommendationStatus.Review);
                    await _photoRepo.UpdateAsync(photo.Model, ct);
                    photo.NotifyStatusChanged();
                }
                RefreshCounts();
            }
            if (ok) _notifications.ShowSuccess("已撤销上一次移动操作");
            else    _notifications.ShowWarning("没有可撤销的操作");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UndoLastMove failed");
            _notifications.ShowError($"撤销失败：{ex.Message}");
        }
        finally { _dataGate.Release(); }
    }
}
