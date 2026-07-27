using System.Text.Json;

namespace Clawbot.Agents.Core.Content.Chain;

// Ảnh chụp L1 (plan) + L2 (outline) đã đọc lại thành công — cả hai non-null, đủ để resume L3+L4 (P4, §4.5).
public sealed record ContentChainSnapshotData(ContentPlan Plan, ContentOutline Outline);

// Serialize/deserialize ảnh chụp L1 (plan) + L2 (outline) để lưu vào content_items (P4, §4.5).
// camelCase (JsonSerializerDefaults.Web) — cùng chuẩn với FE/ResultSummary, tránh vết PascalCase làm đọc undefined.
// Đọc là hàm KHOAN DUNG: JSON hỏng/thiếu => null, KHÔNG throw — caller rơi về chạy full chuỗi từ body (§7).
public static class ContentChainSnapshot
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Null-in => null-out: chỉ chuỗi chạy đủ mới có Plan/Outline; nhánh fallback truyền null và không lưu gì.
    public static string? SerializePlan(ContentPlan? plan) =>
        plan is null ? null : JsonSerializer.Serialize(plan, Options);

    public static string? SerializeOutline(ContentOutline? outline) =>
        outline is null ? null : JsonSerializer.Serialize(outline, Options);

    // Đọc CẢ HAI: thiếu một trong hai (hoặc JSON hỏng/không đủ cấu trúc) => null, resume không chạy được => full chuỗi.
    public static ContentChainSnapshotData? TryDeserialize(string? planJson, string? outlineJson)
    {
        var plan = TryReadPlan(planJson);
        var outline = TryReadOutline(outlineJson);
        if (plan is null || outline is null)
            return null;
        return new ContentChainSnapshotData(plan, outline);
    }

    private static ContentPlan? TryReadPlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var plan = JsonSerializer.Deserialize<ContentPlan>(json, Options);
            // Cấu trúc tối thiểu để L3 chạy được: thiếu keyMessage/cta coi như hỏng.
            if (plan is null || string.IsNullOrWhiteSpace(plan.KeyMessage) || plan.Cta is null)
                return null;
            return plan;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContentOutline? TryReadOutline(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var outline = JsonSerializer.Deserialize<ContentOutline>(json, Options);
            // Outline phải có hook để L3 dùng làm câu mở bài; rỗng coi như hỏng.
            if (outline is null || outline.Hooks is null || outline.Hooks.Count == 0)
                return null;
            return outline;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
