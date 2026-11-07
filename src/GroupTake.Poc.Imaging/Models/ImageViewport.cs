namespace GroupTake.Poc.Imaging.Models;

/// <summary>Uniform fit plus zoom and pan; all view units are device independent pixels.</summary>
public readonly record struct ImageViewport(double ImageWidth, double ImageHeight,
    double ViewWidth, double ViewHeight, double Zoom = 1, double PanX = 0, double PanY = 0)
{
    public double Scale => Math.Min(ViewWidth / ImageWidth, ViewHeight / ImageHeight) * Zoom;
    public double OffsetX => (ViewWidth - ImageWidth * Scale) / 2 + PanX;
    public double OffsetY => (ViewHeight - ImageHeight * Scale) / 2 + PanY;
    public (double X, double Y) ToView(double x, double y) => (x * Scale + OffsetX, y * Scale + OffsetY);
    public bool TryToImage(double x, double y, out double imageX, out double imageY)
    {
        imageX = (x - OffsetX) / Scale;
        imageY = (y - OffsetY) / Scale;
        return double.IsFinite(imageX) && double.IsFinite(imageY)
            && imageX >= 0 && imageY >= 0 && imageX < ImageWidth && imageY < ImageHeight;
    }
}
