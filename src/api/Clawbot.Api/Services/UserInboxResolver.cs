using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Clawbot.Api.Services;

public interface IUserInboxResolver
{
    Task<List<Guid>> GetInboxIdsAsync(ClaimsPrincipal user, CancellationToken ct);
}

public sealed class UserInboxResolver : IUserInboxResolver
{
    private readonly AppDbContext _db;
    private readonly IPermissionResolver _permResolver;
    private List<Guid>? _cached;

    public UserInboxResolver(AppDbContext db, IPermissionResolver permResolver)
    {
        _db = db;
        _permResolver = permResolver;
    }

    public async Task<List<Guid>> GetInboxIdsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (_cached != null) return _cached;

        var roleIdStr = user.FindFirstValue("role_id");
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Admin: tra ve empty list = khong filter
        if (Guid.TryParse(roleIdStr, out var roleId) && roleId != Guid.Empty)
        {
            var perms = await _permResolver.GetPermissionsAsync(roleId, ct);
            if (perms.Contains("admin:inboxes"))
            {
                _cached = new List<Guid>(); // empty = admin = khong filter
                return _cached;
            }
        }

        if (!Guid.TryParse(userIdStr, out var uid))
        {
            _cached = new List<Guid> { Guid.Empty };
            return _cached;
        }

        _cached = await _db.InboxMembers
            .Where(m => m.AgentId == uid)
            .Select(m => m.InboxId)
            .ToListAsync(ct);

        if (_cached.Count == 0)
        {
            _cached = new List<Guid> { Guid.Empty };
        }

        return _cached;
    }
}