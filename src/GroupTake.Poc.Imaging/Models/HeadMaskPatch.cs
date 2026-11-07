namespace GroupTake.Poc.Imaging.Models;

/// <summary>Binary mask crop in EXIF-oriented ORIGINAL pixels, never a display thumbnail.</summary>
public sealed record HeadMaskPatch(int X, int Y, int Width, int Height, int ImageWidth, int ImageHeight,
    byte[] Pixels, string Origin = "Manual")
{
    public HeadMaskPatch Snapshot() => this with { Pixels = Pixels.ToArray() };
}

/// <summary>Ordered edits with independent snapshots; a new edit discards the redo branch.</summary>
public sealed class MaskEditHistory
{
    private readonly List<MaskProtectionStroke> _edits = [];
    private int _position;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _edits.Count;
    private static MaskProtectionStroke Copy(MaskProtectionStroke s) => s with { Points = s.Points.Select(p => p.ToArray()).ToArray() };
    public MaskProtectionStroke[] Snapshot() => _edits.Take(_position).Select(Copy).ToArray();
    public void Add(MaskProtectionStroke stroke)
    {
        _edits.RemoveRange(_position, _edits.Count - _position);
        _edits.Add(Copy(stroke)); _position++;
    }
    public void Undo() { if (CanUndo) _position--; }
    public void Redo() { if (CanRedo) _position++; }
    public void Clear() { _edits.Clear(); _position = 0; }
}
