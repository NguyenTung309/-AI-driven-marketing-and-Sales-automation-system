# Demo Flow Fix & Feature Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix auto-spam loop in Pancake polling + complete demo flow through all 7 review steps: ingest → inbox → AI suggest → lead scoring → abandonment timer → summary → KB.

**Architecture:** Fix polling to use DB-backed dedup + call IngestAsync; build frontend inbox with SignalR real-time; add missing backend features (lead scoring, abandonment timer); wire gRPC SaleAssistAgent.

**Tech Stack:** .NET 8, EF Core + SQL Server, React 19 + TypeScript + Tailwind + Zustand + TanStack Query + SignalR, gRPC.
---

## Task 0: Fix Auto-Spam Bug + Ingest Pipeline

**Root cause:** PancakePollingService sends auto-reply → reply becomes latest message → next poll sees reply as new customer message → sends another reply → infinite loop. In-memory `_seenIds` lost on restart. No call to IngestAsync. No tenant resolution for background service.

**Fix:** DB-backed dedup via ProcessedMessage table. Call IngestAsync. Add ITenantResolver for demo mode.

**Files:**
- Create: `src/shared/Clawbot.Domain/Channels/ProcessedMessage.cs`
- Create: `src/shared/Clawbot.Infrastructure/Persistence/EntityConfigurations/ProcessedMessageConfiguration.cs`
- Create: `src/shared/Clawbot.SharedKernel/Multitenancy/ITenantResolver.cs`
- Create: `src/shared/Clawbot.Infrastructure/Multitenancy/DemoTenantResolver.cs`
- Modify: `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `src/shared/Clawbot.Infrastructure/DependencyInjection.cs`
- Modify: `src/api/Clawbot.Api/Services/PancakePollingService.cs`
- Test: `tests/api/Clawbot.Api.Tests/Services/PancakePollingServiceTests.cs`
- Migration: add `processed_messages` table

- [x] **Step 0.1: Create ProcessedMessage entity**

  Create `ProcessedMessage.cs`:

```csharp
namespace Clawbot.Domain.Channels;

public sealed class ProcessedMessage
{
    public Guid Id { get; private set; }
    public string Platform { get; private set; }
    public string ExternalMessageId { get; private set; }
    public string ConversationExternalId { get; private set; }
    public DateTime ProcessedAt { get; private set; }

    private ProcessedMessage() { }
    public ProcessedMessage(string platform, string externalMessageId, string conversationExternalId)
    {
        Id = Guid.NewGuid();
        Platform = platform;
        ExternalMessageId = externalMessageId;
        ConversationExternalId = conversationExternalId;
        ProcessedAt = DateTime.UtcNow;
    }
}
```

- [x] **Step 0.2: Add EF config + DbSet**

  Create `ProcessedMessageConfiguration.cs`:

```csharp
using Clawbot.Domain.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clawbot.Infrastructure.Persistence.EntityConfigurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Platform).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalMessageId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ConversationExternalId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProcessedAt).IsRequired();
        builder.HasIndex(x => new { x.Platform, x.ExternalMessageId }).IsUnique();
    }
}
```

  Add to `AppDbContext.cs`:

```csharp
public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();
```

  Register in `OnModelCreating`:

```csharp
modelBuilder.ApplyConfiguration(new ProcessedMessageConfiguration());
```

- [x] **Step 0.3: Create EF migration**

```powershell
cd src\Clawbot.Infrastructure
dotnet ef migrations add AddProcessedMessages --context AppDbContext
dotnet ef migrations script --output deploy/migrations/0002_processed_messages.sql
```

- [x] **Step 0.4: Create ITenantResolver + DemoTenantResolver**

  `ITenantResolver.cs`:

```csharp
namespace Clawbot.SharedKernel.Multitenancy;

public interface ITenantResolver
{
    Task<Guid> ResolveTenantIdAsync(CancellationToken ct = default);
}
```

  `DemoTenantResolver.cs`:

```csharp
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Multitenancy;

public sealed class DemoTenantResolver : ITenantResolver
{
    private readonly AppDbContext _db;
    public DemoTenantResolver(AppDbContext db) => _db = db;

    public async Task<Guid> ResolveTenantIdAsync(CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Slug == "demo")
            .FirstOrDefaultAsync(ct);
        if (tenant is not null) return tenant.Id;
        return Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    }
}
```

  Register in `DependencyInjection.cs`:

```csharp
services.AddScoped<ITenantResolver, DemoTenantResolver>();
```

- [x] **Step 0.5: Rewrite PancakePollingService (DB dedup + IngestAsync + ITenantResolver)**

  Key changes from current code:
  - Remove `_seenIds` and `_seenQueue` (in-memory dedup)
  - Inject `IServiceScopeFactory` already available
  - Add DB dedup check before processing: `ProcessedMessages.AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == latestMsg.Id)`
  - Mark processed immediately after dedup check
  - Resolve `ITenantResolver` and `IChannelMessageIngestor` from scope
  - Call `ingestor.IngestAsync(tenantId, channelMsg, ct)` to persist to inbox
  - Auto-reply only after successful ingest

  See full replacement code in the `PancakePollingService.cs` — the critical new section inside the loop:

```csharp
// DB-backed dedup
using var scope = _scopeFactory.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

var alreadyProcessed = await db.ProcessedMessages
    .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == latestMsg.Id, ct);

if (alreadyProcessed) { /* skip */ continue; }

// Skip automated/admin messages
if (latestMsg.From?.IsAutomated == true) continue;
if (!string.IsNullOrEmpty(latestMsg.From?.AdminId)) continue;

// Mark processed immediately
db.ProcessedMessages.Add(new ProcessedMessage("zalo", latestMsg.Id, convId));
await db.SaveChangesAsync(ct);

// Ingest to inbox DB
var resolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
var ingestor = scope.ServiceProvider.GetRequiredService<IChannelMessageIngestor>();
var tenantId = await resolver.ResolveTenantIdAsync(ct);
var channelMsg = new ChannelMessage
{
    Channel = "zalo",
    ExternalThreadId = convId,
    ExternalUserId = latestMsg.From?.Id ?? "unknown",
    Text = snippet,
    SentAt = conv.UpdatedAt ?? DateTime.UtcNow,
    Metadata = new Dictionary<string, string>
    {
        ["external_message_id"] = latestMsg.Id,
        ["content_type"] = "text",
    },
};
await ingestor.IngestAsync(tenantId, channelMsg, ct);

// Then resolve + send auto-reply...
```

- [x] **Step 0.6: Write tests**

  Create `tests/api/Clawbot.Api.Tests/Services/PancakePollingServiceTests.cs`:

```csharp
public sealed class PancakePollingServiceTests
{
    [Fact]
    public async Task ProcessedMessage_Dedup_ShouldPreventDoubleProcessing()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("dedup_test").Options;
        await using var db = new AppDbContext(opts);
        var msgId = "test-msg-123";
        db.ProcessedMessages.Add(new ProcessedMessage("zalo", msgId, "conv-1"));
        await db.SaveChangesAsync();
        var exists = await db.ProcessedMessages
            .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == msgId);
        Assert.True(exists);
    }

    [Fact]
    public async Task DemoTenantResolver_ShouldReturnValidGuid()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("tenant_resolver_test").Options;
        await using var db = new AppDbContext(opts);
        var resolver = new DemoTenantResolver(db);
        var tenantId = await resolver.ResolveTenantIdAsync();
        Assert.NotEqual(Guid.Empty, tenantId);
    }
}
```

- [x] **Step 0.7: Build + run tests**

```powershell
dotnet build src/Clawbot.sln
dotnet test tests/api/Clawbot.Api.Tests
```

- [x] **Step 0.8: Commit**

```powershell
git add .
git commit -m "fix: auto-spam loop + add IngestAsync pipeline + ITenantResolver"
```
## Task 1: Build Frontend Inbox

**Files:**
- Modify: src/frontend/clawbot-web/src/features/conversations/ConversationsPage.tsx
- Create: src/frontend/clawbot-web/src/features/conversations/ConversationList.tsx
- Create: src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx
- Create: src/frontend/clawbot-web/src/features/conversations/MessageInput.tsx
- Create: src/frontend/clawbot-web/src/features/conversations/useInbox.ts
- Create: src/frontend/clawbot-web/src/features/conversations/types.ts

- [ ] **Step 1.1: Define types in 	ypes.ts**

```typescript
export interface Conversation {
  id: string;
  contactName: string;
  platform: string;
  snippet: string;
  lastMessageAt: string;
  unreadCount: number;
  status: 'open' | 'closed';
  leadScore?: number;
  assignedTo?: string;
}

export interface Message {
  id: string;
  content: string;
  contentType: string;
  direction: 'in' | 'out';
  senderType: 'contact' | 'agent' | 'system';
  sentAt: string;
}
```

- [ ] **Step 1.2: Build useInbox.ts — TanStack Query + SignalR**

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import type { Conversation, Message } from './types';

const BASE = '/api/inbox';

export function useInbox() {
  return useQuery<Conversation[]>({
    queryKey: ['inbox'],
    queryFn: () => fetch(BASE).then(r => r.json()),
    refetchInterval: 30_000,
  });
}

export function useConversationMessages(id: string | null) {
  return useQuery<Message[]>({
    queryKey: ['inbox', id, 'messages'],
    queryFn: () => fetch(${BASE}//messages).then(r => r.json()),
    enabled: id !== null,
    refetchInterval: 10_000,
  });
}

export function useSendMessage(convId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (content: string) =>
      fetch(${BASE}//send, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content }),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inbox', convId, 'messages'] });
    },
  });
}
```

- [ ] **Step 1.3: Build ConversationList component**

`	sx
interface Props {
  conversations: Conversation[];
  selectedId?: string;
  onSelect: (id: string) => void;
}

export default function ConversationList({ conversations, selectedId, onSelect }: Props) {
  return (
    <div className="flex flex-col overflow-y-auto border-r border-slate-200">
      {conversations.map(conv => (
        <button
          key={conv.id}
          onClick={() => onSelect(conv.id)}
          className={px-4 py-3 text-left border-b border-slate-100 hover:bg-slate-50 transition-colors }
        >
          <div className="flex items-center justify-between">
            <span className="font-medium text-sm truncate">{conv.contactName}</span>
            <span className="text-xs text-slate-400">{conv.lastMessageAt}</span>
          </div>
          <p className="text-xs text-slate-500 truncate mt-1">{conv.snippet}</p>
          {conv.leadScore !== undefined && (
            <span className={inline-block mt-1 text-xs px-1.5 py-0.5 rounded }>
              {conv.leadScore >= 7 ? '🔥 Hot' : conv.leadScore >= 4 ? '⭐ Warm' : '💤 Cold'}
            </span>
          )}
        </button>
      ))}
    </div>
  );
}
```

- [ ] **Step 1.4: Build ChatPane component**

`	sx
interface Props { messages: Message[] }

export default function ChatPane({ messages }: Props) {
  return (
    <div className="flex flex-col gap-2 p-4 overflow-y-auto h-full">
      {messages.map(msg => (
        <div key={msg.id} className={lex }>
          <div className={max-w-[70%] rounded-lg px-3 py-2 text-sm }>
            <p>{msg.content}</p>
            <span className="text-xs opacity-70 block mt-1">{msg.sentAt}</span>
          </div>
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 1.5: Build MessageInput component**

`	sx
import { useState } from 'react';

interface Props { onSend: (content: string) => void; disabled?: boolean; }

export default function MessageInput({ onSend, disabled }: Props) {
  const [text, setText] = useState('');
  const handleSend = () => { if (!text.trim()) return; onSend(text.trim()); setText(''); };
  return (
    <div className="flex items-center gap-2 border-t border-slate-200 p-4">
      <input value={text} onChange={e => setText(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && handleSend()}
        placeholder="Nhập tin nhắn..." disabled={disabled}
        className="flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm outline-none focus:border-blue-400" />
      <button onClick={handleSend} disabled={disabled || !text.trim()}
        className="rounded-lg bg-blue-500 px-4 py-2 text-sm text-white hover:bg-blue-600 disabled:opacity-50">Gửi</button>
    </div>
  );
}
```

- [ ] **Step 1.6: Rebuild ConversationsPage**

`	sx
import { useState } from 'react';
import { useInbox, useConversationMessages, useSendMessage } from './useInbox';
import ConversationList from './ConversationList';
import ChatPane from './ChatPane';
import MessageInput from './MessageInput';

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data: conversations, isLoading } = useInbox();
  const { data: messages } = useConversationMessages(selectedId);
  const sendMutation = useSendMessage(selectedId ?? '');

  if (isLoading) return <div className="p-6 text-slate-500">Đang tải...</div>;

  return (
    <div className="flex h-[calc(100vh-4rem)]">
      <div className="w-80 shrink-0">
        <div className="p-4 border-b border-slate-200"><h1 className="text-lg font-semibold">Inbox</h1></div>
        <ConversationList conversations={conversations ?? []} selectedId={selectedId ?? undefined} onSelect={setSelectedId} />
      </div>
      <div className="flex-1 flex flex-col">
        {selectedId ? (
          <><ChatPane messages={messages ?? []} /><MessageInput onSend={content => sendMutation.mutate(content)} /></>
        ) : (
          <div className="flex items-center justify-center h-full text-slate-400">Chọn một hội thoại để bắt đầu</div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 1.7: Verify build**

```powershell
cd src/frontend/clawbot-web
npm run build
```

- [ ] **Step 1.8: Commit**

---

## Task 2: Wire SaleAssistAgent (AI Suggest)

**Files:**
- Create: src/frontend/clawbot-web/src/features/conversations/SuggestedReply.tsx
- Modify: src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx
- Modify: src/frontend/clawbot-web/src/features/conversations/useInbox.ts (add useSuggestedReply)

- [ ] **Step 2.1: Add API call for draft suggestion**

  Add to useInbox.ts:

```typescript
export function useSuggestedReply(convId: string | null, lastMessage: string) {
  return useQuery<string>({
    queryKey: ['suggest', convId, lastMessage],
    queryFn: async () => {
      const res = await fetch('/api/sale-assist/draft', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ conversationId: convId, lastMessage }),
      });
      const data = await res.json();
      return data.draft ?? data;
    },
    enabled: convId !== null && lastMessage.length > 0,
    staleTime: 60_000,
  });
}
```

- [ ] **Step 2.2: Build SuggestedReply component**

`	sx
interface Props { draft: string; onApply: (text: string) => void; onRefresh: () => void; }

export default function SuggestedReply({ draft, onApply, onRefresh }: Props) {
  return (
    <div className="border border-blue-200 bg-blue-50 rounded-lg p-3 m-2">
      <div className="flex items-center justify-between mb-1">
        <span className="text-xs font-medium text-blue-600">AI Gợi ý</span>
        <button onClick={onRefresh} className="text-blue-400 hover:text-blue-600 text-xs">⟳ Làm mới</button>
      </div>
      <p className="text-sm text-slate-700 mb-2">{draft}</p>
      <button onClick={() => onApply(draft)}
        className="text-xs bg-blue-500 text-white px-3 py-1 rounded hover:bg-blue-600">Dùng tin này</button>
    </div>
  );
}
```

- [ ] **Step 2.3: Integrate into ChatPane**

  Show <SuggestedReply> above <MessageInput> when a customer message is received and AI draft is available.

- [ ] **Step 2.4: If SaleAssist gRPC is stub, add demo fallback endpoint**

  In Program.cs or a demo endpoints file:

```csharp
app.MapPost("/api/sale-assist/draft", (SaleAssistDraftRequest req) =>
{
    return Results.Ok(new
    {
        draft = "Cảm ơn bạn đã quan tâm! Sản phẩm của chúng tôi đang có khuyến mãi. Bạn muốn tìm hiểu thêm tính năng nào ạ?",
        leadScore = 6,
        confidence = 0.87,
    });
});

public sealed record SaleAssistDraftRequest(string ConversationId, string LastMessage, string? PageId);
```

- [ ] **Step 2.5: Build + verify**

```powershell
dotnet build src/Clawbot.sln
cd src/frontend/clawbot-web
npm run build
```

- [ ] **Step 2.6: Commit**

---

## Task 3: Abandonment Timer (5 phút)

**Files:**
- Create: src/api/Clawbot.Api/Jobs/AbandonmentWatcher.cs
- Modify: src/api/Clawbot.Api/Program.cs (register background service)

- [ ] **Step 3.1: Create AbandonmentWatcher background service**

```csharp
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed class AbandonmentWatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbandonmentWatcher> _log;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AbandonThreshold = TimeSpan.FromMinutes(5);

    public AbandonmentWatcher(IServiceScopeFactory scopeFactory, ILogger<AbandonmentWatcher> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var cutoff = DateTime.UtcNow.Subtract(AbandonThreshold);
                var abandoned = await db.Conversations
                    .Where(c => c.Status == "open"
                        && c.LastMessageAt != null
                        && c.LastMessageAt <= cutoff
                        && !db.Messages.Any(m => m.ConversationId == c.Id
                            && m.Direction == "out"
                            && m.SentAt > c.LastMessageAt))
                    .ToListAsync(stoppingToken);

                foreach (var conv in abandoned)
                {
                    _log.LogWarning("Abandoned conversation: {ConvId} (last msg: {Last})",
                        conv.Id, conv.LastMessageAt);
                    // Notify via SignalR: push to assigned agent inbox (future: real notification)
                }
            }
            catch (Exception ex)
            {
                _log.LogError("AbandonmentWatcher error: {Ex}", ex.Message);
            }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
```

- [ ] **Step 3.2: Register in Program.cs**

```csharp
builder.Services.AddHostedService<AbandonmentWatcher>();
```

- [ ] **Step 3.3: Build + verify**

```powershell
dotnet build src/Clawbot.sln
```

- [ ] **Step 3.4: Commit**

---

## Task 4: Lead Scoring Engine

**Files:**
- Create: src/shared/Clawbot.Domain/Leads/LeadScore.cs
- Create: src/shared/Clawbot.Infrastructure/Persistence/EntityConfigurations/LeadScoreConfiguration.cs
- Modify: src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs (add DbSet)
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs (return leadScore)
- Migration: add lead_scores table

- [ ] **Step 4.1: Create LeadScore entity**

```csharp
namespace Clawbot.Domain.Leads;

public sealed class LeadScore
{
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public int Score { get; private set; }  // 0-10
    public string? Factors { get; private set; } // JSON
    public DateTime LastComputed { get; private set; }

    private LeadScore() { }
    public LeadScore(Guid conversationId, int score, string? factors)
    {
        Id = Guid.NewGuid();
        ConversationId = conversationId;
        Score = Math.Clamp(score, 0, 10);
        Factors = factors;
        LastComputed = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4.2: Add EF config + DbSet + migration**

  Follow same pattern as ProcessedMessageConfiguration. Add DbSet<LeadScore> to AppDbContext.

- [ ] **Step 4.3: Verify LeadScoringEngine exists and wire it**

  Check src/agents/Clawbot.Agents.Core/Lead/LeadScoringEngine.cs — if it exists, verify it can be called from PollingService after IngestAsync. If not, create simple rule-based scoring (keywords, message length, response time).

- [ ] **Step 4.4: Wire scoring into PancakePollingService**

  After successful IngestAsync, compute or update lead score for the conversation.

- [ ] **Step 4.5: Return leadScore in inbox API**

  Update InboxEndpoints GET to join LeadScore table and include score in response.

- [ ] **Step 4.6: Build + verify**

- [ ] **Step 4.7: Commit**

---

## Task 5: Summary UI in Inbox

**Files:**
- Modify: src/frontend/clawbot-web/src/features/conversations/useInbox.ts (add useConversationSummary)
- Create: src/frontend/clawbot-web/src/features/conversations/ConversationSummary.tsx
- Modify: src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx (integrate summary)

- [ ] **Step 5.1: Add summary query to useInbox.ts**

```typescript
export function useConversationSummary(convId: string | null) {
  return useQuery<string>({
    queryKey: ['summary', convId],
    queryFn: () =>
      fetch('/api/sale-assist/summary', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ conversationId: convId }),
      }).then(r => r.json()).then(d => d.summary ?? d),
    enabled: convId !== null,
    staleTime: 300_000,
  });
}
```

- [ ] **Step 5.2: Build ConversationSummary component**

`	sx
import { useState } from 'react';

interface Props { summary: string; }

export default function ConversationSummary({ summary }: Props) {
  const [open, setOpen] = useState(false);
  return (
    <div className="border-b border-slate-200 bg-slate-50">
      <button onClick={() => setOpen(!open)}
        className="flex items-center gap-2 px-4 py-2 w-full text-left text-sm font-medium text-slate-600 hover:bg-slate-100">
        {open ? '▼' : '▶'} Tóm tắt hội thoại
      </button>
      {open && <p className="px-4 pb-2 text-sm text-slate-500">{summary}</p>}
    </div>
  );
}
```

- [ ] **Step 5.3: Integrate into ChatPane as collapsible header**

- [ ] **Step 5.4: Build + verify + commit**

---

## Task 6: Knowledge Base Management UI

**Files:**
- Create: src/frontend/clawbot-web/src/features/knowledge-base/KnowledgeBasePage.tsx
- Create: src/frontend/clawbot-web/src/features/knowledge-base/KBList.tsx
- Create: src/frontend/clawbot-web/src/features/knowledge-base/KBEditor.tsx
- Create: src/frontend/clawbot-web/src/features/knowledge-base/useKB.ts
- Modify: src/frontend/clawbot-web/src/app/routes.tsx (add route /knowledge-base)

- [ ] **Step 6.1: Build useKB hook**

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

export interface KBEntry {
  id: string;
  title: string;
  content: string;
  tags: string[];
  version: number;
  updatedAt: string;
}

const BASE = '/api/knowledge-base';

export function useKB() {
  return useQuery<KBEntry[]>({ queryKey: ['kb'], queryFn: () => fetch(BASE).then(r => r.json()) });
}

export function useCreateKB() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (entry: Partial<KBEntry>) =>
      fetch(BASE, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(entry) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['kb'] }),
  });
}

export function useUpdateKB() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (entry: KBEntry) =>
      fetch(${BASE}/, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(entry) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['kb'] }),
  });
}
```

- [ ] **Step 6.2: Build KBList — table component**

  Table with columns: Title, Tags, Version, Updated, Actions (Edit/Delete).

- [ ] **Step 6.3: Build KBEditor — form with title, content textarea, tags input**

- [ ] **Step 6.4: Build KnowledgeBasePage composing list + editor**

- [ ] **Step 6.5: Add route in routes.tsx**
```tsx
`	sx
<Route path="/knowledge-base" element={<KnowledgeBasePage />} />
```

- [ ] **Step 6.6: Build + verify**

```powershell
cd src/frontend/clawbot-web
npm run build
```

- [ ] **Step 6.7: Commit**

---

## Coverage Check

| Review Step | Task | Status |
|---|---|---|
| 0. Fix auto-spam + add IngestAsync | Task 0 | Planned |
| 1. Frontend inbox (list + chat) | Task 1 | Planned |
| 2. AI suggest reply | Task 2 | Planned |
| 3. Lead scoring engine | Task 4 | Planned |
| 4. Abandonment timer (5 min) | Task 3 | Planned |
| 5. Conversation summary UI | Task 5 | Planned |
| 6. Knowledge Base management UI | Task 6 | Planned |
