using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>
/// AI model router: selects the most suitable model provider for a given task type.
/// Supports a local-first, cloud-fallback hybrid mode.
/// </summary>
public interface IAiModelRouter
{
    /// <summary>Returns the best available model configuration for the specified task.</summary>
    Task<AiModelProfile?> ResolveAsync(string task, CancellationToken ct = default);
}
