using System.Globalization;
using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class DocumentDeliveryService(
    AppDbContext db,
    IEmailSender email,
    IEnumerable<IChannelAdapter> channels,
    IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly IEmailSender _email = email;
    private readonly IReadOnlyList<IChannelAdapter> _channels = channels.ToArray();
    private readonly IClock _clock = clock;

    public async Task<bool> TrySendAsync(Guid documentId, string? sentVia, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sentVia))
            return false;

        if (string.Equals(sentVia, "email", StringComparison.OrdinalIgnoreCase))
            return await TrySendByEmailAsync(documentId, ct).ConfigureAwait(false);

        if (string.Equals(sentVia, "zalo", StringComparison.OrdinalIgnoreCase))
            return await TrySendByZaloAsync(documentId, ct).ConfigureAwait(false);

        return false;
    }

    private async Task<bool> TrySendByEmailAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await _db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct).ConfigureAwait(false);
        if (doc?.ContactId is null) return false;

        var recipient = await _db.Contacts
            .Where(c => c.Id == doc.ContactId)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(recipient)) return false;

        await _email.SendAsync(recipient, "Tài liệu từ Học Bá", BuildMessage(doc.FileUrl, FormatExpiry(doc.ExpiresAt)), ct)
            .ConfigureAwait(false);

        doc.MarkSent("email", _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TrySendByZaloAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await _db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct).ConfigureAwait(false);
        if (doc?.ContactId is null) return false;

        var conversations = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == doc.TenantId
                && c.ContactId == doc.ContactId.Value
                && c.DeletedAt == null
                && c.Platform == "zalo")
            .Select(c => new { c.ExternalThreadId, c.LastMessageAt, c.CreatedAt })
            .ToListAsync(ct).ConfigureAwait(false);

        var threadId = conversations
            .Where(c => !string.IsNullOrWhiteSpace(c.ExternalThreadId))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => c.ExternalThreadId)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(threadId)) return false;

        var adapter = _channels.FirstOrDefault(c =>
            string.Equals(c.Name, "pancake", StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Name, "zalo", StringComparison.OrdinalIgnoreCase));
        if (adapter is null) return false;

        try
        {
            await adapter.SendAsync(threadId, BuildMessage(doc.FileUrl, FormatExpiry(doc.ExpiresAt)), ct)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        doc.MarkSent("zalo", _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static string FormatExpiry(DateTimeOffset? expiresAt) =>
        expiresAt?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "7 ngày";

    // Review-gate P5 (QĐ6 template-approved): template tĩnh duyệt 1 lần; biến nội suy là URL nội bộ do hệ
    // thống sinh + ngày hết hạn — không có dữ liệu ngoài, nên không cần toxicity per-send. Thêm biến từ
    // dữ liệu khách/LLM thì bản render phải qua toxicity trước SendAsync (xem DripSequenceJob).
    private static string BuildMessage(string fileUrl, string expiry) =>
        $"Xin chào, tài liệu của bạn đã sẵn sàng: {fileUrl}\nLiên kết có hiệu lực đến {expiry}.";
}
