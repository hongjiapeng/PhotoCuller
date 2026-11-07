using GroupTake.Poc.Imaging.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using OpenCvSharp;
using Windows.Storage.Streams;
using CvRect = OpenCvSharp.Rect;

namespace GroupTake.Poc.WinUI.Views;

/// <summary>Image and annotations share one full-resolution pixel/view transform, including letterboxing.</summary>
public sealed partial class PhotoPane : UserControl
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Colors.Transparent), MinHeight = 160 };
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _annotations = new() { IsHitTestVisible = false };
    private readonly TextBlock _caption = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _mode = new() { Width = 145, SelectedIndex = 0 };
    private readonly Button _clearProtection = new() { Content = "清除保护", Visibility = Visibility.Collapsed, Padding = new Thickness(8, 4, 8, 4) };
    private readonly StackPanel _editTools = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly MaskEditHistory _protection = new();
    private readonly List<float[]> _strokePoints = [];
    private bool _headMode, _erasing;
    private double _strokeRadius;
    private readonly Point2f?[] _points = new Point2f?[5];
    private CvRect? _bounds;
    private IReadOnlyList<FaceSelection> _detections = [];
    private int _width, _height;
    private double? _scaleOverride;
    private double _panX, _panY;
    private bool _dragging, _panning, _moving;
    private double _startX, _startY, _anchorX, _anchorY, _startPanX, _startPanY;
    private CvRect? _startBounds;
    private bool _showLandmarks = true;
    public event EventHandler? SelectionChanged;
    private bool _editable = true;
    public bool Editable { get => _editable; set { _editable = value; _editTools.Visibility = value ? Visibility.Visible : Visibility.Collapsed; _mode.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        _clearProtection.Visibility = value && _headMode ? Visibility.Visible : Visibility.Collapsed; } }
    public string Caption { get => _caption.Text; set => _caption.Text = value; }
    public bool ShowLandmarks { get => _showLandmarks; set { _showLandmarks = value; Render(); } }
    public FaceSelection? Selection => _bounds is { } b
        ? new FaceSelection(b, _points.Where(p => p.HasValue).Select(p => p!.Value).ToArray()) : null;
    public MaskProtectionStroke[] GetProtectionStrokes() => _protection.Snapshot();
    private ImageViewport Viewport => new(_width, _height, _canvas.ActualWidth, _canvas.ActualHeight,
        _scaleOverride is { } scale ? scale / Math.Min(_canvas.ActualWidth / _width, _canvas.ActualHeight / _height) : 1, _panX, _panY);

    public PhotoPane()
    {
        var root = new Grid { RowSpacing = 8, MinWidth = 200 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(_caption);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var text in new[] { "框选 / 调整", "平移", "1 · 画面左眼", "2 · 画面右眼", "3 · 鼻尖", "4 · 画面左嘴角", "5 · 画面右嘴角", "选择检测框" }) _mode.Items.Add(text);
        AutomationProperties.SetName(_mode, "照片编辑模式或关键点");
        var editbar = _editTools;
        editbar.Children.Add(_mode);
        _clearProtection.Content = "重置修边";
        _clearProtection.Click += (_, _) => { _protection.Clear(); _strokePoints.Clear(); RefreshMaskOverlay(); Render(); SelectionChanged?.Invoke(this, EventArgs.Empty); };
        AutomationProperties.SetName(_clearProtection, "清除保护笔划");
        editbar.Children.Add(_clearProtection);
        AddButton(editbar, "撤销", () => { _protection.Undo(); RefreshMaskOverlay(); Render(); SelectionChanged?.Invoke(this, EventArgs.Empty); });
        AddButton(editbar, "重做", () => { _protection.Redo(); RefreshMaskOverlay(); Render(); SelectionChanged?.Invoke(this, EventArgs.Empty); });
        AddButton(toolbar, "适应", () => { _scaleOverride = null; _panX = _panY = 0; Render(); });
        AddButton(toolbar, "100%", () =>
        {
            if (_width == 0) return;
            _scaleOverride = 1 / (XamlRoot?.RasterizationScale ?? 1);
            if (_bounds is { } b)
            {
                _panX = (_width / 2d - b.X - b.Width / 2d) * _scaleOverride.Value;
                _panY = (_height / 2d - b.Y - b.Height / 2d) * _scaleOverride.Value;
            }
            Render();
        });
        AddButton(toolbar, "−", () => { _scaleOverride = Math.Max(0.01, Viewport.Scale / 1.4); Render(); });
        AddButton(toolbar, "+", () => { _scaleOverride = Math.Min(20, Viewport.Scale * 1.4); Render(); });
        toolbar.Children.Add(_zoomLabel);
        var tools = new StackPanel { Spacing = 4 }; tools.Children.Add(toolbar); tools.Children.Add(editbar);
        var scroller = new ScrollViewer { Content = tools, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1); root.Children.Add(scroller);
        _canvas.Children.Add(_image); _canvas.Children.Add(_maskOverlay); _canvas.Children.Add(_annotations);
        Grid.SetRow(_canvas, 2); root.Children.Add(_canvas); Content = root;
        _canvas.SizeChanged += (_, _) => Render();
        _canvas.PointerPressed += Pressed; _canvas.PointerMoved += Moved;
        _canvas.PointerReleased += Released; _canvas.PointerCanceled += Released;
        _canvas.PointerCaptureLost += (_, _) => _dragging = false;
        _canvas.PointerWheelChanged += (_, e) =>
        {
            _scaleOverride = Math.Clamp(Viewport.Scale * (e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta > 0 ? 1.15 : 1 / 1.15), 0.01, 20);
            Render(); e.Handled = true;
        };
    }
    private static void AddButton(StackPanel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 4, 8, 4), MinWidth = 32 };
        button.Click += (_, _) => action(); panel.Children.Add(button);
    }
    public async Task SetImageAsync(byte[] png, int width, int height, string caption, bool resetSelection = false)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); }
        var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); _image.Source = bitmap;
        var sizeChanged = _width != width || _height != height;
        _width = width; _height = height; _caption.Text = caption;
        if (resetSelection) { ClearAutoMask(); _bounds = null; Array.Clear(_points); _detections = []; _mode.SelectedIndex = 0; _protection.Clear(); _strokePoints.Clear(); }
        if (sizeChanged || resetSelection) { _scaleOverride = null; _panX = _panY = 0; }
        Render();
    }
    public void SetDetections(IReadOnlyList<FaceSelection> detections)
    { _detections = detections; _mode.SelectedIndex = 7; Render(); }
    public void SelectFace(FaceSelection face)
    {
        _bounds = face.Bounds;
        for (var i = 0; i < 5; i++) _points[i] = i < face.Landmarks.Length ? face.Landmarks[i] : null;
        Render(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ClearSelection()
    { ClearAutoMask(); _bounds = null; Array.Clear(_points); _detections = []; _protection.Clear(); _strokePoints.Clear(); Render(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    public void UseHeadMode(bool enabled)
    {
        _headMode = enabled;
        _clearProtection.Visibility = enabled && Editable ? Visibility.Visible : Visibility.Collapsed;
        _mode.Items.Clear();
        foreach (var text in enabled ? new[] { "框选 / 调整", "平移", "擦除 · 保留底图", "补回 · 使用来源" }
            : new[] { "框选 / 调整", "平移", "1 · 画面左眼", "2 · 画面右眼", "3 · 鼻尖", "4 · 画面左嘴角", "5 · 画面右嘴角", "选择检测框" }) _mode.Items.Add(text);
        _mode.SelectedIndex = 0;
    }
    public void ClearImage(string caption)
    { ClearAutoMask(); _image.Source = null; _width = _height = 0; _bounds = null; Array.Clear(_points); _detections = []; _protection.Clear(); _strokePoints.Clear(); _annotations.Children.Clear(); _caption.Text = caption; }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_width == 0) return;
        var point = e.GetCurrentPoint(_canvas); _startX = point.Position.X; _startY = point.Position.Y;
        _erasing = false;
        _panning = !Editable || _mode.SelectedIndex == 1 || point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed;
        if (_panning) { _startPanX = _panX; _startPanY = _panY; }
        else
        {
            if (!Viewport.TryToImage(_startX, _startY, out _anchorX, out _anchorY)) return;
            if (_headMode && _mode.SelectedIndex is 2 or 3)
            {
                if (_bounds is not { } head) return;
                _erasing = true; _strokeRadius = BrushRadius;
                _strokePoints.Clear(); _strokePoints.Add([(float)_anchorX, (float)_anchorY]);
                _dragging = true; _canvas.CapturePointer(e.Pointer); Render(); e.Handled = true; return;
            }
            if (_mode.SelectedIndex == 7)
            {
                var face = _detections.FirstOrDefault(d => d.Bounds.Contains(new Point((int)_anchorX, (int)_anchorY)));
                if (face is not null) SelectFace(face);
                e.Handled = true; return;
            }
            if (_mode.SelectedIndex >= 2)
            {
                if (_bounds is not { } box || !box.Contains(new Point((int)_anchorX, (int)_anchorY))) return;
                var index = _mode.SelectedIndex - 2; _points[index] = new Point2f((float)_anchorX, (float)_anchorY);
                if (index < 4) _mode.SelectedIndex++;
                Render(); SelectionChanged?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
            }
            _startBounds = _bounds; _moving = false;
            if (_bounds is { } b)
            {
                var threshold = Math.Min(12 / Viewport.Scale, Math.Min(b.Width, b.Height) * 0.22);
                var nearLeft = Math.Abs(_anchorX - b.X) < threshold; var nearRight = Math.Abs(_anchorX - b.Right) < threshold;
                var nearTop = Math.Abs(_anchorY - b.Y) < threshold; var nearBottom = Math.Abs(_anchorY - b.Bottom) < threshold;
                if ((nearLeft || nearRight) && (nearTop || nearBottom))
                { _anchorX = nearLeft ? b.Right : b.X; _anchorY = nearTop ? b.Bottom : b.Y; }
                else _moving = b.Contains(new Point((int)_anchorX, (int)_anchorY));
            }
        }
        _dragging = true; _canvas.CapturePointer(e.Pointer); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetCurrentPoint(_canvas).Position;
        if (_panning) { _panX = _startPanX + p.X - _startX; _panY = _startPanY + p.Y - _startY; }
        else
        {
            var v = Viewport;
            var x = Math.Clamp((p.X - v.OffsetX) / v.Scale, 0, _width - 1); var y = Math.Clamp((p.Y - v.OffsetY) / v.Scale, 0, _height - 1);
            if (_erasing)
            {
                var previous = _strokePoints[^1];
                if (Math.Abs(x - previous[0]) + Math.Abs(y - previous[1]) >= Math.Max(1, _strokeRadius * 0.25))
                    _strokePoints.Add([(float)x, (float)y]);
            }
            else if (_moving && _startBounds is { } b)
            {
                var nx = Math.Clamp(b.X + (int)(x - _anchorX), 0, _width - b.Width);
                var ny = Math.Clamp(b.Y + (int)(y - _anchorY), 0, _height - b.Height);
                _bounds = new CvRect(nx, ny, b.Width, b.Height);
            }
            else
            {
                var left = (int)Math.Min(_anchorX, x); var top = (int)Math.Min(_anchorY, y);
                _bounds = new CvRect(left, top, Math.Max(1, (int)Math.Max(_anchorX, x) - left), Math.Max(1, (int)Math.Max(_anchorY, y) - top));
            }
        }
        Render(); e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        if (_erasing && _strokePoints.Count > 0)
        {
            _protection.Add(new MaskProtectionStroke(_strokeRadius, _strokePoints.Select(p => p.ToArray()).ToArray(), _mode.SelectedIndex == 3));
            _strokePoints.Clear(); _erasing = false;
            RefreshMaskOverlay();
        }
        else if (!_panning && _headMode && _mode.SelectedIndex == 0 && _bounds != _startBounds)
        { ClearAutoMask(); _protection.Clear(); }
        _dragging = false; _canvas.ReleasePointerCapture(e.Pointer);
        Render(); if (!_panning) SelectionChanged?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
    private void Render()
    {
        _canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight) };
        if (_width == 0 || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0) return;
        var v = Viewport;
        _image.Width = _width * v.Scale; _image.Height = _height * v.Scale;
        Canvas.SetLeft(_image, v.OffsetX); Canvas.SetTop(_image, v.OffsetY);
        PositionMaskOverlay();
        _zoomLabel.Text = $"{v.Scale * (XamlRoot?.RasterizationScale ?? 1):P0}";
        _annotations.Children.Clear();
        for (var i = 0; i < _detections.Count; i++) DrawBox(_detections[i].Bounds, Colors.Gold, $"人脸 {i + 1}");
        if (_bounds is { } b && Editable) DrawBox(b, Colors.DeepSkyBlue,
            _headMode ? $"头部 {b.Width}×{b.Height} · 修边 {_protection.Snapshot().Length} 笔" : $"{b.Width}×{b.Height} · {_points.Count(p => p.HasValue)}/5 点");
        if (_headMode && Editable)
        {
            if (_autoMask is null) foreach (var stroke in _protection.Snapshot()) DrawProtection(stroke);
            if (_strokePoints.Count > 0) DrawProtection(new MaskProtectionStroke(_strokeRadius, _strokePoints.ToArray(), _mode.SelectedIndex == 3));
        }
        if (!_showLandmarks) return;
        for (var i = 0; i < 5; i++)
        {
            if (_points[i] is not { } p) continue;
            var xy = v.ToView(p.X, p.Y);
            var marker = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Colors.Gold), Stroke = new SolidColorBrush(Colors.Black), StrokeThickness = 1.5 };
            Canvas.SetLeft(marker, xy.X - 5); Canvas.SetTop(marker, xy.Y - 5); _annotations.Children.Add(marker);
            Label($"{i + 1}", xy.X + 6, xy.Y - 18);
        }
    }
    private void DrawProtection(MaskProtectionStroke stroke)
    {
        var brush = new SolidColorBrush(stroke.Restore ? Colors.LimeGreen : Colors.OrangeRed); var radius = stroke.Radius * Viewport.Scale;
        (double X, double Y)? previous = null;
        foreach (var point in stroke.Points)
        {
            var p = Viewport.ToView(point[0], point[1]);
            if (previous is { } start) _annotations.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = p.X, Y2 = p.Y,
                Stroke = brush, StrokeThickness = radius * 2, Opacity = 0.4 });
            var dot = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush, Opacity = 0.4 };
            Canvas.SetLeft(dot, p.X - radius); Canvas.SetTop(dot, p.Y - radius); _annotations.Children.Add(dot);
            previous = p;
        }
    }
    private void DrawBox(CvRect box, Windows.UI.Color color, string label)
    {
        var v = Viewport; var xy = v.ToView(box.X, box.Y);
        var rect = new Rectangle { Width = box.Width * v.Scale, Height = box.Height * v.Scale, Stroke = new SolidColorBrush(color), StrokeThickness = 2 };
        Canvas.SetLeft(rect, xy.X); Canvas.SetTop(rect, xy.Y); _annotations.Children.Add(rect);
        Label(label, xy.X, Math.Max(0, xy.Y - 24));
        if (_bounds == box && Editable)
            foreach (var corner in new[] { (box.X, box.Y), (box.Right, box.Y), (box.X, box.Bottom), (box.Right, box.Bottom) })
            {
                var c = v.ToView(corner.Item1, corner.Item2);
                var handle = new Rectangle { Width = 8, Height = 8, Fill = new SolidColorBrush(color) };
                Canvas.SetLeft(handle, c.X - 4); Canvas.SetTop(handle, c.Y - 4); _annotations.Children.Add(handle);
            }
    }
    private void Label(string text, double x, double y)
    {
        var label = new Border { Background = new SolidColorBrush(Colors.Black), Padding = new Thickness(3), Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(Colors.White), FontSize = 12 } };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); _annotations.Children.Add(label);
    }
}
