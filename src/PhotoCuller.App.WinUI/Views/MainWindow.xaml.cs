using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.App.WinUI.Views.Pages;
using Windows.System;

namespace PhotoCuller.App.WinUI.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        ContentFrame.Navigate(typeof(SimilarGroupsPage));

        // Ctrl+Z → Undo last move
        if (Content is UIElement root)
        {
            var accel = new KeyboardAccelerator
            {
                Key       = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control,
            };
            accel.Invoked += async (_, _) =>
                await ViewModel.UndoLastMoveCommand.ExecuteAsync(null);
            root.KeyboardAccelerators.Add(accel);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
        => Navigate((sender as FrameworkElement)?.Tag?.ToString());

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
