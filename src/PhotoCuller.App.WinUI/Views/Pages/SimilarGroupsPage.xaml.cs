using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;
using Windows.System;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class SimilarGroupsPage : Page
{
    private MainViewModel? _vm;
    private bool _isUpdatingModeSelector;
    private string? _recommendedImagePath;
    private string? _compareImagePath;
    private string? _inspectImagePath;
    private bool _wasFitToWindow = true;
    private bool _isApplyingZoom;
    public MainViewModel? ViewModel => _vm;

    public SimilarGroupsPage()
    {
        InitializeComponent();
        RecommendedScrollViewer.SizeChanged += (_, _) => UpdateZoomState();
        CompareScrollViewer.SizeChanged += (_, _) => UpdateZoomState();
        InspectScrollViewer.SizeChanged += (_, _) => UpdateZoomState();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm is null) return;

        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;

        // If Groups is empty (e.g. app just restarted), load from DB
        if (_vm.Groups.Count == 0)
            await _vm.LoadFromDatabaseAsync();

        Refresh();

        UpdateModeSelector();
        UpdateImages();
        UpdateZoomState();
        Root.Focus(FocusState.Programmatic);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm is not null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.GroupCount)
                           or nameof(MainViewModel.TotalPhotoCount))
            DispatcherQueue.TryEnqueue(Refresh);

        if (e.PropertyName is nameof(MainViewModel.CurrentViewMode)
                           or nameof(MainViewModel.SelectedGroup)
                           or nameof(MainViewModel.RecommendedPhoto)
                           or nameof(MainViewModel.ComparePhoto)
                           or nameof(MainViewModel.InspectPhoto))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateModeSelector();
                UpdateImages();
                UpdateZoomState();
                RefreshStatusBar();
            });
        }

        if (e.PropertyName is nameof(MainViewModel.ZoomLevel)
                           or nameof(MainViewModel.IsFitToWindow))
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateZoomState();
                _wasFitToWindow = _vm?.IsFitToWindow ?? true;
            });
    }

    private void Refresh()
    {
        if (_vm is null) return;
        GroupsList.ItemsSource = _vm.Groups;
        GroupCountLabel.Text = $"({_vm.GroupCount} 组)";
        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        if (_vm is null) return;
        int pending = _vm.Groups
            .SelectMany(g => g.Photos)
            .Count(p => p.Status is RecommendationStatus.Review
                                 or RecommendationStatus.ToDelete);

        StatusLabel.Text =
            $"共 {_vm.TotalPhotoCount} 张照片，{_vm.GroupCount} 组，{pending} 张待处理";
        BtnMoveLabel.Text = pending > 0
            ? $"移动到待删除 ({pending})"
            : "移动到待删除";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_vm is null) return;
        var q = sender.Text.Trim();
        GroupsList.ItemsSource = string.IsNullOrEmpty(q)
            ? _vm.Groups
            : (object)_vm.Groups
                .Where(g => g.Photos.Any(p =>
                    p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                 || p.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
    }

    private void ModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_vm is null || _isUpdatingModeSelector)
            return;

        if (sender.SelectedItem is null)
            return;

        if (ReferenceEquals(sender.SelectedItem, BrowseModeItem))
            _vm.EnterBrowseModeCommand.Execute(null);
        else if (ReferenceEquals(sender.SelectedItem, CompareModeItem))
            _vm.EnterCompareModeCommand.Execute(null);
        else if (ReferenceEquals(sender.SelectedItem, InspectModeItem))
            _vm.EnterInspectModeCommand.Execute(null);
    }

    private void UpdateModeSelector()
    {
        if (_vm is null)
            return;

        _isUpdatingModeSelector = true;
        ModeSelector.SelectedItem = _vm.CurrentViewMode switch
        {
            SimilarGroupViewMode.Compare => CompareModeItem,
            SimilarGroupViewMode.Inspect => InspectModeItem,
            _                            => BrowseModeItem,
        };
        _isUpdatingModeSelector = false;
    }
    private void PhotoCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el
            && el.Tag is Guid id
            && _vm is not null)
        {
            var photo = _vm.AllPhotos.FirstOrDefault(p => p.Id == id);
            if (photo is not null)
            {
                var group = _vm.Groups.FirstOrDefault(g => g.Photos.Any(p => p.Id == id));
                if (group is not null)
                    _vm.SelectGroupCommand.Execute(group);

                _vm.SelectedPhoto = photo;
            }
        }
    }

    private void CompareGroup_Click(object sender, RoutedEventArgs e)
        => EnterCompareFromElement(sender as FrameworkElement);

    private void Group_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        EnterCompareFromElement(sender as FrameworkElement);
        e.Handled = true;
    }

    private void EnterCompareFromElement(FrameworkElement? element)
    {
        if (element?.Tag is not Guid groupId || _vm is null) return;

        var group = _vm.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        _vm.SelectGroupCommand.Execute(group);
        _vm.EnterCompareModeCommand.Execute(null);
    }

    private void FilmstripPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Guid id || _vm is null)
            return;

        var photo = _vm.SelectedGroup?.Photos.FirstOrDefault(p => p.Id == id)
                    ?? _vm.AllPhotos.FirstOrDefault(p => p.Id == id);
        if (photo is not null)
            _vm.SelectComparePhotoCommand.Execute(photo);
    }

    private void LargePhoto_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_vm is null) return;

        _vm.InspectPhoto = ReferenceEquals(sender, RecommendedImage)
            ? _vm.RecommendedPhoto
            : _vm.ComparePhoto;
        _vm.EnterInspectModeCommand.Execute(null);
    }

    private void ActualSizeInspect_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        _vm.EnterInspectModeCommand.Execute(null);
        _vm.SetActualSizeCommand.Execute(null);
    }

    private async void MoveOthers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Guid groupId) return;
        if (_vm is null) return;

        var group = _vm.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;

        int count = group.Photos.Count(p => !p.IsKeep);
        if (count == 0) return;

        var dialog = new ContentDialog
        {
            Title             = "确认移除",
            Content           = $"将把该组中 {count} 张非保留照片移到待删除目录。是否继续？",
            PrimaryButtonText = "继续",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            foreach (var photo in group.Photos.Where(p => !p.IsKeep))
                _vm.MarkDeleteCommand.Execute(photo);

            await _vm.MoveMarkedToTrashCommand.ExecuteAsync(null);
            RefreshStatusBar();
        }
    }

    private async void BtnMoveToTrash_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        int pending = _vm.Groups
            .SelectMany(g => g.Photos)
            .Count(p => p.Status is RecommendationStatus.Review
                                 or RecommendationStatus.ToDelete);
        if (pending == 0) return;

        var dialog = new ContentDialog
        {
            Title             = "确认移动",
            Content           = $"即将移动 {pending} 张照片到待删除目录。\n原图不会被永久删除，仍可手动检查后再删除。\n\n是否继续？",
            PrimaryButtonText = "继续",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _vm.MoveMarkedToTrashCommand.ExecuteAsync(null);
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        // TODO Stage 15: generate export report
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_vm is null || IsTextInputFocused())
            return;

        switch (e.Key)
        {
            case VirtualKey.B:
                _vm.EnterBrowseModeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.C:
                _vm.EnterCompareModeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.I:
                _vm.EnterInspectModeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                if (_vm.IsInspectMode)
                    _vm.EnterCompareModeCommand.Execute(null);
                else if (_vm.IsCompareMode)
                    _vm.EnterBrowseModeCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                _vm.PreviousPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
            case VirtualKey.Space:
                _vm.NextPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Z:
                if (!_vm.IsInspectMode)
                    _vm.EnterInspectModeCommand.Execute(null);
                _vm.ToggleZoomCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.K:
                _vm.MarkAsKeepCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.D:
                _vm.MarkAsDeleteCommand.Execute(null);
                RefreshStatusBar();
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

    private void UpdateImages()
    {
        if (_vm is null) return;

        SetImageSource(RecommendedImage, RecommendedImagePlaceholder, _vm.RecommendedPhoto, ref _recommendedImagePath);
        SetImageSource(CompareImage, CompareImagePlaceholder, _vm.ComparePhoto, ref _compareImagePath);
        SetImageSource(InspectImage, InspectImagePlaceholder, _vm.InspectPhoto, ref _inspectImagePath);
    }

    private static void SetImageSource(
        Image image,
        TextBlock placeholder,
        PhotoItemViewModel? photo,
        ref string? loadedPath)
    {
        if (photo is null || string.IsNullOrWhiteSpace(photo.FilePath) || !File.Exists(photo.FilePath))
        {
            loadedPath = null;
            image.Source = null;
            placeholder.Visibility = Visibility.Visible;
            return;
        }

        if (string.Equals(loadedPath, photo.FilePath, StringComparison.OrdinalIgnoreCase)
            && image.Source is not null)
        {
            placeholder.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            loadedPath = photo.FilePath;
            image.Source = new BitmapImage(new Uri(photo.FilePath));
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            loadedPath = null;
            image.Source = null;
            placeholder.Visibility = Visibility.Visible;
        }
    }

    private void UpdateZoomState()
    {
        if (_vm is null) return;

        _isApplyingZoom = true;
        ApplyZoom(RecommendedImage, RecommendedScrollViewer, _vm.RecommendedPhoto);
        ApplyZoom(CompareImage, CompareScrollViewer, _vm.ComparePhoto);
        ApplyZoom(InspectImage, InspectScrollViewer, _vm.InspectPhoto);
        DispatcherQueue.TryEnqueue(() => _isApplyingZoom = false);
    }

    private void ApplyZoom(Image image, ScrollViewer scroller, PhotoItemViewModel? photo)
    {
        if (_vm is null) return;

        if (photo is null)
        {
            image.Width = double.NaN;
            image.Height = double.NaN;
            scroller.ChangeView(0, 0, 1, true);
            return;
        }

        var center = GetViewportCenterRatio(scroller);
        var targetZoom = _vm.IsFitToWindow
            ? CalculateFitZoom(scroller, photo)
            : Math.Clamp(_vm.ZoomLevel, scroller.MinZoomFactor, scroller.MaxZoomFactor);

        image.Stretch = Stretch.Fill;
        image.Width = photo.Width > 0 ? photo.Width : double.NaN;
        image.Height = photo.Height > 0 ? photo.Height : double.NaN;
        image.HorizontalAlignment = HorizontalAlignment.Left;
        image.VerticalAlignment = VerticalAlignment.Top;

        RestoreViewportCenter(scroller, photo, center, targetZoom);
    }

    private static double CalculateFitZoom(ScrollViewer scroller, PhotoItemViewModel photo)
    {
        if (photo.Width <= 0 || photo.Height <= 0 || scroller.ActualWidth <= 0 || scroller.ActualHeight <= 0)
            return 1;

        var minZoom = scroller.MinZoomFactor;
        var horizontalFit = Math.Max(minZoom, (scroller.ActualWidth - 4) / photo.Width);
        var verticalFit = Math.Max(minZoom, (scroller.ActualHeight - 4) / photo.Height);
        return Math.Clamp(Math.Min(horizontalFit, verticalFit), scroller.MinZoomFactor, scroller.MaxZoomFactor);
    }

    private (double X, double Y) GetViewportCenterRatio(ScrollViewer scroller)
    {
        if (_wasFitToWindow
            || scroller.ExtentWidth <= scroller.ViewportWidth
            || scroller.ExtentHeight <= scroller.ViewportHeight)
        {
            return (0.5, 0.5);
        }

        var centerX = scroller.HorizontalOffset + scroller.ViewportWidth / 2;
        var centerY = scroller.VerticalOffset + scroller.ViewportHeight / 2;

        return (
            Math.Clamp(centerX / scroller.ExtentWidth, 0, 1),
            Math.Clamp(centerY / scroller.ExtentHeight, 0, 1));
    }

    private void RestoreViewportCenter(
        ScrollViewer scroller,
        PhotoItemViewModel photo,
        (double X, double Y) center,
        double targetZoom)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var extentWidth = photo.Width * targetZoom;
            var extentHeight = photo.Height * targetZoom;
            var horizontalOffset = Math.Max(0, extentWidth * center.X - scroller.ViewportWidth / 2);
            var verticalOffset = Math.Max(0, extentHeight * center.Y - scroller.ViewportHeight / 2);

            scroller.ChangeView(horizontalOffset, verticalOffset, (float)targetZoom, true);
        });
    }

    private void ImageScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // User touchpad/pinch zoom is intentionally local to the image being manipulated.
        // Toolbar and keyboard zoom commands still use the shared ViewModel zoom state.
    }

    private void ImageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (IsTextInputFocused() || sender is not ScrollViewer scroller)
            return;

        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
            return;

        var wheelDelta = e.GetCurrentPoint(sender as UIElement).Properties.MouseWheelDelta;
        var zoomDelta = wheelDelta > 0 ? 1.15 : 1 / 1.15;
        var targetZoom = Math.Clamp(scroller.ZoomFactor * zoomDelta, scroller.MinZoomFactor, scroller.MaxZoomFactor);
        var center = GetViewportCenterRatio(scroller);

        if (wheelDelta > 0)
            ChangeLocalZoom(scroller, center, targetZoom);
        else if (wheelDelta < 0)
            ChangeLocalZoom(scroller, center, targetZoom);

        e.Handled = true;
    }

    private static void ChangeLocalZoom(
        ScrollViewer scroller,
        (double X, double Y) center,
        double targetZoom)
    {
        var unscaledWidth = scroller.ExtentWidth / Math.Max(scroller.ZoomFactor, 0.01);
        var unscaledHeight = scroller.ExtentHeight / Math.Max(scroller.ZoomFactor, 0.01);
        var targetExtentWidth = unscaledWidth * targetZoom;
        var targetExtentHeight = unscaledHeight * targetZoom;
        var horizontalOffset = Math.Max(0, targetExtentWidth * center.X - scroller.ViewportWidth / 2);
        var verticalOffset = Math.Max(0, targetExtentHeight * center.Y - scroller.ViewportHeight / 2);

        scroller.ChangeView(horizontalOffset, verticalOffset, (float)targetZoom, true);
    }
}
