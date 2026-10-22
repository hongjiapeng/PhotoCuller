using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Hosting;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using Windows.System;
using Windows.UI.ViewManagement;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class LibraryPage : Page
{
    private MainViewModel? _vm;
    private string _filterTag = "all";
    private string _sortTag = "score";
    private string _searchQuery = string.Empty;
    private readonly ObservableCollection<PhotoItemViewModel> _visiblePhotos = [];
    private readonly UISettings _uiSettings = new();
    private bool _isActive;
    private bool _refreshQueued;
    private PhotoItemViewModel? _selectedLibraryPhoto;
    private PhotoItemViewModel? _inspectPhoto;
    private string? _inspectImagePath;
    private bool _isInspectMode;
    private bool _isFitToWindow = true;

    public LibraryPage()
    {
        InitializeComponent();
        PhotoGrid.ItemsSource = _visiblePhotos;
        InspectFilmstrip.ItemsSource = _visiblePhotos;
        InspectScrollViewer.SizeChanged += (_, _) =>
        {
            if (_isInspectMode && _isFitToWindow)
                FitPhotoToWindow();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm is null)
            return;

        _isActive = true;
        _vm.PropertyChanged += Vm_PropertyChanged;
        _vm.AllPhotos.CollectionChanged += Photos_CollectionChanged;
        if (_vm.AllPhotos.Count == 0)
            await _vm.LoadFromDatabaseAsync();

        if (!_isActive) return;
        ApplyFilters();
        UpdateEmptyState();
        UpdateScanProgress();
        Root.Focus(FocusState.Programmatic);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isActive = false;
        if (_vm is not null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.AllPhotos.CollectionChanged -= Photos_CollectionChanged;
        }
    }

    private void Vm_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        void Update()
        {
            if (!_isActive) return;
            if (e.PropertyName == nameof(MainViewModel.ScanProgress))
                UpdateScanProgress();

            if (e.PropertyName is nameof(MainViewModel.AllPhotos)
                                  or nameof(MainViewModel.IsScanning))
            {
                if (_vm?.IsScanning == true) ExitInspectMode();
                ApplyFilters(preferViewModelSelection: true);
                UpdateEmptyState();
                UpdateScanProgress();
            }
        }

        if (DispatcherQueue.HasThreadAccess) Update();
        else DispatcherQueue.TryEnqueue(Update);
    }

    private void Photos_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm?.IsScanning == true)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                ExitInspectMode();
                _inspectPhoto = null;
                _inspectImagePath = null;
                InspectImage.Source = null;
                _visiblePhotos.Clear();
                SelectPhoto(null);
            }
            else if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (PhotoItemViewModel photo in e.NewItems)
                {
                    if (!MatchesFilters(photo)) continue;
                    _visiblePhotos.Add(photo);
                    if (_selectedLibraryPhoto is null) SelectPhoto(photo);
                }
            }
            else ApplyFilters();

            UpdateEmptyState();
            return;
        }

        // A database load adds many rows in one UI turn. Refresh once for the batch.
        if (_refreshQueued) return;
        _refreshQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _refreshQueued = false;
            if (!_isActive) return;
            ApplyFilters();
            UpdateEmptyState();
        });
    }

    private void UpdateScanProgress()
    {
        if (_vm is null)
            return;

        var progress = _vm.ScanProgress;
        if (_vm.IsScanning)
        {
            ScanInfoBar.IsOpen = true;
            ScanInfoBar.Message = "正在扫描照片";
            ScanInfoBar.Severity = InfoBarSeverity.Informational;
            ScanBar.Value = progress.Percent;
            ScanStatusText.Text = !string.IsNullOrEmpty(progress.Message)
                ? $"{progress.Message}  {progress.Percent}%"
                : progress.CurrentFile is not null
                    ? $"{progress.Current}/{progress.Total}  {progress.CurrentFile}"
                    : $"{progress.Phase}  {progress.Percent}%";
        }
        else if (progress.Phase == Core.Enums.ScanPhase.Completed)
        {
            ScanInfoBar.Message = $"扫描完成：{progress.Total} 张照片";
            ScanInfoBar.Severity = InfoBarSeverity.Success;
            ScanBar.Value = 100;
        }
        else
        {
            ScanInfoBar.IsOpen = false;
        }
    }

    private bool MatchesFilters(PhotoItemViewModel photo)
    {
        if (!string.IsNullOrEmpty(_searchQuery)
            && !photo.FileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
            return false;

        return _filterTag switch
        {
            "keep" => photo.IsKeep,
            "review" => photo.Status == RecommendationStatus.Review,
            "duplicate" => photo.Model.GroupId.HasValue,
            "low" => photo.OverallScore.HasValue && photo.OverallScore < 50,
            "error" => !string.IsNullOrEmpty(photo.ErrorMessage),
            _ => true,
        };
    }

    private void ApplyFilters(bool preferViewModelSelection = false)
    {
        if (_vm is null)
            return;

        IEnumerable<PhotoItemViewModel> source = _vm.AllPhotos.Where(MatchesFilters);

        // Keep arrival order until analysis has finished; score changes must not move cards.
        if (!_vm.IsScanning) source = _sortTag switch
        {
            "time" => source.OrderByDescending(photo =>
                photo.Model.ExifTakenTime ?? photo.Model.CreatedTime),
            "name" => source.OrderBy(photo => photo.FileName),
            "size" => source.OrderByDescending(photo => photo.FileSize),
            _ => source.OrderByDescending(photo => photo.OverallScore ?? 0),
        };

        var filtered = source.ToList();
        _visiblePhotos.Clear();
        foreach (var photo in filtered) _visiblePhotos.Add(photo);

        var selected = preferViewModelSelection && _vm.SelectedPhoto is not null
                       && _visiblePhotos.Contains(_vm.SelectedPhoto)
            ? _vm.SelectedPhoto
            : _selectedLibraryPhoto is not null
                       && _visiblePhotos.Contains(_selectedLibraryPhoto)
            ? _selectedLibraryPhoto
            : _vm.SelectedPhoto is not null
              && _visiblePhotos.Contains(_vm.SelectedPhoto)
                ? _vm.SelectedPhoto
                : _visiblePhotos.Count > 0 ? _visiblePhotos[0] : null;

        SelectPhoto(selected);
    }

    private void UpdateEmptyState()
    {
        if (EmptyState is null)
            return;

        EmptyState.Visibility = _visiblePhotos.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        var hasFilters = _filterTag != "all" || !string.IsNullOrEmpty(_searchQuery);
        EmptyStateTitle.Text = _vm?.IsScanning == true ? "正在读取照片…"
            : hasFilters ? "没有匹配的照片" : "暂无照片";
        EmptyStateHint.Text = _vm?.IsScanning == true
            ? hasFilters ? "扫描结果将持续更新，可切换到全部照片查看" : "缩略图准备好后将在这里逐张显示"
            : hasFilters ? "请调整搜索或筛选条件" : "请在左侧选择目录后开始扫描";
    }

    private void PhotoCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card) AnimatePhotoCard(card);
    }

    private void PhotoCard_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
    {
        if (sender.IsLoaded) AnimatePhotoCard(sender);
    }

    private void PhotoCard_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card) ResetCardAnimation(card);
    }

    private static void ResetCardAnimation(FrameworkElement card)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(card, true);
        var visual = ElementCompositionPreview.GetElementVisual(card);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.Opacity = 1;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    private void AnimatePhotoCard(FrameworkElement card)
    {
        ResetCardAnimation(card);
        if (card.DataContext is not PhotoItemViewModel photo
            || !photo.ConsumeScanEntrance() || _vm?.IsScanning != true
            || !_uiSettings.AnimationsEnabled)
            return;

        var visual = ElementCompositionPreview.GetElementVisual(card);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1, easing);
        opacity.Duration = TimeSpan.FromMilliseconds(180);
        var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new Vector3(0, 8, 0));
        translation.InsertKeyFrame(1, Vector3.Zero, easing);
        translation.Duration = opacity.Duration;
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation", translation);
    }

    private void SelectPhoto(PhotoItemViewModel? photo)
    {
        if (ReferenceEquals(_selectedLibraryPhoto, photo))
        {
            PhotoGrid.SelectedItem = photo;
            InspectFilmstrip.SelectedItem = photo;
            OpenSelectedPhotoInspectButton.IsEnabled = photo is not null;
            if (_vm is not null && photo is not null)
                _vm.SelectedPhoto = photo;
            return;
        }

        if (_selectedLibraryPhoto is not null)
            _selectedLibraryPhoto.IsLibrarySelected = false;

        _selectedLibraryPhoto = photo;
        if (photo is not null)
            photo.IsLibrarySelected = true;

        PhotoGrid.SelectedItem = photo;
        InspectFilmstrip.SelectedItem = photo;
        OpenSelectedPhotoInspectButton.IsEnabled = photo is not null;
        if (_vm is not null && photo is not null)
            _vm.SelectedPhoto = photo;
    }

    private void EnterInspectMode(PhotoItemViewModel? photo)
    {
        if (photo is null || !_visiblePhotos.Contains(photo))
            return;

        _isInspectMode = true;
        BrowseView.Visibility = Visibility.Collapsed;
        InspectView.Visibility = Visibility.Visible;
        ShowInspectPhoto(photo);
        InspectScrollViewer.Focus(FocusState.Programmatic);
    }

    private void ExitInspectMode()
    {
        if (!_isInspectMode)
            return;

        _isInspectMode = false;
        InspectView.Visibility = Visibility.Collapsed;
        BrowseView.Visibility = Visibility.Visible;

        if (_inspectPhoto is not null)
        {
            SelectPhoto(_inspectPhoto);
            PhotoGrid.ScrollIntoView(_inspectPhoto, ScrollIntoViewAlignment.Leading);
        }

        PhotoGrid.Focus(FocusState.Programmatic);
    }

    private void ShowInspectPhoto(PhotoItemViewModel photo)
    {
        _inspectPhoto = photo;
        SelectPhoto(photo);

        var index = IndexOfVisiblePhoto(photo);
        InspectFileName.Text = photo.FileName;
        InspectPhotoMeta.Text =
            $"{photo.FileType} · {photo.FileSizeLabel} · {photo.Resolution} · 评分 {photo.ScoreLabel}";
        InspectPosition.Text = index >= 0
            ? $"{index + 1} / {_visiblePhotos.Count}"
            : $"— / {_visiblePhotos.Count}";
        InspectFilmstrip.ScrollIntoView(photo, ScrollIntoViewAlignment.Leading);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isInspectMode && ReferenceEquals(_inspectPhoto, photo))
            {
                InspectFilmstrip.UpdateLayout();
                InspectFilmstrip.ScrollIntoView(photo, ScrollIntoViewAlignment.Leading);
            }
        });

        PreviousPhotoButton.IsEnabled = _visiblePhotos.Count > 1;
        NextPhotoButton.IsEnabled = _visiblePhotos.Count > 1;
        LoadInspectImage(photo);
    }

    private void LoadInspectImage(PhotoItemViewModel photo)
    {
        InspectImageProgress.IsActive = true;
        InspectImageProgress.Visibility = Visibility.Visible;
        InspectImagePlaceholder.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(photo.FilePath) || !File.Exists(photo.FilePath))
        {
            ShowImageLoadFailure();
            return;
        }

        if (string.Equals(_inspectImagePath, photo.FilePath, StringComparison.OrdinalIgnoreCase)
            && InspectImage.Source is not null)
        {
            InspectImageProgress.IsActive = false;
            InspectImageProgress.Visibility = Visibility.Collapsed;
            FitPhotoToWindow();
            return;
        }

        try
        {
            _inspectImagePath = photo.FilePath;
            InspectImage.Source = new BitmapImage(new Uri(photo.FilePath));
        }
        catch
        {
            ShowImageLoadFailure();
        }
    }

    private void ShowImageLoadFailure()
    {
        _inspectImagePath = null;
        InspectImage.Source = null;
        InspectImageProgress.IsActive = false;
        InspectImageProgress.Visibility = Visibility.Collapsed;
        InspectImagePlaceholder.Visibility = Visibility.Visible;
    }

    private void MoveInspectSelection(int delta)
    {
        if (_inspectPhoto is null || _visiblePhotos.Count == 0)
            return;

        var currentIndex = IndexOfVisiblePhoto(_inspectPhoto);
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = (currentIndex + delta + _visiblePhotos.Count) % _visiblePhotos.Count;
        ShowInspectPhoto(_visiblePhotos[nextIndex]);
    }

    private int IndexOfVisiblePhoto(PhotoItemViewModel photo)
    {
        for (var index = 0; index < _visiblePhotos.Count; index++)
        {
            if (ReferenceEquals(_visiblePhotos[index], photo)
                || _visiblePhotos[index].Id == photo.Id)
            {
                return index;
            }
        }

        return -1;
    }

    private void FitPhotoToWindow()
    {
        if (_inspectPhoto is null)
            return;

        var displaySize = GetDisplaySize(_inspectPhoto);
        if (displaySize.Width <= 0
            || displaySize.Height <= 0
            || InspectScrollViewer.ActualWidth <= 0
            || InspectScrollViewer.ActualHeight <= 0)
        {
            return;
        }

        PrepareImageForZoom(displaySize);
        var horizontalFit = Math.Max(
            InspectScrollViewer.MinZoomFactor,
            (InspectScrollViewer.ActualWidth - 4) / displaySize.Width);
        var verticalFit = Math.Max(
            InspectScrollViewer.MinZoomFactor,
            (InspectScrollViewer.ActualHeight - 4) / displaySize.Height);
        var targetZoom = Math.Clamp(
            Math.Min(horizontalFit, verticalFit),
            InspectScrollViewer.MinZoomFactor,
            InspectScrollViewer.MaxZoomFactor);

        _isFitToWindow = true;
        InspectScrollViewer.ChangeView(0, 0, (float)targetZoom, true);
        UpdateZoomLevelText(targetZoom);
    }

    private void ShowPhotoAtActualSize()
    {
        if (_inspectPhoto is null)
            return;

        PrepareImageForZoom(GetDisplaySize(_inspectPhoto));
        _isFitToWindow = false;
        ChangeZoomPreservingCenter(1);
    }

    private void ZoomBy(double multiplier)
    {
        if (_inspectPhoto is null)
            return;

        PrepareImageForZoom(GetDisplaySize(_inspectPhoto));
        _isFitToWindow = false;
        var targetZoom = Math.Clamp(
            InspectScrollViewer.ZoomFactor * multiplier,
            InspectScrollViewer.MinZoomFactor,
            InspectScrollViewer.MaxZoomFactor);
        ChangeZoomPreservingCenter(targetZoom);
    }

    private void PrepareImageForZoom((double Width, double Height) displaySize)
    {
        InspectImage.Stretch = Stretch.Uniform;
        InspectImage.Width = displaySize.Width;
        InspectImage.Height = displaySize.Height;
        InspectImage.HorizontalAlignment = HorizontalAlignment.Left;
        InspectImage.VerticalAlignment = VerticalAlignment.Top;
    }

    private (double Width, double Height) GetDisplaySize(PhotoItemViewModel photo)
    {
        if (InspectImage.Source is BitmapImage bitmap
            && bitmap.PixelWidth > 0
            && bitmap.PixelHeight > 0)
        {
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        }

        return (
            photo.Width > 0 ? photo.Width : 1,
            photo.Height > 0 ? photo.Height : 1);
    }

    private (double X, double Y) GetViewportCenterRatio()
    {
        if (_isFitToWindow
            || InspectScrollViewer.ExtentWidth <= InspectScrollViewer.ViewportWidth
            || InspectScrollViewer.ExtentHeight <= InspectScrollViewer.ViewportHeight)
        {
            return (0.5, 0.5);
        }

        var centerX =
            InspectScrollViewer.HorizontalOffset + InspectScrollViewer.ViewportWidth / 2;
        var centerY =
            InspectScrollViewer.VerticalOffset + InspectScrollViewer.ViewportHeight / 2;

        return (
            Math.Clamp(centerX / InspectScrollViewer.ExtentWidth, 0, 1),
            Math.Clamp(centerY / InspectScrollViewer.ExtentHeight, 0, 1));
    }

    private void ChangeZoomPreservingCenter(double targetZoom)
    {
        var center = GetViewportCenterRatio();
        var unscaledWidth = InspectScrollViewer.ExtentWidth
                            / Math.Max(InspectScrollViewer.ZoomFactor, 0.01);
        var unscaledHeight = InspectScrollViewer.ExtentHeight
                             / Math.Max(InspectScrollViewer.ZoomFactor, 0.01);
        var targetExtentWidth = unscaledWidth * targetZoom;
        var targetExtentHeight = unscaledHeight * targetZoom;
        var horizontalOffset = Math.Max(
            0,
            targetExtentWidth * center.X - InspectScrollViewer.ViewportWidth / 2);
        var verticalOffset = Math.Max(
            0,
            targetExtentHeight * center.Y - InspectScrollViewer.ViewportHeight / 2);

        InspectScrollViewer.ChangeView(
            horizontalOffset,
            verticalOffset,
            (float)targetZoom,
            true);
        UpdateZoomLevelText(targetZoom);
    }

    private void UpdateZoomLevelText(double zoomFactor)
        => ZoomLevelText.Text = $"{Math.Round(zoomFactor * 100)}%";

    private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItemViewModel photo)
            SelectPhoto(photo);
    }

    private void PhotoCard_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid id })
        {
            EnterInspectMode(_visiblePhotos.FirstOrDefault(photo => photo.Id == id));
            e.Handled = true;
        }
    }

    private void OpenSelectedPhotoInspect_Click(object sender, RoutedEventArgs e)
        => EnterInspectMode(_selectedLibraryPhoto);

    private void BackToLibrary_Click(object sender, RoutedEventArgs e)
        => ExitInspectMode();

    private void PreviousPhoto_Click(object sender, RoutedEventArgs e)
        => MoveInspectSelection(-1);

    private void NextPhoto_Click(object sender, RoutedEventArgs e)
        => MoveInspectSelection(1);

    private void InspectFilmstrip_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItemViewModel photo)
            ShowInspectPhoto(photo);
    }

    private void FitPhoto_Click(object sender, RoutedEventArgs e)
        => FitPhotoToWindow();

    private void ActualSizePhoto_Click(object sender, RoutedEventArgs e)
        => ShowPhotoAtActualSize();

    private void ZoomOutPhoto_Click(object sender, RoutedEventArgs e)
        => ZoomBy(1 / 1.25);

    private void ZoomInPhoto_Click(object sender, RoutedEventArgs e)
        => ZoomBy(1.25);

    private void InspectImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        InspectImageProgress.IsActive = false;
        InspectImageProgress.Visibility = Visibility.Collapsed;
        InspectImagePlaceholder.Visibility = Visibility.Collapsed;
        FitPhotoToWindow();
    }

    private void InspectImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs e)
        => ShowImageLoadFailure();

    private void InspectImage_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        if (_isFitToWindow)
            ShowPhotoAtActualSize();
        else
            FitPhotoToWindow();

        e.Handled = true;
    }

    private void InspectScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
        => UpdateZoomLevelText(InspectScrollViewer.ZoomFactor);

    private void InspectScrollViewer_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
            return;

        var wheelDelta = e.GetCurrentPoint(InspectScrollViewer)
            .Properties.MouseWheelDelta;
        if (wheelDelta == 0)
            return;

        ZoomBy(wheelDelta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
            return;

        if (!_isInspectMode)
        {
            if (e.Key == VirtualKey.Enter
                && PhotoGrid.SelectedItem is PhotoItemViewModel selected)
            {
                EnterInspectMode(selected);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                ExitInspectMode();
                e.Handled = true;
                break;
            case VirtualKey.Left:
                MoveInspectSelection(-1);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                MoveInspectSelection(1);
                e.Handled = true;
                break;
            case VirtualKey.Add:
                ZoomBy(1.25);
                e.Handled = true;
                break;
            case VirtualKey.Subtract:
                ZoomBy(1 / 1.25);
                e.Handled = true;
                break;
            case VirtualKey.Z:
                if (_isFitToWindow)
                    ShowPhotoAtActualSize();
                else
                    FitPhotoToWindow();
                e.Handled = true;
                break;
        }
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox)
                return true;

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void SearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchQuery = sender.Text.Trim();
        ApplyFilters();
        UpdateEmptyState();
    }

    private void FilterCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _filterTag =
            (FilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        ApplyFilters();
        UpdateEmptyState();
    }

    private void SortCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _sortTag =
            (SortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "score";
        ApplyFilters();
        UpdateEmptyState();
    }
}
