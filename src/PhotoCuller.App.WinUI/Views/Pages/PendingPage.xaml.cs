using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class PendingPage : Page
{
    public PendingPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Frame.Navigate(typeof(FilteredPhotoPage), FilteredPhotoPage.FilterMode.Review);
    }
}
