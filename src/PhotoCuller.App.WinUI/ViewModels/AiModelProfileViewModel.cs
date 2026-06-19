using CommunityToolkit.Mvvm.ComponentModel;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.App.WinUI.ViewModels;

/// <summary>
/// Editable view-model wrapper for AiModelProfile.
/// PlainApiKey holds the user-typed key before encryption;
/// it is cleared after SaveSelected writes the encrypted form.
/// </summary>
public sealed partial class AiModelProfileViewModel : ObservableObject
{
    public AiModelProfile Model { get; }

    public AiModelProfileViewModel(AiModelProfile model)
    {
        Model = model;
        // Initialise editable fields from model
        Name           = model.Name;
        ProviderType   = model.ProviderType;
        ModelType      = model.ModelType;
        BaseUrl        = model.BaseUrl ?? string.Empty;
        ModelId        = model.ModelId ?? string.Empty;
        LocalModelPath = model.LocalModelPath ?? string.Empty;
        IsEnabled      = model.IsEnabled;
        TimeoutSeconds = model.TimeoutSeconds;
        MaxConcurrency = model.MaxConcurrency;
        DailyLimit     = model.DailyLimit;
    }

    // ── Editable properties ───────────────────────────────────────────────────

#pragma warning disable MVVMTK0045
    [ObservableProperty] private string       _name           = string.Empty;
    [ObservableProperty] private ProviderType _providerType;
    [ObservableProperty] private ModelType    _modelType;
    [ObservableProperty] private string       _baseUrl        = string.Empty;
    [ObservableProperty] private string       _modelId        = string.Empty;
    [ObservableProperty] private string       _localModelPath = string.Empty;
    [ObservableProperty] private bool         _isEnabled      = true;
    [ObservableProperty] private int          _timeoutSeconds = 30;
    [ObservableProperty] private int          _maxConcurrency = 2;
    [ObservableProperty] private int          _dailyLimit;

    /// <summary>
    /// Plain-text API key entered by the user.
    /// Null = user has not changed the key this session.
    /// </summary>
    [ObservableProperty] private string? _plainApiKey;
#pragma warning restore MVVMTK0045

    public bool HasPlainApiKey =>
        !string.IsNullOrEmpty(PlainApiKey);

    public bool IsCloud =>
        ProviderType != ProviderType.Local;

    public bool IsLocalModel =>
        ProviderType == ProviderType.Local;

    /// <summary>Returns "••••••••" if an encrypted key exists, else "—".</summary>
    public string ApiKeyMask =>
        string.IsNullOrEmpty(Model.EncryptedApiKey) ? "—" : "••••••••";

    public string ProviderLabel => ProviderType switch
    {
        ProviderType.Local            => "本地模型",
        ProviderType.OpenAICompatible => "OpenAI 兼容",
        ProviderType.Gemini           => "Google Gemini",
        ProviderType.CustomHttp       => "自定义 HTTP",
        _                             => ProviderType.ToString(),
    };

    // ── Write-back ────────────────────────────────────────────────────────────

    public void ApplyToModel()
    {
        Model.Name           = Name;
        Model.ProviderType   = ProviderType;
        Model.ModelType      = ModelType;
        Model.BaseUrl        = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl;
        Model.ModelId        = string.IsNullOrWhiteSpace(ModelId) ? null : ModelId;
        Model.LocalModelPath = string.IsNullOrWhiteSpace(LocalModelPath) ? null : LocalModelPath;
        Model.IsEnabled      = IsEnabled;
        Model.TimeoutSeconds = TimeoutSeconds;
        Model.MaxConcurrency = MaxConcurrency;
        Model.DailyLimit     = DailyLimit;
        Model.UpdatedTime    = DateTime.UtcNow;
    }
}
