using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Ai;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.AiProviders.CloudModels;

/// <summary>
/// Generates photo recommendation explanations using any OpenAI-compatible
/// chat endpoint (Ollama, OpenAI, DeepSeek, etc.).
///
/// Uses the first enabled VisionLanguage model in the profile repository.
/// Falls back to the algorithm-based template when no model is configured
/// or the call fails.
/// </summary>
public sealed class OpenAiExplanationService : IAiExplanationService
{
    private readonly IAiModelProfileRepository _profileRepo;
    private readonly IApiKeyProtector _keyProtector;
    private readonly ILogger<OpenAiExplanationService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAiExplanationService(
        IAiModelProfileRepository profileRepo,
        IApiKeyProtector keyProtector,
        ILogger<OpenAiExplanationService> logger)
    {
        _profileRepo  = profileRepo;
        _keyProtector = keyProtector;
        _logger       = logger;
    }

    public async Task<string> GenerateRecommendationReasonAsync(
        PhotoItem recommended,
        IReadOnlyList<PhotoItem> groupPhotos,
        CancellationToken ct = default)
    {
        // Find first enabled cloud VisionLanguage / OpenAI-compatible model
        var profiles = await _profileRepo.GetAllAsync(ct);
        var profile  = profiles.FirstOrDefault(p =>
            p.IsEnabled
            && p.ProviderType != Core.Enums.ProviderType.Local
            && !string.IsNullOrEmpty(p.BaseUrl));

        if (profile is null)
            return BuildAlgorithmicReason(recommended, groupPhotos);

        try
        {
            return await CallApiAsync(profile, recommended, groupPhotos, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AI explanation failed for {File}, falling back to algorithm",
                recommended.FileName);
            return BuildAlgorithmicReason(recommended, groupPhotos);
        }
    }

    // ── API call ──────────────────────────────────────────────────────────────

    private async Task<string> CallApiAsync(
        AiModelProfile profile,
        PhotoItem recommended,
        IReadOnlyList<PhotoItem> groupPhotos,
        CancellationToken ct)
    {
        var baseUrl   = profile.BaseUrl!.TrimEnd('/');
        var modelId   = string.IsNullOrEmpty(profile.ModelId)
                        ? "qwen2.5vl:7b" : profile.ModelId;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(profile.TimeoutSeconds) };

        // Attach API key if present
        if (!string.IsNullOrEmpty(profile.EncryptedApiKey))
        {
            try
            {
                var key = _keyProtector.Decrypt(profile.EncryptedApiKey);
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt API key for profile {Id}", profile.Id);
            }
        }

        var prompt = BuildPrompt(recommended, groupPhotos);

        var body = new ChatRequest
        {
            Model    = modelId,
            Messages =
            [
                new() { Role = "system", Content = SystemPrompt },
                new() { Role = "user",   Content = prompt },
            ],
            MaxTokens   = 300,
            Temperature = 0.3,
        };

        var response = await http.PostAsJsonAsync(
            $"{baseUrl}/chat/completions", body, JsonOpts, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOpts, ct);
        var text   = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

        return string.IsNullOrEmpty(text)
            ? BuildAlgorithmicReason(recommended, groupPhotos)
            : text;
    }

    // ── Prompt building ───────────────────────────────────────────────────────

    private const string SystemPrompt =
        "你是一位专业摄影师助理。用户会给你照片的评分数据，你需要用简洁的中文（2-4句话）" +
        "说明为什么推荐保留这张照片，以及其他照片相比之下的不足。语气自然、专业，不要重复数字罗列。";

    private static string BuildPrompt(PhotoItem rec, IReadOnlyList<PhotoItem> group)
    {
        var others = group.Where(p => p.Id != rec.Id).ToList();

        var recInfo = $"""
            推荐保留的照片：{rec.FileName}
              综合评分: {rec.OverallScore:F0}
              清晰度: {rec.SharpnessScore:F0}  曝光: {rec.ExposureScore:F0}
              对比度: {rec.ContrastScore:F0}   分辨率: {rec.ResolutionScore:F0}
              尺寸: {rec.Width}×{rec.Height}
            """;

        var othersInfo = string.Join("\n", others.Select((p, i) => $"""
              其他照片 {i + 1}：{p.FileName}
                综合评分: {p.OverallScore:F0}  清晰度: {p.SharpnessScore:F0}
                曝光: {p.ExposureScore:F0}
            """));

        return $"""
            这组照片共 {group.Count} 张，请解释为什么推荐保留第一张：

            {recInfo}

            {(others.Count > 0 ? $"同组其他照片：\n{othersInfo}" : "")}
            """;
    }

    // ── Algorithmic fallback ──────────────────────────────────────────────────

    private static string BuildAlgorithmicReason(
        PhotoItem rec, IReadOnlyList<PhotoItem> group)
    {
        var reasons = new List<string>();
        if (rec.SharpnessScore >= 80)  reasons.Add("清晰度高");
        if (rec.ExposureScore  >= 80)  reasons.Add("曝光正常");
        if (rec.ContrastScore  >= 75)  reasons.Add("对比度好");
        if (rec.ResolutionScore >= 85) reasons.Add("分辨率高");

        int count = group.Count;
        var base_ = reasons.Count > 0
            ? $"这张照片在{string.Join("、", reasons)}方面优于同组其他 {count - 1} 张照片"
            : $"在同组 {count} 张照片中综合评分最高";

        return $"{base_}，建议保留。";
    }

    // ── DTO ───────────────────────────────────────────────────────────────────

    private sealed class ChatRequest
    {
        public string  Model       { get; set; } = string.Empty;
        public List<ChatMessage> Messages { get; set; } = [];
        public int     MaxTokens   { get; set; } = 300;
        public double  Temperature { get; set; } = 0.3;
    }

    private sealed class ChatMessage
    {
        public string Role    { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }
}
