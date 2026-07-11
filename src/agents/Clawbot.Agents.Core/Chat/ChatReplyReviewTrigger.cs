using System.Text.RegularExpressions;

namespace Clawbot.Agents.Core.Chat;

// Review-gate P2, tầng 1 của thiết kế tiered (QĐ2): quyết định reply nào cần LLM critic chấm lại.
// Deterministic + miễn phí — chạy trên 100% reply; chỉ tin "nghi ngờ" mới trả thêm 1 LLM call.
public static partial class ChatReplyReviewTrigger
{
    // Nội dung rủi ro: số tiền/%, giá, học phí, cam kết, khuyến mãi — nơi bịa đặt gây thiệt hại thật.
    [GeneratedRegex(
        @"\d{3,}|%|₫|(?i:\bvnd\b|\bvnđ\b)|(?i:giá|học phí|hoc phi|cam kết|cam ket|đảm bảo|dam bao|miễn phí|mien phi|ưu đãi|uu dai|khuyến mãi|khuyen mai|giảm|giam|hoàn tiền|hoan tien|chứng chỉ|chung chi)",
        RegexOptions.CultureInvariant)]
    private static partial Regex RiskyContent();

    /// <summary>
    /// True khi reply cần LLM critic: cờ Escalate (intent lạ / KB không khớp — đã tính sẵn trong ChatAgent)
    /// hoặc nội dung chứa giá/số liệu/cam kết.
    /// </summary>
    public static bool NeedsLlmReview(ChatAgentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Blocked || string.IsNullOrWhiteSpace(reply.Text)) return false;
        return reply.Escalate || RiskyContent().IsMatch(reply.Text);
    }
}
