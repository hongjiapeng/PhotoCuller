using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PhotoCuller.App.WinUI.ViewModels;
using PhotoCuller.Core.Enums;

namespace PhotoCuller.App.WinUI.Views.Pages;

public sealed partial class AiModelsPage : Page
{
    public AiModelsViewModel ViewModel { get; }

    public AiModelsPage()
    {
        ViewModel = App.Services.GetService(typeof(AiModelsViewModel)) as AiModelsViewModel
                    ?? throw new InvalidOperationException("AiModelsViewModel not registered.");
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    // ── Model list click ─────────────────────────────────────────────────────

    private void ModelItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Guid id)
        {
            var vm = ViewModel.LocalModels.FirstOrDefault(m => m.Model.Id == id)
                  ?? ViewModel.CloudModels.FirstOrDefault(m => m.Model.Id == id);

            if (vm is not null)
            {
                ViewModel.SelectedModel = vm;
                PopulateEditForm(vm);
            }
        }
    }

    // ── Edit form ────────────────────────────────────────────────────────────

    private void PopulateEditForm(AiModelProfileViewModel vm)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        EditForm.Visibility   = Visibility.Visible;

        NameBox.Text        = vm.Name;
        EnabledSwitch.IsOn  = vm.IsEnabled;
        ApiKeyMaskLabel.Text = vm.ApiKeyMask;

        // Provider combo
        SelectComboByTag(ProviderCombo, vm.ProviderType.ToString());
        UpdateProviderFields(vm.ProviderType);

        // Model type combo
        SelectComboByTag(ModelTypeCombo, vm.ModelType.ToString());

        // Cloud fields
        BaseUrlBox.Text    = vm.BaseUrl;
        ModelIdBox.Text    = vm.ModelId;
        ApiKeyBox.Password = string.Empty; // never pre-fill the key
        TimeoutBox.Value   = vm.TimeoutSeconds;
        ConcurrencyBox.Value = vm.MaxConcurrency;
        DailyLimitBox.Value  = vm.DailyLimit;

        // Local fields
        LocalPathBox.Text = vm.LocalModelPath;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateProviderFields(ProviderType type)
    {
        bool isCloud = type != ProviderType.Local;
        LocalFields.Visibility = isCloud ? Visibility.Collapsed : Visibility.Visible;
        CloudFields.Visibility = isCloud ? Visibility.Visible   : Visibility.Collapsed;
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            is string tag && Enum.TryParse<ProviderType>(tag, out var t))
        {
            UpdateProviderFields(t);
        }
    }

    private void ApiKeyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedModel is not null)
            ViewModel.SelectedModel.PlainApiKey = ApiKeyBox.Password;
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private async void SaveModel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedModel is null) return;

        var vm = ViewModel.SelectedModel;
        vm.Name          = NameBox.Text.Trim();
        vm.IsEnabled     = EnabledSwitch.IsOn;
        vm.BaseUrl       = BaseUrlBox.Text.Trim();
        vm.ModelId       = ModelIdBox.Text.Trim();
        vm.LocalModelPath = LocalPathBox.Text.Trim();
        vm.TimeoutSeconds = (int)TimeoutBox.Value;
        vm.MaxConcurrency = (int)ConcurrencyBox.Value;
        vm.DailyLimit     = (int)DailyLimitBox.Value;

        if ((ProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            is string pTag && Enum.TryParse<ProviderType>(pTag, out var pt))
            vm.ProviderType = pt;

        if ((ModelTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            is string mTag && Enum.TryParse<ModelType>(mTag, out var mt))
            vm.ModelType = mt;

        await ViewModel.SaveSelectedCommand.ExecuteAsync(null);

        // Refresh API key mask
        ApiKeyMaskLabel.Text = vm.ApiKeyMask;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private async void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedModel is null) return;

        var dialog = new ContentDialog
        {
            Title             = "删除模型",
            Content           = $"确定要删除模型 \"{ViewModel.SelectedModel.Name}\" 吗？",
            PrimaryButtonText = "删除",
            CloseButtonText   = "取消",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSelectedCommand.ExecuteAsync(null);
            EmptyState.Visibility = Visibility.Visible;
            EditForm.Visibility   = Visibility.Collapsed;
        }
    }

    // ── Browse local path ─────────────────────────────────────────────────────

    private async void BrowseLocalPath_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".onnx");
        picker.FileTypeFilter.Add(".pt");
        picker.FileTypeFilter.Add(".bin");
        picker.SuggestedStartLocation =
            Windows.Storage.Pickers.PickerLocationId.ComputerFolder;

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(
                App.Services.GetRequiredService<Views.MainWindow>()));

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            LocalPathBox.Text = file.Path;
    }
}
