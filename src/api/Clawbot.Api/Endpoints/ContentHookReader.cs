using System.Text.Json;

namespace Clawbot.Api.Endpoints;

// P5 §4.5: đọc danh sách hook + hook đang chọn từ ChainOutlineJson (ảnh chụp L2 đã lưu, camelCase).
// KHOAN DUNG: JSON null/hỏng/thiếu hooks => (rỗng, -1) để endpoint trả canRegenerate=false, KHÔNG throw.
internal static class ContentHookReader
{
    public static (IReadOnlyList<string> Hooks, int SelectedIndex) Read(string? outlineJson)
    {
        if (string.IsNullOrWhiteSpace(outlineJson))
            return (Array.Empty<string>(), -1);

        try
        {
            using var doc = JsonDocument.Parse(outlineJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (Array.Empty<string>(), -1);

            var hooks = new List<string>();
            if (root.TryGetProperty("hooks", out var hooksEl) && hooksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in hooksEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            hooks.Add(text);
                    }
                }
            }

            var selectedIndex = root.TryGetProperty("selectedHookIndex", out var idxEl)
                && idxEl.ValueKind == JsonValueKind.Number
                && idxEl.TryGetInt32(out var idx)
                    ? idx
                    : -1;

            return (hooks, selectedIndex);
        }
        catch (JsonException)
        {
            return (Array.Empty<string>(), -1);
        }
    }
}
