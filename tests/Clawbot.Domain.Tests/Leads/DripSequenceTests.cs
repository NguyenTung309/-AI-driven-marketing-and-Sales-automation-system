using Clawbot.Domain.Leads;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Leads;

public sealed class DripSequenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    // ── DripSequence ──────────────────────────────────────────────────

    [Fact]
    public void Create_SetsDefaults()
    {
        var seq = DripSequence.Create(TenantId, "Welcome Series", "lead_created", Now);

        seq.TenantId.Should().Be(TenantId);
        seq.Name.Should().Be("Welcome Series");
        seq.TriggerEvent.Should().Be("lead_created");
        seq.IsActive.Should().BeTrue();
        seq.Description.Should().BeNull();
        seq.Steps.Should().BeEmpty();
        seq.CreatedAt.Should().Be(Now);
        seq.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void AddStep_AppendsStepToCollection()
    {
        var seq = DripSequence.Create(TenantId, "Seq", "evt", Now);

        seq.AddStep(1, 24, "email", "Hello {{name}}");
        seq.AddStep(2, 48, "sms", "Follow up");

        seq.Steps.Should().HaveCount(2);
        seq.Steps.First().StepOrder.Should().Be(1);
        seq.Steps.First().DelayHours.Should().Be(24);
        seq.Steps.First().Channel.Should().Be("email");
        seq.Steps.First().TemplateBody.Should().Be("Hello {{name}}");
        seq.Steps.First().SequenceId.Should().Be(seq.Id);
        seq.Steps.Last().StepOrder.Should().Be(2);
    }

    // ── DripEnrollment ────────────────────────────────────────────────

    [Fact]
    public void Enroll_SetsDefaults()
    {
        var seqId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var nextSend = Now.AddHours(24);

        var enrollment = DripEnrollment.Enroll(TenantId, seqId, leadId, nextSend, Now);

        enrollment.TenantId.Should().Be(TenantId);
        enrollment.SequenceId.Should().Be(seqId);
        enrollment.LeadId.Should().Be(leadId);
        enrollment.CurrentStep.Should().Be(0);
        enrollment.NextSendAt.Should().Be(nextSend);
        enrollment.Status.Should().Be("active");
        enrollment.EnrolledAt.Should().Be(Now);
        enrollment.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Advance_UpdatesStepAndNextSend()
    {
        var enrollment = DripEnrollment.Enroll(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(24), Now);
        var nextSend = Now.AddHours(48);

        enrollment.Advance(2, nextSend);

        enrollment.CurrentStep.Should().Be(2);
        enrollment.NextSendAt.Should().Be(nextSend);
    }

    [Fact]
    public void Complete_SetsStatusAndTimestamp()
    {
        var enrollment = DripEnrollment.Enroll(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(24), Now);

        enrollment.Complete(Now.AddHours(48));

        enrollment.Status.Should().Be("completed");
        enrollment.CompletedAt.Should().Be(Now.AddHours(48));
    }

    [Fact]
    public void Cancel_SetsStatusCancelled()
    {
        var enrollment = DripEnrollment.Enroll(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(24), Now);

        enrollment.Cancel();

        enrollment.Status.Should().Be("cancelled");
        enrollment.CompletedAt.Should().BeNull();
    }
}
