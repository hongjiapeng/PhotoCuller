using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class LibraryPage : Page
{
    private MainViewModel? _vm;
    private string _filterTag = "all";
    private string _sortTag   = "score";
    private string _searchQuery = string.Empty;

    public LibraryPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        if (_vm is null) return;

        // Subscribe to scan progress
        _vm.PropertyChanged += Vm_PropertyChanged;

        ApplyFilters();
        UpdateEmptyState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm is not null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(MainViewModel.ScanProgress))
                UpdateScanProgress();

            if (e.PropertyName is nameof(MainViewModel.AllPhotos)
                              or nameof(MainViewModel.IsScanning))
            {
                ApplyFilters();
                UpdateEmptyState();
            }
        });
    }

    private void UpdateScanProgress()
    {
        if (_vm is null) return;
        var p = _vm.ScanProgress;

        if (_vm.IsScanning)
        {
            ScanInfoBar.IsOpen = true;
            ScanBar.Value = p.Percent;
            ScanStatusText.Text = p.CurrentFile is not null
                ? $"{p.Current}/{p.Total}  {p.CurrentFile}"
                : $"{p.Phase}  {p.Percent}%";
        }
        else if (p.Phase == Core.Enums.ScanPhase.Completed)
        {
            ScanInfoBar.Message = $"扫描完成：{p.Total} 张照片";
            ScanInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            ScanBar.Value = 100;
        }
        else
        {
            ScanInfoBar.IsOpen = false;
        }
    }

    private void ApplyFilters()
    {
        if (_vm is null) return;

        IEnumerable<PhotoItemViewModel> source = _vm.AllPhotos;

        // Search filter
        if (!string.IsNullOrEmpty(_searchQuery))
            source = source.Where(p =>
                p.FileName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));

        // Status filter
        source = _filterTag switch
        {
            "keep"      => source.Where(p => p.IsKeep),
            "review"    => source.Where(p =>
                               p.Status == RecommendationStatus.Review),
            "duplicate" => source.Where(p =>
                               p.Model.GroupId.HasValue),
            "low"       => source.Where(p =>
                               p.OverallScore.HasValue && p.OverallScore < 50),
            "error"     => source.Where(p =>
                               !string.IsNullOrEmpty(p.ErrorMessage)),
            _           => source,
        };

        // Sort
        source = _sortTag switch
        {
            "time"  => source.OrderByDescending(p => p.Model.ExifTakenTime
                                                    ?? p.Model.CreatedTime),
            "name"  => source.OrderBy(p => p.FileName),
            "size"  => source.OrderByDescending(p => p.FileSize),
            _       => source.OrderByDescending(p => p.OverallScore ?? 0),
        };

        PhotoGrid.ItemsSource = source.ToList();
    }

    private void UpdateEmptyState()
    {
        bool hasItems = _vm?.AllPhotos.Count > 0;
        EmptyState.Visibility = hasItems
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItemViewModel vm && _vm is not null)
            _vm.SelectedPhoto = vm;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchQuery = sender.Text.Trim();
        ApplyFilters();
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _filterTag = (FilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        ApplyFilters();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _sortTag = (SortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "score";
        ApplyFilters();
    }
}
