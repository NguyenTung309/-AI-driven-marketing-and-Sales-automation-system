using Clawbot.Domain.Leads.Events;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

public sealed partial class LeadBecameCustomerConsumer(
    AppDbContext db,
    INotificationPublisher publisher,
    ILeadNotificationRecipientResolver recipients,
    ILogger<LeadBecameCustomerConsumer> logger) : IConsumer<LeadBecameCustomer>
{
    public async Task Consume(ConsumeContext<LeadBecameCustomer> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var lead = await db.Leads.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.Id == msg.LeadId && l.TenantId == msg.TenantId,
                context.CancellationToken)
            .ConfigureAwait(false);
        if (lead is null || lead.Stage != "customer")
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
            "lead_customer",
            "Lead đã chuyển thành khách hàng",
            Severity: "info",
            Body: "Đã ghi nhận khách thanh toán. Kiểm tra doanh thu chốt đơn.",
            Link: $"/leads/{msg.LeadId}"), context.CancellationToken).ConfigureAwait(false);

        LogHandled(logger, msg.TenantId, msg.LeadId, recipientId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lead customer notification sent for tenant {TenantId}, lead {LeadId}, owner {OwnerId}")]
    private static partial void LogHandled(ILogger logger, Guid tenantId, Guid leadId, Guid? ownerId);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Lead customer notification skipped — no owner/admin for tenant {TenantId}, lead {LeadId}")]
    private static partial void LogSkippedNoRecipient(ILogger logger, Guid tenantId, Guid leadId);
}
