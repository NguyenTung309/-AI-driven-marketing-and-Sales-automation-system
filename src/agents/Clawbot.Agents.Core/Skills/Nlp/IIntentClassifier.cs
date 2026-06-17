namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record IntentResult(string Label, float Confidence);

public interface IIntentClassifier : ISkill
{
    Task<IntentResult> ClassifyAsync(string text, string? locale, CancellationToken ct);
}

// Baseline heuristic. Vendor swap target: vinai/phobert-base-v2 via HF Inference or ONNX.
public sealed class KeywordIntentClassifier : IIntentClassifier
{
    private static readonly (string Label, string[] Keywords)[] Rules =
    {
        // Chat-2: strong purchase/closing signals rank first so they win over a mere price question.
        ("purchase_intent", new[] { "mua", "chốt", "thanh toán", "chuyển khoản", "đăng ký luôn", "lấy gói", "order", "buy", "purchase", "购买", "支付", "下单" }),
        ("ask_price",    new[] { "giá", "học phí", "bao nhiêu", "phí", "price", "cost", "学费" }),
        ("ask_schedule", new[] { "lịch", "giờ", "khi nào", "schedule", "时间" }),
        ("book_trial",   new[] { "đăng ký", "thử", "trial", "register", "book", "报名" }),
        ("complaint",    new[] { "tệ", "kém", "không hài lòng", "complaint", "phàn nàn", "差" }),
        ("escalation",   new[] { "gặp người", "nói chuyện thật", "human", "không phải bot", "真人" }),
        ("greeting",     new[] { "xin chào", "chào", "hello", "hi", "你好" }),
    };

    public string Name => "intent-classification";

    public Task<IntentResult> ClassifyAsync(string text, string? locale, CancellationToken ct)
    {
        _ = locale;
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new IntentResult("unknown", 0f));

        var lower = text.ToUpperInvariant();
        foreach (var (label, keywords) in Rules)
        {
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw.ToUpperInvariant(), StringComparison.Ordinal))
                    return Task.FromResult(new IntentResult(label, 0.55f));
            }
        }
        return Task.FromResult(new IntentResult("other", 0.30f));
    }
}
