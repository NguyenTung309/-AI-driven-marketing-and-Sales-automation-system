using Clawbot.Domain.Common;

namespace Clawbot.Domain.KnowledgeBase;

public sealed class KbTestCase : Entity<Guid>
{
    public Guid KbModuleId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public string ExpectedAnswer { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private KbTestCase() { }

    public static KbTestCase Create(Guid kbModuleId, string question, string expectedAnswer, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            KbModuleId = kbModuleId,
            Question = question,
            ExpectedAnswer = expectedAnswer,
            CreatedAt = createdAt,
        };
}
