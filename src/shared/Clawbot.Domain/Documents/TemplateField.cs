using System.Text.Json;

namespace Clawbot.Domain.Documents;

/// <summary>
/// Một trường điền của mẫu tài liệu — nguồn sự thật để dựng form nhập liệu cho người dùng cuối
/// (không cần biết code) và để kiểm tra trường bắt buộc trước khi sinh tài liệu.
/// </summary>
public sealed record TemplateField(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? Placeholder,
    string? Sample)
{
    // Các kiểu input mà form frontend hiểu được. Kiểu lạ sẽ được quy về "text".
    public static readonly IReadOnlySet<string> KnownTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "multiline", "number", "currency", "date" };

    public static string NormalizeType(string? type) =>
        !string.IsNullOrWhiteSpace(type) && KnownTypes.Contains(type) ? type.ToLowerInvariant() : "text";
}

/// <summary>
/// Phân giải cột <c>fields_json</c> thành danh sách <see cref="TemplateField"/>.
/// Hỗ trợ 2 định dạng: schema mới (mảng object) và schema cũ (object key→nhãn) để tương thích dữ liệu seed cũ.
/// </summary>
public static class TemplateFieldSchema
{
    // Ghi ra camelCase để khớp đúng tên thuộc tính mà Parse đọc lại (TryGetProperty phân biệt hoa thường).
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<TemplateField> Parse(string? fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson))
            return [];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(fieldsJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        return root.ValueKind switch
        {
            JsonValueKind.Array => ParseArray(root),
            JsonValueKind.Object => ParseLegacyObject(root),
            _ => [],
        };
    }

    /// <summary>Chuẩn hóa danh sách trường về JSON schema mới (mảng) để lưu vào DB.</summary>
    public static string Serialize(IReadOnlyList<TemplateField> fields) =>
        JsonSerializer.Serialize(fields, SerializeOptions);

    /// <summary>Danh sách key của các trường bắt buộc đang thiếu giá trị (rỗng/không có) trong bộ biến truyền vào.</summary>
    public static IReadOnlyList<TemplateField> MissingRequired(
        IReadOnlyList<TemplateField> fields,
        IReadOnlyDictionary<string, string> vars)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(vars);

        return fields
            .Where(f => f.Required)
            .Where(f => !vars.TryGetValue(f.Key, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static List<TemplateField> ParseArray(JsonElement array)
    {
        var fields = new List<TemplateField>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var key = GetString(item, "key");
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var label = GetString(item, "label");
            fields.Add(new TemplateField(
                Key: key.Trim(),
                Label: string.IsNullOrWhiteSpace(label) ? key.Trim() : label.Trim(),
                Type: TemplateField.NormalizeType(GetString(item, "type")),
                Required: GetBool(item, "required"),
                Placeholder: GetString(item, "placeholder"),
                Sample: GetString(item, "sample")));
        }

        return fields;
    }

    // Schema cũ: { "customer_name": "Tên khách hàng", ... } — mọi trường coi như text, không bắt buộc.
    // Giá trị ở schema cũ là mô tả/gợi ý định dạng ("dd/MM/yyyy", "Online/Offline"), KHÔNG phải dữ liệu thật,
    // nên chỉ dùng làm nhãn + placeholder; để nó thành Sample sẽ khiến nút điền mẫu nhồi chuỗi gợi ý vào tài liệu.
    private static List<TemplateField> ParseLegacyObject(JsonElement obj)
    {
        var fields = new List<TemplateField>();
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(prop.Name))
                continue;

            var hint = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString()?.Trim() : null;
            fields.Add(new TemplateField(
                Key: prop.Name.Trim(),
                Label: string.IsNullOrWhiteSpace(hint) ? prop.Name.Trim() : hint,
                Type: "text",
                Required: false,
                Placeholder: string.IsNullOrWhiteSpace(hint) ? null : hint,
                Sample: null));
        }

        return fields;
    }

    // Đọc thuộc tính không phân biệt hoa thường để chấp cả dữ liệu cũ ghi kiểu PascalCase.
    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();
}
