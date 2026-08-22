namespace Clawbot.Agents.Core.Content.Chain;

// Bối cảnh thương hiệu phải đi cùng MỌI bước của chuỗi: bước viết không thấy brief gốc, nên nếu chỉ nhồi
// vào persona của content-agent thì bài vẫn lạc khỏi danh mục khóa học (ca "Cổ Loa" khách báo).
// Thứ tự khóa cứng: guardrail nền tảng -> bối cảnh thương hiệu -> persona của bước -> hợp đồng đầu ra.
// Hợp đồng JSON luôn nằm cuối để cổng kiểm từng bước không vỡ parser.
internal static class ContentChainSystemPrompt
{
    public static string Compose(string persona, string? outputContract = null)
    {
        var body = AgentPromptPacks.BrandContext + "\n\n" + persona.Trim();
        if (!string.IsNullOrWhiteSpace(outputContract))
            body += "\n\n" + outputContract.Trim();

        return AgentPromptDefaults.Compose(body);
    }
}
