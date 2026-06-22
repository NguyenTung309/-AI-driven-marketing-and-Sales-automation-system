with open('src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# CreateAsync - add admin check after tenant check
old = 'var tenant = tenants.Require();\n        var userId = CurrentUserId(user);'
new = 'var tenant = tenants.Require();\n\n        var roleNote = user.FindFirstValue(\"role_id\");\n        if (Guid.TryParse(roleNote, out var ridNote))\n        {\n            var permsNote = await permResolver.GetPermissionsAsync(ridNote, ct);\n            if (permsNote.Contains(\"admin:inboxes\"))\n                return Results.Forbid();\n        }\n\n        var userId = CurrentUserId(user);'
c = c.replace(old, new)

# UpdateAsync - same check
old = 'private static async Task<IResult> UpdateAsync(\n        Guid conversationId, Guid id, UpdateNoteRequest body,\n        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)'
new = 'private static async Task<IResult> UpdateAsync(\n        Guid conversationId, Guid id, UpdateNoteRequest body,\n        AppDbContext db, ITenantAccessor tenants,\n        ClaimsPrincipal user, IPermissionResolver permResolver, CancellationToken ct)'
c = c.replace(old, new)

# Add check in UpdateAsync body after tenant check
old = 'var tenant = tenants.Require();\n        var note = await db.ConversationNotes'
new = 'var tenant = tenants.Require();\n\n        var roleNoteUpd = user.FindFirstValue(\"role_id\");\n        if (Guid.TryParse(roleNoteUpd, out var ridNoteUpd))\n        {\n            var permsNoteUpd = await permResolver.GetPermissionsAsync(ridNoteUpd, ct);\n            if (permsNoteUpd.Contains(\"admin:inboxes\"))\n                return Results.Forbid();\n        }\n\n        var note = await db.ConversationNotes'
c = c.replace(old, new)

with open('src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs', 'w', encoding='utf-8') as f:
    f.write(c)

# Add IPermissionResolver using
with open('src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()
insert_idx = 0
for i, line in enumerate(lines):
    if line.startswith('using ') and 'Clawbot.Infrastructure' not in line:
        insert_idx = i + 1
lines.insert(insert_idx, 'using Clawbot.Infrastructure.Auth;\n')

with open('src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)
print('DONE_InboxNotesEndpoints')
