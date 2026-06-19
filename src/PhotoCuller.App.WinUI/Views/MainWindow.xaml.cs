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
            root.KeyboardAccelerators.Add(new KeyboardAccelerator
            {
                Key       = VirtualKey.Z,
                Modifiers = VirtualKeyModifiers.Control,
            });
            root.KeyboardAccelerators[0].Invoked +=
                async (_, _) => await ViewModel.UndoLastMoveCommand.ExecuteAsync(null);
        }

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
