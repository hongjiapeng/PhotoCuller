using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class SimilarGroupsPage : Page
{
    private MainViewModel? _vm;

    public SimilarGroupsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm is null) return;

        _vm.PropertyChanged += Vm_PropertyChanged;
        Refresh();
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

    private void PhotoCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el
            && el.Tag is Guid id
            && _vm is not null)
        {
            var photo = _vm.AllPhotos.FirstOrDefault(p => p.Id == id);
            if (photo is not null)
                _vm.SelectedPhoto = photo;
        }
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
                photo.Model.RecommendationStatus = RecommendationStatus.ToDelete;

            // TODO Stage 11: call IFileMoveService
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
        {
            // TODO Stage 11: invoke IFileMoveService
        }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        // TODO Stage 15: generate export report
    }
}
