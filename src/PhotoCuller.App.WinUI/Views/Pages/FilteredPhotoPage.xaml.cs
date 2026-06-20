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

    public FilteredPhotoPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (e.Parameter is FilterMode m) _mode = m;

        ConfigurePage();
        _vm!.PropertyChanged += Vm_PropertyChanged;
        ApplyFilter();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm is not null) _vm.PropertyChanged -= Vm_PropertyChanged;
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

    private void ConfigurePage()
    {
        switch (_mode)
        {
            case FilterMode.Keep:
                PageTitle.Text   = "建议保留";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "暂无推荐保留的照片";
                BatchBtn.Content = "全部标记为待确认";
                break;
            case FilterMode.Review:
                PageTitle.Text   = "待确认 / 待删除";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "暂无待处理照片";
                BatchBtn.Content = "全部移动到待删除";
                break;
            case FilterMode.Trash:
                PageTitle.Text   = "待删除";
                EmptyIcon.Glyph  = "";
                EmptyText.Text   = "待删除目录为空";
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
                p.Status is RecommendationStatus.ToDelete or RecommendationStatus.ManualDelete),
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

    private async void BatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        switch (_mode)
        {
            case FilterMode.Review:
            {
                var dialog = new ContentDialog
                {
                    Title             = "确认移动",
                    Content           = $"将把 {_vm.PendingCount} 张待确认照片移入待删除目录，是否继续？",
                    PrimaryButtonText = "继续",
                    CloseButtonText   = "取消",
                    DefaultButton     = ContentDialogButton.Close,
                    XamlRoot          = XamlRoot,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    foreach (var vm in _vm.AllPhotos
                        .Where(p => p.Status == RecommendationStatus.Review).ToList())
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
                foreach (var vm in _vm.AllPhotos
                    .Where(p => p.Status is RecommendationStatus.Keep
                                         or RecommendationStatus.ManualKeep).ToList())
                    _vm.MarkReviewCommand.Execute(vm);
                ApplyFilter();
                break;
            }
        }
    }
}
