using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace Clawbot.Api.Services;

public sealed class ConversationExportService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<ConversationExportResult?> ExportCsvAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Id == conversationId && c.DeletedAt == null)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (conversation is null)
            return null;

        var rows = await _db.Messages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new
            {
                m.SentAt,
                m.Direction,
                m.SenderType,
                m.ContentType,
                m.MessageType,
                m.ParentPostId,
                m.ExternalMessageId,
                Content = m.RedactedContent ?? m.Content,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var csv = new StringBuilder();
        csv.AppendLine("sent_at,direction,sender_type,content_type,message_type,parent_post_id,external_message_id,content");
        foreach (var row in rows)
        {
            csv.Append(Escape(row.SentAt.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.Direction)).Append(',')
                .Append(Escape(row.SenderType)).Append(',')
                .Append(Escape(row.ContentType)).Append(',')
                .Append(Escape(row.MessageType)).Append(',')
                .Append(Escape(row.ParentPostId)).Append(',')
                .Append(Escape(row.ExternalMessageId)).Append(',')
                .Append(Escape(row.Content))
                .AppendLine();
        }

        return new ConversationExportResult($"conversation-{conversationId:N}.csv", csv.ToString());
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}

public sealed record ConversationExportResult(string FileName, string Content);
