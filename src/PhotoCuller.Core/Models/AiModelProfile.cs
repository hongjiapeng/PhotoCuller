using PhotoCuller.Core.Enums;

namespace PhotoCuller.Core.Models;

/// <summary>
/// AI model configuration profile: used for both local and cloud models.
/// The API key field stores DPAPI-encrypted ciphertext; plaintext is not permitted.
/// </summary>
public class AiModelProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public ProviderType ProviderType { get; set; }

    public ModelType ModelType { get; set; }

    // ── Cloud model configuration ────────────────────────────────────
    public string? BaseUrl { get; set; }

    /// <summary>DPAPI-encrypted API key ciphertext (base64).</summary>
    public string? EncryptedApiKey { get; set; }

    public string? ModelId { get; set; }

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of concurrent requests.</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>Daily call limit (0 = unlimited).</summary>
    public int DailyLimit { get; set; }

    // ── Local model configuration ────────────────────────────────────
    public string? LocalModelPath { get; set; }

    /// <summary>Embedding vector dimension (used by embedding models).</summary>
    public int? EmbeddingDimension { get; set; }

    // ── General ──────────────────────────────────────────────────────
    public bool IsEnabled { get; set; } = true;

    /// <summary>List of tasks supported by this model; stored as a serialized JSON array.</summary>
    public List<string> SupportedTasks { get; set; } = [];

    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedTime { get; set; } = DateTime.UtcNow;
}
