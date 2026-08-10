using System.Security.Claims;
using Clawbot.Api.Services;
using Clawbot.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Clawbot.Api.Hubs;

[Authorize]
public sealed class InboxHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantId = user?.FindFirstValue("tenant_id");
        if (user is null || string.IsNullOrEmpty(tenantId))
        {
            Context.Abort();
            return;
        }

        var services = Context.GetHttpContext()?.RequestServices;
        var permissionResolver = services?.GetRequiredService<IPermissionResolver>();
        var roleIdText = user.FindFirstValue("role_id");
        var permissions = Guid.TryParse(roleIdText, out var roleId) && permissionResolver is not null
            ? new HashSet<string>(
                await permissionResolver.GetPermissionsAsync(roleId, Context.ConnectionAborted).ConfigureAwait(false),
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(user.FindAll("perm").Select(c => c.Value), StringComparer.OrdinalIgnoreCase);
        if (!permissions.Contains("conversations:read") && !permissions.Contains("admin:inboxes"))
        {
            Context.Abort();
            return;
        }

        if (Guid.TryParse(userId, out var uid))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(uid)).ConfigureAwait(false);

        var inboxResolver = services?.GetRequiredService<IUserInboxResolver>();
        if (inboxResolver is not null)
        {
            var inboxIds = await inboxResolver.GetInboxIdsAsync(user, Context.ConnectionAborted).ConfigureAwait(false);
            if (inboxIds.Count == 0)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup(tenantId)).ConfigureAwait(false);
            }
            else
            {
                foreach (var inboxId in inboxIds.Where(id => id != Guid.Empty))
                    await Groups.AddToGroupAsync(Context.ConnectionId, InboxGroup(inboxId)).ConfigureAwait(false);
            }
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userId, out var uid))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(uid)).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    public static string InboxGroup(Guid inboxId) => "inbox:" + inboxId;
    public static string AdminGroup(string tenantId) => "inbox-admin:" + tenantId;
    public static string AdminGroup(Guid tenantId) => "inbox-admin:" + tenantId;
    public static string UserGroup(Guid userId) => "user:" + userId;
}
