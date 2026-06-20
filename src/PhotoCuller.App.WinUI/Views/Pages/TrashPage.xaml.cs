using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class TrashPage : Page
{
    public TrashPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Frame.Navigate(typeof(FilteredPhotoPage), FilteredPhotoPage.FilterMode.Trash);
    }
}
