namespace Clawbot.Infrastructure.Learning;

public sealed class LearningOptions
{
    public const string SectionName = "Learning";

    // Trần số cụm tín hiệu xử lý mỗi tenant mỗi đêm — chốt chi phí LLM.
    public int MaxConversationsPerRun { get; init; } = 50;

    // Câu hỏi lặp >= N lần trong cửa sổ mới thành tín hiệu chưng cất.
    public int RepeatedQuestionThreshold { get; init; } = 3;
    public int RepeatedQuestionWindowDays { get; init; } = 7;

    // Cửa sổ quét tín hiệu AI-trượt / sale-trả-lời-tay (giờ).
    public int SignalWindowHours { get; init; } = 24;

    // Trần hội thoại trích memory khách mỗi lượt scan 30 phút.
    public int MaxConversationsPerScan { get; init; } = 20;
}
