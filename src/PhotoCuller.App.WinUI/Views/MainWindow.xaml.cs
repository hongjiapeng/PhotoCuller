using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.App.WinUI.Views.Pages;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        // Default to similar-groups page
        ContentFrame.Navigate(typeof(SimilarGroupsPage));

        // Keep directory label in sync
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedDirectory))
                DirLabel.Text = string.IsNullOrEmpty(ViewModel.SelectedDirectory)
                    ? "未选择目录"
                    : System.IO.Path.GetFileName(ViewModel.SelectedDirectory)
                      is { Length: > 0 } name ? name : ViewModel.SelectedDirectory;
        };
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString();
        Navigate(tag);
    }

    private void Navigate(string? tag)
    {
        Type? pageType = tag switch
        {
            "all"      => typeof(LibraryPage),
            "similar"  => typeof(SimilarGroupsPage),
            "keep"     => typeof(RecommendedPage),
            "review"   => typeof(PendingPage),
            "trash"    => typeof(TrashPage),
            "aimodels" => typeof(AiModelsPage),
            "settings" => typeof(SettingsPage),
            _          => typeof(SimilarGroupsPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }
}
