using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class DocumentOpenReceiptService(AppDbContext db, IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<bool> RecordOpenAsync(Guid documentId, CancellationToken ct = default)
    {
        if (documentId == Guid.Empty)
            return false;

        var doc = await _db.GeneratedDocuments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            .ConfigureAwait(false);
        if (doc is null)
            return false;

        doc.MarkOpened(_clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
