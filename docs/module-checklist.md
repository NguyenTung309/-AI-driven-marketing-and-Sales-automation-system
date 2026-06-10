# ClawBot — Module Implementation Checklist

> Persistent tracking. Tick `[x]` khi xong. Nguồn plan: [../C:/Users/AdminDatVo/.claude/plans/wiggly-wandering-blum.md] + [spec-audit.md](spec-audit.md).
> Convention: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong · `[!]` blocked.
>
> Last updated: 2026-06-07

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
- [x] Password reset: `POST /auth/reset/request` (logs token; email integration deferred) + `/auth/reset/confirm`
- [x] Account lockout: 5 fail attempts × 15 min — Identity options trong `DependencyInjection.cs`
- [x] `api_keys` CRUD issuer (`GET/POST/DELETE /api/api-keys`) với SHA-256 hash + plaintext-once return — [ApiKeysEndpoints.cs](../src/api/Clawbot.Api/Endpoints/ApiKeysEndpoints.cs)
- [x] `GET /auth/me` whoami endpoint
- [x] Build xanh: `dotnet build` → 12 projects, 0 errors, 0 warnings
- [x] `[Authorize(Policy="perm:...")]` AuthorizationHandler reads `perm` claim → **done M02b** — [PermissionAuthorizationHandler.cs](../src/api/Clawbot.Api/Auth/PermissionAuthorizationHandler.cs)
- [x] ApiKey bearer scheme cho incoming auth (consume issued keys) → **done M02b** — [ApiKeyAuthenticationHandler.cs](../src/api/Clawbot.Api/Auth/ApiKeyAuthenticationHandler.cs)
- [x] Test cross-tenant query returns 0 rows → **done M21** — [DatabaseSmokeTests.cs](../tests/Clawbot.Integration.Tests/DatabaseSmokeTests.cs)
- [ ] Tenant-scoped custom Role rows seeded khi register tenant mới → **defer khi có /auth/register**

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
- [ ] **Test runner uses stubbed pass/fail (deterministic per-index)** — real LLM/RAG evaluation → **defer M09 wire-up**
- [ ] Qdrant embedding sync khi deploy: SQL `embedding` JSON + Qdrant upsert → **defer M09**
- [ ] KB seed 6 module tiếng Trung (`deploy/seed/kb-modules.sql`) → **defer; needs P3 Sales + Học thuật input per architecture doc T1–T2**
- [ ] kb_test_cases seed 20 câu / module → **defer cùng KB seed**
- [ ] Alert when accuracy drop <85% (NFR-05) → **defer M12 (Hangfire job)**

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
- [ ] EF migration to add `pancake_configs` table → batched with M21 schema apply
- [ ] Real Pancake account + access_token + webhook_secret → ops setup (not code)
- [ ] First webhook empirical test → may need to adjust `SignatureHeader` / `SignatureEncoding` / payload field names; all swappable via PUT `/api/channels/pancake/config` without redeploy
- [x] Health check `/health/channels/pancake` → after first successful round-trip — [HealthEndpoints.cs](../src/api/Clawbot.Api/Endpoints/HealthEndpoints.cs)
- [ ] Per-tenant outbound rate limit (Pancake quotas) → after empirical measurement
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
- [ ] Full-text search `GET /api/inbox/search?q=` → defer (needs SQL Server FTS index or OpenSearch)
- [ ] Export conversation log `GET /api/inbox/conversations/{id}/export.csv` → defer P3
- [x] `external_message_id` column on `messages` for strict dedup → done W6.12 — [0007_messages_external_id.sql](../deploy/migrations/0007_messages_external_id.sql) + [ChannelMessageIngestor.cs](../src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs)
- [ ] Lead score join in list ordering → after M15

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
- [ ] Anthropic SDK chat completion + streaming → **M10**
- [x] Redis cache `(tenant, kb_versions_hash, query_hash)` TTL 1h → **M10** — [CachedRagRetriever.cs](../src/agents/Clawbot.Agents.Core/Rag/CachedRagRetriever.cs)
- [x] `IClaudeCostTracker` per-call emission → **M11 P0 skill** — done M11
- [x] Wire `KbVersion.Deploy` → embed `content_md` chunks → Qdrant upsert → **done W5** — [KbDeployService.cs](../src/agents/Clawbot.Agents.Core/Kb/KbDeployService.cs)
- [x] Wire `KbEndpoints.RunTestAsync` to call `IRagRetriever` + LLM → **done W5** — [KbEndpoints.cs](../src/api/Clawbot.Api/Endpoints/KbEndpoints.cs)
- [ ] Accuracy ≥85% on 20-câu test set → **after content seed + real embedder + LLM**

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
- [ ] Intent classify via `IIntentClassifier` → defer to M11 (skill impl)
- [ ] Match `chat_scenarios` template by trigger + platform → defer to M11
- [ ] PII redact inbound via `IPiiRedactor` → defer to M11
- [ ] Prompt injection guard via `IPromptInjectionDefender` → defer to M11
- [ ] Toxicity filter output via `IToxicityFilter` → defer to M11
- [ ] Token-by-token streaming (SSE-style chunk) → P2 optimization
- [x] Escalation rule (confidence<threshold | intent=escalation → assign sale) → done W5 — [ChatAgent.cs](../src/agents/Clawbot.Agents.Core/Chat/ChatAgent.cs)
- [x] Out-of-hours auto-reply (UC-A07) scheduled scenario → done W5 — [OutOfHoursAutoReplyJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/OutOfHoursAutoReplyJob.cs)
- [ ] p95 latency <3s Serilog+OTel histograms → M11 cost-tracker batch
- [ ] Cost tracker (`IClaudeCostTracker.RecordAsync`) wire → M11 P0 skill

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
- [x] Alert job: conversation idle >5 min → Telegram + SignalR → done W6.6 — [IdleConversationAlertJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/IdleConversationAlertJob.cs) (SignalR only, Telegram blocked)
- [x] Context panel API: lead history + score + next-step → done W6.7 — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [x] Upsell suggestion when lead.stage='hot' + gói ngắn → done W6.8 — [SaleAssistEndpoints.cs](../src/api/Clawbot.Api/Endpoints/SaleAssistEndpoints.cs)
- [ ] Sale tone check (block banned phrases) before send → M11 toxicity-filter
- [x] Daily summary endpoint `GET /api/sale-assist/daily-summary` → done W6.9 — [SaleAssistEndpoints.cs](../src/api/Clawbot.Api/Endpoints/SaleAssistEndpoints.cs)

### M15 — Lead scoring + dedup + drip · Imp 5 · Diff 3 · T7 · **DONE 2026-05-29**
- [x] Impl `LeadAgentGrpcService.Score` — [LeadAgentGrpcService.cs](../src/agents/Clawbot.AgentService/Services/LeadAgentGrpcService.cs)
- [x] `LeadScoringEngine.Evaluate` static rule evaluator: sum weights matching event_code (+optional platform) — [LeadScoringEngine.cs](../src/agents/Clawbot.Agents.Core/Lead/LeadScoringEngine.cs)
- [x] Domain `Lead.AdjustScore` → stage classifier cold<30, warm 30–70, hot≥70 (already in entity)
- [x] `ILeadDedupService` + `EfLeadDedupService`: contact match + phone/email join — [EfLeadDedupService.cs](../src/shared/Clawbot.Infrastructure/Leads/EfLeadDedupService.cs)
- [x] `ILeadAssignmentService` + `RoundRobinLeadAssignmentService` + `EfAssignmentPoolSource` (Sale role) — [LeadAssignmentService.cs](../src/agents/Clawbot.Agents.Core/Lead/LeadAssignmentService.cs)
- [x] `POST /api/leads` (auto-dedup + auto-assign on create) — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [x] `POST /api/leads/{id}/activities` (record event → engine evaluate → score adjust)
- [x] `POST /api/leads/{id}/assign` (explicit or round-robin)
- [x] `GET/POST /api/lead-scoring-rules` + soft-deactivate
- [x] `GET /api/leads` paginated, ordered by score desc → last_activity_at
- [x] Build xanh 12 projects, 0/0
- [x] `lead_scoring_rules` seed defaults (asks_price+10, shares_phone+20, etc.) → done W6.1 — [lead-scoring-rules.sql](../deploy/seed/lead-scoring-rules.sql)
- [ ] Qdrant similarity dedup ≥0.92 on (name+phone tail+email embedding) → M11 lead-deduplicator skill
- [x] Drip sequences (per-channel templates) → done W6.2 — [0008_drip_sequences.sql](../deploy/migrations/0008_drip_sequences.sql) + [DripSequenceJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/DripSequenceJob.cs)
- [x] No-show follow-up 2h after demo missed → done W6.3 — [LeadFollowUpJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/LeadFollowUpJob.cs)
- [x] Re-engage stale lead 30d → done W6.4 — [LeadFollowUpJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/LeadFollowUpJob.cs)
- [x] Pipeline forecast endpoint `GET /api/leads/forecast` → done W6.5 — [LeadsEndpoints.cs](../src/api/Clawbot.Api/Endpoints/LeadsEndpoints.cs)
- [ ] Lead import/export CSV → P3
- [ ] Telegram alert <2 min on hot lead → after M12 + Telegram channel adapter

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
- [ ] EF migration to add chat_scenarios rows is data-seed only (DDL already in `0001_init.sql`); KB-tone refinement after real conversion data lands

### M07 — ~~TikTok/IG/YT native adapters~~ → SUPERSEDED by M06 Pancake unified · 2026-05-29
**No longer planned.** All 5 channels (Facebook, Instagram, TikTok Shop, WhatsApp, Google Business, Zalo OA) routed via Pancake per M06 strategy pivot. Reasons:
- Vendor SDK churn (TikTok Business API breaks q/q, IG Graph deprecates fields, YT comment quota draconian)
- OAuth refresh complexity × 3 vendors = 3 refresh failure modes
- Comment-vs-DM routing already solved by Pancake unified inbox
- Single billing relationship vs 3 vendor accounts
- If Pancake outage / disagreement: re-evaluate. Migration path: implement individual adapters under same `IChannelAdapter` interface — schema + ingestor pipeline (M08) unchanged.
- [ ] Webhook subscription setup script trong `deploy/`
- [ ] Polly retry với exponential backoff
- [ ] Health checks 3 channel

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
- [x] `IVideoScriptComposer` — Hook/Value/CTA JSON schema via Claude — [IVideoScriptComposer.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IVideoScriptComposer.cs)
- [x] `IViZhTranslator` — Claude + glossary tracking — [IViZhTranslator.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/IViZhTranslator.cs)
- [x] `ICompetitorMonitor` — RSS via System.Xml.Linq + URL dedupe — [ICompetitorMonitor.cs](../src/agents/Clawbot.Agents.Core/Skills/Content/ICompetitorMonitor.cs)
- [x] `IPdfTableRenderer` — QuestPDF table renderer — [IPdfTableRenderer.cs](../src/agents/Clawbot.Agents.Core/Skills/Ops/IPdfTableRenderer.cs)
- [x] `IQrGenerator` — QRCoder PNG — [IQrGenerator.cs](../src/agents/Clawbot.Agents.Core/Skills/Ops/IQrGenerator.cs)
- [x] `IAnomalyDetector` — Math.NET z-score — already done M20
- [x] `IForecaster` — ML.NET TimeSeries SSA — already done M20

### M12 — Scheduled job runner (Hangfire) · Imp 4 · Diff 2 · T2 · **DONE 2026-05-29**
- [x] Hangfire registered with SQL Server storage (auto-schema, 5min batch timeout) — [HangfireModule.cs](../src/shared/Clawbot.Infrastructure/Jobs/HangfireModule.cs)
- [x] Hangfire dashboard `/hangfire` mounted (auth: TODO Admin-only filter — open in dev)
- [x] `RetentionPurgeJob` daily 02:00 — purges `audit_logs` >30d via `ExecuteDeleteAsync` — [RetentionPurgeJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/RetentionPurgeJob.cs)
- [x] `DailyKpiRollupJob` daily 07:30 — aggregate leads/conversations/replies/conversions per tenant → `kpi_daily` (platform=`all`) — [DailyKpiRollupJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/DailyKpiRollupJob.cs)
- [x] Worker queues: default/retention/kpi
- [x] Build xanh 12 projects, 0/0
- [x] Admin-only auth filter on `/hangfire` dashboard → tighten before prod — [HangfireAdminFilter.cs](../src/api/Clawbot.Api/Auth/HangfireAdminFilter.cs)
- [ ] `messages` retention purge (>30d) — schema needs to expose PII flag first
- [ ] `DailyReportJob` (Telegram push UC-I01) → after Telegram channel adapter
- [ ] `DripSequenceJob` per-lead → after M15 drip templates + Pancake outbound batch
- [ ] `KbAccuracyTestJob` (daily) → after real embedder lands per RFC-001 open question
- [x] `HealthCheckJob` (hourly) → after Telegram channel adapter — [HealthCheckJob.cs](../src/shared/Clawbot.Infrastructure/Jobs/HealthCheckJob.cs)

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
- [ ] Vendor-specific verifiers (since Pancake unified — only need Pancake; rest deferred)
- [x] 401 audit log on reject → after M03 audit interceptor lands — [WebhookEndpoints.cs](../src/api/Clawbot.Api/Endpoints/WebhookEndpoints.cs)

### M16 — Frontend UI (12 surface) · Imp 4 · Diff 4 · T4–T11
- [ ] Login + 2FA flow
- [ ] Unified Inbox (priority sort + filter + SignalR realtime)
- [ ] Conversation view + context panel
- [ ] Sale Assist (draft + quick reply + alert toast)
- [ ] KB editor + version history + accuracy chart
- [ ] Agent dashboard + start/stop + logs
- [ ] Lead list + Kanban pipeline + detail
- [ ] Content brief editor + queue + calendar
- [ ] Document library + preview + send
- [ ] Analytics dashboard (KPI 5 kênh)
- [ ] Admin (users/roles/api-keys/integrations)
- [ ] Notification center + Telegram link

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
- [ ] MinIO signed URL (7d) — `LocalDocumentStorage` is the baseline; swap `IDocumentStorage` impl later (Minio pkg already referenced)
- [ ] QR code footer via `IQrGenerator` (M11 P2) → defer
- [ ] Read receipt tracker (open beacon) → defer (`generated_documents.opened_at` column + `MarkOpened` ready)
- [ ] BROCHURE-HSK, SLIDE-DEMO-5 templates → defer (renderer already handles any `doc_type`)
- [ ] Real send via `sent_via` channel → defer (delivery separated from generation)
- [ ] p95 <30s instrument → after OTel histograms
- [ ] EF migration for new rows is data-seed only (DDL `document_templates`/`generated_documents` already in `0001_init.sql`)

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
- [ ] M18 full HTTP endpoint tests for `/api/content`
- [x] CI workflow `.github/workflows/test.yml` (build + test + coverage report) — [test.yml](../.github/workflows/test.yml)
- [~] Coverage gate ≥80% in CI fail build dưới ngưỡng — report-only first, hard-fail after backfill
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
| T7 | — | **M15** | Lead scoring + dedup + round-robin assign. |
| T8 | — | **M18** | Content + Research pipeline (commit cf553a0). |
| T9 | — | **M17** | Document generation — QuestPDF (commit 9eb8e6d). |
| T10 | — | **M19** | Ads automation Meta+TikTok (commit fcddfbe). |
| T11 | — | **M20** | Analytics KPI + Metabase + anomaly/forecast (commit cf553a0). |
| T12 | **M16** | — | Frontend UI (12 surfaces) — pending. |
| T13 | **M21** | — | Test infra: integration (Testcontainers) + CI + coverage gate — pending. |

---

## Cross-cutting deferred items (Phase 2 / nice-to-have)

- [ ] Multi-region replication
- [ ] GDPR data export per contact
- [ ] White-label tenant branding
- [ ] Python alternative AgentService
- [ ] Mobile app (React Native)
- [ ] A/B test framework (UC-K10) full impl
- [ ] Pixel agents office UI (SW-043)
