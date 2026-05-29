using Clawbot.Agents.Core.Lead;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Leads;

public sealed class EfLeadDedupService(AppDbContext db) : ILeadDedupService
{
    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(DedupRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = new List<DedupCandidate>();

        if (request.ContactId.HasValue)
        {
            var sameContact = await _db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == request.TenantId && l.ContactId == request.ContactId && l.DeletedAt == null)
                .Select(l => new { l.Id, l.ContactId })
                .ToListAsync(ct).ConfigureAwait(false);
            results.AddRange(sameContact.Select(x => new DedupCandidate(x.Id, x.ContactId!.Value, "same_contact", 1.0f)));
        }

        if (!string.IsNullOrWhiteSpace(request.Phone) || !string.IsNullOrWhiteSpace(request.Email))
        {
            var matches = await _db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == request.TenantId && l.ContactId.HasValue && l.DeletedAt == null)
                .Join(_db.Contacts.IgnoreQueryFilters(),
                    l => l.ContactId, c => c.Id,
                    (l, c) => new { LeadId = l.Id, ContactId = c.Id, c.Phone, c.Email })
                .Where(x => (request.Phone != null && x.Phone == request.Phone)
                    || (request.Email != null && x.Email == request.Email))
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var m in matches)
            {
                if (results.Any(r => r.LeadId == m.LeadId)) continue;
                var reason = m.Phone == request.Phone ? "phone_match" : "email_match";
                results.Add(new DedupCandidate(m.LeadId, m.ContactId, reason, 0.9f));
            }
        }

        return results;
    }
}
