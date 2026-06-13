using Clawbot.Domain.Leads;
using Clawbot.Domain.Leads.Events;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Lead-3: when a lead crosses into 'warm' (30-69), auto-enroll into the tenant's default
// warm drip sequence (TriggerEvent='warm_lead'). Idempotent — skips if an active enrollment
// already exists. Runs outside an HTTP scope, so queries ignore the ambient tenant filter.
public sealed partial class LeadBecameWarmConsumer(
    AppDbContext db,
    IClock clock,
    ILogger<LeadBecameWarmConsumer> logger) : IConsumer<LeadBecameWarm>
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadBecameWarmConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<LeadBecameWarm> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        var sequence = await _db.Set<DripSequence>().IgnoreQueryFilters()
            .Where(s => s.TenantId == msg.TenantId && s.TriggerEvent == "warm_lead" && s.IsActive)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (sequence is null)
        {
            LogNoSequence(_logger, msg.TenantId, msg.LeadId);
            return;
        }

        // Idempotent: skip if this lead was ever enrolled into this sequence — covers re-warming
        // (cold->warm again) which would otherwise violate UNIQUE(sequence_id, lead_id).
        var alreadyEnrolled = await _db.Set<DripEnrollment>().IgnoreQueryFilters()
            .AnyAsync(e => e.LeadId == msg.LeadId && e.SequenceId == sequence.Id, ct)
            .ConfigureAwait(false);
        if (alreadyEnrolled) return;

        var firstStepDelay = await _db.Set<DripSequenceStep>()
            .Where(s => s.SequenceId == sequence.Id)
            .OrderBy(s => s.StepOrder)
            .Select(s => (int?)s.DelayHours)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (firstStepDelay is null) return; // sequence has no steps

        var now = _clock.UtcNow;
        var enrollment = DripEnrollment.Enroll(
            msg.TenantId, sequence.Id, msg.LeadId, now.AddHours(firstStepDelay.Value), now);
        _db.Set<DripEnrollment>().Add(enrollment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        LogEnrolled(_logger, msg.TenantId, msg.LeadId, sequence.Id);
    }

    [LoggerMessage(EventId = 9131, Level = LogLevel.Information,
        Message = "LeadBecameWarm enrolled tenant {TenantId} lead {LeadId} into drip {SequenceId}")]
    private static partial void LogEnrolled(ILogger logger, Guid tenantId, Guid leadId, Guid sequenceId);

    [LoggerMessage(EventId = 9132, Level = LogLevel.Debug,
        Message = "LeadBecameWarm: no default warm_lead drip for tenant {TenantId} (lead {LeadId}) — skipped")]
    private static partial void LogNoSequence(ILogger logger, Guid tenantId, Guid leadId);
}
