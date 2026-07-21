using System.Text;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Leads;

// Job-scope: không có HTTP tenant — mọi query IgnoreQueryFilters + TenantId tường minh.
// Idempotent: active (pending|approved) thì no-op LLM; pending đã có → ensure notification.
public sealed partial class LeadRevenueEstimateService(
    AppDbContext db,
    LeadRevenueEstimator estimator,
    IPiiRedactor pii,
    INotificationPublisher publisher,
    ILeadNotificationRecipientResolver recipients,
    IClock clock,
    ILogger<LeadRevenueEstimateService> logger)
{
    private const int TranscriptMaxMessages = 40;

    private readonly AppDbContext _db = db;
    private readonly LeadRevenueEstimator _estimator = estimator;
    private readonly IPiiRedactor _pii = pii;
    private readonly INotificationPublisher _publisher = publisher;
    private readonly ILeadNotificationRecipientResolver _recipients = recipients;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadRevenueEstimateService> _logger = logger;

    public async Task<string?> EstimateAndPersistAsync(
        Guid tenantId,
        Guid leadId,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || leadId == Guid.Empty)
            return null;

        var lead = await _db.Leads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId && l.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (lead is null || lead.Stage != "customer")
            return null;

        var existingPending = await _db.LeadRevenues.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId
                     && r.LeadId == leadId
                     && r.Status == LeadRevenue.StatusPending,
                ct)
            .ConfigureAwait(false);
        if (existingPending is not null)
        {
            // Commit trước, notify fail → retry chỉ ensure notification (không skip im lặng).
            await EnsurePendingNotificationAsync(tenantId, lead, existingPending, ct).ConfigureAwait(false);
            return "skipped_existing_revenue";
        }

        if (await HasActiveRevenueAsync(tenantId, leadId, ct).ConfigureAwait(false))
            return "skipped_existing_revenue";

        if (lead.ContactId is null)
            return "skipped_no_contact";

        var transcript = await BuildTranscriptAsync(tenantId, lead.ContactId.Value, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(transcript))
            return "skipped_empty_transcript";

        var estimate = await _estimator.EstimateAsync(tenantId, transcript, ct).ConfigureAwait(false);
        if (estimate is null || estimate.Amount <= 0)
            return "skipped_no_amount";

        // Race: sale có thể vừa nhập tay trong lúc LLM chạy.
        if (await HasActiveRevenueAsync(tenantId, leadId, ct).ConfigureAwait(false))
            return "skipped_existing_revenue";

        var evidence = estimate.Evidence;
        if (!string.IsNullOrWhiteSpace(evidence))
            evidence = (await _pii.RedactAsync(evidence, ct).ConfigureAwait(false)).RedactedText;

        LeadRevenue revenue;
        try
        {
            revenue = LeadRevenue.ProposeByAi(
                tenantId,
                leadId,
                estimate.Amount,
                estimate.Currency,
                evidence,
                _clock.UtcNow);
        }
        catch (ArgumentException)
        {
            return "skipped_invalid_amount";
        }

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);
        var wantAuto = tenant?.AutoApproveLeadRevenue == true;
        // Auto-approve chỉ khi evidence ground amount (chống prompt injection từ tin khách).
        var grounded = LeadRevenue.EvidenceGroundsAmount(revenue.Amount, revenue.Evidence);
        var autoApprove = wantAuto && grounded;
        if (autoApprove)
            revenue.Approve(byUserId: null, amendedAmount: null, _clock.UtcNow);

        _db.LeadRevenues.Add(revenue);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique active index: sale vừa ghi manual / job khác thắng race.
            return "skipped_existing_revenue";
        }

        if (!autoApprove)
            await EnsurePendingNotificationAsync(tenantId, lead, revenue, ct).ConfigureAwait(false);

        LogEstimated(_logger, tenantId, leadId, revenue.Amount, revenue.Status, autoApprove);
        return autoApprove ? "approved" : "pending";
    }

    private Task<bool> HasActiveRevenueAsync(Guid tenantId, Guid leadId, CancellationToken ct) =>
        _db.LeadRevenues.IgnoreQueryFilters()
            .AnyAsync(
                r => r.TenantId == tenantId
                     && r.LeadId == leadId
                     && (r.Status == LeadRevenue.StatusPending || r.Status == LeadRevenue.StatusApproved),
                ct);

    private async Task EnsurePendingNotificationAsync(
        Guid tenantId,
        Lead lead,
        LeadRevenue revenue,
        CancellationToken ct)
    {
        if (revenue.Status != LeadRevenue.StatusPending)
            return;

        var recipientId = await _recipients
            .ResolveAsync(tenantId, lead.OwnerUserId, ct)
            .ConfigureAwait(false);
        if (recipientId is null)
            return;

        // Body generic — không nhét tên/số tiền vào lock screen / Web Push.
        await _publisher.PublishAsync(new NotificationRequest(
            tenantId,
            recipientId,
            Type: "lead_revenue_pending",
            Title: "Có đề xuất doanh thu chờ duyệt",
            Severity: "info",
            Body: "AI ước tính doanh thu cho một lead đã thành khách. Mở lead để xem số tiền và duyệt.",
            Link: $"/leads/{lead.Id}"), ct).ConfigureAwait(false);
    }

    private async Task<string> BuildTranscriptAsync(Guid tenantId, Guid contactId, CancellationToken ct)
    {
        var rows = await (
            from m in _db.Messages.IgnoreQueryFilters()
            join c in _db.Conversations.IgnoreQueryFilters() on m.ConversationId equals c.Id
            where m.TenantId == tenantId
                  && c.TenantId == tenantId
                  && c.ContactId == contactId
                  && m.Content != null
                  && m.Content != ""
            orderby m.SentAt descending
            select new { m.Direction, m.SenderType, m.Content, m.SentAt }
        ).Take(TranscriptMaxMessages).ToListAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        foreach (var msg in rows.OrderBy(m => m.SentAt))
        {
            var speaker = msg.Direction == "in"
                ? "khách"
                : string.Equals(msg.SenderType, "user", StringComparison.OrdinalIgnoreCase) ? "sale" : "AI";
            sb.Append(speaker).Append(": ").AppendLine(msg.Content);
        }

        return sb.ToString();
    }

    [LoggerMessage(EventId = 5120, Level = LogLevel.Information,
        Message = "Lead revenue estimated tenant={TenantId} lead={LeadId} amount={Amount} status={Status} autoApprove={AutoApprove}")]
    private static partial void LogEstimated(
        ILogger logger, Guid tenantId, Guid leadId, decimal amount, string status, bool autoApprove);
}
