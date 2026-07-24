using Clawbot.Domain.Leads.Events;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

public sealed partial class LeadReactivatedConsumer(
    AppDbContext db,
    INotificationPublisher publisher,
    ILeadNotificationRecipientResolver recipients,
    ILogger<LeadReactivatedConsumer> logger) : IConsumer<LeadReactivated>
{
    public async Task Consume(ConsumeContext<LeadReactivated> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var lead = await db.Leads.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.Id == msg.LeadId && l.TenantId == msg.TenantId,
                context.CancellationToken)
            .ConfigureAwait(false);
        if (lead is null || lead.Stage == "lost")
            return;

        var recipientId = await recipients
            .ResolveAsync(msg.TenantId, lead.OwnerUserId, context.CancellationToken)
            .ConfigureAwait(false);
        if (recipientId is null)
        {
            LogSkippedNoRecipient(logger, msg.TenantId, msg.LeadId);
            return;
        }

        await publisher.PublishAsync(new NotificationRequest(
            msg.TenantId,
            recipientId,
            "lead_reactivated",
            "Khách đã quay lại",
            Severity: "warning",
            Body: "Khách đã phản hồi sau khi mất liên lạc — liên hệ ngay.",
            Link: $"/leads/{msg.LeadId}"), context.CancellationToken).ConfigureAwait(false);

        LogHandled(logger, msg.TenantId, msg.LeadId, recipientId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lead reactivated notification sent for tenant {TenantId}, lead {LeadId}, owner {OwnerId}")]
    private static partial void LogHandled(ILogger logger, Guid tenantId, Guid leadId, Guid? ownerId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Lead reactivated notification skipped — no owner/admin for tenant {TenantId}, lead {LeadId}")]
    private static partial void LogSkippedNoRecipient(ILogger logger, Guid tenantId, Guid leadId);
}
