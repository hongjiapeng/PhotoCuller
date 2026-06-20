using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class RecommendedPage : Page
{
    public RecommendedPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Frame.Navigate(typeof(FilteredPhotoPage), FilteredPhotoPage.FilterMode.Keep);
    }
}
