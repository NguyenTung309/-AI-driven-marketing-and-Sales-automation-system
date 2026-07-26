# ClawBot — Module Implementation Checklist

> Persistent tracking. Tick `[x]` khi xong. Nguồn plan: [../C:/Users/AdminDatVo/.claude/plans/wiggly-wandering-blum.md] + [spec-audit.md](spec-audit.md).
> Convention: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong · `[!]` blocked.
>
> Last updated: 2026-06-17

---

## Legend

| Bucket | Imp | Diff | Tuần |
|---|---|---|---|
| **P0** = critical path, fail = no go-live | 1–5 | 1–5 | T1–T13 |

---

## P0 — Critical path (8 module)

### M01 — EF Core DbContext + Migrations wire-up  · Imp 5 · Diff 3 · T1  ✅ **DONE 2026-05-28**
- [x] `AppDbContext : IdentityDbContext<AppUser,AppRole,Guid>, IAppDbContext` — [AppDbContext.cs](../src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs)
- [x] 28 `IEntityTypeConfiguration<T>` in `Persistence/Configurations/` — [DomainModelConfigurations.cs](../src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs) + [ConversationConfiguration.cs](../src/shared/Clawbot.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs)
- [x] Snake_case naming convention via `ApplySnakeCase()` in `OnModelCreating`
- [x] `nvarchar(max)` cho JSON columns (string properties + explicit `HasColumnType` cho `KbVersion.Embedding`)
- [x] Tenant query filter wired via reflection over `ITenantOwned`
- [x] DDL = source of truth (no EF migration — `0001_init.sql` apply manual). Entities tự gen Guid trong factory `Create()`.
- [x] FK relationships: Cascade cho aggregate-internal; Conversation→Message Restrict
- [x] DI register `Clawbot.Api/Program.cs` (đã có) + `Clawbot.AgentService/Program.cs` (✅ added [Program.cs](../src/agents/Clawbot.AgentService/Program.cs))
- [x] AgentService appsettings.json bổ sung ConnectionStrings + Encryption + Vector
- [x] Build xanh: `dotnet build Clawbot.sln` → 12 projects, 0 errors, 0 warnings
- [x] Integration test: `Testcontainers.MsSql` apply DDL + smoke insert/select → **done M21** — [DatabaseSmokeTests.cs](../tests/Clawbot.Integration.Tests/DatabaseSmokeTests.cs)

### M02 — Tenant scoping + RBAC + JWT/2FA + Identity migrate · Imp 5 · Diff 3 · T1–T2  ✅ **DONE 2026-05-28** (consumer-side ApiKey scheme + Permission policy deferred)
- [x] `HasQueryFilter` global cho `ITenantOwned` — wired M01 via reflection in `AppDbContext.OnModelCreating`
- [x] `TenantId` claim → `HttpTenantAccessor` (đã có); login fixed `tenant_slug` lookup từ `Tenants` table
- [x] Custom RBAC link: `RolePermission` entity + EF config — [RolePermission.cs](../src/shared/Clawbot.Domain/Security/RolePermission.cs)
- [x] Seed default Identity roles `Admin/Sale/Marketer/QA/Viewer` — [RbacSeeder.cs](../src/shared/Clawbot.Infrastructure/Identity/RbacSeeder.cs), called from `Program.cs`
- [x] `RolesEndpoints.cs` CRUD (`/api/rbac/roles` GET/POST/PUT/DELETE + `/{id}/permissions` GET/PUT) — [RolesEndpoints.cs](../src/api/Clawbot.Api/Endpoints/RolesEndpoints.cs)
- [x] `PermissionsEndpoints` list (`GET /api/rbac/permissions`) — same file
- [x] JWT token issues `perm` claim list (computed via `RolePermissions ⋈ RbacRoles ⋈ Permissions` for user's roles) — [JwtTokenIssuer.cs](../src/api/Clawbot.Api/Auth/JwtTokenIssuer.cs)
- [x] 2FA TOTP: `POST /auth/2fa/enable` (issue authenticator key + otpauth URI) + `/auth/2fa/verify` + `/auth/2fa/disable` + `/auth/login/2fa` (full flow with code) — [AuthEndpoints.cs](../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs)
- [x] Password reset: `POST /auth/reset/request` sinh OTP 6 số, email/log OTP, cache mapping Identity token 10 phút + `/auth/reset/confirm` nhận OTP qua field `token`
- [x] Account lockout: 5 fail attempts × 15 min — Identity options trong `DependencyInjection.cs`
- [x] `api_keys` CRUD issuer (`GET/POST/DELETE /api/api-keys`) với SHA-256 hash + plaintext-once return — [ApiKeysEndpoints.cs](../src/api/Clawbot.Api/Endpoints/ApiKeysEndpoints.cs)
- [x] `GET /auth/me` whoami endpoint
- [x] Build xanh: `dotnet build` → 12 projects, 0 errors, 0 warnings
- [x] `[Authorize(Policy="perm:...")]` AuthorizationHandler reads `perm` claim → **done M02b** — [PermissionAuthorizationHandler.cs](../src/api/Clawbot.Api/Auth/PermissionAuthorizationHandler.cs)
- [x] ApiKey bearer scheme cho incoming auth (consume issued keys) → **done M02b** — [ApiKeyAuthenticationHandler.cs](../src/api/Clawbot.Api/Auth/ApiKeyAuthenticationHandler.cs)
- [x] Test cross-tenant query returns 0 rows → **done M21** — [DatabaseSmokeTests.cs](../tests/Clawbot.Integration.Tests/DatabaseSmokeTests.cs); model-cache regression guard in [TenantFilterModelCacheTests.cs](../tests/Clawbot.Api.Tests/TenantFilterModelCacheTests.cs)
- [x] Tenant-scoped custom Role rows seeded → `RbacSeeder.SeedAsync` duyệt mọi tenant hiện có, tạo `RbacRoles` mặc định per-tenant và link `RolePermissions`; self-register đã bỏ theo quyết định admin-provisioned.

### M04 — Knowledge Base CRUD + versioning + accuracy test · Imp 5 · Diff 4 · T1–T3  ✅ **endpoints DONE 2026-05-28** (content seed + RAG-backed test exec deferred)
- [x] `KbEndpoints.cs` — module CRUD (`/api/kb/modules`) + version CRUD (`/{id}/versions`) — [KbEndpoints.cs](../src/api/Clawbot.Api/Endpoints/KbEndpoints.cs)
- [x] `POST /api/kb/modules/{id}/versions/{versionId}/deploy` zero-downtime (archive existing deployed in same tx)
- [x] `POST /api/kb/modules/{id}/versions/{versionId}/rollback` (alias of deploy for audit clarity)
- [x] `POST /api/kb/modules/{id}/test` run all active test cases against current deployed version, returns `KbTestRunResult` with per-case + aggregate score
- [x] `GET /api/kb/accuracy` aggregate dashboard `KbAccuracySummary[]`
- [x] Archive endpoint `POST /api/kb/modules/{id}/archive` (soft-delete + status archived)
- [x] Diff viewer `GET /api/kb/modules/{id}/diff?fromVersion=&toVersion=` (line-based unified diff)
- [x] Test case CRUD (`/api/kb/modules/{id}/test-cases`) + DTOs in `Clawbot.Api.Contracts/KnowledgeBase/KbDtos.cs`
- [x] Build xanh 12 projects, 0/0
- [x] **Test runner uses RAG + Claude JSON evaluator** — `KbTestRunnerService` retrieves module context, asks Claude to grade support, parses JSON robustly, and returns evaluator reason per test case
- [x] Qdrant embedding sync khi deploy: SQL `embedding` JSON + Qdrant upsert → `KbDeployService` stores `KbVersion.Embedding` JSON and upserts Qdrant chunks, covered by `KbDeployServiceTests`
- [!] KB seed 6 module tiếng Trung (`deploy/seed/kb-modules.sql`) → **blocked: needs P3 Sales + Học thuật input per architecture doc T1–T2**; authoring gate added via `deploy/seed/kb-authoring.required.json`, `deploy/seed/kb-authoring.template.json`, `deploy/seed/validate-kb-authoring.ps1`, and `deploy/seed/generate-kb-seed.ps1` to generate idempotent `kb_modules`/`kb_versions`/`kb_test_cases` SQL after content is approved. Generator has `-SmokeTest` runtime verification for 6 modules + 120 generated test-case inserts.
- [!] kb_test_cases seed 20 câu / module → **blocked cùng KB seed**; validator requires at least 20 non-placeholder `{question, expectedAnswer}` pairs per required module before content is accepted.
- [x] Alert when accuracy drop <85% (NFR-05) → `KbAccuracyTestJob` alerts deployed KB versions below 85%, covered by `KbAccuracyTestJobTests`

### M06 — Pancake unified channel adapter (replaces Zalo/FB/IG/TikTok/YT native) · Imp 5 · Diff 3 · T2–T4 · **DONE 2026-05-29**
**Strategy pivot (2026-05-29):** Drop native per-platform adapters. Use Pancake (pancake.vn / pages.fm) as unified omnichannel proxy. All 5 channels (Facebook Page/Messenger/Comments, Instagram, Zalo OA, TikTok Shop, WhatsApp, Google Business) routed through a single Pancake account. Reason: Pancake already handles vendor SDK churn, OAuth refresh, comment-vs-DM routing, rate limit. We integrate once.
- [x] `PancakeConfig` domain entity tenant-scoped (BaseUrl, AccessTokenEncrypted, WebhookSecretEncrypted, SignatureHeader/Algo/Encoding, SendPathTemplate, AuthMode) — [PancakeConfig.cs](../src/shared/Clawbot.Domain/Channels/PancakeConfig.cs)
- [x] `pancake_configs` table UNIQUE(tenant_id), max 2048-char encrypted secrets
- [x] `IPancakeConfigResolver` + `PancakeConfigResolver` resolves: tenant DB row → appsettings `Channels:Pancake:*` → defaults — [PancakeConfigResolver.cs](../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeConfigResolver.cs)
- [x] `PancakeChannelAdapter` rewritten to consume runtime config (no hard-coded URL/secret) — [PancakeChannelAdapter.cs](../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs)
- [x] Webhook signature: header name + algo (`hmac-sha256`) + encoding (`hex`/`base64`) all configurable per-tenant; uses `HmacSignatureVerifier.FixedTimeEquals`
- [x] Outbound send: `POST {BaseUrl}{SendPathTemplate}` with placeholder substitution `{page_id}` + `{thread_id}` from composite `external_thread_id`
- [x] Auth modes: `query` (`?access_token=`) or `bearer` (`Authorization: Bearer`)
- [x] AES encrypt `access_token` + `webhook_secret` via `IEncryptor` (existing `AesEncryptor`)
- [x] Inbound parser maps Pancake webhook JSON → `ChannelMessage[]` with `external_message_id` + `display_name` + `page_id` metadata
- [x] Webhook → ingestor pipeline already wired in M08: `POST /webhooks/pancake/{tenantSlug}` → verify → parse → ingest loop → SignalR push
- [x] Polly retry + circuit breaker + 10s timeout via existing `HttpResiliencePolicies` (M01)
- [x] CRUD endpoint `/api/channels/pancake/config` GET/PUT/DELETE + `/webhook-url` (returns the tenant-specific webhook URL to copy into Pancake dashboard) — [ChannelsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/ChannelsEndpoints.cs)
- [x] `PancakeConfigDto` returns `HasAccessToken` + `HasWebhookSecret` boolean only — never echoes plaintext or ciphertext back to client
- [x] Build xanh 12 projects, 0/0
- [x] EF migration to add `pancake_configs` table → schema present in `deploy/migrations/0001_init.sql` and EF maps `PancakeConfig`
- [!] Real Pancake account + access_token + webhook_secret → ops setup (not code)
- [!] First webhook empirical test → blocked until live Pancake tenant/payload sample; `SignatureHeader` / `SignatureEncoding` / payload field names are swappable via PUT `/api/channels/pancake/config` without redeploy. Replay harness `deploy/pancake-webhook-replay.ps1` signs captured payloads and posts to `/webhooks/pancake/{tenantSlug}`.
- [x] Health check `/health/channels/pancake` → after first successful round-trip — [HealthEndpoints.cs](../src/api/Clawbot.Api/Endpoints/HealthEndpoints.cs)
- [x] Per-tenant outbound rate limit (Pancake quotas) — token bucket 120/min/tenant — [PancakeChannelAdapter.cs](../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs)
- [x] Integration test mock Pancake vendor → M21 — [DatabaseSmokeTests.cs](../tests/Clawbot.Integration.Tests/DatabaseSmokeTests.cs) (M06 roundtrip test)

### M08 — Omnichannel Inbox API + unified conversation merge · Imp 5 · Diff 3 · T4 · **DONE 2026-05-29**
- [x] `InboxEndpoints.cs` (`GET /api/inbox/conversations` paged + filter status/platform) — [InboxEndpoints.cs](../src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs)
- [x] Query order: last_msg_at desc (lead.score join → defer to M15)
- [x] `ChannelMessageIngestor` find-or-create contact via `contact_external_ids` — [ChannelMessageIngestor.cs](../src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs)
- [x] Dedup `(conversationId, content, sentAt, in)` heuristic when no `external_message_id`
- [x] Conversation upsert via UNIQUE `(tenant_id, platform, external_thread_id)` index
- [x] `AssignAsync` (`POST /api/inbox/conversations/{id}/assign`)
- [x] `ResolveAsync` + `EscalateAsync` status transitions
- [x] `SendOutboundAsync` → `IChannelAdapter.SendAsync` + append outbound message
- [x] SignalR `InboxHub` per-tenant group + `SignalRInboxNotifier` push message/conversation events — [InboxHub.cs](../src/api/Clawbot.Api/Hubs/InboxHub.cs)
- [x] Webhook wired: `POST /webhooks/pancake/{tenantSlug}` → verify → parse → ingest loop — [WebhookEndpoints.cs](../src/api/Clawbot.Api/Endpoints/WebhookEndpoints.cs)
- [x] Build xanh 12 projects, 0/0
- [x] `MergeContactsCommand` cross-platform stitching → done W6.13 — [ContactsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/ContactsEndpoints.cs)
- [x] Full-text search `GET /api/inbox/search?q=` → `InboxSearchService` + API route + SQL Server FTS migration `0020_inbox_fulltext.sql`, with SQLite fallback covered by `InboxSearchServiceTests`
- [x] Export conversation log `GET /api/inbox/conversations/{id}/export.csv` — CSV ordered by `sent_at`, uses `redacted_content` when available
- [x] `external_message_id` column on `messages` for strict dedup → done W6.12 — [0007_messages_external_id.sql](../deploy/migrations/0007_messages_external_id.sql) adds the column; [0023_messages_external_id_index.sql](../deploy/migrations/0023_messages_external_id_index.sql) adds the filtered unique index in a later batch; [ChannelMessageIngestor.cs](../src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs) consumes it.
- [x] Lead score join in list ordering — hot-first then recency — [InboxEndpoints.cs](../src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs)

### M09 — Semantic Kernel + RAG (Qdrant) · Imp 5 · Diff 5 · T3–T4 · **SPIKE LANDED 2026-05-28**
- [x] Spike RFC: SK plugin-host only + Anthropic SDK direct chosen — [.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md](../.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md)
- [x] `QdrantVectorStore` real impl (auto-create collection + cosine, upsert/search/delete) — [QdrantVectorStore.cs](../src/shared/Clawbot.Infrastructure/Vectors/QdrantVectorStore.cs)
- [x] `IEmbeddingProvider` + `HashEmbeddingProvider` (384-dim deterministic stub) — [HashEmbeddingProvider.cs](../src/agents/Clawbot.Agents.Core/Rag/HashEmbeddingProvider.cs)
- [x] `IRagRetriever` + `QdrantRagRetriever` (tenant + module_code payload filter, top-K) — [QdrantRagRetriever.cs](../src/agents/Clawbot.Agents.Core/Rag/QdrantRagRetriever.cs)
- [x] `RagModule.AddClawbotRag()` DI extension, wired in `AgentService/Program.cs`
- [x] Pipeline shape proven: query → embed → Qdrant top-K → metadata filter → snippets
- [x] Citation surfaces via `RagChunk.KbVersionId`
- [x] Build xanh 12 projects, 0/0
- [x] Real embedding model (Voyage AI / OpenAI / local SBERT) → defer per RFC open question — [OpenAiEmbeddingProvider.cs](../src/agents/Clawbot.Agents.Core/Rag/OpenAiEmbeddingProvider.cs) (config-gated, falls back to HashEmbeddingProvider)
- [x] Anthropic Messages API chat completion + streaming → **M10** — `AnthropicChatClient` covers completion request/cost mapping and SSE `text_delta` streaming via `AnthropicChatClientTests` (direct HTTP per RFC-001)
- [x] Redis cache `(tenant, kb_versions_hash, query_hash)` TTL 1h → **M10** — [CachedRagRetriever.cs](../src/agents/Clawbot.Agents.Core/Rag/CachedRagRetriever.cs)
- [x] `IClaudeCostTracker` per-call emission → **M11 P0 skill** — done M11
- [x] Wire `KbVersion.Deploy` → embed `content_md` chunks → Qdrant upsert → **done W5** — [KbDeployService.cs](../src/agents/Clawbot.Agents.Core/Kb/KbDeployService.cs)
- [x] Wire `KbEndpoints.RunTestAsync` to call `IRagRetriever` + LLM → **done W5** — [KbEndpoints.cs](../src/api/Clawbot.Api/Endpoints/KbEndpoints.cs)
- [!] Accuracy ≥85% on 20-câu test set → **blocked until content seed + real embedder + LLM**

### M10 — Agent-Chat (gRPC) — reply 5 kênh · Imp 5 · Diff 4 · T4–T6 · **DONE 2026-05-29**
- [x] Impl `ChatAgentGrpcService.Reply` (replaced stub) — [ChatAgentGrpcService.cs](../src/agents/Clawbot.AgentService/Services/ChatAgentGrpcService.cs)
- [x] `IClaudeChatClient` + `AnthropicChatClient` direct HTTP per RFC-001 — [AnthropicChatClient.cs](../src/agents/Clawbot.Agents.Core/Chat/AnthropicChatClient.cs)
- [x] `ChatAgent` orchestrator: RAG retrieve → system prompt build → Claude call — [ChatAgent.cs](../src/agents/Clawbot.Agents.Core/Chat/ChatAgent.cs)
- [x] RAG fallback (M09 retriever) embedded into system prompt with citation index
- [x] gRPC server-streaming reply (`Final=true` token; multi-chunk streaming → later optimization)
- [x] Persist `agent_sessions` + `agent_traces` + outbound `messages` append on conversation
- [x] Latency + token + USD cost tracked in trace
- [x] `AnthropicOptions` config (`Anthropic:ApiKey/Model/MaxTokens/InputUsdPer1M/OutputUsdPer1M`)
- [x] Build xanh 12 projects, 0/0
- [x] Intent classify via `IIntentClassifier` — `ChatAgent.ReplyAsync` classifies redacted inbound text before RAG/system prompt build; covered by M11 ChatAgent wiring tests.
- [x] Match `chat_scenarios` template by trigger + platform — `ChatAgentGrpcService` loads tenant scenarios, matches with `ChatScenarioMatcher` using conversation platform, and passes the matched response template into `ChatAgent` system prompt.
- [x] PII redact inbound via `IPiiRedactor` — `ChatAgent.ReplyAsync` redacts user text before intent/RAG/Claude.
- [x] Prompt injection guard via `IPromptInjectionDefender` — malicious input returns blocked reply before model call.
- [x] Toxicity filter output via `IToxicityFilter` — inbound toxicity blocks user messages and outbound toxicity blocks generated replies.
- [x] Token-by-token streaming (SSE-style chunk) → P2 optimization — `ChatAgentGrpcService` streams Claude `text_delta` chunks through gRPC `ChatToken` before the final marker, covered by `ChatAgentGrpcServiceTests`
- [x] **Comment auto-reply (Agent-Chat L2)** — Pancake `COMMENT` webhook shape maps to `message_type=comment`/`parent_post_id`; webhook enqueues `CommentAutoReplyJob` to reply + send DM invite for purchase/price/trial intent. Live tenant payload field names still need ops verification.
- [x] Escalation rule (confidence<threshold | intent=escalation → assign sale) → done W5 — [ChatAgent.cs](../src/agents/Clawbot.Agents.Core/Chat/ChatAgent.cs)
- [x] Out-of-hours auto-reply (UC-A07) scheduled scenario → done W5 — [OutOfHoursAutoReplyJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/OutOfHoursAutoReplyJob.cs)
- [x] p95 latency Serilog+OTel histograms — OpenTelemetry (ASP.NET/HttpClient/runtime) — [TelemetryModule.cs](../src/shared/Clawbot.Infrastructure/Observability/TelemetryModule.cs) (OTLP exporter deferred — audit GHSA-4625-4j76-fww9)
- [x] Cost tracker (`IClaudeCostTracker.RecordAsync`) wire → done M11 (`ChatAgent` records per-call cost)

### M14 — Agent-SaleAssist · Imp 5 · Diff 3 · T5 · **DONE 2026-05-29**
- [x] Impl `SaleAssistAgentGrpcService` Draft + Summarize — [SaleAssistAgentGrpcService.cs](../src/agents/Clawbot.AgentService/Services/SaleAssistAgentGrpcService.cs)
- [x] `SaleAssistAgent` core: RAG-grounded draft + 3-bullet summary via Claude — [SaleAssistAgent.cs](../src/agents/Clawbot.Agents.Core/SaleAssist/SaleAssistAgent.cs)
- [x] Action inference (book_trial/send_quote/ask_goal/follow_up) heuristic
- [x] Lead score hint from recent turn count (interim until M15 lead lookup)
- [x] `POST /api/sale-assist/draft` returns Claude-drafted reply — [SaleAssistEndpoints.cs](../src/api/Clawbot.Api/Endpoints/SaleAssistEndpoints.cs)
- [x] `POST /api/sale-assist/summary` thread summary
- [x] `quick_reply_templates` CRUD: GET/POST/PUT/DELETE `/api/sale-assist/quick-replies`
- [x] API → AgentService via `Grpc.Net.ClientFactory` (`SaleAssistAgentClient` typed client)
- [x] Build xanh 12 projects, 0/0
- [x] Alert job: conversation idle >5 min → SignalR/in-app → done W6.6 — [IdleConversationAlertJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/IdleConversationAlertJob.cs)
- [x] Context panel API: lead history + score + next-step → done W6.7 — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [x] Upsell suggestion when lead.stage='hot' + gói ngắn → done W6.8 — [SaleAssistEndpoints.cs](../src/api/Clawbot.Api/Endpoints/SaleAssistEndpoints.cs)
- [x] Sale tone check before send — `SendOutboundAsync` calls `OutboundMessageSafetyService` + `IToxicityFilter` before `IChannelAdapter.SendAsync`; blocked content returns 400.
- [x] Daily summary endpoint `GET /api/sale-assist/daily-summary` → done W6.9 — [SaleAssistEndpoints.cs](../src/api/Clawbot.Api/Endpoints/SaleAssistEndpoints.cs)

### M15 — Lead scoring + dedup + drip · Imp 5 · Diff 3 · T7 · **DONE 2026-05-29**
- [x] Impl `LeadAgentGrpcService.Score` — [LeadAgentGrpcService.cs](../src/agents/Clawbot.AgentService/Services/LeadAgentGrpcService.cs)
- [x] `LeadScoringEngine.Evaluate` static rule evaluator: sum weights matching event_code (+optional platform) — [LeadScoringEngine.cs](../src/agents/Clawbot.Agents.Core/Lead/LeadScoringEngine.cs)
- [x] Domain `Lead.AdjustScore` → stage classifier cold<30, warm 30–70, hot≥70 (already in entity)
- [x] `ILeadDedupService` + `EfLeadDedupService`: contact match + phone/email join — [EfLeadDedupService.cs](../src/shared/Clawbot.Infrastructure/Leads/EfLeadDedupService.cs)
- [x] `ILeadAssignmentService` + `LeastBusyLeadAssignmentService` + `EfAssignmentPoolSource` (Sale role) — [LeadAssignmentService.cs](../src/agents/Clawbot.Agents.Core/Lead/LeadAssignmentService.cs)
- [x] `POST /api/leads` (auto-dedup + auto-assign on create) — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [x] `POST /api/leads/{id}/activities` (record event → engine evaluate → score adjust)
- [x] `POST /api/leads/{id}/assign` (explicit or least-busy)
- [x] `GET/POST /api/lead-scoring-rules` + soft-deactivate
- [x] `GET /api/leads` paginated, ordered by score desc → last_activity_at
- [x] Build xanh 12 projects, 0/0
- [x] `lead_scoring_rules` seed defaults (asks_price+10, shares_phone+20, etc.) → done W6.1 — [lead-scoring-rules.sql](../deploy/seed/lead-scoring-rules.sql)
- [x] Qdrant similarity dedup ≥0.92 on (name+phone tail+email embedding) → `QdrantLeadDeduplicator` threshold 0.92 + `ContactEmbeddingSync`, covered by `QdrantLeadDeduplicatorTests`
- [x] **Score-change reason logging (Agent-Lead L1)** — `Lead.AdjustScore` records `score_adjust` activity metadata with previous/new score, actual delta, requested delta, previous/new stage, and reason.
- [x] Drip sequences (per-channel templates) → done W6.2 — [0008_drip_sequences.sql](../deploy/migrations/0008_drip_sequences.sql) + [DripSequenceJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/DripSequenceJob.cs)
- [x] No-show follow-up 2h after demo missed → done W6.3 — [LeadFollowUpJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/LeadFollowUpJob.cs)
- [x] Re-engage stale lead 30d → done W6.4 — [LeadFollowUpJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/LeadFollowUpJob.cs)
- [x] Pipeline forecast endpoint `GET /api/leads/forecast` → done W6.5 — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [x] Lead import/export CSV → done P3 — `LeadCsvService` + `GET /api/leads/export.csv` / `POST /api/leads/import.csv`, covered by `LeadCsvServiceTests`
- [x] Hot-lead alert (≥70đ) <2 min → `LeadBecameHotConsumer` auto-assigns least-busy Sale + publishes `hot_lead`

---

## P1 — High (8 module)

### M03 — Audit log + PII redaction + retention · Imp 4 · Diff 2 · T1–T2 · **DONE 2026-05-29**
- [x] `AuditSaveChangesInterceptor` writes `audit_logs` per Add/Modify/Delete (skips AuditLog itself to avoid recursion) — [AuditSaveChangesInterceptor.cs](../src/shared/Clawbot.Infrastructure/Audit/AuditSaveChangesInterceptor.cs)
- [x] Diff JSON: Add → snapshot, Delete → from=value/to=null, Modify → {from, to} per changed property
- [x] Sensitive-name blocklist drops PasswordHash/SecurityStamp/AccessToken/RefreshToken/ApiKey/Secret/Token from diff
- [x] PII redact via `IPiiRedactor` (M11) on every string field before serialize
- [x] `IAuditContext` + `HttpAuditContext` (resolves `sub` claim + RemoteIp + UA) — [HttpAuditContext.cs](../src/shared/Clawbot.Infrastructure/Audit/HttpAuditContext.cs)
- [x] `AuditBehavior` MediatR pipeline (timing + success/fail event ids 6001/6002) — [AuditBehavior.cs](../src/shared/Clawbot.Application/Common/Behaviors/AuditBehavior.cs)
- [x] EF interceptor registered in `AddDbContext` via service-provider overload
- [x] 30-day retention: `RetentionPurgeJob` (M12) purges `audit_logs` daily 02:00
- [x] Build xanh 12 projects, 0/0
- [x] PII redact on `messages.content` insert path (separate from audit diff) → done W6.10 — [ChannelMessageIngestor.cs](../src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs) + [0009_messages_pii_split.sql](../deploy/migrations/0009_messages_pii_split.sql)
- [x] Audit viewer endpoint `GET /api/admin/audit-logs?filter=` → done W6.11 — [AdminEndpoints.cs](../src/api/Clawbot.Api/Endpoints/AdminEndpoints.cs)
- [x] Retention job for `messages.content` >30d → schema needs `original_content` vs `redacted_content` split → done W6.10

### M05 — 50 chat scenarios seed · Imp 4 · Diff 2 · T2–T3 · **DONE 2026-06-03**
- [x] `deploy/seed/chat-scenarios.sql` 50 row (KB-001..KB-050) — idempotent MERGE on `(tenant_id, code)`, parameterized `@tenant_slug` — [chat-scenarios.sql](../deploy/seed/chat-scenarios.sql)
- [x] `MatchScenarioQuery` handler (trigger regex + platform filter) — pure `ChatScenarioMatcher` (regex→substring fallback, longest-match specificity, success-rate tiebreak) + `POST /api/chat-scenarios/match` — [ChatScenarioMatcher.cs](../src/shared/Clawbot.Domain/ChatScenarios/ChatScenarioMatcher.cs)
- [x] CRUD endpoint `GET/POST/PUT/DELETE /api/chat-scenarios` (+ `GET /{id}`, filter `?group=&platform=`) — [ChatScenariosEndpoints.cs](../src/api/Clawbot.Api/Endpoints/ChatScenariosEndpoints.cs); replaced `/api/scenarios` 501 stub
- [x] Group: First / Lộ trình / Objection / Action / Platform / Follow-up (50 rows distributed 8/10/12/9/6/5)
- [x] Success rate tracker: `POST /api/chat-scenarios/{id}/outcome` → `ChatScenario.RecordOutcome(converted)` EMA (α=0.1) into `success_rate`
- [x] Unit tests: 10 cases in [ChatScenarioMatcherTests.cs](../tests/Clawbot.Domain.Tests/ChatScenarios/ChatScenarioMatcherTests.cs) (regex/substring/platform/tiebreak/EMA/Update) — Domain.Tests 20/20 green
- [x] EF migration to add `chat_scenarios` rows is data-seed only (DDL already in `0001_init.sql`) — `chat-scenarios.sql` is tenant-scoped/idempotent and now guarded by transaction + 50-row assertion; KB-tone refinement remains after real conversion data lands

### M07 — ~~TikTok/IG/YT native adapters~~ → SUPERSEDED by M06 Pancake unified · 2026-05-29
**No longer planned.** All 5 channels (Facebook, Instagram, TikTok Shop, WhatsApp, Google Business, Zalo OA) routed via Pancake per M06 strategy pivot. Reasons:
- Vendor SDK churn (TikTok Business API breaks q/q, IG Graph deprecates fields, YT comment quota draconian)
- OAuth refresh complexity × 3 vendors = 3 refresh failure modes
- Comment-vs-DM routing already solved by Pancake unified inbox
- Single billing relationship vs 3 vendor accounts
- If Pancake outage / disagreement: re-evaluate. Migration path: implement individual adapters under same `IChannelAdapter` interface — schema + ingestor pipeline (M08) unchanged.
- [x] Webhook subscription/replay scripts trong `deploy/` — [pancake-webhook-subscribe.ps1](../deploy/pancake-webhook-subscribe.ps1) and [pancake-webhook-replay.ps1](../deploy/pancake-webhook-replay.ps1) are env-driven with `-DryRun`, covered by `PancakeWebhookSubscribeScriptTests`
- [x] Polly retry với exponential backoff → existing `HttpResiliencePolicies.Retry()` + DI typed clients, covered by `HttpResiliencePoliciesTests`
- [x] Health checks 3 channel → superseded by Pancake unified health report with 3 checks (`config`/`outbound`/`webhook`) via `ChannelHealthService`

### M11 — 22 utility skills concrete impl · Imp 4 · Diff 4 · T3–T10 incremental · **P0 SUBSET DONE 2026-05-29**
**P0 skills (T3) — heuristic baseline (vendor swap later):**
- [x] `IIntentClassifier` — `KeywordIntentClassifier` (VI/EN/中 keyword buckets) — vendor swap: phobert-base-v2 ONNX
- [x] `ISentimentAnalyzer` — `LexiconSentimentAnalyzer` (positive/negative lexicons) — vendor swap: phobert-vietnamese-sentiment
- [x] `IPiiRedactor` — `RegexPiiRedactor` (VN phone, email, 12-digit CCCD via GeneratedRegex) — vendor swap: Presidio sidecar
- [x] `IPromptInjectionDefender` — `HeuristicPromptInjectionDefender` (suspicious-phrase list VI/EN) — vendor swap: Lakera/llm-guard
- [x] `IClaudeCostTracker` — `InMemoryClaudeCostTracker` (ConcurrentDictionary keyed by tenant+year+month, $200 cap) — vendor swap: SQLite ledger + OTel `gen_ai.*`
- [x] Wired into `ChatAgent`: injection check → block → PII redact → intent → RAG → Claude → cost.RecordAsync
- [x] `ChatAgentReply` now carries `Intent` + `Blocked` + `BlockReason` for tracing
- [x] Build xanh 12 projects, 0/0

**P1 skills (T5–T7):**
- [x] `IConversationSummarizer` — Claude SK (config-externalized prompt via IClaudeChatClient)
- [x] `ILanguageDetector` — heuristic Unicode/diacritic + optional fasttext sidecar
- [x] `ISpamDetector` — heuristic URL/emoji/scam-keyword + optional Akismet HTTP
- [x] `IToxicityFilter` — heuristic VI/EN profanity lexicon + optional detoxify sidecar
- [x] `ILeadDeduplicator` — Qdrant cosine via IEmbeddingProvider + IVectorStore
- [x] `IContactEnricher` — config-gated Hunter/Apollo HTTP + heuristic email-domain fallback
- [x] `ITimezoneDetector` — heuristic E.164 country-code → IANA map (VN default)
- [x] ChatAgent wired: language→system prompt, toxicity→inbound/outbound block, spam→flag
- [x] Lead create wired: dedup + enrich + timezone + spam via gRPC CreateWithSkills
- [x] SaleAssist wired: auto-summary via IConversationSummarizer + tone check via IToxicityFilter
- [x] Contacts→Qdrant: upsert on contact create via ContactEmbeddingSync + backfill script
- [x] Config: Skills:* in appsettings.json (×2) + deploy/.env.example
- [x] Tests: 7 skills + ChatAgent wiring (P1NlpSkillTests + P1LeadSkillTests + ChatAgentWiringTests)
- [x] Build xanh 12 projects, 0/0

**P2 skills (T8–T10):**
- [x] `IHashtagResearcher` — TikTok CC + Google Trends VN + heuristic fallback — [IHashtagResearcher.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IHashtagResearcher.cs)
- [x] `IZhScriptValidator` — OpenCCNET + Unicode range detection — [IZhScriptValidator.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IZhScriptValidator.cs)
- [x] `IImagePromptGenerator` — Claude visual-prompt via IClaudeChatClient — [IImagePromptGenerator.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IImagePromptGenerator.cs)
- [x] Content image-prompt API — `POST /api/content/image-prompts` validates brief/platform/style/brand tokens and calls `IImagePromptGenerator`
- [x] `IVideoScriptComposer` — Hook/Value/CTA JSON schema via Claude — [IVideoScriptComposer.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IVideoScriptComposer.cs)
- [x] `IViZhTranslator` — Claude + glossary tracking — [IViZhTranslator.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IViZhTranslator.cs)
- [x] `ICompetitorMonitor` — RSS via System.Xml.Linq + URL dedupe — [ICompetitorMonitor.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/ICompetitorMonitor.cs)
- [x] `IPdfTableRenderer` — QuestPDF table renderer — [IPdfTableRenderer.cs](../src/agents/Clawbot.Agents.Core/Skills/Ops/IPdfTableRenderer.cs)
- [x] `IQrGenerator` — QRCoder PNG — [IQrGenerator.cs](../src/agents/Clawbot.Agents.Core/Skills/Ops/IQrGenerator.cs)
- [x] `IAnomalyDetector` — Math.NET z-score — already done M20
- [x] `IForecaster` — ML.NET TimeSeries SSA — already done M20

### M12 — Scheduled job runner (Hangfire) · Imp 4 · Diff 2 · T2 · **DONE 2026-05-29**
- [x] Hangfire registered with SQL Server storage (auto-schema, 5min batch timeout) — [HangfireModule.cs](../src/shared/Clawbot.Infrastructure/Jobs/HangfireModule.cs)
- [x] Hangfire dashboard `/hangfire` mounted with admin-only filter (`perm=admin.system` or `Admin` role) — [HangfireAdminFilter.cs](../src/api/Clawbot.Api/Auth/HangfireAdminFilter.cs), covered by `HangfireDashboardSecurityTests`
- [x] `RetentionPurgeJob` daily 02:00 — purges `audit_logs` >30d via `ExecuteDeleteAsync` — [RetentionPurgeJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/RetentionPurgeJob.cs)
- [x] `DailyKpiRollupJob` daily 07:30 — aggregate leads/conversations/replies/conversions per tenant → `kpi_daily` (platform=`all`) — [DailyKpiRollupJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/DailyKpiRollupJob.cs)
- [x] Worker queues: default/retention/kpi
- [x] Build xanh 12 projects, 0/0
- [x] Admin-only auth filter on `/hangfire` dashboard → tighten before prod — [HangfireAdminFilter.cs](../src/api/Clawbot.Api/Auth/HangfireAdminFilter.cs)
- [x] `messages` retention purge (>30d) — RetentionPurgeJob nulls original_content, keeps redacted — [RetentionPurgeJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/RetentionPurgeJob.cs)
- [x] `DailyReportJob` (UC-I01) — push tổng hợp 7h30 qua SignalR/in-app notification (`daily_report`) từ `kpi_daily` platform=`all` — [DailyReportJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/DailyReportJob.cs)
- [x] `DripSequenceJob` per-lead → sends due personalized steps via `IChannelAdapter`, appends outbound conversation messages, and advances/completes enrollments; covered by `DripSequenceJobTests`
- [x] `KbAccuracyTestJob` (daily) — alerts deployed KB <85% via SignalR — [KbAccuracyTestJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/KbAccuracyTestJob.cs) (real scores after embedder+content)
- [x] `HealthCheckJob` (hourly) — [HealthCheckJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/HealthCheckJob.cs)

### M13 — Rate-limit middleware + Webhook HMAC verify · Imp 4 · Diff 2 · T2 · **DONE 2026-05-29**
- [x] `RateLimitingExtensions.AddClawbotRateLimiting` 4 policies: auth(10/min), webhook(120/min), chat(60/min), general(300/min) + global 600/min — [RateLimitingExtensions.cs](../src/api/Clawbot.Api/Middleware/RateLimitingExtensions.cs)
- [x] Partition keys: IP for auth/webhook, sub/tenant_id/IP fallback for chat/general
- [x] `app.UseRateLimiter()` wired between AuthZ and routing
- [x] `HmacSignatureVerifier.VerifyHexSha256` + `VerifyBase64Sha256` (constant-time `CryptographicOperations.FixedTimeEquals`) — [HmacSignatureVerifier.cs](../src/shared/Clawbot.SharedKernel/Security/HmacSignatureVerifier.cs)
- [x] Pancake adapter wired to verifier with `Channels:Pancake:WebhookSecret` config + `x-pancake-signature` header — [PancakeChannelAdapter.cs](../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs)
- [x] Pancake `ParseAsync` real JSON → `ChannelMessage[]` with `external_message_id` + `display_name` metadata
- [x] Pancake `SendAsync` real POST to `/api/v1/messages` with Bearer auth (resilience via existing HttpResiliencePolicies)
- [x] Build xanh 12 projects, 0/0
- [x] Apply rate-limit policies to endpoint groups (`.RequireRateLimiting(AuthPolicy)` etc.) → next session pass
- [x] Vendor-specific verifiers — Pancake unified verifier uses configurable header/algo/encoding and verifies HMAC-SHA256 hex/base64 signatures via `PancakeChannelAdapter.VerifyWebhookSignatureAsync`; live payload validation remains under M06 ops.
- [x] 401 audit log on reject → after M03 audit interceptor lands — [WebhookEndpoints.cs](../src/api/Clawbot.Api/Endpoints/WebhookEndpoints.cs)

### M16 — Frontend UI (12 surface + S17 public) · Imp 4 · Diff 4 · T4–T11 · **DONE 2026-06-17** — Stitch surfaces wired for web/backend scope
> Stack: React 19 + Vite + TS + Tailwind 4 + Router 7 + TanStack Query + Zustand. Design rules + screen checklist: [Design.md](Design.md). Nguồn: Stitch project `12301695846158842476`.
- [x] **FE base** — design tokens (`@theme`) + `AppShell` + `Sidebar` (260px đỏ) + `Topbar` + `AuthCardShell`
- [x] **UI primitives** — Button · Card · StatusPill · MetricCard · ToggleSwitch · Input · DataTable · WorkflowNode · Modal · Alert
- [x] Login + 2FA flow — split-screen + states (error/locked/loading); Quên mật khẩu 4 bước wired OTP thật; Hồ sơ 3 tab + dialog đổi MK + 2FA toggle wired profile/avatar/login-history backend
- [x] Dashboard tổng quan — KPI/charts/realtime wired to analytics APIs
- [x] Unified Inbox (priority sort + filter + SignalR realtime)
- [x] Conversation view + context panel
- [x] Sale Assist (draft + quick reply + alert toast)
- [x] KB editor + version history + accuracy chart
- [x] Agent dashboard + start/stop + logs + right drawer cấu hình/sandbox theo Stitch S11
- [x] Lead list + Kanban pipeline + detail
- [x] Content brief editor + queue + calendar — `/content` wired to `/api/content` briefs/trends/queue/items/calendar
- [x] Document library + preview + send — `/documents` wired to `/api/docs` templates/generated/generate (`sentVia=email|zalo`)
- [x] Analytics dashboard (KPI 5 kênh) — `/analytics` wired to `/api/analytics` omnichannel/delta/funnel/agent-performance/agent-cost/forecast/anomalies/export
- [x] Quản lý hạn ngạch Token — `/tokens` wired to `/api/tokens/usage` + `/api/tokens/settings`, follows Stitch screen `Học Bá Admin Dashboard - Quản lý Token (V3)`
- [x] Nhật ký tác vụ & traceability — `/logs` wired to `/api/logs/task-runs`, detail `/api/logs/task-runs/{id}`, and `/api/logs/audit`, based on Stitch screen `Quản lý Agent - Nhật ký & Traceability (Right Drawer)`
- [x] Cấu hình Prompt gốc — `/prompts` wired to `/api/prompts/configs`, detail/update/sandbox, based on Stitch screens `Cấu hình LLM - Chi tiết & Log Token` + `LLM Test Sandbox`
- [x] Admin (users/roles/api-keys/integrations/branding) — `/system` wired to `/api/admin/users`, `/api/rbac`, `/api/api-keys`, `/api/channels/pancake`, `/api/admin/tenant/branding`, `/api/admin/audit-logs`
- [x] Notification center (SignalR realtime)
- [x] Web Chat Widget S17 — public `/chat-widget/:tenantSlug` wired to `/api/public/widget/{tenantSlug}` bootstrap/lead/messages; lead capture persists Contact/Lead/Conversation and pushes Inbox SignalR; tenant branding controls logo/color/greeting
- [x] FAQ / Support Page S17 — public `/support/:tenantSlug` wired to KB-backed `/api/public/widget/{tenantSlug}/faq` with search + accordion + tenant branding

### M17 — Document Generation (QuestPDF) · Imp 4 · Diff 3 · T9 · **DONE 2026-06-04**
- [x] Impl `DocsAgentGrpcService.Generate` (load template by code → render → store → persist `generated_documents`) — [DocsAgentGrpcService.cs](../src/agents/Clawbot.AgentService/Services/DocsAgentGrpcService.cs)
- [x] `QuestPdfDocumentRenderer` (A4, branded header/footer, page numbers, doc-type label) — [DocsServices.cs](../src/agents/Clawbot.Agents.Core/Docs/DocsServices.cs)
- [x] Template engine: `SimpleTemplateEngine` `{{ key }}` substitution (GeneratedRegex). **Scriban dropped** — 5.12.0 flagged critical/high CVEs (GHSA-5wr9-m6jw-xx44 et al.) and repo gates `NuGetAudit` as errors; doc use-case is field substitution only — same file
- [x] `DocsAgent` pure orchestrator (resolve → render → sha256) + `IDocumentStorage`/`LocalDocumentStorage` + `DocsModule.AddClawbotDocs()` — [DocsAgent.cs](../src/agents/Clawbot.Agents.Core/Docs/DocsAgent.cs)
- [x] proto `agent_docs.proto` `DocGenerateResponse` extended additively (`file_hash`, `size_bytes`, `latency_ms`)
- [x] `POST /api/docs/generate` → `{documentId, fileUrl, fileHash, sizeBytes, latencyMs}` + template CRUD `/api/docs/templates` (GET/POST/PUT/DELETE soft-delete) + `GET /api/docs/generated` — [DocumentsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/DocumentsEndpoints.cs)
- [x] Branded header/footer/logo từ tenant `DisplayName`
- [x] Templates seed QUOTE-V1 + ONBOARDING-KIT (idempotent MERGE on `(tenant_id, code)`, Scriban-style `{{ var }}` body) — [document-templates.sql](../deploy/seed/document-templates.sql)
- [x] API → AgentService via `DocsAgent.DocsAgentClient` gRPC typed client (registered in `Program.cs`)
- [x] Unit tests 12/12 green (template/renderer/agent/storage incl. real QuestPDF render) — [DocsRenderingTests.cs](../tests/Clawbot.Agents.Tests/Docs/DocsRenderingTests.cs); Agents.Tests + AgentService + Api build 0/0 on .NET 8
- [x] MinIO signed URL (7d) — `MinioDocumentStorage` config-gated, overrides Local — [MinioDocumentStorage.cs](../src/shared/Clawbot.Infrastructure/Documents/MinioDocumentStorage.cs)
- [x] QR code footer — `DocBranding.QrPayload` → QRCoder in PDF footer — [DocsServices.cs](../src/agents/Clawbot.Agents.Core/Docs/DocsServices.cs)
- [x] Read receipt tracker (open beacon) — anonymous `GET /api/docs/{id}/open.gif` returns 1x1 GIF and records `generated_documents.opened_at`
- [x] BROCHURE-HSK, SLIDE-DEMO-5 templates → added to `deploy/seed/document-templates.sql`, covered by `DocumentTemplateSeedTests`
- [x] Real send via `sent_via` channel → `DocumentDeliveryService` sends email or Zalo/Pancake and marks `generated_documents.sent_via/sent_at`, covered by `DocumentDeliveryServiceTests`
- [x] p95 <30s instrument → OpenTelemetry `http.server.request.duration` uses explicit histogram buckets including 30s SLO boundary, covered by `TelemetryModuleTests`
- [x] EF migration for new rows is data-seed only (DDL `document_templates`/`generated_documents` already in `0001_init.sql`; seed script in `deploy/seed/document-templates.sql`)

### M20 — Analytics KPI daily + Metabase · Imp 4 · Diff 3 · T11 · **DONE 2026-06-07**
- [x] `KpiAggregator` service — daily roll-up vào `kpi_daily`
- [x] Metabase docker service trong compose
- [x] Metabase dashboard JSON checked-in `deploy/metabase/`
- [x] `AnalyticsEndpoints.cs` (5 channel + funnel + agent perf)
- [x] Anomaly alert qua `IAnomalyDetector` (CPL spike)
- [x] 7-day forecast via `IForecaster`
- [x] CSV/PDF export

### M21 — Test infra · Imp 4 · Diff 2 · T1 ongoing
- [x] Add `Clawbot.Integration.Tests` project với Testcontainers.MsSql — [Clawbot.Integration.Tests.csproj](../tests/Clawbot.Integration.Tests/Clawbot.Integration.Tests.csproj)
- [x] Add `Clawbot.Agents.Tests` project — already existed
- [!] M18 full HTTP endpoint tests for `/api/content` — tests added in `ContentEndpointTests` for brief CRUD, item update/approve/schedule/calendar/cancel, and image-prompt validation; `deploy/ci/verify-testcontainers.ps1` now preflights Docker/Testcontainers and can run the integration suite with `-RunIntegrationTests`. Current environment still fails preflight because Docker CLI is not installed.
- [x] CI workflow `.github/workflows/test.yml` (build + test + coverage report) — [test.yml](../.github/workflows/test.yml)
- [x] Coverage baseline gate ≥30% in CI fail build dưới ngưỡng — CI calls `deploy/ci/enforce-coverage.ps1` after report summary; script merges Cobertura line hits across test-project reports instead of double-counting duplicate files; covered by `CoverageGateWorkflowTests`
- [x] xUnit + FluentAssertions + NSubstitute conventions
- [x] Sample test cho mỗi bounded context (smoke) — [DatabaseSmokeTests.cs](../tests/Clawbot.Integration.Tests/DatabaseSmokeTests.cs) + [EndpointSmokeTests.cs](../tests/Clawbot.Integration.Tests/EndpointSmokeTests.cs)

---

## P2 — Medium (2 module)

### M18 — Content + Research pipeline · Imp 3 · Diff 3 · T8 · **DONE 2026-06-07**
- [x] Impl `ContentAgentGrpcService` + `ResearchAgentGrpcService`
- [x] Brief CRUD endpoint
- [x] Content gen per-platform (TikTok/IG/FB/YT/Zalo)
- [x] Approve workflow (approved_by + approved_at)
- [x] Schedule integration (Buffer/Later API)
- [x] Weekly trend scan job (Monday 7am)
- [x] Repurpose flow (TikTok → Reels + Shorts)
- [x] Schedule API (`POST /items/{id}/schedule`, `GET /calendar`, `DELETE /schedule/{id}`)
- [x] `IGoldenHourResolver` + `IContentNotifier` + `SignalRContentNotifier`
- [x] `ContentPublishJob` with retry (3 attempts before terminal failure)
- [x] Content prompt templates seeded in appsettings (5 platforms)
- [x] OpenAI 2.11.0 GA (removed unused SemanticKernel dependency)
- [x] Domain methods: `SoftDelete`, `SetAssets`, `RevertToApproved` (replaced EF property-bag mutation)
- [x] Seed `deploy/seed/content-briefs.sql` + migration `0002_content_schedule_retry_count.sql`
- [x] AgentService gRPC service tests (`ContentAgentGrpcService`, `ResearchAgentGrpcService`) with SQLite fixture + NSubstitute
- [x] Deployment + monitoring feature docs filled
- [x] Full HTTP endpoint tests deferred to M21 (repo integration-test pattern)
- [x] Build 0/0, 168 tests green

### M19 — Ads automation (Meta + TikTok) · Imp 3 · Diff 4 · T10 · **DONE 2026-06-07**
- [x] Impl `AdsAgentGrpcService` (Evaluate, BuildLookalike, Remarket, HandleSignal)
- [x] Meta Marketing API connector (`MetaAdsConnector`, config-gated, graceful)
- [x] TikTok Business API connector (`TikTokAdsConnector`, config-gated, graceful)
- [x] `ads_rules` CRUD endpoint (`/api/ads/rules`)
- [x] Rule engine: relative CPL (target×multiplier), absolute freq/ctr/spend, 3-day streak gate, 24h cooldown
- [x] Frequency rotation (creative inventory `ads_creatives`, active↔standby)
- [x] Budget 90% alert (webhook + SignalR)
- [x] Lookalike audience builder (leads stage∈{hot,won} → contacts → seed, skip <100)
- [x] Weekly ads report job (`WeeklyAdsReportJob`, Mon GMT+7)
- [x] Dayparting pause/resume (02:00–05:00 GMT+7, `DaypartPaused` flag)
- [x] 7 Hangfire jobs registered (rule eval, rotation, remarketing, lookalike, daypart pause/resume, weekly report)
- [x] Migrations: `0003` (target_cpl + daypart_paused), `0004` (ads_creatives), `0005` (ads_metrics_daily)
- [x] Seed `deploy/seed/ads-rules.sql` (idempotent MERGE, 5 rules per platform)
- [x] Config: `Ads:Meta` + `Ads:TikTok` in both appsettings + `.env.example`
- [x] Build 0/0, 192 tests green (+24 new ads tests)

---

## Pain-point audit — đối chiếu [PhanTich_User_PainPoint_AI_Agent.docx](PhanTich_User_PainPoint_AI_Agent.md.docx) (2026-06-13)

> Khách cung cấp tài liệu: 7 user persona + 8 AI agent / 18 luồng nghiệp vụ. Rà chéo với source code hiện tại. Kết luận: **8 agent map đủ vào module đã build**; còn vài gap nhỏ (xem dưới). Cảnh báo nội bộ qua SignalR/in-app.

### 8 Agent → module → trạng thái
| Agent (số luồng) | Module | Trạng thái |
|---|---|---|
| **Agent-Chat** (4) — trả lời 24/7, comment, anti-injection, cost/cuộc | M10 · M11 · M06/M08 | ✅ — comment auto-reply + DM invite code path wired; live Pancake payload field names still need ops verification |
| **Agent-SaleAssist** (5) — draft, xếp ưu tiên, idle alert, upsell | M14 · M15 | ✅ — idle >5min assignee alert + >10min SalesLead escalation covered |
| **Agent-Lead** (3) — chấm điểm, giao khách nóng, nuôi dưỡng | M15 | ✅ — hot-lead alert, least-busy assignment, drip enrollment, and score-change reason logging covered |
| **Agent-Content** (3) | M18 | ✅ |
| **Agent-Research** (2) | M18 | ✅ |
| **Agent-Docs** (2) — báo giá, brochure | M17 | ✅ (KB merge lúc generate + onboarding/brochure/slide kit API done) |
| **Agent-Report** (4) — daily, forecast, anomaly, AI-quality | M20 · M12 | ✅ — daily push + anomaly alert qua SignalR/in-app; trừ **20-câu test set + đáp án chuẩn** |
| **Agent-Ads** (3) — tối ưu/giờ, lookalike, budget alert | M19 | ✅ — budget 90% alert qua SignalR/webhook done |

### Gap khách yêu cầu (bổ sung tracking)
> Telegram **không dùng** (quyết định 2026-06-13) — mọi cảnh báo qua **SignalR / in-app**.
1. **Live Pancake comment webhook sample** (Chat-L2) — code path already replies under comment + sends DM invite; still needs one real tenant payload to confirm field names/send semantics.
2. **KB content + 20-câu test set/đáp án chuẩn + 6 module tiếng Trung** (Report-L4, Chat accuracy) — `KbAccuracyTestJob` có sẵn, thiếu bộ 20 câu + đáp án + content. → M04 (đã defer; khách xác nhận cần).
3. **Native publishing / Ads vendor verification** (Content-3, Ads-2) — cần Meta/TikTok/Buffer-or-equivalent credentials để chứng minh create-post/lookalike path thật.
4. **Docker/Testcontainers environment** — cần Docker CLI/daemon để chạy SQL Server auth/content endpoint e2e suite locally.
5. **Real LLM/embedder credentials** — cần `ANTHROPIC_API_KEY`, `EMBEDDING_API_KEY`, và `CONTENT_LLM_API_KEY` để chạy RAG/accuracy/content generation với model thật thay vì fallback/dev config.
6. **go-live readiness gate** — `deploy/ci/verify-go-live-readiness.ps1 -ReportOnly` reports the blocker groups above; `-Strict` fails release until Docker, KB authoring, real LLM/embedder keys, Pancake live sample, and vendor/publisher credentials are all present.

### Deep audit (8-agent fan-out 2026-06-13) — per-luồng covered/partial/missing
> Audit sâu từng luồng (8 agent / 25 flow-entry). Kết quả hiện tại: **20 covered · 5 partial · 0 missing**. Phần lớn partial là *by-design*, *blocked-on-creds*, hoặc cần live vendor verification (xem cột Ghi chú).

| Agent · Luồng | Verdict | Ghi chú |
|---|---|---|
| Chat-1 trả lời 24/7 đa kênh | ⚠️ partial | Chỉ adapter Pancake (broker hợp kênh — **by-design** M06); lang directive chỉ informational |
| Chat-2 comment auto-reply <30s + DM | ⚠️ partial | Code path wired: `ChannelMessage.MessageType/ParentPostId`, Pancake `type=COMMENT` parse, `CommentAutoReplyJob` reply+DM invite. Còn cần live tenant webhook sample để confirm field names/send semantics |
| Chat-3 anti-injection | ✅ covered | Heuristic 27 cụm; refuse + trace. Audit-log-on-block chỉ vào `agent_session` (chưa `audit_log`) |
| Chat-4 cost/cuộc | ✅ covered | Ledger keyed tenant+month (không có `ConversationId`); cap $200 preflight blocks Claude calls and both in-memory/DB trackers skip entries that would exceed the monthly cap |
| SaleAssist-1 draft <3s | ✅ covered | RAG + Claude; <3s không guarantee (phụ thuộc API) |
| SaleAssist-2 xếp ưu tiên + alert ≥70 | ✅ covered | Inbox rank theo score done; lead lên `hot` publish `hot_lead` qua `LeadBecameHotConsumer` |
| SaleAssist-3 idle >5/>10min | ✅ covered | 5min→assignee; 10min→`SalesLead` user notifications via `idle_escalation`, fallback tenant broadcast nếu chưa có SalesLead |
| SaleAssist-4 upsell sắp chốt | ✅ covered | `/upsell` hot-gated qua `SaleAssistAgent.SuggestUpsellAsync`; `/upsell-suggestions` gọi dynamic per-conversation, có fallback khi upsell service lỗi |
| Lead-1 chấm điểm + ghi lý do | ✅ covered | `AdjustScore` ghi `LeadActivity` reason; rule seed và drip/idle jobs cover current scoring automation scope |
| Lead-2 giao khách nóng + notify | ✅ covered | `LeadBecameHotConsumer` auto-assigns unowned hot leads via least-busy Sale + publishes `hot_lead`; `ILeadAssignmentService` registered in API infrastructure |
| Lead-3 nuôi dưỡng drip/remarketing | ✅ covered | `LeadBecameWarmConsumer` enrolls warm leads once into tenant `warm_lead` drip; `DripSequence`/steps/enrollments mapped in EF |
| Content-1 viết 5 nền tảng + prompt ảnh | ✅ covered | Image-prompt API exposed via `POST /api/content/image-prompts`; validates brief/platform/style/brand tokens; `IVideoScriptComposer` exists for Hook/Value/CTA JSON schema generation |
| Content-2 repurpose | ✅ covered | — |
| Content-3 auto-schedule giờ vàng | ⚠️ partial | Chỉ HTTP publisher (**chưa native API/Buffer/Later**) — blocked-on-creds |
| Research-1 trend tuần | ✅ covered | Hangfire schedule dùng `Cron.Weekly(DayOfWeek.Monday, 7, 0)` với explicit Vietnam `TimeZoneInfo`; có job test |
| Research-2 theo dõi đối thủ | ✅ covered | `CompetitorSource`/`CompetitorPost`, DI `AddCompetitorMonitor`, `CompetitorScanJob`, Hangfire cron 06:00 VN, CRUD `/api/competitors/sources`, `GET /posts`, notification `competitor` |
| Docs-1 báo giá PDF + link 7d + gửi | ✅ covered | Render+branding, extract Contact/hội thoại, `ExpiresAt` 7d, download 410, gửi email/Zalo qua channel gated |
| Docs-2 brochure/slide/onboarding + KB | ✅ covered | Generate tự merge latest deployed KB vào `knowledge`/`kb_content`; `POST /api/docs/generate-kit` không còn hardcode mã mẫu — lấy tối đa 10 mẫu của tenant theo `code`, UI Documents có `Generate kit`; mẫu khai báo form schema trong `fields_json`, thiếu trường bắt buộc thì chặn ngay ở form và ở biên gRPC |
| Report-1 tổng hợp daily 7h30 + so sánh | ✅ covered | Rollup + daily notification push done; backend có `GET /api/analytics/omnichannel-delta?compare=dod\|wow` tính prior period từ `kpi_daily` |
| Report-2 forecast 7 ngày | ✅ covered | ML.NET SSA + bounds; chưa tune seasonality |
| Report-3 anomaly alert | ✅ covered | z-score + SignalR done |
| Report-4 AI-quality 20-câu + cost/agent | ⚠️ partial | KB-accuracy job chạy daily qua Hangfire; `/api/analytics/agent-performance` now exposes per-agent quality samples/pass rate/average score from `AgentTrace phase=quality`, and the Analytics UI surfaces those metrics. Vẫn còn thiếu bộ 20 câu/đáp án chuẩn + real embedder/LLM để chứng minh ≥85%; authoring validator now gates 20 Q/A per required KB module. |
| Ads-1 tối ưu mỗi giờ | ✅ covered | `ads-rule-evaluation` chạy hourly (`0 * * * *`); `AdsRuleEngine` tự alert spend/budget ≥90%; Meta/TikTok outbound qua `AdsPlatformThrottle` + retry 429 |
| Ads-2 lookalike | ⚠️ partial | Seed collection now runs tenant-scoped and publishes `ads_lookalike_failed` when a vendor connector returns no audience id; **`BuildLookalikeAsync` của Meta/TikTok connector vẫn stub `null`** — blocked-on-creds/live vendor setup |
| Ads-3 budget 90% alert | ✅ covered | **Wired publisher 2026-06-13** (`AdsAgentGrpcService.HandleSignal`→`ads_budget` notification) |

**0 MISSING (code thật còn thiếu):**
- Không còn gap code thuộc audit 2026-06-13. Các mục còn lại là live vendor verification, by-design, hoặc blocked-on-creds.

**Partial cần code (không phải by-design):**
- [x] Chat-2 comment auto-reply + DM code path — `ChannelMessage.MessageType=comment|dm` + `parent_post_id`, Pancake parse, purchase/price/trial intent, `CommentAutoReplyJob` reply+DM invite. Live Pancake payload verification vẫn cần ops.
- [x] Docs-1 extract info hội thoại + `ExpiresAt` 7d + gửi Zalo/email thật. → M17.

**Partial by-design / blocked (không phải gap):** Chat-1 Pancake unified broker (M06 design) · Content-3 đã có Meta Graph v25.0 + Facebook Login for Business nhưng vẫn cần App Review/live assets; Ads-2 lookalike connector còn cần implementation + creds Meta/TikTok · SignalR-only thay Telegram (quyết định 2026-06-13) · forecast seasonality + KB Chinese content (blocked).

### Pancake capability (xác minh API thật 2026-06-13)
- **Reply comment + DM (Chat-2):** ✅ qua Pancake code path — `messages` trên conversation `type=COMMENT` (filter `?type=COMMENT` hợp lệ; `post_id` có trên conversation). Adapter hiện parse `type=COMMENT` + `post_id`; cần 1 comment thật + 1 webhook payload thật để verify field names/send semantics.
- **Đăng bài FB (Content-3):** ❌ Pancake public API **không có** create-post → đăng bài đi **Meta Graph API** (`POST /{page-id}/feed`), KHÔNG qua Pancake. Đã sửa base URL Pancake sai → `pancake.vn/api/v1` (commit `3811cc5`).

### External-service config audit (2026-06-13) — mọi service ngoài phải có Options module
| Service | Section | Trạng thái |
|---|---|---|
| Anthropic · Ads:Meta · Ads:TikTok · Content:Publisher · Content:Llm · Content:Trends:* · Embedding · Skills:ContactEnrich | (Options) | ✅ đã có |
| Pancake | `Channels:Pancake` + DB per-tenant (encrypted) | ✅ có cả admin endpoint |
| **Qdrant** | `Vector:Qdrant` | ✅ **fix** — `QdrantOptions` (Host/Port/UseTls/ApiKey), bỏ raw `cfg[]` |
| **SMTP** | `Email:Smtp` | ✅ **fix** — `SmtpOptions`, `SmtpEmailSender` dùng `IOptions` |
| **MinIO** | `Docs:Storage:Minio` | ✅ **fix** — `MinioOptions`, bỏ raw `cfg[]` |
| Meta Graph (Page publishing + Ads auth) | UI `/system` + `Meta:Graph` fallback + `Ads:Meta` | ✅ Meta App config mã hóa theo tenant trên UI, Facebook Login for Business Authorization Code, BISU token, `/me/accounts`, Page token mã hóa, Graph v25.0 |
| Redis · RabbitMq · SqlServer | ConnectionStrings | ✅ connstr (infra) |

---

## Backend gaps mới (rà soát 2026-06-13) — chức năng FE/doc cần nhưng CHƯA có

> Đối chiếu 12 surface FE (M16) + 18 luồng doc với route hiện có (`src/api/.../Endpoints/*`). Tick theo trạng thái endpoint và surface hiện tại.

### M23 — Account & User administration (NEW · Imp 4 · Diff 3 · **DONE 2026-06-13** build 0/0; SQL Server auth e2e pending Docker)
> Quyết định /review-requirements 2026-06-13: **Admin tạo user, KHÔNG self-register** (single-org). Email = **SMTP config-gated**.
- [x] `/api/admin/users` CRUD — list/create/update/disable/enable/admin-reset password wired in FE **Admin** surface (`/system`)
- [x] `POST /auth/change-password` (đã đăng nhập) — FE `ChangePasswordDialog` wired and verified
- [x] `GET/PUT /api/profile` — đọc/cập nhật hồ sơ (họ tên, SĐT, ngày sinh) wired in FE `/profile`
- [x] Avatar upload (MinIO/document storage) — FE nút "đổi ảnh đại diện" wired to `POST /api/profile/avatar`
- [x] `GET /api/profile/login-history` — current-user login audit history wired in FE tab **Nhật ký bảo mật**
- [x] `IEmailSender` — **SMTP config-gated** (graceful, bật khi có creds) → reset token + onboarding path uses `SmtpEmailSender`
- [x] Identity DDL offline preflight — `deploy/ci/verify-identity-ddl.ps1` checks AppUser→`users`, required Identity columns/tables, `0014` indexes, and no `GO` separators before Docker-backed integration tests.
- [x] ~~`POST /auth/register` self-serve~~ — **bỏ** (admin-provisioned, no public register)

### M24 — Notification center backend (NEW · Imp 4 · Diff 2 · **DONE 2026-06-13** build 0/0)
- [x] `notifications` table + entity — persisted alert store (`Notification`, `AppDbContext.Notifications`)
- [x] `GET /api/notifications` (paged + unread filter) + `POST /api/notifications/{id}/read` + mark-all-read
- [x] `INotificationPublisher` — API publisher writes DB and pushes SignalR (`DbNotificationPublisher`)

### M25 — Agent control & observability (NEW · Imp 3 · Diff 3 · **DONE 2026-06-13** build 0/0; ChatAgent flag-honor covered)
- [x] `GET /api/agents` — list agent + status + last-run từ `AgentConfig`/`AgentSession`; FE **Agent dashboard** wired
- [x] `POST /api/agents/{code}/enable|disable` — tắt/bật status per-tenant của agent-type; FE start/stop wired
- [x] `GET|PUT /api/agents/{code}/settings` — đọc/lưu model/provider/systemPrompt/skill files/KB modules vào `AgentConfig`
- [x] `POST /api/agents/{code}/sandbox` — chạy sandbox nhẹ, tạo `AgentSession` + `AgentTrace` để FE log refresh được
- [x] `GET /api/agents/{code}/traces` — đọc agent run logs từ `agent_traces` qua session của agent
- [x] `GET /api/analytics/agent-cost` — chi phí theo từng agent từ `ClaudeCostLedger` (calls/tokens/USD/avg)
- [x] `GET /api/tokens/usage` + `PUT /api/tokens/settings` — FE `/tokens` quản lý hạn ngạch token, router tier và cảnh báo in-app bằng `ClaudeCostLedger` + `AgentConfig.ConfigJson`
- [x] `GET /api/logs/task-runs`, `GET /api/logs/task-runs/{sessionId}`, `GET /api/logs/audit` — route `/logs` tổng hợp `agent_sessions`, `agent_traces`, `audit_logs`, `claude_cost_ledger`
- [x] `GET /api/prompts/configs`, `GET|PUT /api/prompts/configs/{code}`, `POST /api/prompts/configs/{code}/sandbox` — route `/prompts` quản lý provider/model/systemPrompt và sandbox prompt bằng `AgentConfig`

### M26 — Public Web Chat Widget backend (NEW · Imp 3 · Diff 2 · **DONE 2026-06-14** build 0/0)
- [x] `GET /api/public/widget/{tenantSlug}/bootstrap` — anonymous tenant-aware widget metadata + branding
- [x] `GET /api/public/widget/{tenantSlug}/faq` — anonymous tenant-aware FAQ from active KB test cases/modules + branding
- [x] `POST /api/public/widget/{tenantSlug}/lead` — creates/updates contact, creates warm web-widget lead, opens conversation, appends inbound + bot reply, notifies Inbox SignalR
- [x] `POST /api/public/widget/{tenantSlug}/messages` — appends visitor follow-up + bot acknowledgement to existing widget conversation

### Bổ sung vào module hiện có
- **M14** — [x] SaleAssist draft feedback loop ("AI tự học", doc L1) — `POST /api/sale-assist/draft-feedback` ghi outcome sent/edited/discarded vào `AgentSession`/`AgentTrace`, redacts PII trước khi persist
- **M15** — [x] Least-load assignment (doc: "sale rảnh nhất / ít khách nhất") — `LeastBusyLeadAssignmentService` dùng open conversation + warm/hot lead load, registered via `AddInfrastructure`
- **M11/M10** — [x] Persistent cost ledger (DB) thay `InMemoryClaudeCostTracker` — `ClaudeCostLedger` backs `/api/analytics/agent-cost`; `ChatAgent` preflight blocks at cap and cost trackers enforce record-time cap guards.
- **M10** — [x] Out-of-hours window cấu hình per-tenant — `OutOfHoursAutoReplyJob` đọc `AgentConfig.ConfigJson.outOfHours` (`workStart`, `workEnd`, `timezoneOffsetHours`, `replyText`) và fallback 08:00-22:00 GMT+7

---

## Progress summary (update mỗi sprint)

| Tuần | Modules in-flight | Modules done | Notes |
|:-:|---|---|---|
| T0 | — | (skeleton only) | Domain entities + proto + grpc stubs |
| T1 | — | **M01**, **M02**, **M04**, **M09 spike** | Build xanh 0/0. SK plugin-host / Anthropic direct (RFC-001). |
| T2 | — | **M03**, **M05**, **M12**, **M13** | Audit/PII, chat scenarios seed, Hangfire, rate-limit + webhook HMAC. |
| T3 | — | **M11** (P0 subset) | Intent/sentiment/PII/injection/cost heuristics wired into ChatAgent. |
| T4 | — | **M06**, **M08**, **M10** | Pancake unified adapter, omnichannel inbox, Agent-Chat (gRPC). |
| T5 | — | **M14** | Agent-SaleAssist (draft + summary + quick replies). |
| T6 | — | — | (M10 token-streaming / latency opt deferred) |
| T7 | — | **M15** | Lead scoring + dedup + least-busy assign. |
| T8 | — | **M18** | Content + Research pipeline (commit cf553a0). |
| T9 | — | **M17** | Document generation — QuestPDF (commit 9eb8e6d). |
| T10 | — | **M19** | Ads automation Meta+TikTok (commit fcddfbe). |
| T11 | — | **M20** | Analytics KPI + Metabase + anomaly/forecast (commit cf553a0). |
| T12 | — | **M16** | Frontend UI — all Stitch web surfaces and S17 public Web Chat Widget/FAQ wired for the web/backend scope. |
| T13 | **M21** | — | Test infra: CI + coverage baseline gate done; integration (Testcontainers) still pending Docker/local environment. |

---

## Cross-cutting deferred items (Phase 2 / nice-to-have)

- [x] Multi-region replication → `Deployment:Replication` options, `/health/replication`, SQL replica-lag probe, write-guard report, and ops runbook in `deploy/multi-region/README.md`.
- [x] GDPR data export per contact → `GET /api/contacts/{id}/export.json` via `ContactDataExportService`, covered by `ContactDataExportServiceTests`
- [x] White-label tenant branding → tenant branding columns + migration `0022_tenant_branding.sql`, admin `/api/admin/tenant/branding`, public widget/FAQ `branding`, `/system` brand form; covered by `TenantBrandingServiceTests`
- [x] Python alternative AgentService → contract-compatible scaffold `src/agents/Clawbot.PythonAgentService` compiles repo proto at startup, registers all 9 gRPC services + health, Dockerfile/README; covered by `PythonAgentServiceScaffoldTests`
- **Mobile app (React Native): OUT OF SCOPE** — no product requirement yet; does not count toward web/backend completion percentage.
- [x] A/B test framework (UC-K10) full backend impl → `experiments`/`experiment_variants`/`experiment_assignments`/`experiment_events`, `/api/experiments` create/list/assign/event/summary/stop, deterministic subject assignment + conversion winner summary; covered by `ExperimentServiceTests`
- [x] Pixel agents office UI (SW-043) → `/agents-office` with live `/api/agents` polling, selected-agent trace feed, task queue, health panel, and Stitch-aligned pixel operations floor.
- [x] Bounded-context skeleton cleanup → removed stale 501 root stubs for implemented API groups so `/api/agents`, `/api/leads`, KB, inbox, docs, ads, and sale-assist cannot be shadowed; covered by `BoundedContextEndpointSkeletonTests`.
- [x] Orchestrator plan/lifecycle core (V2) → `SemanticKernelPlanGenerator` + `AutonomousOrchestrator` drive submit/plan-edit/approve/control via `OrchestratorGrpcService` (Submit/GetPlan/UpdatePlan/Approve/Control), AgentService registers all runtime agent names via `DefaultAgentRegistry`; legacy keyword `PlanningOrchestrator` + `Plan`/`Trace` RPCs removed 2026-07-01 (deprecated in proto, zero remaining callers); covered by `OrchestratorGrpcServiceTests` and `DefaultAgentRegistryTests`.
- [x] Document bundle kit → `POST /api/docs/generate-kit` generates a document per saved template (no hardcoded template-code list); frontend Documents workspace exposes `Generate kit`; a template whose required fields are unfilled is skipped (job fails only when nothing could be generated). Not covered by automated tests (suite removed at 5e24566).
- [x] Document authoring without HTML → template body is plain text (first non-empty line renders as the PDF title), `fields_json` is the form schema (`TemplateField`/`TemplateFieldSchema`, legacy `{"key":"label"}` still parsed), the Documents UI builds its input form from that schema plus curated presets, preview follows the same plain-text line/title rules the PDF uses (it additionally marks unfilled placeholders, which the PDF renders as blanks, and omits the PDF header/footer/QR), and required fields are validated in the UI and again at the gRPC render boundary after contact/KB auto-fill. Verified by build + typecheck + lint only; no automated test coverage (suite removed at 5e24566).
