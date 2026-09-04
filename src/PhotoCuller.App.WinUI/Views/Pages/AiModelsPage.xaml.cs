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
        SelectComboByTag(LocalPresetCombo, vm.LocalModelPreset);
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
        vm.LocalModelPreset = LocalPresetCombo.SelectedValue?.ToString()
            ?? "clip-aesthetic-0-1";
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

    // ── Recommended model sources ────────────────────────────────────────────

    private async void RecommendedModels_Click(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 640,
        };

        content.Children.Add(new TextBlock
        {
            Text = "这些模型适合做照片美学评分，但大多发布为 PyTorch / safetensors 权重。PhotoCuller 当前本地推理入口需要完整的 .onnx 图片输入模型；下载后请用 tools/model-export 中的脚本转换，再在本页选择 .onnx 文件。",
            TextWrapping = TextWrapping.Wrap,
        });

        AddModelSource(
            content,
            "Aesthetics Predictor V2 — SAC + LOGOS + AVA",
            "Hugging Face 上下载量较高的 CLIP 美学评分模型，适合作为默认推荐来源；通常需要导出为 ONNX 后使用。",
            "https://huggingface.co/shunk031/aesthetics-predictor-v2-sac-logos-ava1-l14-linearMSE");

        AddModelSource(
            content,
            "Improved Aesthetic Predictor 权重集合",
            "包含 LAION 系列 aesthetic predictor 的 .pth 权重，可作为导出/转换素材。",
            "https://huggingface.co/camenduru/improved-aesthetic-predictor");

        AddModelSource(
            content,
            "Improved Aesthetic Predictor 源码",
            "原始推理与训练代码，适合用来确认模型结构或编写 ONNX 导出脚本。",
            "https://github.com/christophschuhmann/improved-aesthetic-predictor");

        content.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "兼容性提醒",
            Message = "请不要直接把 .pth、.pt、.bin 或 .safetensors 当作本地模型路径；它们需要先转换为 .onnx。",
        });

        var dialog = new ContentDialog
        {
            Title = "推荐美学评分模型",
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = content,
            },
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        await dialog.ShowAsync();
    }

    private static void AddModelSource(
        Panel parent,
        string title,
        string description,
        string url)
    {
        var card = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var brush)
            && brush is Microsoft.UI.Xaml.Media.Brush borderBrush)
        {
            card.BorderBrush = borderBrush;
        }

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new HyperlinkButton
        {
            Content = title,
            NavigateUri = new Uri(url),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = url,
            FontSize = 11,
            Opacity = 0.5,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        card.Child = stack;
        parent.Children.Add(card);
    }
}
