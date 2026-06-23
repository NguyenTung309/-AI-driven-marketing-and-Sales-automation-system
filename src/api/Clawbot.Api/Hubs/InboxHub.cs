using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Clawbot.Infrastructure.Persistence;

namespace Clawbot.Api.Hubs;

[Authorize]
public sealed class InboxHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "user:"+ userId).ConfigureAwait(false);
        var tenantId = Context.User?.FindFirstValue("tenant_id");
        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "tenant:"+ tenantId).ConfigureAwait(false);
            var db = Context.GetHttpContext()?.RequestServices.GetRequiredService<AppDbContext>();
            if (db != null && Guid.TryParse(userId, out var uid))
            {
                var inboxIds = await db.InboxMembers.Where(m => m.AgentId == uid).Select(m => m.InboxId).ToListAsync();
                foreach (var inboxId in inboxIds)
                    await Groups.AddToGroupAsync(Context.ConnectionId, "inbox:"+ inboxId).ConfigureAwait(false);
            }
        }
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = Context.User?.FindFirstValue("tenant_id");
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(tenantId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "tenant:"+ tenantId).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "user:"+ userId).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
    public static string TenantGroup(string tenantId) => "tenant:"+ tenantId;
    public static string TenantGroup(Guid tenantId) => "tenant:"+ tenantId;
    public static string UserGroup(Guid userId) => "user:"+ userId;
}