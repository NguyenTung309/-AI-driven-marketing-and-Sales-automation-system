namespace Clawbot.Agents.Core.Skills.Lead;

// Deterministic keyword baseline — always available, no LLM/config required. The Claude-backed
// classifier is preferred when an LLM config is bound; this is the fallback and the unit-test oracle.
public sealed class KeywordLeadSignalClassifier : ILeadSignalClassifier
{
    // Multi-label: one message may trip several signals (e.g. "lớp mấy người, học phí nhiêu?").
    private static readonly (string Code, string[] Keywords)[] Rules =
    {
        (LeadSignalCodes.PurchaseIntent, new[] { "đăng ký luôn", "chốt", "thanh toán", "chuyển khoản", "lấy gói", "mua", "order", "购买", "下单" }),
        (LeadSignalCodes.AskedClassSize, new[] { "sĩ số", "bao nhiêu người", "mấy người", "mấy bạn", "số lượng học viên", "lớp mấy", "班级人数", "多少人" }),
        (LeadSignalCodes.AskedSchedule, new[] { "lịch học", "lịch", "thời khoá biểu", "mấy giờ", "khi nào học", "buổi tối", "cuối tuần", "时间", "课程表" }),
        (LeadSignalCodes.AskedTeacher, new[] { "giáo viên", "giảng viên", "thầy", "cô", "ai dạy", "người bản xứ", "老师", "外教" }),
        (LeadSignalCodes.AskedCommitment, new[] { "cam kết", "đầu ra", "đảm bảo", "bao đậu", "không đậu", "hoàn tiền", "保证", "承诺" }),
        (LeadSignalCodes.AskedPrice, new[] { "học phí", "giá", "bao nhiêu tiền", "chi phí", "phí", "学费", "多少钱" }),
    };

    // Acknowledgements that should NOT count as a substantive question even if a "?" slips in.
    private static readonly string[] Acknowledgements =
    {
        "vâng", "dạ", "ok", "oke", "okay", "để em xem", "để mình xem", "để xem", "cảm ơn", "thanks", "ừ", "uh", "好的", "谢谢",
    };

    public string Name => "lead-signal-classification";

    public Task<LeadSignalResult> ClassifyAsync(string message, string? locale, CancellationToken ct = default)
    {
        _ = locale;
        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult(new LeadSignalResult(Array.Empty<string>()));

        var lower = message.ToLowerInvariant();
        var codes = new List<string>();

        foreach (var (code, keywords) in Rules)
        {
            if (keywords.Any(kw => lower.Contains(kw, StringComparison.Ordinal)))
                codes.Add(code);
        }

        // A question mark with real content (not a bare acknowledgement) is a substantive question.
        if (message.Contains('?', StringComparison.Ordinal) && !IsAcknowledgementOnly(lower))
            codes.Add(LeadSignalCodes.AskedSubstantiveQuestion);

        return Task.FromResult(new LeadSignalResult(codes.Distinct(StringComparer.Ordinal).ToList()));
    }

    private static bool IsAcknowledgementOnly(string lower)
    {
        var stripped = lower.Trim(' ', '?', '.', '!', ',', '。', '？');
        return stripped.Length <= 12 && Acknowledgements.Any(a => stripped.Contains(a, StringComparison.Ordinal));
    }
}
