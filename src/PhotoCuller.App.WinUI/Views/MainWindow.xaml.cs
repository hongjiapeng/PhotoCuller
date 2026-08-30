using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
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

        ContentFrame.Navigated += ContentFrame_Navigated;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
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

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        => UpdateDetailPanelVisibility();

    private void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentViewMode))
            DispatcherQueue.TryEnqueue(UpdateDetailPanelVisibility);
    }

    private void UpdateDetailPanelVisibility()
    {
        var pageType = ContentFrame.CurrentSourcePageType;
        var isPhotoListPage = pageType == typeof(LibraryPage)
                           || pageType == typeof(RecommendedPage)
                           || pageType == typeof(PendingPage)
                           || pageType == typeof(TrashPage);
        var shouldShow = isPhotoListPage
                      || pageType == typeof(SimilarGroupsPage) && ViewModel.IsBrowseMode;

        DetailColumn.Width = shouldShow ? new GridLength(310) : new GridLength(0);
        DetailPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
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
