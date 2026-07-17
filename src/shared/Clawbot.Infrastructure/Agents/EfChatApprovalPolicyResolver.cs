using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// Mirror of EfContentReviewPolicyResolver for the chat manual-approval tenant flag.
public sealed class EfChatApprovalPolicyResolver(AppDbContext db) : IChatApprovalPolicyResolver
{
    private readonly AppDbContext _db = db;

    public async Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return false;
        return await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.RequireChatReplyApproval)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;
    }

    public async Task<bool> IsReviewGateBypassedAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Tenant không tìm thấy -> false = fail-closed (gate vẫn chạy).
        if (tenantId == Guid.Empty) return false;
        return await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.SkipChatReplyReview)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;
    }
}
