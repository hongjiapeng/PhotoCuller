namespace PhotoCuller.Core.Enums;

/// <summary>AI model capability type.</summary>
public enum ModelType
{
    /// <summary>Basic algorithm (no AI required).</summary>
    Basic = 0,

    /// <summary>Vision-language model (VLM; analyses image content and generates text).</summary>
    VisionLanguage = 1,

    /// <summary>Image embedding model (CLIP, etc.).</summary>
    Embedding = 2,

    /// <summary>Aesthetic quality scoring model.</summary>
    Aesthetic = 3,

    /// <summary>Face quality assessment model.</summary>
    FaceQuality = 4,
}
