using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>Local AI model provider (ONNX / local executable).</summary>
public interface ILocalAiModelProvider : IAiModelProvider
{
    /// <summary>Loads the local model (initialises on first call).</summary>
    Task LoadAsync(AiModelProfile profile, CancellationToken ct = default);

    /// <summary>Runs inference on an image file and returns a raw score (0-1).</summary>
    Task<float?> ScoreAsync(AiModelProfile profile, string filePath, CancellationToken ct = default);
}
