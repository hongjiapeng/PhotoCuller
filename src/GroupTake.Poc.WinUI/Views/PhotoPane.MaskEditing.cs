using GroupTake.Poc.Imaging.ImageProcessing;
using GroupTake.Poc.Imaging.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenCvSharp;
using System.Runtime.InteropServices;
using Windows.Storage.Streams;

namespace GroupTake.Poc.WinUI.Views;

public sealed partial class PhotoPane
{
    private readonly Image _maskOverlay = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private HeadMaskPatch? _autoMask;
    private int _overlayVersion;
    public double BrushRadius { get; set; } = 6;
    private bool _showMask = true;
    public bool ShowMask { get => _showMask; set { _showMask = value; PositionMaskOverlay(); } }
    public HeadMaskPatch? GetAutoMask() => _autoMask?.Snapshot();
    public void ClearAutoMask()
    { _overlayVersion++; _autoMask = null; _maskOverlay.Source = null; }
    public void SetAutoMask(HeadMaskPatch mask)
    { _autoMask = mask.Snapshot(); _protection.Clear(); RefreshMaskOverlay(); Render(); }

    private void PositionMaskOverlay()
    {
        _maskOverlay.Visibility = _showMask && _autoMask is not null ? Visibility.Visible : Visibility.Collapsed;
        if (_autoMask is not { } mask || _width == 0) return;
        var view = Viewport; var xy = view.ToView(mask.X, mask.Y);
        Canvas.SetLeft(_maskOverlay, xy.X); Canvas.SetTop(_maskOverlay, xy.Y);
        _maskOverlay.Width = mask.Width * view.Scale; _maskOverlay.Height = mask.Height * view.Scale;
    }

    private async void RefreshMaskOverlay()
    {
        var version = ++_overlayVersion;
        if (_autoMask is not { } patch || _bounds is not { } bounds) return;
        try
        {
            using var edited = HeadMaskEditor.Materialize(patch, new Size(_width, _height));
            HeadMaskEditor.Apply(edited, _protection.Snapshot(), bounds);
            using var crop = new Mat(edited, new Rect(patch.X, patch.Y, patch.Width, patch.Height));
            var mask = new byte[patch.Width * patch.Height];
            // ROI rows may be strided in the full-resolution image.
            for (var y = 0; y < patch.Height; y++) Marshal.Copy(crop.Ptr(y), mask, y * patch.Width, patch.Width);
            var rgba = new byte[mask.Length * 4];
            for (var p = 0; p < mask.Length; p++)
            { rgba[p * 4] = 80; rgba[p * 4 + 1] = 225; rgba[p * 4 + 2] = 20; rgba[p * 4 + 3] = mask[p] == 0 ? (byte)0 : (byte)110; }
            using var overlay = new Mat(patch.Height, patch.Width, MatType.CV_8UC4);
            Marshal.Copy(rgba, 0, overlay.Data, rgba.Length); Cv2.ImEncode(".png", overlay, out var png);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); }
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (version == _overlayVersion) { _maskOverlay.Source = bitmap; PositionMaskOverlay(); }
        }
        catch (Exception ex) { if (version == _overlayVersion) _caption.Text = $"蒙版预览失败：{ex.Message}"; }
    }
}
