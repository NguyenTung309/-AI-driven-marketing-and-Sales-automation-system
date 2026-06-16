using Clawbot.Domain.Contacts;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Vectors;

// One-time backfill script: embeds all existing contacts into Qdrant "contacts" collection.
// Run via: dotnet run --project src/agents/Clawbot.AgentService -- backfill-contacts --tenant <id>
public static partial class ContactBackfillScript
{
    public static async Task<int> RunAsync(
        AppDbContext db,
        IContactEmbeddingSync sync,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct = default)
    {
        var contacts = await db.Contacts
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.DeletedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);

        LogBackfillStart(logger, contacts.Count, tenantId);

        var batch = 0;
        const int batchSize = 50;
        for (var i = 0; i < contacts.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var slice = contacts.Skip(i).Take(batchSize).ToList();
            await sync.BackfillAllAsync(tenantId, slice, ct).ConfigureAwait(false);
            batch++;
            LogBackfillBatch(logger, batch, Math.Min(i + batchSize, contacts.Count), contacts.Count);
        }

        LogBackfillComplete(logger, contacts.Count);
        return contacts.Count;
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Backfilling {Count} contacts for tenant {TenantId}")]
    private static partial void LogBackfillStart(ILogger logger, int count, Guid tenantId);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information, Message = "Backfill batch {Batch} done ({Processed}/{Total})")]
    private static partial void LogBackfillBatch(ILogger logger, int batch, int processed, int total);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "Backfill complete: {Count} contacts embedded")]
    private static partial void LogBackfillComplete(ILogger logger, int count);
}
