using GroupTake.Poc.Imaging.Models;
using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Services;
using GroupTake.Poc.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace GroupTake.Poc.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly PocSession _session = new();
    private CancellationTokenSource? _operation;
    private string? _modelPath;
    private string? _segmentationModelPath;
    private bool _ready, _closed;
    public MainWindow()
    {
        InitializeComponent();
        Title = "GroupTake POC — BiSeNet 修边验证";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1560, 960));
        ResultPane.Editable = false;
        BasePane.Caption = "底图 A · 导入或拖入照片";
        SourcePane.Caption = "来源 B · 导入或拖入照片";
        ResultPane.Caption = "结果 · 先框选两个头部再合成";
        BasePane.UseHeadMode(true); SourcePane.UseHeadMode(true);
        BasePane.ShowLandmarks = SourcePane.ShowLandmarks = false;
        ExportName.Text = $"GroupTake-head-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        BasePane.SelectionChanged += (_, _) => Invalidate();
        SourcePane.SelectionChanged += (_, _) => Invalidate();
        Closed += (_, _) => { _closed = true; _operation?.Cancel(); if (_operation is null) _session.Dispose(); };
        _ready = true;
    }
    private void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
    private FileOpenPicker PhotoPicker()
    {
        var picker = new FileOpenPicker(); InitializePicker(picker);
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png" }) picker.FileTypeFilter.Add(extension);
        return picker;
    }
    private async void LoadBase_Click(object sender, RoutedEventArgs e) => await PickPhoto(true);
    private async void LoadSource_Click(object sender, RoutedEventArgs e) => await PickPhoto(false);
    private Task PickPhoto(bool isBase) => Run("选择照片…", async ct =>
    {
        var file = await PhotoPicker().PickSingleFileAsync();
        if (file is not null) await LoadPhoto(isBase, file.Path, ct);
    });
    private async Task LoadPhoto(bool isBase, string path, CancellationToken ct)
    {
        var photo = await Task.Run(() => PhotoCodec.Load(path, ct), ct);
        try
        {
            var bytes = await Task.Run(() => PhotoCodec.Preview(photo, int.MaxValue), ct);
            ct.ThrowIfCancellationRequested();
            await (isBase ? BasePane : SourcePane).SetImageAsync(bytes, photo.Width, photo.Height,
                $"{(isBase ? "底图 A" : "来源 B")} · {Path.GetFileName(path)} · {photo.Width}×{photo.Height}", true);
            _session.SetPhoto(isBase, photo, path); photo = null!;
            ResultPane.ClearImage("照片已更新 · 请重新合成");
            ExportButton.IsEnabled = DebugButton.IsEnabled = false;
            Status.Text = HeadMode ? "加载完成。分别框住同一个人的完整头部，然后合成；无需标记五官。" : "加载完成。框选同一个人，再标记五个点。";
        }
        finally { photo?.Dispose(); }
    }
    private void Invalidate()
    {
        if (!_ready || _operation is not null) return;
        _session.Invalidate(); ExportButton.IsEnabled = DebugButton.IsEnabled = false;
        ResultPane.ClearImage("选择或参数已更新 · 请重新合成");
        Status.Text = "选择或参数已更新，请重新合成。";
    }
    private BlendOptions Options => new((BlendMethod)Math.Max(0, Blend.SelectedIndex), double.IsFinite(Feather.Value) ? Feather.Value : 16);
    private bool HeadMode => ReplacementMode.SelectedIndex == 0;
    private bool AutoHeadMode => HeadMode && AutoMaskToggle.IsChecked == true;
    private HeadReplacementOptions HeadOptions => new(HeadFeather.Value, HeadAngle.Value, HeadScale.Value, HeadX.Value, HeadY.Value,
        BasePane.GetProtectionStrokes(), SourcePane.GetProtectionStrokes(),
        AutoHeadMode ? BasePane.GetAutoMask() : null, AutoHeadMode ? SourcePane.GetAutoMask() : null);
    private void AutoMask_Click(object sender, RoutedEventArgs e)
    {
        BasePane.ShowMask = SourcePane.ShowMask = AutoHeadMode && MaskOverlayToggle.IsChecked == true;
        Invalidate();
    }
    private void MaskOverlay_Click(object sender, RoutedEventArgs e)
    { BasePane.ShowMask = SourcePane.ShowMask = AutoHeadMode && MaskOverlayToggle.IsChecked == true; }
    private void BrushSize_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_ready) return;
        BasePane.BrushRadius = SourcePane.BrushRadius = double.IsFinite(sender.Value) ? Math.Clamp(sender.Value, 1, 100) : 6;
    }
    private void SegmentationModel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _segmentationModelPath = null;
        BasePane.ClearAutoMask(); SourcePane.ClearAutoMask(); Invalidate();
    }
    private async void SegmentationModelFile_Click(object sender, RoutedEventArgs e)
    {
        await Run("选择本地 BiSeNet ONNX 权重…", async ct =>
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".onnx"); InitializePicker(picker);
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            await Task.Run(() => { using var check = new BiSeNetHeadSegmentationService(file.Path); }, ct);
            _segmentationModelPath = file.Path; BasePane.ClearAutoMask(); SourcePane.ClearAutoMask();
            _session.Invalidate(); ResultPane.ClearImage("模型已更新 · 请重新分割");
            Status.Text = $"已验证模型 {file.Name}。请重新点击自动分割；模型选择框用于切回缓存的 ResNet18/34。";
        });
    }
    private async void Segment_Click(object sender, RoutedEventArgs e)
    {
        await Run("正在本地 CPU 分割两个头部…", async ct =>
        {
            if (!AutoHeadMode) throw new InvalidOperationException("请先选择整头路线并启用 BiSeNet 蒙版。");
            var a = BasePane.Selection ?? throw new InvalidOperationException("请先框选底图的完整头部。");
            var b = SourcePane.Selection ?? throw new InvalidOperationException("请先框选来源图同一人的完整头部。");
            var target = _session.Base ?? throw new InvalidOperationException("请加载底图。");
            var source = _session.Source ?? throw new InvalidOperationException("请加载来源图。");
            var model = SegmentationModel.SelectedIndex == 1 ? "resnet18" : "resnet34";
            // A user-profile cache remains visible across the development tool and external app identities.
            var path = _segmentationModelPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "GroupTake.Poc", "models", "bisenet", model + ".onnx");
            if (!File.Exists(path)) throw new FileNotFoundException("模型尚未缓存。请运行 experiments/GroupTake/Get-BiSeNetModels.ps1 后重试。", path);
            var masks = await Task.Run(() =>
            {
                using var service = new BiSeNetHeadSegmentationService(path);
                return (A: service.Segment(target, a.Bounds, ct), B: service.Segment(source, b.Bounds, ct));
            }, ct);
            ct.ThrowIfCancellationRequested();
            _session.Invalidate(); ExportButton.IsEnabled = DebugButton.IsEnabled = false;
            ResultPane.ClearImage("蒙版已更新 · 修边后重新合成");
            BasePane.SetAutoMask(masks.A.Mask); SourcePane.SetAutoMask(masks.B.Mask);
            BasePane.ShowMask = SourcePane.ShowMask = MaskOverlayToggle.IsChecked == true;
            Status.Text = $"{Path.GetFileNameWithoutExtension(path)} · 两图分割 {masks.A.Milliseconds + masks.B.Milliseconds:F0} ms（不含模型加载）· 绿色代表候选替换区域。请 100% 检查头发、耳朵、下巴、衣领；误分用擦除/补回修正。";
        });
    }
    private void Replacement_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        BasePane.UseHeadMode(HeadMode); SourcePane.UseHeadMode(HeadMode);
        BasePane.ClearSelection(); SourcePane.ClearSelection();
        BasePane.ShowLandmarks = SourcePane.ShowLandmarks = !HeadMode;
        LegacySettings.Visibility = HeadMode ? Visibility.Collapsed : Visibility.Visible;
        SegmentationSettings.Visibility = HeadMode ? Visibility.Visible : Visibility.Collapsed;
        SegmentButton.IsEnabled = HeadMode;
        HeadAdjustments.Visibility = HeadFeather.Visibility = HeadMode ? Visibility.Visible : Visibility.Collapsed;
        Instructions.Text = HeadMode ? "框选两图完整头部 → 自动分割 → 100% 检查绿色蒙版并擦除/补回 → 合成对比。修改头部框后须重新分割。取消 BiSeNet 可对照旧椭圆。"
            : "旧实验：框选人脸并标记五个点。此路线未通过视觉验收，保留用于对照，不推荐用于最终照片。";
        Invalidate();
    }
    private void Options_Changed(object sender, SelectionChangedEventArgs e) => Invalidate();
    private void Feather_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Invalidate();
    private void Landmarks_Click(object sender, RoutedEventArgs e)
    { BasePane.ShowLandmarks = SourcePane.ShowLandmarks = LandmarksToggle.IsChecked == true; }
    private async void Compose_Click(object sender, RoutedEventArgs e)
    {
        await Run("正在按原始分辨率对齐并融合…", async ct =>
        {
            var a = BasePane.Selection ?? throw new InvalidOperationException(HeadMode ? "请框选底图头部。" : "请框选底图人脸。");
            var b = SourcePane.Selection ?? throw new InvalidOperationException(HeadMode ? "请框选来源头部。" : "请框选来源人脸。");
            var target = _session.Base ?? throw new InvalidOperationException("请加载底图。");
            var source = _session.Source ?? throw new InvalidOperationException("请加载来源图。");
            var options = Options;
            var headOptions = HeadMode ? HeadOptions : null;
            if (AutoHeadMode && (headOptions?.TargetMask is null || headOptions.SourceMask is null))
                throw new InvalidOperationException("请先点击“自动分割”。重新框选或更换模型后需要重新分割。");
            var result = await Task.Run(() => headOptions is not null
                ? HeadReplacementService.Compose(target, source, a.Bounds, b.Bounds, headOptions, ct)
                : CompositionService.Compose(target, source, a, b, options, ct), ct);
            if (ct.IsCancellationRequested) { result.Dispose(); ct.ThrowIfCancellationRequested(); }
            _session.SetResult(result, a, b, headOptions); ResultView.SelectedIndex = HeadResultView.SelectedIndex = 0; await ShowResult(ct);
            Status.Text = headOptions is not null
                ? $"整头替换 · {result.ElapsedMilliseconds:F0} ms · 整体等比缩放 {result.Alignment.Scale:F3} · 请 100% 对比发际、耳朵、下巴和脖子；明显姿态差异请换来源。"
                : $"旧实验 {options.Method} · {result.ElapsedMilliseconds:F0} ms · RMS {result.Alignment.RmsError:F2}px · 未通过视觉验收。";
        });
    }
    private async Task ShowResult(CancellationToken ct)
    {
        var result = _session.Result;
        var view = HeadMode ? HeadResultView : ResultView;
        var mat = view.SelectedIndex switch { 1 => _session.Base, 2 => result?.Aligned, 3 => result?.Mask, _ => result?.Composite };
        if (mat is null) return;
        var bytes = await Task.Run(() => PhotoCodec.Preview(mat, int.MaxValue), ct); ct.ThrowIfCancellationRequested();
        await ResultPane.SetImageAsync(bytes, mat.Width, mat.Height, $"{((ComboBoxItem)view.SelectedItem).Content} · {mat.Width}×{mat.Height}");
        if (_session.ComposedBaseFace is { } face) ResultPane.SelectFace(face);
        ResultPane.ShowLandmarks = false;
    }
    private async void ResultView_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && _operation is null) await Run("更新预览…", ShowResult); }
    private async void HeadResultView_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && _operation is null) await Run("更新预览…", ShowResult); }
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        _session.Invalidate(); ResultPane.ClearImage("结果已重置");
        _ready = false; ResultView.SelectedIndex = HeadResultView.SelectedIndex = 1; _ready = true;
        await Run("重置…", ShowResult);
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { BasePane.ClearSelection(); SourcePane.ClearSelection(); }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        await Run("准备高清导出…", async ct =>
        {
            if (_session.Result is null) throw new InvalidOperationException("请先合成。");
            // Folder picker avoids SavePicker's zero-byte placeholder and any overwrite prompt/side effect.
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) { Status.Text = "已取消导出。"; return; }
            var name = ExportName.Text;
            if (name != Path.GetFileName(name) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("请输入文件名，不要包含文件夹。");
            var path = Path.Combine(folder.Path, name);
            var a = _session.ComposedBaseFace!; var b = _session.ComposedSourceFace!;
            var options = _session.Result.Options; var quality = (int)JpegQuality.Value;
            var headOptions = _session.HeadOptions;
            await Task.Run(() =>
            {
                using var full = headOptions is not null
                    ? HeadReplacementService.Compose(_session.Base!, _session.Source!, a.Bounds, b.Bounds, headOptions, ct)
                    : CompositionService.Compose(_session.Base!, _session.Source!, a, b, options, ct);
                PhotoCodec.Export(full.Composite, path, [_session.BasePath, _session.SourcePath], quality, ct);
            }, ct);
            Status.Text = $"已导出原图尺寸：{path}";
        });
    }
    private async void Debug_Click(object sender, RoutedEventArgs e) => await SaveDebug();
    private Task SaveDebug() => Run("保存调试结果及中间图…", async ct =>
    {
        if (_session.Result is null) throw new InvalidOperationException("请先合成。");
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
        var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
        var directory = Path.Combine(folder.Path, $"case-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        var headOptions = _session.HeadOptions;
        var success = await Task.Run(() =>
        {
            if (headOptions is null) return DebugExporter.SaveCase(directory, _session.Base!, _session.Source!,
                _session.ComposedBaseFace!, _session.ComposedSourceFace!, _session.Result.Options.FeatherRadius, ct);
            DebugExporter.SaveHeadCase(directory, _session.Base!, _session.Source!, _session.ComposedBaseFace!.Bounds,
                _session.ComposedSourceFace!.Bounds, headOptions, ct); return true;
        }, ct);
        Status.Text = $"{(success ? (headOptions is not null ? "整头调试结果已保存" : "旧版三种融合已保存") : "部分算法失败，请检查 metrics.json")}：{directory}";
    });
    private async void Model_Click(object sender, RoutedEventArgs e)
    {
        await Run("选择本地检测模型…", async _ =>
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".onnx"); InitializePicker(picker);
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            _modelPath = file.Path; ModelLabel.Text = file.Name;
        });
    }
    private async void Detect_Click(object sender, RoutedEventArgs e) => await Detect();
    private Task Detect() => Run("正在本地检测两图全部人脸…", async ct =>
    {
        if (_modelPath is null || _session.Base is null || _session.Source is null) throw new InvalidOperationException("先加载两张照片和 YuNet 模型。");
        var model = _modelPath;
        var a = await Task.Run(() => FaceDetectionService.Detect(_session.Base, model, ct), ct);
        var b = await Task.Run(() => FaceDetectionService.Detect(_session.Source, model, ct), ct);
        _session.Invalidate(); BasePane.ClearSelection(); SourcePane.ClearSelection();
        ResultPane.ClearImage("检测完成 · 请分别选择同一人物");
        BasePane.SetDetections(a); SourcePane.SetDetections(b);
        Status.Text = $"底图 {a.Count} 张、来源 {b.Count} 张人脸。请分别点击同一个人的检测框；编号不表示身份对应。可切换关键点模式修正。";
    });
    private void Photo_DragOver(object sender, DragEventArgs e)
    { if (_operation is null && e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy; }
    private async void Base_Drop(object sender, DragEventArgs e) => await DropPhoto(e, true);
    private async void Source_Drop(object sender, DragEventArgs e) => await DropPhoto(e, false);
    private Task DropPhoto(DragEventArgs e, bool isBase) => Run("加载拖入照片…", async ct =>
    {
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count != 1) throw new ArgumentException("每个面板一次拖入一张 JPEG/PNG。");
        await LoadPhoto(isBase, items[0].Path, ct);
    });
    private void Cancel_Click(object sender, RoutedEventArgs e) { _operation?.Cancel(); Status.Text = "正在取消；等待当前 OpenCV 调用结束…"; }
    private async Task Run(string status, Func<CancellationToken, Task> action)
    {
        if (_operation is not null || _closed) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        Commands.IsEnabled = BasePane.IsEnabled = SourcePane.IsEnabled = ResultPane.IsEnabled = SettingsScroll.IsEnabled = false;
        Progress.Visibility = CancelButton.Visibility = Visibility.Visible; Progress.IsActive = true; Status.Text = status;
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { if (!_closed) Status.Text = "操作已取消，未写入最终文件。"; }
        catch (Exception ex) { if (!_closed) Status.Text = $"操作失败：{ex.Message}"; }
        finally
        {
            _operation = null;
            if (_closed) _session.Dispose();
            else
            {
                Commands.IsEnabled = BasePane.IsEnabled = SourcePane.IsEnabled = ResultPane.IsEnabled = SettingsScroll.IsEnabled = true;
                Progress.IsActive = false; Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
                ExportButton.IsEnabled = DebugButton.IsEnabled = _session.Result is not null;
                DetectButton.IsEnabled = _modelPath is not null && _session.Base is not null && _session.Source is not null;
            }
        }
    }
}
