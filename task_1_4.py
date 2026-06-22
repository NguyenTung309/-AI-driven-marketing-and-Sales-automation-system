with open('src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Inject admin check after the existing Forbid check in SendOutboundAsync
old = '            return Results.Forbid();\n\n        try { await safety.EnsureAllowedAsync'
new = '''            return Results.Forbid();

        var roleIdStr = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleIdStr, out var adminRoleId))
        {
            var adminPerms = await permResolver.GetPermissionsAsync(adminRoleId, ct);
            if (adminPerms.Contains("admin:inboxes"))
            {
                var adminUid = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var isMember = await db.InboxMembers
                    .AnyAsync(m => m.AgentId == adminUid && m.InboxId == conv.InboxId, ct);
                if (!isMember)
                    return Results.Forbid();
            }
        }

        try { await safety.EnsureAllowedAsync'''

content = content.replace(old, new)

with open('src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print('DONE_InboxEndpoints')
