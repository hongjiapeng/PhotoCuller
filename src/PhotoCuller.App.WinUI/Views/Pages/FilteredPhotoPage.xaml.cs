using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.App.WinUI.Views.Pages;

/// <summary>
/// Reusable filtered photo grid.
/// Navigated-to with a FilterMode parameter to show keep/review/trash photos.
/// </summary>
public sealed partial class FilteredPhotoPage : Page
{
    public enum FilterMode { Keep, Review, Trash }

    private MainViewModel? _vm;
    private FilterMode _mode = FilterMode.Keep;
    private string _search = string.Empty;
    private bool _isSubscribed;

    public FilteredPhotoPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => DetachViewModel();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm is null) return;

        if (e.Parameter is FilterMode m) _mode = m;

        ConfigurePage();
        AttachViewModel();

        if (_vm.AllPhotos.Count == 0)
            await _vm.LoadFromDatabaseAsync();

        ApplyFilter();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DetachViewModel();
    }

    private void AttachViewModel()
    {
        if (_vm is null || _isSubscribed)
            return;

        _vm.PropertyChanged += Vm_PropertyChanged;
        _vm.AllPhotos.CollectionChanged += AllPhotos_CollectionChanged;
        _isSubscribed = true;
    }

    private void DetachViewModel()
    {
        if (_vm is null || !_isSubscribed)
            return;

        _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm.AllPhotos.CollectionChanged -= AllPhotos_CollectionChanged;
        _isSubscribed = false;
    }

    private void Vm_PropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.AllPhotos)
                           or nameof(MainViewModel.KeepCount)
                           or nameof(MainViewModel.PendingCount)
                           or nameof(MainViewModel.TrashCount))
            DispatcherQueue.TryEnqueue(ApplyFilter);
    }

    private void AllPhotos_CollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(ApplyFilter);

    private void ConfigurePage()
    {
        switch (_mode)
        {
            case FilterMode.Keep:
                PageTitle.Text   = "建议保留";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "暂无推荐保留的照片";
                EmptyHint.Text   = "扫描完成后，评分较高或手动标记保留的照片会显示在这里";
                BatchBtn.Content = "全部标记为待确认";
                break;
            case FilterMode.Review:
                PageTitle.Text   = "待确认 / 待删除";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "暂无待处理照片";
                EmptyHint.Text   = "需要人工判断的照片会汇总到这里";
                BatchBtn.Content = "全部移动到待删除";
                break;
            case FilterMode.Trash:
                PageTitle.Text   = "待删除";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "待删除目录为空";
                EmptyHint.Text   = "被标记待删除或已移入待删除目录的照片会显示在这里";
                BatchBtn.Content = "移动所有到待删除目录";
                break;
        }
    }

    private void ApplyFilter()
    {
        if (_vm is null) return;

        IEnumerable<PhotoItemViewModel> source = _mode switch
        {
            FilterMode.Keep   => _vm.AllPhotos.Where(p =>
                p.Status is RecommendationStatus.Keep or RecommendationStatus.ManualKeep),
            FilterMode.Review => _vm.AllPhotos.Where(p =>
                p.Status == RecommendationStatus.Review),
            FilterMode.Trash  => _vm.AllPhotos.Where(p =>
                p.Status is RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete
                || p.IsMovedToTrash),
            _                 => _vm.AllPhotos,
        };

        if (!string.IsNullOrEmpty(_search))
            source = source.Where(p =>
                p.FileName.Contains(_search, StringComparison.OrdinalIgnoreCase));

        var list = source.OrderByDescending(p => p.OverallScore ?? 0).ToList();

        PhotoGrid.ItemsSource = list;

        bool empty = list.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible  : Visibility.Collapsed;
        PhotoGrid.Visibility  = empty ? Visibility.Collapsed : Visibility.Visible;

        CountLabel.Text = $"({list.Count})";
        StatusBar.Text  = $"共 {list.Count} 张照片";
        BatchBtn.IsEnabled = list.Count > 0;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        _search = sender.Text.Trim();
        ApplyFilter();
    }

    private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItemViewModel vm && _vm is not null)
            _vm.SelectedPhoto = vm;
    }

    private PhotoItemViewModel? GetPhotoFromSender(object sender)
    {
        if (sender is not FrameworkElement { Tag: Guid id } || _vm is null)
            return null;

        return _vm.AllPhotos.FirstOrDefault(p => p.Id == id);
    }

    private void MarkKeep_Click(object sender, RoutedEventArgs e)
    {
        var photo = GetPhotoFromSender(sender);
        if (photo is null || _vm is null) return;

        _vm.MarkKeepCommand.Execute(photo);
        _vm.SelectedPhoto = photo;
        ApplyFilter();
    }

    private void MarkReview_Click(object sender, RoutedEventArgs e)
    {
        var photo = GetPhotoFromSender(sender);
        if (photo is null || _vm is null) return;

        _vm.MarkReviewCommand.Execute(photo);
        _vm.SelectedPhoto = photo;
        ApplyFilter();
    }

    private void MarkDelete_Click(object sender, RoutedEventArgs e)
    {
        var photo = GetPhotoFromSender(sender);
        if (photo is null || _vm is null) return;

        _vm.MarkDeleteCommand.Execute(photo);
        _vm.SelectedPhoto = photo;
        ApplyFilter();
    }

    private async void BatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        switch (_mode)
        {
            case FilterMode.Review:
            {
                var reviewPhotos = _vm.AllPhotos
                    .Where(p => p.Status == RecommendationStatus.Review)
                    .ToList();
                if (reviewPhotos.Count == 0) return;

                var dialog = new ContentDialog
                {
                    Title             = "确认移动",
                    Content           = $"将把 {reviewPhotos.Count} 张待确认照片移入待删除目录，是否继续？",
                    PrimaryButtonText = "继续",
                    CloseButtonText   = "取消",
                    DefaultButton     = ContentDialogButton.Close,
                    XamlRoot          = XamlRoot,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    foreach (var vm in reviewPhotos)
                        _vm.MarkDeleteCommand.Execute(vm);
                    await _vm.MoveMarkedToTrashCommand.ExecuteAsync(null);
                    ApplyFilter();
                }
                break;
            }
            case FilterMode.Trash:
                await _vm.MoveMarkedToTrashCommand.ExecuteAsync(null);
                ApplyFilter();
                break;
            case FilterMode.Keep:
            {
                var keepPhotos = _vm.AllPhotos
                    .Where(p => p.Status is RecommendationStatus.Keep
                                         or RecommendationStatus.ManualKeep)
                    .ToList();
                foreach (var vm in keepPhotos)
                    _vm.MarkReviewCommand.Execute(vm);
                ApplyFilter();
                break;
            }
        }
    }
}
