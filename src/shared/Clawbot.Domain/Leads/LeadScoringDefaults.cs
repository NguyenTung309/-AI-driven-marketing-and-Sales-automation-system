namespace Clawbot.Domain.Leads;

// Default lead-scoring rule weights for an education/tutoring tenant. Used by the
// seed-defaults endpoint and matched by the message classifier's event codes.
public static class LeadScoringDefaults
{
    public sealed record RuleSpec(string EventCode, int Weight, string Description);

    public static readonly IReadOnlyList<RuleSpec> Rules =
    [
        new("asked_substantive_question", 8,  "Khách đặt câu hỏi thực chất (không phải 'vâng ạ', 'để em xem')."),
        new("asked_class_size",          12, "Khách hỏi sĩ số lớp."),
        new("asked_schedule",            10, "Khách hỏi lịch học."),
        new("asked_teacher",             10, "Khách hỏi về giáo viên."),
        new("asked_commitment",          15, "Khách hỏi cam kết đầu ra."),
        new("asked_price",               9,  "Khách hỏi học phí / giá."),
        new("purchase_intent",           20, "Khách muốn đăng ký / thanh toán ngay."),
        new("fast_reply",                5,  "Khách trả lời nhanh (trong ngưỡng phản hồi)."),
    ];
}
