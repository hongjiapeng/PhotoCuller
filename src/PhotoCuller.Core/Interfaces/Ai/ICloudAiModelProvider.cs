using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>Cloud AI model provider (OpenAI-compatible API / Gemini / custom HTTP).</summary>
public interface ICloudAiModelProvider : IAiModelProvider
{
    /// <summary>
    /// Analyses an image and returns a natural-language description.
    /// Uses the thumbnail rather than the original image by default.
    /// </summary>
    Task<string?> DescribeAsync(
        AiModelProfile profile,
        string imagePath,
        string? prompt = null,
        CancellationToken ct = default);
}
