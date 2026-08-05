using System.Text.Json;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public enum FailedMessageRetryOutcome
{
    Sent,
    NotFound,
    NotAvailable,
    AlreadyClaimed,
    SafetyRejected,
    ChannelFailed,
    DeliveryAmbiguous,
}

public sealed record FailedMessageRetryResult(
    FailedMessageRetryOutcome Outcome,
    Message? Message = null,
    string? ErrorMessage = null);

public sealed partial class FailedMessageRetryService(
    AppDbContext db,
    IChannelAdapter adapter,
    OutboundMessageSafetyService safety,
    IInboxNotifier notifier,
    IClock clock,
    ILogger<FailedMessageRetryService> logger)
{
    private static readonly string[] AiSenderTypes = ["agent", "ai", "bot"];
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    public async Task<FailedMessageRetryResult> RetryAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        Guid actorUserId,
        string platform,
        string externalThreadId,
        Guid? assignedTo,
        Guid? inboxId,
        CancellationToken ct = default)
    {
        var candidate = await db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.Id == messageId
                && m.ConversationId == conversationId
                && m.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (candidate is null)
            return new(FailedMessageRetryOutcome.NotFound);
        if (!IsRetryable(candidate))
            return new(FailedMessageRetryOutcome.NotAvailable);

        var claimed = await ClaimAsync(
            tenantId, conversationId, messageId, actorUserId, ct).ConfigureAwait(false);
        if (!claimed)
            return new(FailedMessageRetryOutcome.AlreadyClaimed);

        await TryNotifyStatusAsync(
            tenantId, conversationId, messageId, "pending_send", assignedTo, inboxId).ConfigureAwait(false);

        var trackedMessage = db.ChangeTracker.Entries<Message>()
            .FirstOrDefault(entry => entry.Entity.Id == messageId);
        if (trackedMessage is not null)
            trackedMessage.State = EntityState.Detached;

        Message message;
        try
        {
            message = await db.Messages
                .FirstAsync(m =>
                    m.Id == messageId
                    && m.ConversationId == conversationId
                    && m.TenantId == tenantId,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPreSendFailure(logger, ex, tenantId, conversationId, messageId);
            await TryRestoreClaimAsync(
                tenantId, conversationId, messageId, assignedTo, inboxId).ConfigureAwait(false);
            throw;
        }

        using var deliveryCts = new CancellationTokenSource(DeliveryTimeout);
        var prepared = await EnsureSafeAsync(
            message, tenantId, conversationId, assignedTo, inboxId, deliveryCts.Token).ConfigureAwait(false);
        if (prepared is not null)
            return prepared;

        return await DeliverAsync(
            message,
            tenantId,
            conversationId,
            platform,
            externalThreadId,
            assignedTo,
            inboxId,
            deliveryCts.Token)
            .ConfigureAwait(false);
    }

    private async Task<FailedMessageRetryResult?> EnsureSafeAsync(
        Message message,
        Guid tenantId,
        Guid conversationId,
        Guid? assignedTo,
        Guid? inboxId,
        CancellationToken ct)
    {
        try
        {
            await safety.EnsureAllowedAsync(message.Content, ct).ConfigureAwait(false);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            await RestoreFailedAsync(message, tenantId, conversationId, assignedTo, inboxId).ConfigureAwait(false);
            return new(FailedMessageRetryOutcome.SafetyRejected, message, ex.Message);
        }
        catch (Exception ex)
        {
            LogSafetyFailure(logger, ex, tenantId, conversationId, message.Id);
            await RestoreFailedAsync(message, tenantId, conversationId, assignedTo, inboxId).ConfigureAwait(false);
            return new(FailedMessageRetryOutcome.SafetyRejected, message, "Không thể kiểm tra an toàn tin nhắn.");
        }
    }

    private async Task<FailedMessageRetryResult> DeliverAsync(
        Message message,
        Guid tenantId,
        Guid conversationId,
        string platform,
        string externalThreadId,
        Guid? assignedTo,
        Guid? inboxId,
        CancellationToken ct)
    {
        string? externalMessageId;
        try
        {
            externalMessageId = await adapter.SendAsync(
                tenantId,
                platform,
                externalThreadId,
                message.Content,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ChannelSendRejectedException or InvalidOperationException or ArgumentException)
        {
            LogChannelFailure(logger, ex, tenantId, conversationId, message.Id);
            await RestoreFailedAsync(message, tenantId, conversationId, assignedTo, inboxId).ConfigureAwait(false);
            return new(FailedMessageRetryOutcome.ChannelFailed, message);
        }
        catch (Exception ex)
        {
            // Transport timeout/cancellation/malformed confirmation may occur after provider acceptance.
            // Keep pending_send so a later manual retry cannot duplicate an already-delivered message.
            LogAmbiguousDelivery(logger, ex, tenantId, conversationId, message.Id);
            return new(FailedMessageRetryOutcome.DeliveryAmbiguous, message);
        }

        if (externalMessageId is not null)
            message.SetExternalMessageId(externalMessageId);
        message.MarkSent();
        try
        {
            // Keep this save outside the adapter catch. If the provider accepted the message but this
            // persistence fails, the durable row remains pending_send and is not unsafe to retry.
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogReconciliationRequired(
                logger, ex, tenantId, conversationId, message.Id, externalMessageId is not null);
            throw;
        }

        await TryNotifyStatusAsync(
            tenantId, conversationId, message.Id, "sent", assignedTo, inboxId).ConfigureAwait(false);
        return new(FailedMessageRetryOutcome.Sent, message);
    }

    private async Task<bool> ClaimAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        Guid actorUserId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var claimed = await db.Messages
            .Where(m =>
                m.Id == messageId
                && m.ConversationId == conversationId
                && m.TenantId == tenantId
                && m.Direction == "out"
                && AiSenderTypes.Contains(m.SenderType)
                && m.Status == "send_failed"
                && m.ExternalMessageId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Status, "pending_send"), ct)
            .ConfigureAwait(false);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        db.AuditLogs.Add(AuditLog.Create(
            tenantId,
            actorUserId,
            "message:retry",
            "Message",
            messageId,
            clock.UtcNow,
            JsonSerializer.Serialize(new
            {
                conversationId,
                fromStatus = "send_failed",
                toStatus = "pending_send",
            })));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task RestoreFailedAsync(
        Message message,
        Guid tenantId,
        Guid conversationId,
        Guid? assignedTo,
        Guid? inboxId)
    {
        message.MarkSendFailed();
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await TryNotifyStatusAsync(
            tenantId, conversationId, message.Id, "send_failed", assignedTo, inboxId).ConfigureAwait(false);
    }

    private async Task TryRestoreClaimAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        Guid? assignedTo,
        Guid? inboxId)
    {
        try
        {
            var restored = await db.Messages
                .Where(m =>
                    m.Id == messageId
                    && m.ConversationId == conversationId
                    && m.TenantId == tenantId
                    && m.Status == "pending_send")
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(m => m.Status, "send_failed"),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (restored > 0)
            {
                await TryNotifyStatusAsync(
                    tenantId, conversationId, messageId, "send_failed", assignedTo, inboxId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogClaimRestoreFailure(logger, ex, tenantId, conversationId, messageId);
        }
    }

    private async Task TryNotifyStatusAsync(
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        string status,
        Guid? assignedTo,
        Guid? inboxId)
    {
        try
        {
            await notifier.NotifyMessageStatusAsync(
                tenantId,
                new InboxMessageStatusEvent(conversationId, messageId, status, assignedTo, inboxId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Realtime is best-effort and must never control delivery correctness.
            LogRealtimeFailure(logger, ex, tenantId, conversationId, messageId, status);
        }
    }

    private static bool IsRetryable(Message message) =>
        message.Direction == "out"
        && AiSenderTypes.Contains(message.SenderType, StringComparer.OrdinalIgnoreCase)
        && message.Status == "send_failed"
        && message.ExternalMessageId is null;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed-message retry channel send rejected tenant={TenantId} conversation={ConversationId} message={MessageId}")]
    private static partial void LogChannelFailure(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed-message retry delivery is ambiguous tenant={TenantId} conversation={ConversationId} message={MessageId}; row remains pending_send")]
    private static partial void LogAmbiguousDelivery(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed-message retry safety check failed tenant={TenantId} conversation={ConversationId} message={MessageId}")]
    private static partial void LogSafetyFailure(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed-message retry pre-send load failed tenant={TenantId} conversation={ConversationId} message={MessageId}")]
    private static partial void LogPreSendFailure(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed-message retry could not restore pre-send claim tenant={TenantId} conversation={ConversationId} message={MessageId}")]
    private static partial void LogClaimRestoreFailure(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Failed-message retry provider confirmed but terminal persistence failed tenant={TenantId} conversation={ConversationId} message={MessageId} hasExternalId={HasExternalId}")]
    private static partial void LogReconciliationRequired(
        ILogger logger,
        Exception ex,
        Guid tenantId,
        Guid conversationId,
        Guid messageId,
        bool hasExternalId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed-message retry realtime notification failed tenant={TenantId} conversation={ConversationId} message={MessageId} status={Status}")]
    private static partial void LogRealtimeFailure(
        ILogger logger, Exception ex, Guid tenantId, Guid conversationId, Guid messageId, string status);
}
