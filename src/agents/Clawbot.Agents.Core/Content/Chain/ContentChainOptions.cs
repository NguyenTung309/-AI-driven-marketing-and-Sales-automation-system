namespace Clawbot.Agents.Core.Content.Chain;

// Giới hạn độ dài thân bài L3 theo nền tảng (đơn vị: ký tự).
public sealed record ContentChainLimits(int Min, int Max);

// Cấu hình chuỗi. Mặc định TẮT — bật dần theo tenant qua allow-list (§7, §5.D).
// Nạp phẳng từ section "Content:Chain" trong ContentModule.LoadChainOptions.
public sealed class ContentChainOptions
{
    public const string SectionName = "Content:Chain";
    public const string DefaultKey = "_default";

    public bool Enabled { get; set; }

    // Danh sách tenant id được bật. Rỗng + Enabled=true => bật cho mọi tenant.
    public IReadOnlyList<Guid> TenantAllowList { get; set; } = Array.Empty<Guid>();

    // Ghi vào trace để so sánh chất lượng khi sửa prompt (§5.D).
    public string Version { get; set; } = "2026-07-24.1";

    // Cap thời gian: mỗi step + cả chuỗi. Vượt => fallback single-shot (§6, §7).
    public int StepTimeoutSeconds { get; set; } = 15;
    public int ChainTimeoutSeconds { get; set; } = 60;

    // Số hashtag tối đa mỗi bài (G4, §4.4). IG chốt 30 (Q5); nền tảng khác lấy mặc định.
    public const int DefaultHashtagMax = 30;

    // Ghi đè giới hạn độ dài theo nền tảng; thiếu thì lấy built-in (LimitsFor).
    public IReadOnlyDictionary<string, ContentChainLimits> Limits { get; set; } =
        new Dictionary<string, ContentChainLimits>(StringComparer.OrdinalIgnoreCase);

    // Ghi đè số hashtag tối đa theo nền tảng; thiếu thì lấy built-in (HashtagMaxFor).
    public IReadOnlyDictionary<string, int> HashtagLimits { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    // Ghi đè persona/prompt từng step: Steps[stepId][platform | "_default"].
    // Thiếu thì step dùng prompt mặc định trong code + template nền tảng cũ (§5.D).
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Steps { get; set; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    // Mặc định do plan chốt (Q5) vì repo lẫn DB không có số nào; config có thể ghi đè.
    private static readonly Dictionary<string, ContentChainLimits> BuiltInLimits =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["facebook"] = new(Min: 80, Max: 63206),
            ["instagram"] = new(Min: 80, Max: 2200),
            ["zalo"] = new(Min: 40, Max: 4000),
            [DefaultKey] = new(Min: 40, Max: 5000),
        };

    // IG chốt 30 (Q5); các nền tảng khác chưa có trần cứng — lấy chung mặc định.
    private static readonly Dictionary<string, int> BuiltInHashtagMax =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["instagram"] = 30,
            [DefaultKey] = DefaultHashtagMax,
        };

    public bool IsEnabledFor(Guid tenantId) =>
        Enabled && (TenantAllowList.Count == 0 || TenantAllowList.Contains(tenantId));

    public ContentChainLimits LimitsFor(string platform)
    {
        var key = string.IsNullOrWhiteSpace(platform) ? DefaultKey : platform.Trim();
        if (Limits.TryGetValue(key, out var configured))
            return configured;
        if (BuiltInLimits.TryGetValue(key, out var builtIn))
            return builtIn;
        if (Limits.TryGetValue(DefaultKey, out var configuredDefault))
            return configuredDefault;
        return BuiltInLimits[DefaultKey];
    }

    public int HashtagMaxFor(string platform)
    {
        var key = string.IsNullOrWhiteSpace(platform) ? DefaultKey : platform.Trim();
        if (HashtagLimits.TryGetValue(key, out var configured))
            return configured;
        if (BuiltInHashtagMax.TryGetValue(key, out var builtIn))
            return builtIn;
        if (HashtagLimits.TryGetValue(DefaultKey, out var configuredDefault))
            return configuredDefault;
        return BuiltInHashtagMax[DefaultKey];
    }

    public string? PromptOverride(string stepId, string platform)
    {
        if (!Steps.TryGetValue(stepId, out var byPlatform))
            return null;

        if (!string.IsNullOrWhiteSpace(platform)
            && byPlatform.TryGetValue(platform.Trim(), out var perPlatform)
            && !string.IsNullOrWhiteSpace(perPlatform))
        {
            return perPlatform;
        }

        return byPlatform.TryGetValue(DefaultKey, out var fallback) && !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : null;
    }
}
