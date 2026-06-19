using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
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

    public ObservableCollection<PhotoGroupViewModel> Groups { get; } = [];
    public ObservableCollection<PhotoItemViewModel>  AllPhotos { get; } = [];

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PickDirectoryAsync()
    {
        var path = await _fileDialog.PickFolderAsync();
        if (!string.IsNullOrEmpty(path))
            SelectedDirectory = path;
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
            var appSettings = await _settings.LoadAsync();

            // Phase 1: scan + metadata
            await foreach (var photo in _scanner.ScanAsync(SelectedDirectory, _scanCts.Token))
            {
                ScanProgress = _scanner.IsScanning
                    ? new Core.Models.ScanProgress(ScanPhase.ReadingMetadata,
                        allPhotos.Count, 0, photo.FileName)
                    : ScanProgress.Idle;

                // Generate thumbnail
                photo.ThumbnailPath = await _thumbnails
                    .GetOrCreateAsync(photo, cancellationToken: _scanCts.Token);

                // Compute hashes
                photo.Sha256Hash    = await _hash.ComputeSha256Async(photo.FilePath, _scanCts.Token);
                photo.PerceptualHash = await _pHash.ComputePHashAsync(photo.FilePath, _scanCts.Token);

                allPhotos.Add(photo);
                AllPhotos.Add(new PhotoItemViewModel(photo));
            }

            TotalPhotoCount = allPhotos.Count;

            // Phase 2: scoring
            ScanProgress = new Core.Models.ScanProgress(ScanPhase.Scoring, 0, allPhotos.Count);
            var weights = appSettings.ScoringWeights;
            await _scoring.ScoreRangeAsync(allPhotos, weights,
                new Progress<int>(n =>
                    ScanProgress = new Core.Models.ScanProgress(ScanPhase.Scoring, n, allPhotos.Count)),
                _scanCts.Token);

            // Phase 3: grouping
            ScanProgress = new Core.Models.ScanProgress(ScanPhase.Grouping, 0, 0);
            var photoGroups = await _grouping.BuildGroupsAsync(allPhotos,
                ct: _scanCts.Token);

            // Phase 4: persist
            await _photoRepo.AddRangeAsync(allPhotos, _scanCts.Token);
            await _groupRepo.AddRangeAsync(photoGroups, _scanCts.Token);

            // Phase 5: build view-models
            var photoVmMap = AllPhotos.ToDictionary(vm => vm.Id);
            Groups.Clear();
            foreach (var group in photoGroups.OrderByDescending(g => g.PhotoCount))
            {
                var memberVms = group.Photos
                    .Select(p => photoVmMap.TryGetValue(p.Id, out var vm) ? vm : null)
                    .Where(vm => vm is not null)
                    .Select(vm => vm!);
                Groups.Add(new PhotoGroupViewModel(group, memberVms));
            }

            GroupCount   = photoGroups.Count;
            PendingCount = allPhotos.Count(p =>
                p.RecommendationStatus == RecommendationStatus.Review);
            LastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ScanDuration = $"{(DateTime.Now - started).TotalSeconds:F0}s";

            ScanProgress = Core.Models.ScanProgress.Completed(allPhotos.Count, started.ToUniversalTime());
            _notifications.ShowSuccess($"扫描完成：{allPhotos.Count} 张照片，{photoGroups.Count} 个相似组");
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
}
