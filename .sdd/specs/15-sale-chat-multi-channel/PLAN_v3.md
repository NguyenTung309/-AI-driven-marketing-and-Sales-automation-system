# Channel Auto-Naming Implementation Plan v3

> REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Auto-set channel name from Pancake API when admin creates channel, remove manual name input.

**Architecture:** Frontend remove "name" field from create form. Backend fetch page admin name from Pancake conversations API before inserting Inbox.

**Tech Stack:** .NET 8, EF Core, React + TanStack Query, Tailwind CSS, Pancake API.

---

## File Structure

### Modified files

| File | Change |
|---|---|
| `src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs` | POST /api/admin/inboxes: bo field name, fetch pageName tu Pancake API |
| `src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx` | Bo dong hien thi externalPageId, chi hien platform |
| `src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx` | Bo input "Ten kenh", chi giu platform + pageId + token |
| `src/frontend/clawbot-web/src/shared/api/admin.ts` | Loai bo `name` khoi CreateInboxRequest type |

---

## Tasks

### Task 1: Backend — POST /api/admin/inboxes fetch page name

**Files:**
- Modify: `src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs`

- [ ] Step 1: Add CreateInboxRequest record (bo name)

```csharp
public sealed record CreateInboxRequest(
    string Platform,
    string ExternalPageId,
    string PageAccessToken);
```

- [ ] Step 2: Add method to fetch page name from Pancake

```csharp
private static async Task<string?> FetchPageNameAsync(string pageId, string token, CancellationToken ct)
{
    using var http = new HttpClient();
    var url = $"https://pages.fm/api/public_api/v2/pages/{pageId}/conversations?page_access_token={token}&per_page=5";
    
    var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode) return null;
    
    var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    var data = JsonSerializer.Deserialize<PancakeLookupResponse>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    
    if (data?.Conversations is null) return null;
    
    foreach (var conv in data.Conversations)
    {
        if (conv.LastSentBy?.Id == pageId && !string.IsNullOrEmpty(conv.LastSentBy.Name))
            return conv.LastSentBy.Name;
        if (conv.LastSentBy?.DisplayName != null && conv.LastSentBy?.Id == pageId)
            return conv.LastSentBy.DisplayName;
    }
    
    return null;
}

private sealed record PancakeLookupResponse(IReadOnlyList<PancakeConvLookup>? Conversations);
private sealed record PancakeConvLookup(string? PageId, PancakeLookupSender? LastSentBy);
private sealed record PancakeLookupSender(string? Id, string? Name, string? DisplayName);
```

- [ ] Step 3: Update Create endpoint

```csharp
public static IEndpointRouteBuilder MapAdminInbox(this IEndpointRouteBuilder app)
{
    var grp = app.MapGroup("/api/admin")
        .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy)
        .RequirePermission("admin:inboxes");
    grp.MapPost("/inboxes", CreateInboxAsync);
    // ... existing routes ...
    return app;
}

private static async Task<IResult> CreateInboxAsync(
    CreateInboxRequest body,
    AppDbContext db,
    ITenantAccessor tenants,
    IEncryptor encryptor,
    IClock clock,
    ILogger<AdminInboxEndpoints> logger,
    CancellationToken ct)
{
    var tenant = tenants.Require();
    
    // Validate
    if (string.IsNullOrWhiteSpace(body.Platform)) return Results.BadRequest(new { error = "platform_required" });
    if (string.IsNullOrWhiteSpace(body.ExternalPageId)) return Results.BadRequest(new { error = "page_id_required" });
    if (string.IsNullOrWhiteSpace(body.PageAccessToken)) return Results.BadRequest(new { error = "token_required" });
    
    // Fetch page name from Pancake
    var pageName = await FetchPageNameAsync(body.ExternalPageId, body.PageAccessToken, ct);
    if (string.IsNullOrEmpty(pageName))
    {
        // Fallback: log warning, use placeholder
        logger.LogWarning("Could not fetch page name for {PageId}, using fallback", body.ExternalPageId);
        pageName = $"{body.Platform} OA - {body.ExternalPageId}";
    }
    
    // Create inbox
    var inbox = Inbox.Create(tenant.TenantId, pageName, body.Platform, body.ExternalPageId);
    inbox.SetAccessToken(encryptor.Encrypt(body.PageAccessToken), clock.UtcNow);
    db.Inboxes.Add(inbox);
    await db.SaveChangesAsync(ct);
    
    logger.LogInformation("Created inbox {InboxId} with name {Name} from Pancake", inbox.Id, inbox.Name);
    
    return Results.Ok(new { inbox.Id, inbox.Name, inbox.Platform, inbox.ExternalPageId, inbox.AvatarUrl });
}
```

- [ ] Step 4: Build + commit

```bash
dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj --no-restore 2>&1 | tail -10
git add src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs
git commit -m "feat(v3): POST /api/admin/inboxes auto-fetch page name from Pancake, remove name from request"
```

### Task 2: Frontend — ChannelManagementPage remove name input

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx`

- [ ] Step 1: Remove "name" state and input field

Remove:
```tsx
const [name, setName] = useState("");
```
And the corresponding input:
```tsx
<label className="block">
  <span>Ten kenh</span>
  <input type="text" value={name} onChange={e => setName(e.target.value)} />
</label>
```

Keep:
```tsx
<label className="block">
  <span>Nen tang</span>
  <select value={platform} onChange={e => setPlatform(e.target.value)}>
    <option value="zalo">Zalo</option>
    <option value="facebook">Facebook</option>
  </select>
</label>
<label className="block">
  <span>External Page ID</span>
  <input type="text" value={externalPageId} onChange={e => setExternalPageId(e.target.value)} />
</label>
<label className="block">
  <span>Page Access Token</span>
  <input type="password" value={pageAccessToken} onChange={e => setPageAccessToken(e.target.value)} />
</label>
```

- [ ] Step 2: Build FE

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -10
```

- [ ] Step 3: Commit

```bash
git add src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx
git commit -m "feat(v3): remove channel name input from ChannelManagementPage"
```

### Task 3: Frontend — ChannelCard remove externalPageId

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx`

- [ ] Step 1: Remove the externalPageId line

Remove:
```tsx
<span className="text-label-xs text-tertiary block truncate mt-0.5">{channel.platform} &middot; {channel.externalPageId}</span>
```

- [ ] Step 2: Build FE

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -10
```

- [ ] Step 3: Commit

```bash
git add src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx
git commit -m "feat(v3): remove externalPageId from ChannelCard, show only platform"
```

### Task 4: Frontend — update admin.ts types

**Files:**
- Modify: `src/frontend/clawbot-web/src/shared/api/admin.ts`

- [ ] Step 1: Remove name from CreateInboxBody

```typescript
export interface CreateInboxBody {
  platform: string;
  externalPageId: string;
  pageAccessToken: string;  // name removed
}
```

- [ ] Step 2: Build + commit

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -10
git add src/frontend/clawbot-web/src/shared/api/admin.ts
git commit -m "feat(v3): remove name from CreateInboxBody type"
```

### Task 5: Build full + deploy

- [ ] Step 1: Build full project

```bash
dotnet build src/Clawbot.sln 2>&1 | tail -10
```

- [ ] Step 2: Publish and deploy

```bash
dotnet publish src/api/Clawbot.Api/Clawbot.Api.csproj -c Release -o deploy/api-publish
docker cp deploy/api-publish/. clawbot-api:/app/
docker restart clawbot-api
```

- [ ] Step 3: Commit

```bash
git add .
git commit -m "feat(v3): channel auto-naming from Pancake API — complete"
```