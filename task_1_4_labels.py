with open('src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs', 'r', encoding='utf-8') as f:
    c = f.read()

old = 'private static async Task<IResult> CreateAsync(\n        CreateLabelRequest body, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)'
new = 'private static async Task<IResult> CreateAsync(\n        CreateLabelRequest body, AppDbContext db, ITenantAccessor tenants,\n        ClaimsPrincipal user, IPermissionResolver permResolver, CancellationToken ct)'
c = c.replace(old, new)

old = 'var tenant = tenants.Require();\n        var exists'
new = 'var tenant = tenants.Require();\n\n        var roleLabel = user.FindFirstValue(\"role_id\");\n        if (Guid.TryParse(roleLabel, out var ridLabel))\n        {\n            var permsLabel = await permResolver.GetPermissionsAsync(ridLabel, ct);\n            if (permsLabel.Contains(\"admin:inboxes\"))\n                return Results.Forbid();\n        }\n\n        var exists'
c = c.replace(old, new)

with open('src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs', 'w', encoding='utf-8') as f:
    f.write(c)

# Add usings
with open('src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()
insert_idx = 0
for i, line in enumerate(lines):
    if line.startswith('using ') and not line.startswith('using Clawbot.Api.Auth'):
        insert_idx = i + 1
new_usings = ['using System.Security.Claims;\n', 'using Clawbot.Infrastructure.Auth;\n']
for ns in reversed(new_usings):
    lines.insert(insert_idx, ns)

with open('src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs', 'w', encoding='utf-8') as f:
    f.writelines(lines)
print('DONE_LabelsEndpoints')
