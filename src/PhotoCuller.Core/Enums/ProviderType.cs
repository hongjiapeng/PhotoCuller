namespace PhotoCuller.Core.Enums;

/// <summary>AI model provider type.</summary>
public enum ProviderType
{
    /// <summary>Local model (ONNX / local executable).</summary>
    Local = 0,

    /// <summary>OpenAI-compatible API (OpenAI, DeepSeek, local Ollama, etc.).</summary>
    OpenAICompatible = 1,

    /// <summary>Google Gemini.</summary>
    Gemini = 2,

    /// <summary>Custom HTTP endpoint.</summary>
    CustomHttp = 3,
}
