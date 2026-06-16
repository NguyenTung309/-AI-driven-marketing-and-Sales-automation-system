using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads;

public sealed class DripSequence : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string TriggerEvent { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<DripSequenceStep> _steps = new();
    public IReadOnlyCollection<DripSequenceStep> Steps => _steps.AsReadOnly();

    private DripSequence() { }

    public static DripSequence Create(Guid tenantId, string name, string triggerEvent, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            TriggerEvent = triggerEvent,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void AddStep(int order, int delayHours, string channel, string templateBody)
    {
        _steps.Add(new DripSequenceStep
        {
            Id = Guid.NewGuid(),
            SequenceId = Id,
            StepOrder = order,
            DelayHours = delayHours,
            Channel = channel,
            TemplateBody = templateBody,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }
}

public sealed class DripSequenceStep
{
    public Guid Id { get; init; }
    public Guid SequenceId { get; init; }
    public int StepOrder { get; init; }
    public int DelayHours { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string TemplateBody { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class DripEnrollment : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid SequenceId { get; private set; }
    public Guid LeadId { get; private set; }
    public int CurrentStep { get; private set; }
    public DateTimeOffset NextSendAt { get; private set; }
    public string Status { get; private set; } = "active";
    public DateTimeOffset EnrolledAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private DripEnrollment() { }

    public static DripEnrollment Enroll(Guid tenantId, Guid sequenceId, Guid leadId, DateTimeOffset nextSendAt, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SequenceId = sequenceId,
            LeadId = leadId,
            CurrentStep = 0,
            NextSendAt = nextSendAt,
            Status = "active",
            EnrolledAt = now,
        };

    public void Advance(int nextStep, DateTimeOffset nextSendAt)
    {
        CurrentStep = nextStep;
        NextSendAt = nextSendAt;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = "completed";
        CompletedAt = now;
    }

    public void Cancel()
    {
        Status = "cancelled";
    }
}
