using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Leads.Events;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Lead-2: when a lead crosses into 'hot', auto-assign to the least-busy sale (if unassigned)
// and push a notification to the owner. Runs outside an HTTP scope, so queries ignore the
// ambient tenant filter and match TenantId explicitly.
public sealed partial class LeadBecameHotConsumer(
    AppDbContext db,
    ILeadAssignmentService assignment,
    INotificationPublisher publisher,
    ILogger<LeadBecameHotConsumer> logger) : IConsumer<LeadBecameHot>
{
    private readonly AppDbContext _db = db;
    private readonly ILeadAssignmentService _assignment = assignment;
    private readonly INotificationPublisher _publisher = publisher;
    private readonly ILogger<LeadBecameHotConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<LeadBecameHot> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var ct = context.CancellationToken;

        var lead = await _db.Leads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == msg.LeadId && l.TenantId == msg.TenantId, ct)
            .ConfigureAwait(false);
        if (lead is null) return;

        var ownerId = lead.OwnerUserId;
        if (ownerId is null)
        {
            ownerId = await _assignment.PickOwnerAsync(msg.TenantId, ct).ConfigureAwait(false);
            if (ownerId is not null)
            {
                lead.Assign(ownerId.Value);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        await _publisher.PublishAsync(new NotificationRequest(
            msg.TenantId, ownerId, "hot_lead", "Khách hàng tiềm năng (hot)",
            Severity: "warning",
            Body: $"Lead đạt {msg.Score} điểm — liên hệ ngay.",
            Link: $"/leads/{msg.LeadId}"), ct).ConfigureAwait(false);

        LogHotHandled(_logger, msg.TenantId, msg.LeadId, ownerId);
    }

    [LoggerMessage(EventId = 9130, Level = LogLevel.Information,
        Message = "LeadBecameHot handled for tenant {TenantId} lead {LeadId} -> owner {OwnerId}")]
    private static partial void LogHotHandled(ILogger logger, Guid tenantId, Guid leadId, Guid? ownerId);
}
