using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetService(typeof(SettingsViewModel)) as SettingsViewModel
                    ?? throw new InvalidOperationException("SettingsViewModel not registered.");
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);

        // Sync weight info bar severity
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.WeightTotalValid))
                WeightInfoBar.Severity = ViewModel.WeightTotalValid
                    ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success
                    : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => Frame.GoBack();

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title             = "清理缓存",
            Content           = "将删除所有缩略图缓存文件。下次浏览时会重新生成。是否继续？",
            PrimaryButtonText = "清理",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var thumbService = App.Services.GetService(
                typeof(PhotoCuller.Core.Interfaces.Scanning.IThumbnailService))
                as PhotoCuller.Core.Interfaces.Scanning.IThumbnailService;

            if (thumbService is not null)
            {
                await thumbService.ClearAllAsync();
                var notif = App.Services.GetService(
                    typeof(PhotoCuller.Core.Interfaces.Platform.INotificationService))
                    as PhotoCuller.Core.Interfaces.Platform.INotificationService;
                notif?.ShowSuccess("缩略图缓存已清理");
            }
        }
    }

    private void OpenLogDir_Click(object sender, RoutedEventArgs e)
    {
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoCuller", "logs");

        if (System.IO.Directory.Exists(logDir))
            System.Diagnostics.Process.Start("explorer.exe", logDir);
    }
}
