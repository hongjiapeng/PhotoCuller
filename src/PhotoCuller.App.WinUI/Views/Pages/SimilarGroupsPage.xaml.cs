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

        GroupsList.ItemsSource = _vm.Groups;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_vm is null) return;
        GroupCountLabel.Text = $"({_vm.GroupCount} 组)";
        int pending = _vm.Groups.SelectMany(g => g.Photos)
            .Count(p => p.Status == RecommendationStatus.Review
                     || p.Status == RecommendationStatus.ToDelete);
        StatusLabel.Text =
            $"共 {_vm.TotalPhotoCount} 张照片，{_vm.GroupCount} 组，已选 {pending} 张待处理";
        BtnMoveLabel.Text = $"移动到待删除 ({pending})";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_vm is null) return;
        var q = sender.Text.Trim();
        GroupsList.ItemsSource = string.IsNullOrEmpty(q)
            ? _vm.Groups
            : (object)_vm.Groups.Where(g => g.Photos.Any(p =>
                p.FileName.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private async void BtnMoveToTrash_Click(
        object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_vm is null) return;
        int pending = _vm.Groups.SelectMany(g => g.Photos)
            .Count(p => p.Status == RecommendationStatus.Review
                     || p.Status == RecommendationStatus.ToDelete);
        if (pending == 0) return;

        var dialog = new ContentDialog
        {
            Title             = "确认移动",
            Content           = $"即将移动 {pending} 张照片到待删除目录。\n原图不会被永久删除，你仍然可以手动检查后再删除。\n\n是否继续？",
            PrimaryButtonText = "继续",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // TODO Stage 11: invoke IFileMoveService
        }
    }
}
