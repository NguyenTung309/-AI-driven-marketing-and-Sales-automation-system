using System.Text.Json;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Api.Services;

internal sealed record KbModuleChoice(string Code, string Name, string? Description);

internal sealed record KbClassificationVerdict(
    string? ModuleCode,
    string? NewCode,
    string? NewName,
    string? NewDescription,
    double Confidence,
    string? Reason);

// Asks the LLM to route an uploaded document into one of the tenant's KB modules
// (or propose a new module when nothing fits). Used by POST /api/kb/classify-upload.
internal sealed class KbAutoClassifyService(IClaudeChatClient claude, ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";

    // ponytail: first 3k chars is enough signal to pick a bucket; full doc still lands in the version.
    private const int MaxClassifyPromptChars = 3000;

    private const string SystemPrompt =
        "Bạn là agent phân loại tài liệu cho kho tri thức của doanh nghiệp. " +
        "Nhiệm vụ: đọc trích đoạn tài liệu và xếp nó vào nhóm tri thức phù hợp nhất, " +
        "hoặc đề xuất nhóm mới nếu không nhóm nào khớp. Chỉ trả về JSON hợp lệ, không thêm chữ nào khác.";

    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;

    public async Task<KbClassificationVerdict?> ClassifyAsync(
        Guid tenantId,
        string fileName,
        string contentMd,
        IReadOnlyList<KbModuleChoice> modules,
        CancellationToken ct)
    {
        using var _llm = _llmScope.Begin(tenantId, AgentCode);
        var excerpt = contentMd.Length > MaxClassifyPromptChars
            ? contentMd[..MaxClassifyPromptChars]
            : contentMd;

        var reply = await _claude.CompleteAsync(
            SystemPrompt, Array.Empty<ChatTurn>(), BuildPrompt(fileName, excerpt, modules), ct).ConfigureAwait(false);
        return ParseVerdict(reply.Text);
    }

    internal static string BuildPrompt(string fileName, string excerpt, IReadOnlyList<KbModuleChoice> modules)
    {
        var moduleList = modules.Count == 0
            ? "(chưa có nhóm nào — luôn đề xuất nhóm mới)"
            : string.Join('\n', modules.Select(m =>
                $"- {m.Code}: {m.Name}" + (string.IsNullOrWhiteSpace(m.Description) ? string.Empty : $" — {m.Description}")));

        return
            $"Các nhóm tri thức hiện có:\n{moduleList}\n\n" +
            $"Tài liệu cần phân loại:\nTên tệp: {fileName}\nTrích đoạn nội dung:\n{excerpt}\n\n" +
            "Trả về JSON đúng cấu trúc sau:\n" +
            "{\"moduleCode\":\"code nhóm hiện có hoặc null\"," +
            "\"newModule\":{\"code\":\"slug-thuong-khong-dau\",\"name\":\"Tên nhóm\",\"description\":\"Mô tả ngắn\"} hoặc null," +
            "\"confidence\":0.0-1.0,\"reason\":\"lý do ngắn gọn tiếng Việt\"}\n" +
            "Quy tắc: moduleCode phải khớp chính xác một code trong danh sách; " +
            "chỉ điền newModule khi moduleCode là null.";
    }

    internal static KbClassificationVerdict? ParseVerdict(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return null;

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(responseText));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var moduleCode = ReadString(root, "moduleCode");
            string? newCode = null, newName = null, newDescription = null;
            if (root.TryGetProperty("newModule", out var newModule) && newModule.ValueKind == JsonValueKind.Object)
            {
                newCode = ReadString(newModule, "code");
                newName = ReadString(newModule, "name");
                newDescription = ReadString(newModule, "description");
            }

            var confidence = root.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number
                ? Math.Clamp(conf.GetDouble(), 0d, 1d)
                : 0d;

            if (moduleCode is null && (newCode is null && newName is null)) return null;
            return new KbClassificationVerdict(moduleCode, newCode, newName, newDescription, confidence, ReadString(root, "reason"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }
}
