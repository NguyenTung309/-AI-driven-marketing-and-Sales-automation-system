using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Clawbot.Api.Services;

/// <summary>
/// Pham vi lead ma nguoi goi duoc xem tren trang "Khach hang tiem nang".
/// Unrestricted = true: thay toan bo lead cua tenant (Admin, SalesLead, ... - co quyen
/// <c>leads:read:all</c>). Unrestricted = false: chi thay lead thuoc kenh Pancake cua
/// chinh minh (InboxIds - lay tu inbox_members) hoac lead duoc gan truc tiep cho minh
/// (OwnerUserId), dung cho role Sale.
/// </summary>
public sealed record LeadScope(bool Unrestricted, Guid UserId, IReadOnlyList<Guid> InboxIds)
{
    /// <summary>Pham vi khong gioi han - dung cho Admin/SalesLead.</summary>
    public static LeadScope All { get; } = new(true, Guid.Empty, Array.Empty<Guid>());
}

public interface ILeadScopeResolver
{
    Task<LeadScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct);
}

public static class LeadScopeQueryExtensions
{
    /// <summary>
    /// Gioi han query lead theo pham vi: lead do chinh minh so huu, hoac lead co contact
    /// tung nhan tin trong kenh Pancake cua minh (lead.ContactId -> conversations.InboxId).
    /// </summary>
    public static IQueryable<Lead> ApplyLeadScope(
        this IQueryable<Lead> query,
        LeadScope scope,
        AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(db);

        if (scope.Unrestricted) return query;

        var userId = scope.UserId;
        var inboxIds = scope.InboxIds.ToList();

        return query.Where(l =>
            (userId != Guid.Empty && l.OwnerUserId == userId)
            || (l.ContactId != null && db.Conversations.Any(c =>
                c.ContactId == l.ContactId
                && c.InboxId != null
                && inboxIds.Contains(c.InboxId.Value))));
    }
}

public sealed class LeadScopeResolver : ILeadScopeResolver
{
    /// <summary>Quyen xem toan bo lead cua tenant; thieu quyen nay = chi thay lead cua kenh minh.</summary>
    public const string ReadAllPermission = "leads:read:all";

    private readonly AppDbContext _db;
    private readonly IPermissionResolver _permResolver;
    private LeadScope? _cached;

    public LeadScopeResolver(AppDbContext db, IPermissionResolver permResolver)
    {
        _db = db;
        _permResolver = permResolver;
    }

    public async Task<LeadScope> GetScopeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_cached is not null) return _cached;

        // API key mang scope truc tiep qua claim "perm" (khong co role_id de resolve).
        if (user.HasClaim("perm", ReadAllPermission))
            return _cached = LeadScope.All;

        // API key la credential do admin phat hanh: scope cua key da la bien kiem soat,
        // khong ep vao kenh cua mot nguoi dung cu the (key khong so huu inbox nao).
        if (user.FindFirstValue("api_key_id") is not null)
            return _cached = LeadScope.All;

        if (Guid.TryParse(user.FindFirstValue("role_id"), out var roleId) && roleId != Guid.Empty)
        {
            var perms = await _permResolver.GetPermissionsAsync(roleId, ct).ConfigureAwait(false);
            if (perms.Contains(ReadAllPermission))
                return _cached = LeadScope.All;
        }

        _ = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);

        var inboxIds = userId == Guid.Empty
            ? new List<Guid>()
            : await _db.InboxMembers
                .AsNoTracking()
                .Where(m => m.AgentId == userId)
                .Select(m => m.InboxId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

        return _cached = new LeadScope(false, userId, inboxIds);
    }
}
