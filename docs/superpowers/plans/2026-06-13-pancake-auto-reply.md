# Auto Reply Fixed Message (Pancake ? Zalo) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Replace hardcoded demo auto-reply text with configurable replies from QuickReplyTemplate, plus separate read/write tokens for Pancake API.

**Architecture:** Two isolated changes: (1) split PancakeAccessToken into read-only token + write-only PancakePageAccessToken so polling still works while sends use correct credentials; (2) both reply paths (webhook + polling service) look up QuickReplyTemplate from DB by code "auto_reply" instead of hardcoded strings. Fallback to hardcoded text if no template exists.

**Tech Stack:** .NET 8, EF Core, Redis, Pancake Pages API v2

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| src/shared/Clawbot.SharedKernel/Demo/DemoRuntimeConfig.cs | Modify | Add PancakePageAccessToken property |
| src/api/Clawbot.Api/Services/DemoRuntimeConfigStore.cs | Modify | Load + update PancakePageAccessToken from env |
| src/api/Clawbot.Api/Endpoints/DemoEndpoints.cs | Modify | Update config endpoint, use page token for send, integrate QRT lookup |
| src/api/Clawbot.Api/Services/PancakePollingService.cs | Modify | Use page token for send, integrate QRT lookup |
| src/api/Clawbot.Api/Program.cs | Modify | Load PANCAKE_PAGE_ACCESS_TOKEN env var |

---

### Task 1: Add PancakePageAccessToken to DemoRuntimeConfig

**Files:**
- Modify: src/shared/Clawbot.SharedKernel/Demo/DemoRuntimeConfig.cs

- [ ] **Step 1: Add property + computed flag**

`csharp
// In DemoRuntimeConfig class — add after PancakeAccessToken
public string? PancakePageAccessToken { get; set; }

// Add computed property
public bool IsPageTokenConfigured => !string.IsNullOrEmpty(PancakePageAccessToken);
`

---

### Task 2: Update DemoRuntimeConfigStore for page token

**Files:**
- Modify: src/api/Clawbot.Api/Services/DemoRuntimeConfigStore.cs

- [ ] **Step 1: Load from env var in constructor**

`csharp
// In constructor, after existing token line
var pageToken = Environment.GetEnvironmentVariable("PANCAKE_PAGE_ACCESS_TOKEN");
// ...existing code...
_config = new DemoRuntimeConfig
{
    PancakeAccessToken = token,
    PancakeWebhookSecret = secret,
    PancakePageAccessToken = pageToken,  // NEW
};
`

- [ ] **Step 2: Add update + include in Get()**

`csharp
// Add method
public void UpdatePageAccessToken(string? token)
{
    lock (_lock) _config.PancakePageAccessToken = token;
}

// In Get() — add to returned copy
PancakePageAccessToken = _config.PancakePageAccessToken,

// In Override() — add
_config.PancakePageAccessToken = cfg.PancakePageAccessToken;
`

- [ ] **Step 3: Add auto-reply text fallback property**

`csharp
// Add to DemoRuntimeConfig
public string? AutoReplyText { get; set; }

// Add computed fallback
public string EffectiveAutoReplyText =>
    AutoReplyText ?? "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m";

// In DemoRuntimeConfigStore.Get() — add to copy
AutoReplyText = _config.AutoReplyText,

// Add method
public void UpdateAutoReplyText(string? text)
{
    lock (_lock) _config.AutoReplyText = text;
}
`

---

### Task 3: Update DemoEndpoints — token separation + QRT integration

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/DemoEndpoints.cs

- [ ] **Step 1: Extend SetTokenRequest record with PageAccessToken**

`csharp
// Replace existing record at bottom of file
public sealed record SetTokenRequest(
    string? Token,
    string? Secret,
    string? PageId,
    string? BaseUrl,
    string? PageAccessToken     // NEW
);
`

- [ ] **Step 2: Update SetTokenAsync handler**

`csharp
// In SetTokenAsync — add after store.UpdateToken(req.Token)
if (req.PageAccessToken is not null) store.UpdatePageAccessToken(req.PageAccessToken);
`

- [ ] **Step 3: Update GetConfigStatusAsync to show page token status**

`csharp
// In returned object, add:
pageTokenConfigured = c.IsPageTokenConfigured,
autoReplyText = c.AutoReplyText ?? "(using default)",
`

- [ ] **Step 4: Add auto-reply text config endpoint**

`csharp
// After SetWebhookSecretAsync in MapDemo(), add:
group.MapPost("/config/auto-reply", SetAutoReplyAsync);

// Handler:
private static IResult SetAutoReplyAsync(
    SetAutoReplyRequest req, DemoRuntimeConfigStore store)
{
    store.UpdateAutoReplyText(req.Text);
    return Results.Ok(new { status = "auto_reply_updated", text = req.Text });
}

// Request DTO:
public sealed record SetAutoReplyRequest(string? Text);
`

- [ ] **Step 5: Update ProcessWebhookAsync to use page token + QRT lookup**

Find the outbound section (around line 246-275). Replace token and add QRT lookup:

`csharp
// At top of ProcessWebhookAsync, add AppDbContext parameter + resolve
// Change signature to:
private static async Task ProcessWebhookAsync(
    string traceId, string rawBody,
    DemoTraceService traces, DemoRuntimeConfigStore configStore,
    IOptions<DemoOptions> opts, ILogger<Program> log, IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory = null)  // NEW — for QRT lookup
`

Actually — ProcessWebhookAsync is called from HandleWebhookAsync via _ = Task.Run(...). We need to pass IServiceScopeFactory from HandleWebhookAsync. Let me trace the full flow.

In HandleWebhookAsync, we have access to HttpContext and can resolve services. Update:

`csharp
// In HandleWebhookAsync — change the Task.Run call
var scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>();
_ = Task.Run(async () => await ProcessWebhookAsync(
    traceId, rawBody, traces, configStore, opts, log, httpClientFactory, scopeFactory));
`

In ProcessWebhookAsync, add QRT lookup at the top (after parsing):

`csharp
// After parsing block, before creating trace — resolve reply text
string replyText;
try
{
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var qrt = await db.QuickReplyTemplates
        .AsNoTracking()
        .Where(q => q.Code == "auto_reply")
        .FirstOrDefaultAsync();
    replyText = qrt?.Body ?? "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m";
}
catch
{
    replyText = "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m";
}
`

Replace the hardcoded draft:
`csharp
// Replace:
var draft = $"Ca on ban da nhan tin!...";
// With:
var draft = replyText;
`

Replace the API URL token parameter:
`csharp
// Find the send API URL line — replace cfg.PancakeAccessToken with cfg.PancakePageAccessToken
var apiUrl = $"{sendBaseUrl}/pages/{config.PancakePageId}/conversations/{conversationId}/messages?page_access_token={config.PancakePageAccessToken}";
`

- [ ] **Step 6: Add using for AppDbContext**

`csharp
// At top of file, add:
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
`

---

### Task 4: Update PancakePollingService — page token + QRT lookup

**Files:**
- Modify: src/api/Clawbot.Api/Services/PancakePollingService.cs

- [ ] **Step 1: Inject IServiceScopeFactory**

`csharp
// In constructor, add field:
private readonly IServiceScopeFactory _scopeFactory;

// Update constructor signature:
public PancakePollingService(
    DemoTraceService traces,
    DemoRuntimeConfigStore config,
    IHttpClientFactory httpFactory,
    ILogger<PancakePollingService> log,
    IServiceScopeFactory scopeFactory)   // NEW
{
    // ...existing assignments...
    _scopeFactory = scopeFactory;
}
`

- [ ] **Step 2: Resolve reply text from QuickReplyTemplate**

`csharp
// In PollConversationsAsync, before the "Agent step" section, add QRT lookup:

// Resolve auto-reply text from QuickReplyTemplate
string replyText;
try
{
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var qrt = await db.QuickReplyTemplates
        .AsNoTracking()
        .Where(q => q.Code == "auto_reply")
        .FirstOrDefaultAsync(ct);
    replyText = qrt?.Body ?? "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m";
}
catch
{
    replyText = "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m";
}
`

- [ ] **Step 3: Replace hardcoded draft**

`csharp
// Replace:
var draft = $"Cam on ban da nhan tin!...";
// With:
var draft = replyText;
`

- [ ] **Step 4: Use page token for API call**

`csharp
// Find the send URL construction — replace cfg.PancakeAccessToken with cfg.PancakePageAccessToken
var apiUrl = $"{sendBaseUrl}/pages/{cfg.PancakePageId}/conversations/{conv.Id}/messages?page_access_token={cfg.PancakePageAccessToken}";
`

- [ ] **Step 5: Check token existence check**

Update the condition that gates sending (currently checks cfg.IsTokenConfigured):
`csharp
// Change from:
if (cfg.IsTokenConfigured && !string.IsNullOrEmpty(cfg.PancakePageId) && conv.Id is not null)
// To:
if (cfg.IsPageTokenConfigured && !string.IsNullOrEmpty(cfg.PancakePageId) && conv.Id is not null)
`

Also update the Trace output ["tokenConfigured"] ? ["pageTokenConfigured"].

- [ ] **Step 6: Add usings**

`csharp
// At top of file, add:
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
`

---

### Task 5: Update Program.cs — load new env var

**Files:**
- Modify: src/api/Clawbot.Api/Program.cs

- [ ] **Step 1: No code change needed** — DemoRuntimeConfigStore already reads PANCAKE_PAGE_ACCESS_TOKEN from env in its constructor (Task 2). The existing uilder.Services.AddSingleton<DemoRuntimeConfigStore>() will pick it up.

---

### Task 6: Seed auto_reply QuickReplyTemplate

**Files:**
- Modify: src/shared/Clawbot.Infrastructure/Persistence/DevDataSeeder.cs

- [ ] **Step 1: Add auto-reply template seeding**

Find the SeedAdminAsync or an appropriate seed method:

`csharp
// Add after existing seed data
public static async Task SeedAutoReplyTemplatesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!await db.QuickReplyTemplates.AnyAsync(q => q.Code == "auto_reply"))
    {
        // Find demo tenant
        var tenant = await db.Tenants.FirstOrDefaultAsync();
        if (tenant is null) return;

        db.QuickReplyTemplates.Add(QuickReplyTemplate.Create(
            tenant.Id,
            "auto_reply",
            "C?m on b?n dã liên h?, chúng tôi s? ph?n h?i s?m",
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }
}
`

- [ ] **Step 2: Call seeder in Program.cs**

`csharp
// In Program.cs, after RbacSeeder.SeedAsync, add:
if (app.Environment.IsDevelopment())
{
    await DevDataSeeder.SeedAutoReplyTemplatesAsync(app.Services).ConfigureAwait(false);
}
`

---

## Self-Review Checklist

1. **Spec coverage:** All 3 requirements covered — (1) fixed reply text, (2) same for all topics, (3) uses QuickReplyTemplate. Token separation covered.
2. **Placeholder scan:** No TODOs or TBDs. Every step has real code.
3. **Type consistency:** 
   - PancakePageAccessToken property consistent across DemoRuntimeConfig, DemoRuntimeConfigStore, DemoEndpoints, PancakePollingService
   - SetAutoReplyRequest record name consistent
   - IsPageTokenConfigured matches naming convention of existing IsTokenConfigured
4. **Dependencies:** Code "auto_reply" is used consistently. IServiceScopeFactory is how scoped DB access works from singletons (correct pattern).
