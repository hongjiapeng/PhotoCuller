using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>Base interface for AI model providers.</summary>
public interface IAiModelProvider
{
    /// <summary>Tests model connectivity and returns whether the model is available.</summary>
    Task<bool> TestConnectionAsync(AiModelProfile profile, CancellationToken ct = default);
}
