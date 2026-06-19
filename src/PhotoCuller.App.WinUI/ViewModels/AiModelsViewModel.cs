using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using System.Collections.ObjectModel;

namespace PhotoCuller.App.WinUI.ViewModels;

/// <summary>View-model for the AI model management page.</summary>
public sealed partial class AiModelsViewModel : ObservableObject
{
    private readonly IAiModelProfileRepository _repo;
    private readonly IApiKeyProtector _keyProtector;
    private readonly ILogger<AiModelsViewModel> _logger;

    public AiModelsViewModel(
        IAiModelProfileRepository repo,
        IApiKeyProtector keyProtector,
        ILogger<AiModelsViewModel> logger)
    {
        _repo         = repo;
        _keyProtector = keyProtector;
        _logger       = logger;
    }

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<AiModelProfileViewModel> LocalModels  { get; } = [];
    public ObservableCollection<AiModelProfileViewModel> CloudModels  { get; } = [];

#pragma warning disable MVVMTK0045
    [ObservableProperty] private AiModelProfileViewModel? _selectedModel;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
#pragma warning restore MVVMTK0045

    // ── Load ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var all = await _repo.GetAllAsync(ct);
            LocalModels.Clear();
            CloudModels.Clear();

            foreach (var m in all.OrderBy(m => m.Name))
            {
                var vm = new AiModelProfileViewModel(m);
                if (m.ProviderType == ProviderType.Local)
                    LocalModels.Add(vm);
                else
                    CloudModels.Add(vm);
            }
        }
        finally { IsBusy = false; }
    }

    // ── Add ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AddLocalModelAsync(CancellationToken ct = default)
    {
        var profile = new AiModelProfile
        {
            Name         = "新本地模型",
            ProviderType = ProviderType.Local,
            ModelType    = ModelType.Basic,
            IsEnabled    = true,
        };
        await _repo.AddAsync(profile, ct);
        var vm = new AiModelProfileViewModel(profile);
        LocalModels.Add(vm);
        SelectedModel = vm;
        _logger.LogInformation("Added local model {Id}", profile.Id);
    }

    [RelayCommand]
    public async Task AddCloudModelAsync(CancellationToken ct = default)
    {
        var profile = new AiModelProfile
        {
            Name         = "新云端模型",
            ProviderType = ProviderType.OpenAICompatible,
            ModelType    = ModelType.VisionLanguage,
            IsEnabled    = true,
            TimeoutSeconds = 30,
            MaxConcurrency = 2,
        };
        await _repo.AddAsync(profile, ct);
        var vm = new AiModelProfileViewModel(profile);
        CloudModels.Add(vm);
        SelectedModel = vm;
        _logger.LogInformation("Added cloud model {Id}", profile.Id);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task SaveSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedModel is null) return;
        IsBusy = true;
        try
        {
            // Encrypt API key if plain text was entered
            if (SelectedModel.HasPlainApiKey)
            {
                SelectedModel.Model.EncryptedApiKey =
                    _keyProtector.Encrypt(SelectedModel.PlainApiKey!);
                SelectedModel.PlainApiKey = null;
            }

            SelectedModel.ApplyToModel();
            await _repo.UpdateAsync(SelectedModel.Model, ct);
            StatusMessage = $"已保存：{SelectedModel.Model.Name}";
            _logger.LogInformation("Saved model {Id}", SelectedModel.Model.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
            _logger.LogError(ex, "Save model failed");
        }
        finally { IsBusy = false; }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedModel is null) return;
        IsBusy = true;
        try
        {
            await _repo.DeleteAsync(SelectedModel.Model.Id, ct);

            if (SelectedModel.Model.ProviderType == ProviderType.Local)
                LocalModels.Remove(SelectedModel);
            else
                CloudModels.Remove(SelectedModel);

            SelectedModel = null;
            StatusMessage = "已删除";
        }
        finally { IsBusy = false; }
    }

    // ── Test connection ───────────────────────────────────────────────────────

    [RelayCommand]
    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        if (SelectedModel is null) return;
        IsBusy = true;
        StatusMessage = "正在测试连接...";

        try
        {
            // Read from the ViewModel (current UI values), NOT from Model (saved values)
            var isLocal = SelectedModel.ProviderType == ProviderType.Local;
            var baseUrl = SelectedModel.BaseUrl?.Trim();

            if (!isLocal && !string.IsNullOrEmpty(baseUrl))
            {
                // Normalise: strip trailing slash, then append /models
                var modelsUrl = baseUrl.TrimEnd('/') + "/models";

                using var http = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                // Ollama & OpenAI-compatible servers return 200 on GET /v1/models
                var resp = await http.GetAsync(modelsUrl, ct);
                StatusMessage = resp.IsSuccessStatusCode
                    ? $"✓ 连接成功 ({(int)resp.StatusCode}) — {modelsUrl}"
                    : $"⚠ 服务响应 {(int)resp.StatusCode} — 检查 Base URL 是否正确";
            }
            else if (isLocal)
            {
                var modelPath = SelectedModel.LocalModelPath?.Trim();
                if (string.IsNullOrEmpty(modelPath))
                {
                    StatusMessage = "✗ 未填写模型路径";
                }
                else
                {
                    StatusMessage = File.Exists(modelPath)
                        ? $"✓ 模型文件存在：{modelPath}"
                        : $"✗ 模型文件不存在，请检查路径：{modelPath}";
                }
            }
            else
            {
                StatusMessage = "✗ Base URL 为空，请先填写后再测试";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ 连接失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
