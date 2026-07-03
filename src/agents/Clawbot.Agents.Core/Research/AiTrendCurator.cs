using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Research;

// "Quét" gọi agent nghiên cứu: dùng kho tri thức của tenant (keywords lấy từ KbModules) làm ngữ cảnh
// domain, để LLM lọc các chủ đề đang thịnh hành CHỈ GIỮ những chủ đề liên quan lĩnh vực + viết ý tưởng
// nội dung tiếng Việt. Không gắn LLM (hoặc lỗi/parse hỏng) → trả null để caller fallback keyword scorer.
internal sealed class AiTrendCurator(IClaudeChatClient chat, ILlmCallScope scope) : ITrendCurator
{
    private const string AgentCode = "research-agent";
    private const int MaxCandidates = 40;   // giới hạn token: chỉ gửi top-N theo traffic
    private const int MinRelevance = 40;    // 0-100: ngưỡng giữ lại
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IClaudeChatClient _chat = chat;
    private readonly ILlmCallScope _scope = scope;

    public async Task<IReadOnlyList<ScoredTrend>?> CurateAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> candidates,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            return [];

        var shortlisted = candidates
            .OrderByDescending(c => c.SourceScore)
            .Take(MaxCandidates)
            .ToList();
        var byTopic = shortlisted.ToDictionary(c => Normalize(c.Topic), c => c, StringComparer.Ordinal);

        ClaudeReply reply;
        try
        {
            using (_scope.Begin(tenantId, AgentCode))
            {
                reply = await _chat.CompleteAsync(BuildSystemPrompt(keywords), [], BuildUserMessage(shortlisted), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (LlmConfigNotConfiguredException)
        {
            return null; // chưa gắn LLM cho research-agent → fallback keyword scorer
        }

        var items = TryParse(reply.Text);
        if (items is null)
            return null; // LLM trả không đúng JSON → fallback

        var curated = new List<ScoredTrend>();
        foreach (var item in items)
        {
            if (item.Relevance < MinRelevance || string.IsNullOrWhiteSpace(item.Topic))
                continue;
            if (!byTopic.TryGetValue(Normalize(item.Topic), out var raw))
                continue; // chỉ giữ chủ đề map được về nguồn thật (giữ Source + Metric)

            var idea = string.IsNullOrWhiteSpace(item.Idea)
                ? $"Biến '{raw.Topic}' thành nội dung học tiếng Trung"
                : item.Idea.Trim();
            curated.Add(new ScoredTrend(raw.Topic, raw.Source, raw.Metric, item.Relevance, [idea]));
        }

        return curated;
    }

    private static string BuildSystemPrompt(IReadOnlyList<string> keywords)
    {
        var domain = keywords.Count == 0
            ? "dạy và học tiếng Trung (HSK, giao tiếp, luyện thi)"
            : string.Join(", ", keywords.Take(30));
        return
            "Bạn là chuyên viên nghiên cứu xu hướng nội dung cho một thương hiệu giáo dục.\n" +
            $"Lĩnh vực/kho tri thức của thương hiệu (từ khoá cốt lõi): {domain}.\n\n" +
            "Nhiệm vụ: từ danh sách chủ đề ĐANG THỊNH HÀNH được cung cấp, CHỈ chọn những chủ đề LIÊN QUAN " +
            "đến lĩnh vực của thương hiệu — hoặc có thể dùng làm góc tiếp cận nội dung hợp lý cho lĩnh vực đó. " +
            "Bỏ hết chủ đề không liên quan (thể thao, tin thời sự, giải trí... trừ khi khai thác được cho lĩnh vực).\n" +
            "Với mỗi chủ đề được giữ: chấm relevance 0-100 (mức liên quan tới lĩnh vực) và viết 1 ý tưởng nội dung " +
            "tiếng Việt ngắn gọn kết nối chủ đề đó với thương hiệu.\n\n" +
            "Chỉ trả về JSON thuần (không markdown, không giải thích) dạng mảng: " +
            "[{\"topic\":\"<chép ĐÚNG NGUYÊN VĂN chủ đề đầu vào>\",\"relevance\":<0-100>,\"idea\":\"<ý tưởng tiếng Việt>\"}]. " +
            "Nếu không có chủ đề nào liên quan, trả về [].";
    }

    private static string BuildUserMessage(IReadOnlyList<RawTrend> candidates)
    {
        var sb = new StringBuilder("Danh sách chủ đề đang thịnh hành:\n");
        foreach (var c in candidates)
            sb.Append("- ").Append(c.Topic).Append(" (nguồn: ").Append(c.Source).Append(", chỉ số: ").Append(c.Metric).AppendLine(")");
        return sb.ToString();
    }

    private static List<CuratedItem>? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var json = StripFences(text);
        var start = json.IndexOf('[');
        var end = json.LastIndexOf(']');
        if (start < 0 || end <= start)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<CuratedItem>>(json[start..(end + 1)], JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var body = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        return body.Replace("```", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static string Normalize(string topic) => topic.Trim().ToLowerInvariant();

    private sealed record CuratedItem(string Topic, double Relevance, string? Idea);
}
