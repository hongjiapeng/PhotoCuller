using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.Views.Pages;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        // Navigate to the photo library by default
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(LibraryPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            Type? pageType = tag switch
            {
                "library"     => typeof(LibraryPage),
                "similar"     => typeof(SimilarGroupsPage),
                "recommended" => typeof(RecommendedPage),
                "pending"     => typeof(PendingPage),
                "trash"       => typeof(TrashPage),
                "aimodels"    => typeof(AiModelsPage),
                _             => null
            };

            if (pageType is not null)
                ContentFrame.Navigate(pageType);
        }
    }
}
