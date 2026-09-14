namespace DeepSeekHarness.Models;

/// <summary>官方 DeepSeek 模型目录(与 dsh-llm-deepseek 适配器的默认模型列表一致)。</summary>
public sealed record OfficialModel(
    string Id,
    string DisplayName,
    string Description,
    bool Vision,
    int ContextWindow);

public static class ModelCatalog
{
    public const string Provider = "deepseek-official";
    public const string ApiKeyRef = "DEEPSEEK_API_KEY";
    public const string DefaultBaseUrl = "https://api.deepseek.com";
    public const string DefaultModelId = "deepseek-flash";

    public static readonly IReadOnlyList<OfficialModel> Models = new List<OfficialModel>
    {
        new("deepseek-flash", "DeepSeek-V41-Flash", "旗舰多模态模型,支持图像输入,适合日常编码与通用任务。", true, 1_000_000),
        new("deepseek-v4-flash", "DeepSeek-V4-Flash", "高速文本模型,低延迟、高性价比。", false, 1_000_000),
        new("deepseek-v4-pro", "DeepSeek-V4-Pro", "深度推理模型,适合复杂工程与长程任务。", false, 1_000_000),
        new("deepseek-v4-flash-vision-exp", "DeepSeek-V4-Flash-Vision-Exp", "视觉实验模型,支持图像理解。", true, 1_000_000),
    };

    public static OfficialModel? Find(string id) =>
        Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
}
