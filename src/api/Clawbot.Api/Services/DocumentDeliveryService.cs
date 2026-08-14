using System.Globalization;
using Clawbot.Agents.Core.Docs;
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
    IDocumentStorage storage,
    DocsStorageOptions storageOptions,
    IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly IEmailSender _email = email;
    private readonly IReadOnlyList<IChannelAdapter> _channels = channels.ToArray();
    private readonly IDocumentStorage _storage = storage;
    private readonly DocsStorageOptions _storageOptions = storageOptions;
    private readonly IClock _clock = clock;

    public Task<bool> TrySendAsync(
        Guid tenantId,
        Guid documentId,
        string? sentVia,
        CancellationToken ct = default) =>
        TrySendAsync(tenantId, documentId, sentVia, recipientEmail: null, ct);

    public async Task<bool> TrySendAsync(
        Guid tenantId,
        Guid documentId,
        string? sentVia,
        string? recipientEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sentVia))
            return false;

        if (string.Equals(sentVia, "email", StringComparison.OrdinalIgnoreCase))
            return await TrySendByEmailAsync(tenantId, documentId, recipientEmail, ct).ConfigureAwait(false);

        if (string.Equals(sentVia, "zalo", StringComparison.OrdinalIgnoreCase))
            return await TrySendByZaloAsync(tenantId, documentId, ct).ConfigureAwait(false);

        return false;
    }

    private async Task<bool> TrySendByEmailAsync(
        Guid tenantId,
        Guid documentId,
        string? recipientEmail,
        CancellationToken ct)
    {
        var doc = await _db.GeneratedDocuments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (doc is null) return false;

        // Email gõ tay thắng: sale thường gửi báo giá cho người chưa có hồ sơ trong CRM. Không có
        // email tay thì mới tra ngược contact như cũ.
        if (!DocumentDeliveryTargetValidator.TryNormalizeEmail(
                recipientEmail,
                out var recipient))
        {
            return false;
        }

        if (recipient is null)
        {
            if (doc.ContactId is null) return false;
            var contactEmail = await _db.Contacts.IgnoreQueryFilters()
                .Where(c => c.Id == doc.ContactId && c.TenantId == tenantId)
                .Select(c => c.Email)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (!DocumentDeliveryTargetValidator.TryNormalizeEmail(
                    contactEmail,
                    out recipient) ||
                recipient is null)
            {
                return false;
            }
        }

        // FileUrl tuyệt đối (presigned MinIO) thì người nhận click link được ngay. Ngược lại đó là
        // key nội bộ dạng "/generated-docs/..." không thể mở trực tiếp → đính kèm file vào mail để
        // người nhận luôn xem được mà không phụ thuộc public base URL của deployment.
        var attachments = Array.Empty<EmailAttachment>();
        string body;
        if (Uri.TryCreate(doc.FileUrl, UriKind.Absolute, out _)
            && (doc.FileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || doc.FileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            body = BuildLinkMessage(doc.FileUrl, FormatExpiry(doc.ExpiresAt));
        }
        else
        {
            try
            {
                var storageKey = ResolveStorageKey(doc.FileUrl, _storageOptions.PublicBaseUrl);
                var bytes = await _storage.ReadAsync(storageKey, ct).ConfigureAwait(false);
                var fileName = Path.GetFileName(storageKey);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"document-{documentId}.pdf";

                attachments = [new EmailAttachment(fileName, bytes, "application/pdf")];
                body = BuildAttachedMessage(FormatExpiry(doc.ExpiresAt));
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Storage đã xóa file hoặc di chuyển — giữ nguyên body link cũ để không phá luồng gửi,
                // nhưng người nhận sẽ không xem được. Ghi log để ops điều tra.
                body = BuildLinkMessage(doc.FileUrl, FormatExpiry(doc.ExpiresAt));
            }
        }

        await _email.SendAsync(recipient, "Tài liệu từ Học Bá", body, attachments, ct)
            .ConfigureAwait(false);

        doc.MarkSent("email", _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // Cắt prefix PublicBaseUrl khỏi FileUrl để lấy đúng storage key lúc đọc bytes (giống DocumentsEndpoints.ResolveStorageKey).
    private static string ResolveStorageKey(string fileUrl, string publicBaseUrl)
    {
        var trimmed = (fileUrl ?? string.Empty).Trim();
        var prefix = (publicBaseUrl ?? string.Empty).TrimEnd('/') + "/";
        if (prefix.Length > 1 && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return trimmed[prefix.Length..];
        return trimmed.TrimStart('/');
    }

    private async Task<bool> TrySendByZaloAsync(Guid tenantId, Guid documentId, CancellationToken ct)
    {
        var doc = await _db.GeneratedDocuments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId, ct)
            .ConfigureAwait(false);
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
            await adapter.SendAsync(
                    doc.TenantId,
                    "zalo",
                    threadId,
                    BuildLinkMessage(doc.FileUrl, FormatExpiry(doc.ExpiresAt)),
                    ct)
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
    private static string BuildLinkMessage(string fileUrl, string expiry) =>
        $"Xin chào, tài liệu của bạn đã sẵn sàng: {fileUrl}\nLiên kết có hiệu lực đến {expiry}.";

    // Khi gửi email với file đính kèm, không hiển thị đường dẫn nội bộ trong body vì người nhận
    // không thể mở được. Thay vào đó thông báo rằng tài liệu nằm trong file đính kèm.
    private static string BuildAttachedMessage(string expiry) =>
        $"Xin chào, tài liệu của bạn đã được đính kèm trong email này.\nTài liệu có hiệu lực đến {expiry}.";
}
