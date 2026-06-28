hello world
new line# Agent Hub Implementation Plan

> REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Redesign sale agent inbox voi per-sale isolation, multi-conversation tabs, inline AI copilot, labels/notes, quick actions.

**Architecture:** Backend filter InboxEndpoints by InboxMembers, SignalR per-user groups, React AgentHubLayout voi CommandPalette + ComposerWithAI + SideDrawer, Label/Note entities.

**Tech Stack:** .NET 8, EF Core, SignalR, React + TanStack Query, Tailwind CSS, SQL Server.

---

## File Structure

### New files

| File | Responsibility |
|---|---|
| deploy/migrations/0022_labels_conversation_labels_notes.sql | Tao Labels, ConversationLabels, ConversationNotes |
| src/.../Domain/ChatScenarios/Label.cs | Entity Label |
| src/.../Domain/Conversations/ConversationLabel.cs | Entity ConversationLabel |
| src/.../Domain/Conversations/ConversationNote.cs | Entity ConversationNote |
| src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs | CRUD labels |
| src/api/Clawbot.Api/Endpoints/InboxLabelsEndpoints.cs | Gan/bo label cho conversation |
| src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs | CRUD notes |
| src/api/Clawbot.Api/Endpoints/CopilotEndpoints.cs | Suggest + summarize |
| src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs | Members CRUD + user list |
| src/frontend/.../features/agent-hub/AgentHubLayout.tsx | Main layout |
| src/frontend/.../features/agent-hub/ConversationTabs.tsx | Tab bar |
| src/frontend/.../features/agent-hub/TabConversation.tsx | Single tab chat area |
| src/frontend/.../features/agent-hub/ChatMessageThread.tsx | Message list |
| src/frontend/.../features/agent-hub/QuickActionBar.tsx | Quick action buttons |
| src/frontend/.../features/agent-hub/ComposerWithAI.tsx | Composer + ghost text |
| src/frontend/.../features/agent-hub/CommandPalette.tsx | Ctrl+K command modal |
| src/frontend/.../features/agent-hub/SideDrawer.tsx | Customer context drawer |
| src/frontend/.../features/agent-hub/CustomerTimeline.tsx | Timeline component |
| src/frontend/.../features/agent-hub/index.ts | Barrel export |

### Modified files

| File | Change |
|---|---|
| InboxEndpoints.cs | Filter ListAsync/GetAsync/SearchAsync by InboxMembers |
| InboxHub.cs | Them per-user groups + inbox group join on connect |
| SignalRInboxNotifier.cs | Send to inbox groups + user groups |
| AppDbContext.cs | Them DbSet Label, ConversationLabel, ConversationNote |
| Program.cs | Register new endpoint groups + services |
| ChannelManagementPage.tsx | Multi-select agent + edit mode |
| admin.ts | API functions for user list, inbox members |
| nav.ts + Sidebar.tsx | Sidebar link /system/channels |
| routes.tsx + lazyPages.tsx | Route to AgentHubLayout |

---

## Phase 1: Per-Sale Conversation Isolation

### Task 1.1: Filter InboxEndpoints by InboxMembers

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Inject ClaimsPrincipal vao ListAsync, GetAsync, SearchAsync

`csharp
private static async Task<IResult> ListAsync(
    AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user,
    IPermissionResolver permResolver,
    [FromQuery] string? status, [FromQuery] string? platform,
    [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
    CancellationToken ct = default)
`

- [ ] Step 2: Lay userId + roleId, resolve permissions, filter query

`csharp
var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
var roleId = user.FindFirstValue("role_id");
bool isAdmin = false;
if (Guid.TryParse(roleId, out var rid))
{
    var perms = await permResolver.GetPermissionsAsync(rid, ct);
    isAdmin = perms.Contains("admin:inboxes");
}
var query = db.Conversations.AsNoTracking().AsQueryable();
if (!isAdmin && Guid.TryParse(userId, out var uid))
{
    var inboxIds = await db.InboxMembers
        .Where(m => m.AgentId == uid)
        .Select(m => m.InboxId)
        .ToListAsync(ct);
    if (inboxIds.Count == 0)
        return Results.Ok(new ConversationListResponse(new List<object>(), 0, page, pageSize));
    query = query.Where(c => c.InboxId != null && inboxIds.Contains(c.InboxId.Value));
}
`

- [ ] Step 3: Ap dung filter tuong tu cho GetAsync (403 neu khong co quyen)

`csharp
if (!isAdmin && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
    return Results.Forbid();
`

- [ ] Step 4: Ap dung filter tuong tu cho SearchAsync qua InboxSearchService

- [ ] Step 5: Commit

### Task 1.2: Per-user groups in InboxHub

**Files:**
- Modify: src/api/Clawbot.Api/Hubs/InboxHub.cs
- Modify: src/api/Clawbot.Api/Hubs/SignalRInboxNotifier.cs

- [ ] Step 1: Join user:{userId} group trong OnConnectedAsync

`csharp
public override async Task OnConnectedAsync()
{
    var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!string.IsNullOrEmpty(userId))
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}").ConfigureAwait(false);
    var tenantId = Context.User?.FindFirstValue("tenant_id");
    if (!string.IsNullOrEmpty(tenantId))
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}").ConfigureAwait(false);
        var db = Context.GetHttpContext()?.RequestServices.GetRequiredService<AppDbContext>();
        if (db != null && Guid.TryParse(userId, out var uid))
        {
            var inboxIds = await db.InboxMembers.Where(m => m.AgentId == uid).Select(m => m.InboxId).ToListAsync();
            foreach (var inboxId in inboxIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"inbox:{inboxId}").ConfigureAwait(false);
        }
    }
    await base.OnConnectedAsync().ConfigureAwait(false);
}
`

- [ ] Step 2: Sua SignalRInboxNotifier gui vao user group

`csharp
public async Task NotifyMessageAsync(Guid tenantId, Guid inboxId, Guid? assignedTo, InboxMessageEvent evt, CancellationToken ct)
{
    await _hubContext.Clients.Group($"inbox:{inboxId}").SendAsync("message", evt, ct).ConfigureAwait(false);
    if (assignedTo.HasValue)
        await _hubContext.Clients.Group($"user:{assignedTo}").SendAsync("message", evt, ct).ConfigureAwait(false);
}
`

- [ ] Step 3: Commit

### Task 1.3: Admin endpoints for agent assignment

**Files:**
- Create: src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs

- [ ] Step 1: Tao GET /api/admin/users/simple

`csharp
public static IEndpointRouteBuilder MapAdminInbox(this IEndpointRouteBuilder app)
{
    var grp = app.MapGroup("/api/admin")
        .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy)
        .RequirePermission("admin:inboxes");
    grp.MapGet("/users/simple", ListSimpleUsersAsync);
    grp.MapPut("/inboxes/{id:guid}/members", UpdateMembersAsync);
    return app;
}

private static async Task<IResult> ListSimpleUsersAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
{
    var tenantId = tenants.Require().TenantId;
    var users = await db.Users.AsNoTracking()
        .Where(u => u.TenantId == tenantId)
        .Select(u => new { u.Id, u.DisplayName, u.Email })
        .ToListAsync(ct);
    return Results.Ok(users);
}
`

- [ ] Step 2: Tao PUT /api/admin/inboxes/{id}/members

`csharp
public sealed record UpdateMembersRequest(Guid[] AgentIds);

private static async Task<IResult> UpdateMembersAsync(Guid id, UpdateMembersRequest body, AppDbContext db, CancellationToken ct)
{
    var existing = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
    db.InboxMembers.RemoveRange(existing);
    foreach (var agentId in body.AgentIds)
        db.InboxMembers.Add(InboxMember.Create(id, agentId));
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}
`

- [ ] Step 3: Dang ky routes trong Program.cs

- [ ] Step 4: Commit

### Task 1.4: Channel form multi-select agent UI

**Files:**
- Modify: src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx
- Modify: src/frontend/clawbot-web/src/shared/api/admin.ts

- [ ] Step 1: Them getSimpleUserList() trong admin.ts

`	ypescript
export interface SimpleUser { readonly id: string; readonly displayName: string; readonly email: string; }
export async function getSimpleUserList(): Promise<SimpleUser[]> {
  const res = await apiClient.get<SimpleUser[]>('/api/admin/users/simple');
  return res.data;
}
`

- [ ] Step 2: Load users list trong ChannelManagementPage, state selectedAgentIds

- [ ] Step 3: Them multi-select checkbox list trong form modal

`	sx
<label className="block">
  <span className="mb-1 block text-label-caps uppercase text-secondary">Gan Sale</span>
  <div className="max-h-40 overflow-y-auto rounded border border-outline p-2">
    {users.map(u => (
      <label key={u.id} className="flex items-center gap-2 py-1 cursor-pointer hover:bg-surface-container-low rounded px-1">
        <input type="checkbox" className="size-4 accent-primary"
          checked={selectedAgentIds.includes(u.id)}
          onChange={() => setSelectedAgentIds(prev =>
            prev.includes(u.id) ? prev.filter(id => id !== u.id) : [...prev, u.id])} />
        <span className="text-body-md">{u.displayName || u.email}</span>
      </label>
    ))}
  </div>
</label>
`

- [ ] Step 4: Them edit mode (click inbox row -> open modal with data)

- [ ] Step 5: Gui selectedAgentIds khi save, them edit button trong row

- [ ] Step 6: Sidebar link /system/channels (nav.ts + Sidebar.tsx)

- [ ] Step 7: Commit

---

## Phase 2: Labels & Notes

### Task 2.1: Migration tao bang Labels, ConversationLabels, ConversationNotes

**Files:**
- Create: deploy/migrations/0022_labels_conversation_labels_notes.sql

- [ ] Step 1: Tao migration

`sql
CREATE TABLE Labels (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES Tenants(Id),
    Name NVARCHAR(128) NOT NULL,
    Color NVARCHAR(7) NOT NULL DEFAULT '#6366f1',
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    DeletedAt DATETIMEOFFSET NULL
);
CREATE UNIQUE INDEX ix_labels_tenant_name ON Labels (TenantId, Name) WHERE DeletedAt IS NULL;

CREATE TABLE ConversationLabels (
    ConversationId UNIQUEIDENTIFIER NOT NULL REFERENCES Conversations(Id),
    LabelId UNIQUEIDENTIFIER NOT NULL REFERENCES Labels(Id),
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (ConversationId, LabelId)
);

CREATE TABLE ConversationNotes (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    TenantId UNIQUEIDENTIFIER NOT NULL REFERENCES Tenants(Id),
    ConversationId UNIQUEIDENTIFIER NOT NULL REFERENCES Conversations(Id),
    CreatedByUserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id),
    Content NVARCHAR(2000) NOT NULL,
    Type NVARCHAR(32) NOT NULL DEFAULT 'private',
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX ix_notes_conv ON ConversationNotes (ConversationId);
CREATE INDEX ix_conv_labels_label ON ConversationLabels (LabelId);
`

- [ ] Step 2: Commit

### Task 2.2: Domain entities

**Files:**
- Create: src/shared/Clawbot.Domain/ChatScenarios/Label.cs
- Create: src/shared/Clawbot.Domain/Conversations/ConversationLabel.cs
- Create: src/shared/Clawbot.Domain/Conversations/ConversationNote.cs

- [ ] Step 1: Tao Label entity

`csharp
public sealed class Label : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#6366f1";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    private Label() { }
    public static Label Create(Guid tenantId, string name, string color)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Color = color, CreatedAt = DateTimeOffset.UtcNow };
}
`

- [ ] Step 2: Tao ConversationLabel entity

`csharp
public sealed class ConversationLabel
{
    public Guid ConversationId { get; private set; }
    public Guid LabelId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private ConversationLabel() { }
    public static ConversationLabel Create(Guid conversationId, Guid labelId)
        => new() { ConversationId = conversationId, LabelId = labelId, CreatedAt = DateTimeOffset.UtcNow };
}
`

- [ ] Step 3: Tao ConversationNote entity

`csharp
public sealed class ConversationNote : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string Type { get; private set; } = "private";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    private ConversationNote() { }
    public static ConversationNote Create(Guid tenantId, Guid conversationId, Guid userId, string content, string type = "private")
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, CreatedByUserId = userId, Content = content, Type = type, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
}
`

- [ ] Step 4: Commit

### Task 1.5: Backfill migration cho conversation cu

**Files:**
- Create: deploy/migrations/0023_backfill_conversation_inboxid.sql
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Viet migration backfill

`sql
-- Gan InboxId cho conversation cu dua tren ExternalThreadId + Platform
UPDATE c
SET c.InboxId = (
    SELECT TOP 1 i.Id
    FROM Inboxes i
    WHERE i.TenantId = c.TenantId
      AND i.Platform = c.Platform
      AND (i.ExternalPageId = c.ExternalThreadId
           OR c.ExternalThreadId LIKE '%' + i.ExternalPageId + '%')
)
FROM Conversations c
WHERE c.InboxId IS NULL
  AND c.DeletedAt IS NULL;

-- Neu khong tim duoc InboxId, set InboxId = default inbox cung platform
UPDATE c
SET c.InboxId = fallback.Id
FROM Conversations c
CROSS APPLY (
    SELECT TOP 1 Id FROM Inboxes
    WHERE TenantId = c.TenantId AND Platform = c.Platform AND IsActive = 1
    ORDER BY CreatedAt
) fallback
WHERE c.InboxId IS NULL AND c.DeletedAt IS NULL;
`

- [ ] Step 2: Commit

### Task 1.6: Seed permission admin:inboxes

**Files:**
- Modify: deploy/seed/04_permissions.sql (hoac migration rieng)
- Modify: src/api/Clawbot.Api/Auth/PermissionAuthorizationHandler.cs

- [ ] Step 1: Them permission code admin:inboxes trong seed script

`sql
-- Them permission admin:inboxes neu chua co
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'admin:inboxes')
BEGIN
    INSERT INTO Permissions (Id, Code, Name, [Group])
    VALUES (NEWID(), 'admin:inboxes', 'Xem tat ca inbox va conversation', 'inbox');
END;

-- Seed cho role Admin (khong seed cho role Sale)
INSERT INTO RolePermissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM Roles r CROSS JOIN Permissions p
WHERE r.Name = 'Admin' AND p.Code = 'admin:inboxes'
  AND NOT EXISTS (
      SELECT 1 FROM RolePermissions rp
      WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
  );
`

- [ ] Step 2: Viet assertion script kiem tra sau migrate

`sql
-- Log role nao co permission admin:inboxes de xac nhan truoc go-live
SELECT r.Name AS RoleName, COUNT(rp.Id) AS HasPermission
FROM Roles r
JOIN RolePermissions rp ON rp.RoleId = r.Id
JOIN Permissions p ON p.Id = rp.PermissionId AND p.Code = 'admin:inboxes'
GROUP BY r.Name;
`

- [ ] Step 3: Commit

### Task 1.7: RowVersion concurrency cho Conversation

**Files:**
- Modify: deploy/migrations/0021_alter_conversations_messages.sql (them RowVersion)
- Modify: src/shared/Clawbot.Domain/Conversations/Conversation.cs
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Them RowVersion vao migration

`sql
ALTER TABLE Conversations ADD RowVersion TIMESTAMP NOT NULL;
`

- [ ] Step 2: Them property trong Conversation entity

`csharp
public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
`

- [ ] Step 3: Cau hinh EF Core concurrency

`csharp
// Trong AppDbContext OnModelCreating
modelBuilder.Entity<Conversation>(e =>
{
    e.Property(c => c.RowVersion).IsRowVersion();
});
`

- [ ] Step 4: Kiem tra RowVersion trong update endpoints

`csharp
// Trong ResolveAsync, EscalateAsync, AssignAsync, SnoozeAsync
var conv = await db.Conversations
    .FirstOrDefaultAsync(c => c.Id == id, ct);
if (conv is null) return Results.NotFound();

// Kiem tra concurrency
var bodyRowVersion = httpCtx.GetTypedHeaders().Get<byte[]>("If-Match");
if (bodyRowVersion != null && !conv.RowVersion.SequenceEqual(bodyRowVersion))
    return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });
`

- [ ] Step 5: Commit

### Task 1.8: Assignee list scoped by InboxMembers

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Tao GET /api/inbox/conversations/{id}/assignable-agents

`csharp
grp.MapGet(\"/inbox/conversations/{id:guid}/assignable-agents\", ListAssignableAgentsAsync)
    .RequirePermission(\"conversations:write\");

private static async Task<IResult> ListAssignableAgentsAsync(
    Guid id, AppDbContext db, CancellationToken ct)
{
    var conv = await db.Conversations.AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == id, ct);
    if (conv is null || !conv.InboxId.HasValue)
        return Results.Ok(new List<object>());

    var agents = await db.InboxMembers
        .Where(m => m.InboxId == conv.InboxId.Value)
        .Join(db.Users, m => m.AgentId, u => u.Id, (m, u) => new { u.Id, u.DisplayName, u.Email })
        .ToListAsync(ct);
    return Results.Ok(agents);
}
`

- [ ] Step 2: Validate AssignAsync - chi cho assign vao InboxMembers

`csharp
private static async Task<IResult> AssignAsync(Guid id, AssignConversationRequest body,
    AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier, CancellationToken ct)
{
    var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct);
    if (conv is null) return Results.NotFound();

    if (conv.InboxId.HasValue)
    {
        var isMember = await db.InboxMembers
            .AnyAsync(m => m.InboxId == conv.InboxId.Value && m.AgentId == body.UserId, ct);
        if (!isMember)
            return Results.BadRequest(new { error = \"agent_not_in_inbox\" });
    }

    conv.Assign(body.UserId);
    await db.SaveChangesAsync(ct);
    // ... notify
}
`

- [ ] Step 3: Frontend goi assignable-agents thay vi users/simple khi assign

- [ ] Step 4: Commit

### Task 2.3: Auto-unsnooze khi co inbound message moi

**Files:**
- Modify: src/shared/Clawbot.Domain/Conversations/Conversation.cs
- Modify: src/api/Clawbot.Api/Endpoints/PublicWidgetEndpoints.cs hoac Ingestor
- Modify: src/api/Clawbot.Api/Hubs/SignalRInboxNotifier.cs

- [ ] Step 1: Them method Unsnooze trong Conversation entity

`csharp
public void Unsnooze()
{
    if (Status != "snoozed") return;
    Status = "open";
    SnoozedUntil = null;
}
`

- [ ] Step 2: Trong inbound message flow (Ingestor / PublicWidgetEndpoints), goi Unsnooze

`csharp
// Sau khi upsert conversation moi hoac tim thay conversation cu
if (conv.Status == "snoozed")
{
    conv.Unsnooze();
}
`

- [ ] Step 3: Unsnooze broadcast qua SignalR

`csharp
// Sau khi Unsnooze
if (unsnoozed)
{
    await notifier.NotifyConversationUpdatedAsync(tenantId,
        new InboxConversationEvent(conv.Id, "open", conv.AssignedTo, DateTimeOffset.UtcNow), ct);
}
`

- [ ] Step 4: Commit

### Task 2.4: User snapshot cho Note

**Files:**
- Modify: src/shared/Clawbot.Domain/Conversations/ConversationNote.cs
- Modify: src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs

- [ ] Step 1: Them field CreatedByDisplayName vao ConversationNote

`csharp
public string? CreatedByDisplayName { get; private set; }

public static ConversationNote Create(Guid tenantId, Guid conversationId, Guid userId,
    string content, string? createdByName, string type = "private")
    => new() {
        Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId,
        CreatedByUserId = userId, Content = content, Type = type,
        CreatedByDisplayName = createdByName,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
`

- [ ] Step 2: Khi tao note, luu displayName snapshot

`csharp
// Trong AddNoteAsync
var currentUser = await db.Users.FindAsync(new object[] { userId }, ct);
var displayName = currentUser?.DisplayName ?? currentUser?.Email ?? "Unknown";

var note = ConversationNote.Create(tenant.TenantId, id, userId, req.Content,
    displayName, req.Type ?? "private");
`

- [ ] Step 3: API tra ve CreatedByDisplayName thay vi join User

- [ ] Step 4: Commit

### Task 2.5: Reassign event cho sale dang mo tab

**Files:**
- Modify: src/api/Clawbot.Api/Hubs/SignalRInboxNotifier.cs
- Modify: src/shared/Clawbot.SharedKernel/Inbox/InboxConversationEvent.cs
- Modify: src/frontend/.../features/agent-hub/AgentHubLayout.tsx

- [ ] Step 1: Them event conversation:reassigned vao signalR contracts

`csharp
// Them field vao InboxConversationEvent
public sealed record InboxConversationEvent(
    Guid ConversationId,
    string Status,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt,
    Guid? PreviousAssignee = null);  // Them field nay
`

- [ ] Step 2: Trong AssignAsync, gui event toi user:{oldAssignee}

`csharp
// Sau khi assign thanh cong
if (oldAssignee.HasValue)
{
    await notifier.NotifyUserAsync(oldAssignee.Value, "conversation:reassigned",
        new { ConversationId = id }, ct);
}
`

- [ ] Step 3: Frontend lang nghe event, dong tab + read-only

`	ypescript
// Trong useInboxRealtime
connection.on("conversation:reassigned", (evt: { conversationId: string }) => {
  // Dong tab neu dang mo
  setOpenTabs(prev => prev.filter(t => t.id !== evt.conversationId));
  // Toast notification
  showToast("Conversation da duoc reassign", "warning");
});
`

- [ ] Step 4: Commit

### Task 2.6: Multi-device sync cho cung 1 sale

**Files:**
- Modify: src/api/Clawbot.Api/Hubs/SignalRInboxNotifier.cs
- Modify: src/frontend/.../shared/api/inbox.ts

- [ ] Step 1: Broadcast outbound message toi chinh user:{userId}

`csharp
// Trong SendOutboundAsync endpoint, sau khi gui message
await notifier.NotifyMessageAsync(
    tenant.TenantId,
    conv.InboxId!.Value,
    conv.AssignedTo,  // Gui toi chinh sale do
    new InboxMessageEvent(conv.Id, msg.Id, msg.Direction, msg.SenderType,
        msg.Content, msg.ContentType, msg.SentAt), ct);
`

- [ ] Step 2: Frontend lang nghe message event tu chinh minh

`	ypescript
// Trong ComposerWithAI, khi nhan duoc message tu chinh minh gui (tu tab khac)
connection.on("message", (evt: InboxMessageEvent) => {
  if (evt.senderType === "user" && evt.direction === "out") {
    // Dong bo: neu tab khac da gui, clear composer + append message
    setComposerText("");
    queryClient.invalidateQueries({ queryKey: ["inbox", "conversation", evt.conversationId] });
  }
});
`

- [ ] Step 3: Commit

### Task 4.3: RequestId cho suggest de tranh race condition

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/CopilotEndpoints.cs
- Modify: src/frontend/.../features/agent-hub/ComposerWithAI.tsx

- [ ] Step 1: Them requestId vao suggest request/response

`csharp
public sealed record CopilotSuggestRequest(string CurrentDraft, int DraftVersion);
public sealed record CopilotSuggestResponse(string? Suggestion, int DraftVersion);
`

- [ ] Step 2: Frontend tracking draftVersion

`	ypescript
const [draftVersion, setDraftVersion] = useState(0);

useEffect(() => {
  if (text.length < 3 || text.length > 200) { setGhost(''); return; }
  const currentVersion = draftVersion + 1;
  setDraftVersion(currentVersion);
  const timer = setTimeout(async () => {
    try {
      const res = await apiClient.post(/api/inbox/conversations//copilot/suggest, {
        currentDraft: text,
        draftVersion: currentVersion,
      });
      // Chi accept neu version khop
      if (res.data.draftVersion === currentVersion) {
        setGhost(res.data.suggestion || '');
      }
    } catch { /* ignore */ }
  }, 400);
  return () => clearTimeout(timer);
}, [text, draftVersion]);
`

- [ ] Step 3: Commit

### Task 4.4: PII redaction cho Copilot suggest

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/CopilotEndpoints.cs
- Modify: src/shared/Clawbot.Infrastructure/.../PIIRedactor.cs (hoac service injection)

- [ ] Step 1: Inject IPiiRedactor vao CopilotEndpoints

`csharp
private static async Task<IResult> SuggestAsync(
    Guid id, CopilotSuggestRequest req,
    AppDbContext db, IChatAgentFactory agentFactory,
    ITenantAccessor tenants, IPiiRedactor pii,
    CancellationToken ct)
`

- [ ] Step 2: Redact history truoc khi gui vao agent

`csharp
// Redact history de tranh leak PII vao model
var redactedHistory = new List<ChatTurn>();
foreach (var turn in history)
{
    var redacted = await pii.RedactAsync(turn.Content, ct);
    redactedHistory.Add(turn with { Content = redacted });
}

// Dung redactedHistory thay vi history khi goi agent
var reply = await agent.ReplyAsync(new ChatAgentRequest(
    tenant.TenantId, id, null,
    $"The sales agent is typing a response. Current draft: \"{pii.Redact(req.CurrentDraft)}\". Suggest the next part of this message.",
    redactedHistory));
`

- [ ] Step 3: Commit

### Task 4.5: Token cost quota cho Copilot

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/CopilotEndpoints.cs
- Modify: src/api/Clawbot.Api/Middleware/RateLimitingExtensions.cs

- [ ] Step 1: Them rate limit rieng cho copilot suggest

`csharp
// Trong RateLimitingExtensions
public const string CopilotPolicy = "copilot:suggest";

public static void AddCopilotRateLimiting(this IServiceCollection services)
{
    services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter(CopilotPolicy, config =>
        {
            config.PermitLimit = 30;  // 30 requests
            config.Window = TimeSpan.FromMinutes(1);  // per minute
            config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            config.QueueLimit = 5;
        });
    });
}
`

- [ ] Step 2: Ap dung rate limit cho suggest endpoint

`csharp
grp.MapPost("/suggest", SuggestAsync)
    .RequireRateLimiting(CopilotPolicy);
`

- [ ] Step 3: Them token cost tracking (su dung IClaudeCostTracker co san)

`csharp
// Trong SuggestAsync, sau khi goi agent
var currentCost = await cost.GetMonthlyCostAsync(tenant.TenantId, ct);
if (currentCost >= monthlyCap * 0.8m)
{
    // Chi suggest khi duoi 80% cap
    // Neu tren 80%: return null suggestion (bypass suggest)
    return Results.Ok(new CopilotSuggestResponse(null));
}
`

- [ ] Step 4: Commit

### Task 3.8: Pipeline stage suggestion trong SideDrawer

**Files:**
- Create: src/api/Clawbot.Api/Endpoints/PipelineEndpoints.cs
- Modify: src/frontend/.../features/agent-hub/SideDrawer.tsx

- [ ] Step 1: Tao GET /api/inbox/conversations/{id}/pipeline-stage

Tra ve pipeline stage dua tren lead score + history:
- new (score < 30): Moi tiep can => Tim hieu nhu cau
- consulting (30-69): Dang tu van => Gui bao gia
- closing (>= 70): Sap chot => Goi y upsell
- closed (>= 70 + resolved): Da chot => Gui feedback

- [ ] Step 2: Implement endpoint

`csharp
public sealed record PipelineStageResponse(string Stage, string StageLabel, int Score, string SuggestedAction);

private static async Task<IResult> GetPipelineStageAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
{
    var tenant = tenants.Require();
    var conv = await db.Conversations.AsNoTracking()
        .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(50))
        .FirstOrDefaultAsync(c => c.Id == id, ct);
    if (conv is null) return Results.NotFound();

    var lead = conv.ContactId.HasValue
        ? await db.Leads.FirstOrDefaultAsync(l => l.ContactId == conv.ContactId, ct)
        : null;
    var score = lead?.Score ?? 0;

    string stage, label, action;
    if (score >= 70 && conv.Status == "resolved")
        { stage = "closed"; label = "Da chot"; action = "Gui feedback survey, referral"; }
    else if (score >= 70)
        { stage = "closing"; label = "Sap chot"; action = "Chot ngay, goi y upsell"; }
    else if (score >= 30)
        { stage = "consulting"; label = "Dang tu van"; action = "Gui bao gia, dat lich hoc thu"; }
    else
        { stage = "new"; label = "Moi tiep can"; action = "Tim hieu nhu cau, gui brochure"; }

    return Results.Ok(new PipelineStageResponse(stage, label, score, action));
}
`

- [ ] Step 3: Frontend PipelineBar component trong SideDrawer

`	ypescript
function PipelineBar({ conversationId }: { conversationId: string }) {
  const { data } = useQuery({
    queryKey: ['inbox', 'pipeline', conversationId],
    queryFn: () => apiClient.get('/api/inbox/conversations/' + conversationId + '/pipeline-stage').then(r => r.data),
  });
  if (!data) return null;
  const stages = [
    { key: 'new', label: 'Moi', color: 'bg-surface-container' },
    { key: 'consulting', label: 'Tu van', color: 'bg-warning-container' },
    { key: 'closing', label: 'Sap chot', color: 'bg-tertiary-container' },
    { key: 'closed', label: 'Da chot', color: 'bg-primary-container' },
  ];
  const idx = stages.findIndex(s => s.key === data.stage);
  return (
    <div className="space-y-2">
      <h4 className="text-label-sm font-bold">Pipeline</h4>
      <div className="flex gap-1">
        {stages.map((s, i) => (
          <div key={s.key} className={'flex-1 h-2 rounded-full ' + (i <= idx ? s.color : 'bg-outline/20')} />
        ))}
      </div>
      <div className="text-label-sm">{data.stageLabel} | Score: {data.score}</div>
      <div className="rounded-lg bg-surface-container p-2 text-label-sm">{data.suggestedAction}</div>
    </div>
  );
}
`

- [ ] Step 4: Commit

### Task 3.9: Tone warning khi gui tin nhan

**Files:**
- Create: src/shared/Clawbot.Agents.Core/Chat/ToneCheckerService.cs
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Tao ToneCheckerService

`csharp
public interface IToneChecker
{
    ToneCheckResult Check(string content);
}
public sealed record ToneCheckResult(bool HasIssue, string? Warning, string[]? Flags);

public sealed class ToneChecker : IToneChecker
{
    private static readonly string[] Blacklist = {
        \"sao lai\", \"khong biet a\", \"ban cu\", \"de nghi\",
        \"phai hieu\", \"co hieu khong\", \"sao khong\", \"nhanh len\"
    };
    public ToneCheckResult Check(string content)
    {
        var flags = new List<string>();
        foreach (var word in Blacklist)
            if (content.ToLowerInvariant().Contains(word))
                flags.Add(\"Tu ngay: \" + word);
        if (content.Length > 2 && content.Where(char.IsUpper).Count() > content.Length / 3)
            flags.Add(\"Viet hoa qua nhieu\");
        return new ToneCheckResult(flags.Count > 0,
            flags.Count > 0 ? \"Tin nhan co the gay hieu lam. Gui tiep?\" : null,
            flags.ToArray());
    }
}
`

- [ ] Step 2: Inject vao SendOutboundAsync

`csharp
private static async Task<IResult> SendOutboundAsync(Guid id, SendMessageRequest body,
    AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
    IChannelAdapter adapter, OutboundMessageSafetyService safety,
    IClock clock, IToneChecker toneChecker, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(body.Content))
        return Results.BadRequest(new { error = \"Content required\" });
    var toneResult = toneChecker.Check(body.Content);
    if (toneResult.HasIssue && !body.ForceSend.GetValueOrDefault())
        return Results.Ok(new { warning = true, message = toneResult.Warning, flags = toneResult.Flags });
    // ... existing send logic
}
`

- [ ] Step 3: Them bool ForceSend trong SendMessageRequest + frontend confirm dialog

`	ypescript
const sendMutation = useMutation({
  mutationFn: (payload) => apiClient.post('/api/inbox/conversations/' + id + '/messages', payload),
  onSuccess: (res) => {
    if (res.data.warning) setToneWarning(res.data);
    else queryClient.invalidateQueries({ queryKey: ['inbox', 'conversation', id] });
  },
});
`

- [ ] Step 4: Commit

### Task 3.10: Daily summary endpoint + job

**Files:**
- Create: src/api/Clawbot.Api/Endpoints/SummaryEndpoints.cs
- Create: src/shared/Clawbot.Infrastructure/Jobs/DailySummaryJob.cs
- Modify: src/api/Clawbot.Api/Program.cs

- [ ] Step 1: Tao GET /api/inbox/daily-summary

`csharp
public sealed record DailySummaryResponse(
    int ConversationsHandled, int MessagesSent, int NewLeads,
    int OpenConversations, double CloseRate, string Period);

private static async Task<IResult> GetDailySummaryAsync(
    AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user, IClock clock, CancellationToken ct)
{
    var tenant = tenants.Require();
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var today = clock.UtcNow.Date;

    var inboxIds = await db.InboxMembers
        .Where(m => m.AgentId == userId).Select(m => m.InboxId).ToListAsync(ct);

    var handled = await db.Conversations
        .CountAsync(c => inboxIds.Contains(c.InboxId!.Value) && c.AssignedTo == userId && c.UpdatedAt >= today, ct);
    var sent = await db.Messages
        .CountAsync(m => m.SenderUserId == userId && m.SentAt >= today, ct);
    var open = await db.Conversations
        .CountAsync(c => inboxIds.Contains(c.InboxId!.Value) && c.AssignedTo == userId && c.Status == \"open\", ct);
    var total = await db.Conversations
        .CountAsync(c => inboxIds.Contains(c.InboxId!.Value) && c.AssignedTo == userId && c.CreatedAt >= today.AddDays(-30), ct);
    var resolved = await db.Conversations
        .CountAsync(c => inboxIds.Contains(c.InboxId!.Value) && c.AssignedTo == userId && c.Status == \"resolved\" && c.UpdatedAt >= today.AddDays(-30), ct);
    var rate = total > 0 ? Math.Round((double)resolved / total * 100, 1) : 0;

    return Results.Ok(new DailySummaryResponse(handled, sent, 0, open, rate, today.ToString(\"yyyy-MM-dd\")));
}
`

- [ ] Step 2: Tao DailySummaryJob (Hangfire, chay 21:00 GMT+7)

`csharp
public sealed class DailySummaryJob(IServiceScopeFactory scopeFactory)
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();
        // Query tat ca user role Sale, send notification
        var sales = await db.AccountUsers
            .Where(au => au.Role.Name == \"Sale\").ToListAsync(ct);
        foreach (var sale in sales)
        {
            await notifier.SendAsync(sale.UserId, \"daily_summary\", new
            {
                title = \"Bao cao cuoi ngay\",
                body = \"Xem tong ket hoat dong hom nay\",
                url = \"/inbox?summary=\" + DateTime.UtcNow.ToString(\"yyyy-MM-dd\")
            }, ct);
        }
    }
}
`

- [ ] Step 3: Dang ky job trong Program.cs

`csharp
RecurringJob.AddOrUpdate<DailySummaryJob>(\"daily-summary\",
    j => j.RunAsync(CancellationToken.None), \"0 21 * * *\");
`

- [ ] Step 4: Frontend hien thi summary popup

`	ypescript
function DailySummaryPopup() {
  const { data } = useQuery({
    queryKey: ['inbox', 'daily-summary'],
    queryFn: () => apiClient.get('/api/inbox/daily-summary').then(r => r.data),
  });
  if (!data) return null;
  return (
    <div className=\"absolute top-12 right-4 w-72 rounded-xl bg-surface-container-lowest shadow-xl border border-outline p-4 z-50\">
      <h3 className=\"text-label-md font-bold mb-2\">Hom nay cua ban</h3>
      <div className=\"grid grid-cols-2 gap-2 text-label-sm\">
        <div><span className=\"text-primary font-bold\">{data.conversationsHandled}</span> hoithoai</div>
        <div><span className=\"text-primary font-bold\">{data.messagesSent}</span> tin nhan</div>
        <div><span className=\"text-primary font-bold\">{data.openConversations}</span> dang mo</div>
        <div><span className=\"text-primary font-bold\">{data.closeRate}%</span> chot</div>
      </div>
    </div>
  );
}
`

- [ ] Step 5: Commit


---

## Phase 5: Business Model Enforcement (P0 - Critical)

> Tasks below enforce the 1-channel-1-sale model. Must be done before production.

### Task 5.1: Admin view-only enforcement (SendOutbound block)

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

**Risk:** Admin can send messages as any sale. This violates business model and creates confusion.

- [ ] **Step 1: Add IsMember check to SendOutboundAsync**

`csharp
// After loading conversation, before sending
var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
var uid = Guid.Parse(userId!);
var roleId = user.FindFirstValue("role_id");
Guid.TryParse(roleId, out var rid);
var perms = await permResolver.GetPermissionsAsync(rid, ct);
var isAdmin = perms.Contains("admin:inboxes");

if (isAdmin)
{
    var isMember = await db.InboxMembers
        .AnyAsync(m => m.AgentId == uid && m.InboxId == conv.InboxId, ct);
    if (!isMember)
        return Results.Forbid();
}
`

- [ ] **Step 2: Add same check to POST/PUT label and note endpoints**

`csharp
// LabelsEndpoints.cs - CreateAsync
var isAdmin = perms.Contains("admin:inboxes");
if (isAdmin) return Results.Forbid();

// InboxNotesEndpoints.cs - CreateAsync, UpdateAsync
var isAdmin = perms.Contains("admin:inboxes");
if (isAdmin) return Results.Forbid();
`

- [ ] **Step 3: Frontend - hide composer and quick actions for admin**

`	ypescript
// AgentHubLayout.tsx
const { data: permissions } = usePermissions();
const isAdmin = permissions?.includes('admin:inboxes') ?? false;

// In render:
{isAdmin && (
  <div className="rounded-lg bg-warning-container px-3 py-1 text-label-sm text-on-warning-container">
    Xem chi doc - Ban co quyen admin
  </div>
)}
{!isAdmin && <ComposerWithAI ... />}
{!isAdmin && <QuickActionBar ... />}
`

- [ ] **Step 4: Commit**

`ash
git add src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs
git add src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs
git add src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs
git add src/frontend/.../features/agent-hub/AgentHubLayout.tsx
git commit -m "fix: admin view-only - block send/label/note for non-member admins"
`

### Task 5.2: Resolved -> reopen on inbound message

**Files:**
- Modify: src/shared/Clawbot.Domain/Conversations/Conversation.cs
- Modify: src/api/Clawbot.Api/Endpoints/PublicWidgetEndpoints.cs (hoac ChannelMessageIngestor.cs)

- [ ] **Step 1: Add ReopenIfNeeded method to Conversation entity**

`csharp
// Conversation.cs
public void ReopenIfNeeded()
{
    if (Status != "snoozed" && Status != "resolved") return;
    Status = "open";
    SnoozedUntil = null;
    // Giữ nguyên AssignedTo - sale cũ vẫn xử lý khách quen
}
`

- [ ] **Step 2: Write the failing test**

`csharp
// ConversationTests.cs
[Fact]
public void ReopenIfNeeded_Should_SetStatusToOpen_WhenResolved()
{
    var conv = Conversation.Create(...);
    conv.Resolve();
    Assert.Equal("resolved", conv.Status);
    conv.ReopenIfNeeded();
    Assert.Equal("open", conv.Status);
}

[Fact]
public void ReopenIfNeeded_Should_KeepAssignedTo_WhenResolved()
{
    var conv = Conversation.Create(...);
    conv.Assign(saleId);
    conv.Resolve();
    conv.ReopenIfNeeded();
    Assert.Equal(saleId, conv.AssignedTo);
}
`

- [ ] **Step 3: Run test to verify it fails**

Run: dotnet test tests/Clawbot.Api.Tests/ --filter ReopenIfNeeded
Expected: FAIL (method not found or not implemented)

- [ ] **Step 4: Implement ReopenIfNeeded in Conversation.cs**

`csharp
public void ReopenIfNeeded()
{
    if (Status != "snoozed" && Status != "resolved") return;
    Status = "open";
    SnoozedUntil = null;
}
`

- [ ] **Step 5: Run test to verify it passes**

Run: dotnet test tests/Clawbot.Api.Tests/ --filter ReopenIfNeeded
Expected: PASS

- [ ] **Step 6: Call ReopenIfNeeded from inbound ingestor**

`csharp
// PublicWidgetEndpoints.cs hoac ChannelMessageIngestor.cs
// After finding or creating conversation for incoming message:
if (conv.Status == "snoozed" || conv.Status == "resolved")
{
    conv.ReopenIfNeeded();
    await db.SaveChangesAsync(ct);
    await notifier.NotifyConversationUpdatedAsync(tenantId,
        new InboxConversationEvent(conv.Id, "open", conv.AssignedTo, DateTimeOffset.UtcNow), ct);
}
`

- [ ] **Step 7: Commit**

`ash
git add src/shared/Clawbot.Domain/Conversations/Conversation.cs
git add src/api/Clawbot.Api/Endpoints/PublicWidgetEndpoints.cs
git add tests/Clawbot.Api.Tests/Domain/ConversationTests.cs
git commit -m "fix: reopen resolved/snoozed conversations on inbound message"
`

---

## Phase 6: Channel Assignment & Reassignment (P1)

### Task 6.1: Change InboxMembers from multi-select to single-select

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs
- Modify: src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx
- Modify: src/frontend/clawbot-web/src/shared/api/admin.ts

- [ ] **Step 1: Change PUT /api/admin/inboxes/{id}/members to accept single agentId**

`csharp
// AdminInboxEndpoints.cs
public sealed record UpdateMemberRequest(Guid? AgentId);
public sealed record ReassignRequest(Guid NewAgentId);

private static async Task<IResult> UpdateMemberAsync(
    Guid id, UpdateMemberRequest body,
    AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
    ClaimsPrincipal user, IClock clock, CancellationToken ct)
{
    var tenantId = tenants.Require().TenantId;
    // Validate inbox belongs to tenant
    var inboxExists = await db.Inboxes.AnyAsync(i => i.Id == id && i.TenantId == tenantId, ct);
    if (!inboxExists) return Results.NotFound();

    if (body.AgentId == null)
    {
        // Unassign request - validate not last member
        var currentMembers = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
        if (currentMembers.Count == 0)
            return Results.BadRequest(new { error = "inbox_must_have_member", message = "Kenh phai co it nhat 1 sale phu trach" });
        
        // Unassign all conversations of this inbox currently assigned to removed members
        var oldAssigneeIds = currentMembers.Select(m => m.AgentId).ToList();
        var conversations = await db.Conversations
            .Where(c => c.InboxId == id && oldAssigneeIds.Contains(c.AssignedTo!.Value))
            .ToListAsync(ct);
        foreach (var conv in conversations)
            conv.Assign(null);
        
        db.InboxMembers.RemoveRange(currentMembers);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Validate agentId exists and belongs to same tenant
    var agentExists = await db.Users.AnyAsync(u => u.Id == body.AgentId && u.TenantId == tenantId, ct);
    if (!agentExists) return Results.BadRequest(new { error = "agent_not_found" });

    // Replace all current members with this one agent
    var existing = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
    var oldMembers = existing.Select(e => e.AgentId).ToList();
    
    db.InboxMembers.RemoveRange(existing);
    db.InboxMembers.Add(InboxMember.Create(id, body.AgentId.Value));
    
    // Unassign conversations from old members who are being removed
    var oldMemberConvs = await db.Conversations
        .Where(c => c.InboxId == id && oldMembers.Contains(c.AssignedTo!.Value))
        .ToListAsync(ct);
    foreach (var conv in oldMemberConvs)
        conv.Assign(null);
    
    await db.SaveChangesAsync(ct);
    
    // Notify old members
    foreach (var oldId in oldMembers)
        await notifier.NotifyUserAsync(oldId, "inbox:membership_changed",
            new { InboxId = id, Reason = "Member was replaced" }, ct);
    
    return Results.NoContent();
}
`

- [ ] **Step 2: Add POST /api/admin/inboxes/{id}/reassign endpoint**

`csharp
// AdminInboxEndpoints.cs
grp.MapPost("/inboxes/{id:guid}/reassign", ReassignAsync);

private static async Task<IResult> ReassignAsync(
    Guid id, ReassignRequest body,
    AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
    ClaimsPrincipal user, IClock clock, CancellationToken ct)
{
    var tenantId = tenants.Require().TenantId;
    var adminUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    
    // Validate inbox
    var inbox = await db.Inboxes.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, ct);
    if (inbox is null) return Results.NotFound();
    
    // Validate new agent
    var newAgent = await db.Users.FirstOrDefaultAsync(u => u.Id == body.NewAgentId && u.TenantId == tenantId, ct);
    if (newAgent is null) return Results.BadRequest(new { error = "agent_not_found" });
    
    // Get old members
    var oldMembers = await db.InboxMembers.Where(m => m.InboxId == id).Select(m => m.AgentId).ToListAsync(ct);
    
    // Replace members
    var existing = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
    db.InboxMembers.RemoveRange(existing);
    db.InboxMembers.Add(InboxMember.Create(id, body.NewAgentId));
    
    // Unassign conversations from old assignees
    var convs = await db.Conversations
        .Where(c => c.InboxId == id && c.AssignedTo.HasValue && oldMembers.Contains(c.AssignedTo.Value))
        .ToListAsync(ct);
    foreach (var conv in convs)
        conv.Assign(null);
    
    await db.SaveChangesAsync(ct);
    
    // Audit log
    db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = adminUserId,
        Action = "inbox:reassign",
        EntityType = "Inbox",
        EntityId = id.ToString(),
        OldValue = JsonSerializer.Serialize(new { AgentIds = oldMembers }),
        NewValue = JsonSerializer.Serialize(new { AgentIds = new[] { body.NewAgentId } }),
        CreatedAt = clock.UtcNow
    });
    await db.SaveChangesAsync(ct);
    
    // Notify
    foreach (var oldId in oldMembers)
        await notifier.NotifyUserAsync(oldId, "inbox:membership_changed",
            new { InboxId = id, Reason = "Channel reassigned to another agent" }, ct);
    
    return Results.Ok(new
    {
        InboxId = id,
        OldAgentIds = oldMembers,
        NewAgentId = body.NewAgentId,
        UnassignedConversationCount = convs.Count
    });
}
`

- [ ] **Step 3: Update frontend API in admin.ts**

`	ypescript
// admin.ts
export async function updateInboxMember(inboxId: string, agentId: string | null): Promise<void> {
  await apiClient.put(/api/admin/inboxes//members, { agentId });
}

export async function reassignInbox(inboxId: string, newAgentId: string): Promise<ReassignResult> {
  const res = await apiClient.post<ReassignResult>(/api/admin/inboxes//reassign, { newAgentId });
  return res.data;
}

export interface ReassignResult {
  inboxId: string;
  oldAgentIds: string[];
  newAgentId: string;
  unassignedConversationCount: number;
}
`

- [ ] **Step 4: Update ChannelManagementPage.tsx - single-select dropdown**

`	sx
// Thay multi-select checkbox bang dropdown single-select
<label className="block">
  <span className="mb-1 block text-label-caps uppercase text-secondary">Gan kenh nay cho sale</span>
  <select
    className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2 text-body-md"
    value={selectedAgentId ?? ''}
    onChange={e => setSelectedAgentId(e.target.value || null)}
  >
    <option value="">-- Chon sale --</option>
    {users.map(u => (
      <option key={u.id} value={u.id}>{u.displayName || u.email}</option>
    ))}
  </select>
</label>
`

- [ ] **Step 5: Add reassign UI in Channel Management**

`	sx
// Trong hang row cua inbox, them button "Chuyen giao"
function ReassignDialog({ inbox, onClose }: { inbox: InboxItem; onClose: () => void }) {
  const [selectedSale, setSelectedSale] = useState('');
  const { data: users } = useQuery({ queryKey: ['users', 'simple'], queryFn: getSimpleUserList });
  const reassignMutation = useMutation({
    mutationFn: (newAgentId: string) => reassignInbox(inbox.id, newAgentId),
    onSuccess: (data) => {
      toast.success(Da chuyen kenh.  conversation da duoc bo gan.);
      queryClient.invalidateQueries({ queryKey: ['inboxes'] });
      onClose();
    },
  });
  
  return (
    <Dialog open onClose={onClose}>
      <DialogTitle>Chuyen giao kenh: {inbox.name}</DialogTitle>
      <DialogContent>
        <p className="text-body-md text-secondary mb-4">
          Chon sale moi de tiep quan kenh nay. Conversations dang xu ly se duoc bo gan.
        </p>
        <select className="w-full rounded border border-outline px-3 py-2" value={selectedSale} onChange={e => setSelectedSale(e.target.value)}>
          <option value="">-- Chon sale --</option>
          {users?.map(u => <option key={u.id} value={u.id}>{u.displayName || u.email}</option>)}
        </select>
      </DialogContent>
      <DialogActions>
        <button onClick={onClose}>Huy</button>
        <button onClick={() => reassignMutation.mutate(selectedSale)} disabled={!selectedSale}>
          Chuyen giao
        </button>
      </DialogActions>
    </Dialog>
  );
}
`

- [ ] **Step 6: Commit**

`ash
git add src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs
git add src/frontend/.../shared/api/admin.ts
git add src/frontend/.../features/admin/ChannelManagementPage.tsx
git commit -m "feat: channel reassign with single-agent model - transfer ownership, unassign conversations"
`

### Task 6.2: Validate max 1 member per inbox (backend+DB)

**Files:**
- Modify: src/shared/Clawbot.Infrastructure/Data/AppDbContext.cs (hoac migration)

- [ ] **Step 1: Add DB unique constraint on InboxId in InboxMembers**

`sql
-- Migration: add unique constraint to enforce 1 member per inbox
CREATE UNIQUE INDEX uq_inbox_members_inbox ON InboxMembers (InboxId);
-- Note: InboxMembers has PK (InboxId, AgentId). Unique constraint on InboxId alone means only 1 row per inbox.
`

- [ ] **Step 2: Add EF Core index configuration**

`csharp
// AppDbContext.cs OnModelCreating
modelBuilder.Entity<InboxMember>(e =>
{
    e.HasIndex(m => m.InboxId).IsUnique().HasDatabaseName("uq_inbox_members_inbox");
});
`

- [ ] **Step 3: Commit**

`ash
git add deploy/migrations/XXXX_add_unique_inbox_members.sql
git add src/shared/.../Data/AppDbContext.cs
git commit -m "feat: enforce 1 member per inbox with unique constraint"
`

---

## Phase 7: Channel-Token Mapping (P2)

### Task 7.1: Check Inbox entity for token field

**Files:**
- Search: src/shared/Clawbot.Domain/Channels/Inbox.cs

- [ ] **Step 1: Check if Inbox entity has EncryptedAccessToken field**

`ash
rg "AccessToken" src/shared/Clawbot.Domain/Channels/Inbox.cs
`
If not found, add field.

- [ ] **Step 2: Add page_access_token support if missing**

`csharp
// Inbox.cs
public string? EncryptedAccessToken { get; private set; }
public void SetAccessToken(string encryptedToken) => EncryptedAccessToken = encryptedToken;
`

- [ ] **Step 3: Commit** (if changes needed)

### Task 7.2: Add token input to Channel form

**Files:**
- Modify: src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx

- [ ] **Step 1: Add token text input in Channel Create form**

`	sx
<div className="space-y-4">
  <label className="block">
    <span className="mb-1 block text-label-caps uppercase text-secondary">Ten kenh</span>
    <input type="text" className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2" value={name} onChange={e => setName(e.target.value)} />
  </label>
  <label className="block">
    <span className="mb-1 block text-label-caps uppercase text-secondary">Page Access Token (tu Pancake)</span>
    <input type="password" className="w-full rounded border border-outline bg-surface-container-lowest px-3 py-2" value={token} onChange={e => setToken(e.target.value)} />
    <p className="text-label-sm text-secondary mt-1">Token nay duoc encrypt va luu tru bao mat.</p>
  </label>
</div>
`

- [ ] **Step 2: Add token field to CreateInbox API call**

- [ ] **Step 3: Commit**

`ash
git add src/frontend/.../features/admin/ChannelManagementPage.tsx
git commit -m "feat: add page_access_token input to channel form"
`


## Phase 8: Channel Selection Screen (New)

### Task 8.1: Backend GET /api/inbox/channels

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] Step 1: Add route grp.MapGet("/channels", ListChannelsAsync)
- [ ] Step 2: Implement: resolve isAdmin, filter channels, return id/name/platform/hasToken/unreadCount/memberDisplayName
- [ ] Step 3: Add Guid? inboxId query param to ListAsync, GetAsync, SearchAsync
- [ ] Step 4: Commit

### Task 8.2: Frontend ChannelListPage + ChannelCard

**Files:**
- Create: src/frontend/.../features/inbox/ChannelListPage.tsx
- Create: src/frontend/.../features/inbox/ChannelCard.tsx
- Modify: routes.tsx, lazyPages.tsx

- [ ] Step 1: ChannelListPage - query GET /api/inbox/channels, grid layout, empty state
- [ ] Step 2: ChannelCard - platform icon, name, member name, unread badge
- [ ] Step 3: Routes /inbox and /inbox/:channelId
- [ ] Step 4: Commit

### Task 8.3: Agent Hub scope by channelId

**Files:**
- Modify: src/frontend/.../features/agent-hub/AgentHubLayout.tsx

- [ ] Step 1: Read channelId from useParams, pass to query
- [ ] Step 2: Header back button + channel name
- [ ] Step 3: Admin read-only mode (hide composer, show badge)
- [ ] Step 4: Commit

---

## Phase 9: Channel-Token Mapping (New)

### Task 9.1: EncryptedAccessToken on Inbox

**Files:**
- Modify: src/shared/Clawbot.Domain/Channels/Inbox.cs
- Create: deploy/migrations/0030_add_inbox_encrypted_token.sql

- [ ] Step 1: Add property + SetAccessToken method
- [ ] Step 2: Create migration
- [ ] Step 3: Commit

### Task 9.2: Token input in Admin UI

**Files:**
- Modify: src/frontend/.../features/admin/ChannelManagementPage.tsx
- Modify: src/frontend/.../shared/api/admin.ts
- Modify: src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs

- [ ] Step 1: Add pageAccessToken to create/update channel API
- [ ] Step 2: Add token input (password) to ChannelManagementPage form
- [ ] Step 3: Commit
