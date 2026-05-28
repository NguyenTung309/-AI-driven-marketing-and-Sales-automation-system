# ClawBot — Module Implementation Checklist

> Persistent tracking. Tick `[x]` khi xong. Nguồn plan: [../C:/Users/AdminDatVo/.claude/plans/wiggly-wandering-blum.md] + [spec-audit.md](spec-audit.md).
> Convention: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong · `[!]` blocked.
>
> Last updated: 2026-05-28

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
- [ ] Integration test: `Testcontainers.MsSql` apply DDL + smoke insert/select → **defer M21** (test infra)

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
- [ ] `[Authorize(Policy="perm:...")]` AuthorizationHandler reads `perm` claim → **defer M02b** (perm claims đã có trong JWT, handler chỉ cần thêm sau)
- [ ] ApiKey bearer scheme cho incoming auth (consume issued keys) → **defer M02b**
- [ ] Test cross-tenant query returns 0 rows → **defer M21**
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

### M06 — Channel adapter Zalo + Facebook · Imp 5 · Diff 4 · T2–T4
- [ ] `ZaloChannelAdapter : IChannelAdapter` (OA send + webhook parse)
- [ ] `FacebookChannelAdapter : IChannelAdapter` (Graph API send + page webhook)
- [ ] HMAC verify: `ZaloWebhookSignatureVerifier` (sha256 over body + secret)
- [ ] HMAC verify: `FacebookWebhookSignatureVerifier` (`X-Hub-Signature-256`)
- [ ] Inbound parser → `ChannelMessage` → MediatR `IngestMessageCommand`
- [ ] Outbound send với Polly retry + circuit breaker
- [ ] Idempotency key `(platform, external_message_id)` dedup
- [ ] `pancake_configs` rename / extend → `channel_configs` per-tenant per-channel
- [ ] AES encrypt `access_token` + `webhook_secret` qua `IEncryptor`
- [ ] Health check `/health/channels/zalo`, `/health/channels/facebook`
- [ ] Integration test mock vendor: send+receive round-trip

### M08 — Omnichannel Inbox API + unified conversation merge · Imp 5 · Diff 3 · T4
- [ ] `InboxEndpoints.cs` (`GET /api/inbox` paged + filter)
- [ ] Query priority: lead.score desc, last_msg_at desc, unread first
- [ ] `IngestMessageCommand` handler: find-or-create contact via `contact_external_ids`
- [ ] Dedup `(platform, external_id)` UNIQUE enforce
- [ ] `MergeContactsCommand` cross-platform stitching
- [ ] `AssignConversationCommand` (`POST /api/conversations/{id}/assign`)
- [ ] `MarkConversationStatusCommand` (open/pending/resolved/escalated)
- [ ] SignalR push `DashboardHub.NotifyNewMessage(tenantId)`
- [ ] Full-text search `GET /api/conversations/search?q=`
- [ ] Export conversation log `GET /api/conversations/{id}/export.csv`

### M09 — Semantic Kernel + RAG (Qdrant) · Imp 5 · Diff 5 · T3–T4 · **SPIKE FIRST**
- [ ] Spike: SK 1.x vs direct Anthropic SDK decision RFC trong `.sdd/rfcs/`
- [ ] `Kernel` builder DI in `Clawbot.Agents.Core/SemanticKernelModule.cs`
- [ ] `Anthropic` chat completion connector (community pkg or custom)
- [ ] Embedding generator (e.g., OpenAI `text-embedding-3-small` or local)
- [ ] `IRagRetriever` interface + `SemanticKernelRagRetriever` impl
- [ ] Pipeline: query → embed → Qdrant top-K → re-rank → context window
- [ ] Citation: response includes `kb_version_id` source
- [ ] Caching key `(tenant, kb_versions_hash, query_hash)` Redis TTL 1h
- [ ] Cost ledger emit per call (wire vào M11 `IClaudeCostTracker`)
- [ ] Test: 20-câu KB test set returns ≥85% correct

### M10 — Agent-Chat (gRPC) — reply 5 kênh · Imp 5 · Diff 4 · T4–T6
- [ ] Impl `ChatAgentGrpcService.Reply` (replace stub)
- [ ] Intent classify via `IIntentClassifier` (M11)
- [ ] Match `chat_scenarios` template by trigger + platform
- [ ] Fallback to RAG (M09) khi không match scenario
- [ ] PII redact inbound qua `IPiiRedactor` trước log
- [ ] Prompt injection guard via `IPromptInjectionDefender`
- [ ] Toxicity filter output via `IToxicityFilter`
- [ ] Stream reply via gRPC server-streaming
- [ ] Persist `messages` + `agent_sessions` + `agent_traces`
- [ ] Escalation rule: if confidence<threshold OR intent=escalation → assign sale
- [ ] Out-of-hours auto-reply (UC-A07) via scheduled scenario
- [ ] p95 latency <3s instrument Serilog + OTel

### M14 — Agent-SaleAssist · Imp 5 · Diff 3 · T5
- [ ] Impl `SaleAssistAgentGrpcService` (Draft/Summary/Alert)
- [ ] `GET /api/sale-assist/draft?conversationId=` returns Claude-drafted reply
- [ ] `GET /api/sale-assist/summary?conversationId=` thread summary
- [ ] `quick_reply_templates` CRUD + endpoint `GET /api/quick-replies`
- [ ] Alert job: conversation idle >5 min → Telegram + SignalR
- [ ] Context panel API: lead history + score + next-step suggestion
- [ ] Upsell suggestion when lead.stage='hot' + gói ngắn
- [ ] Sale tone check (block banned phrases) before send
- [ ] Daily summary endpoint `GET /api/sale-assist/daily-summary`

### M15 — Lead scoring + dedup + drip · Imp 5 · Diff 3 · T7
- [ ] Impl `LeadAgentGrpcService` (Score/Dedup/Drip/Assign)
- [ ] `LeadScoringEngine`: read `lead_scoring_rules` → weighted sum events
- [ ] `lead_scoring_rules` seed defaults (asks_price+10, shares_phone+20, etc.)
- [ ] Stage classifier: cold<30, warm 30–70, hot≥70
- [ ] Auto-assign hot lead + Telegram alert <2 min
- [ ] Dedup via Qdrant similarity ≥0.92 on (name+phone tail+email embedding)
- [ ] Drip sequences (Hangfire jobs) — per-channel templates
- [ ] No-show follow-up 2h after demo missed
- [ ] Re-engage stale lead 30d via `IContactEnricher` (M11)
- [ ] Pipeline forecast endpoint `GET /api/leads/forecast`
- [ ] Lead import/export CSV

---

## P1 — High (8 module)

### M03 — Audit log + PII redaction + retention · Imp 4 · Diff 2 · T1–T2
- [ ] `AuditSaveChangesInterceptor` ghi `audit_logs` mỗi mutation
- [ ] `AuditBehavior` MediatR pipeline (command/who/diff)
- [ ] PII redact via Presidio sidecar (M11) trước insert `messages.content`
- [ ] Retention job: purge `messages.content` >30 days (replace với redacted snapshot)
- [ ] Audit viewer endpoint `GET /api/admin/audit-logs?filter=`

### M05 — 50 chat scenarios seed · Imp 4 · Diff 2 · T2–T3
- [ ] `deploy/seed/chat-scenarios.sql` 50 row (KB-001..KB-050)
- [ ] `MatchScenarioQuery` handler (trigger regex + platform filter)
- [ ] CRUD endpoint `GET/POST/PUT/DELETE /api/chat-scenarios`
- [ ] Group: First / Lộ trình / Objection / Action / Platform / Follow-up
- [ ] Success rate tracker: update `chat_scenarios.success_rate` từ conversions

### M07 — Channel adapter TikTok + Instagram + YouTube · Imp 4 · Diff 4 · T6
- [ ] `TiktokChannelAdapter` (Business API DM + comment)
- [ ] `InstagramChannelAdapter` (Graph API DM + comment + Reels)
- [ ] `YoutubeChannelAdapter` (Data API v3 comment only — DM N/A)
- [ ] OAuth refresh token rotation
- [ ] Comment-vs-DM routing logic
- [ ] Rate limit handle per vendor quotas
- [ ] Webhook subscription setup script trong `deploy/`
- [ ] Polly retry với exponential backoff
- [ ] Health checks 3 channel

### M11 — 22 utility skills concrete impl · Imp 4 · Diff 4 · T3–T10 incremental
**P0 skills (T3):**
- [ ] `IIntentClassifier` — phobert-base-v2 ONNX runtime
- [ ] `ISentimentAnalyzer` — phobert-vietnamese-sentiment
- [ ] `IPiiRedactor` — Presidio REST sidecar in docker-compose
- [ ] `IPromptInjectionDefender` — Lakera Guard or llm-guard local
- [ ] `IClaudeCostTracker` — OTel `gen_ai.*` + SQLite ledger + $200/tenant cap

**P1 skills (T5–T7):**
- [ ] `IConversationSummarizer` — Claude SK
- [ ] `ILanguageDetector` — fasttext lid.176
- [ ] `ISpamDetector` — Akismet + heuristic fallback
- [ ] `IToxicityFilter` — detoxify sidecar
- [ ] `ILeadDeduplicator` — Qdrant cosine
- [ ] `IContactEnricher` — Hunter.io + Apollo.io
- [ ] `ITimezoneDetector` — NodaTime + libphonenumber

**P2 skills (T8–T10):**
- [ ] `IHashtagResearcher` — TikTok CC + Google Trends VN
- [ ] `IZhScriptValidator` — OpenCC
- [ ] `IImagePromptGenerator` — Claude → Replicate FLUX
- [ ] `IVideoScriptComposer` — Hook/Value/CTA schema
- [ ] `IViZhTranslator` — Claude + glossary KB
- [ ] `ICompetitorMonitor` — RSS + AngleSharp
- [ ] `IPdfTableRenderer` — QuestPDF
- [ ] `IQrGenerator` — QRCoder
- [ ] `IAnomalyDetector` — Math.NET z-score
- [ ] `IForecaster` — ML.NET TimeSeries SSA

### M12 — Scheduled job runner (Hangfire) · Imp 4 · Diff 2 · T2
- [ ] Hangfire register với SQL Server storage
- [ ] Hangfire dashboard `/hangfire` Admin-only
- [ ] `RetentionPurgeJob` (daily) — purge `messages` >30d
- [ ] `DailyKpiRollupJob` (daily 23:55) — aggregate → `kpi_daily`
- [ ] `DailyReportJob` (07:30) — push Telegram (UC-I01)
- [ ] `DripSequenceJob` per-lead (M15 dependency)
- [ ] `KbAccuracyTestJob` (daily) — run 20-câu set + alert
- [ ] `HealthCheckJob` (hourly) — agent + channel health → Telegram

### M13 — Rate-limit middleware + Webhook HMAC verify · Imp 4 · Diff 2 · T2
- [ ] `RateLimiter` AddFixedWindowLimiter `/auth/*` 5 req/min
- [ ] `/webhooks/*` 100 req/min per IP
- [ ] HMAC verifier base class `WebhookSignatureVerifier`
- [ ] Vendor-specific verifiers (Zalo, FB, TikTok, IG, YT, Meta Ads)
- [ ] Reject 401 với body log audit

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

### M17 — Document Generation (QuestPDF) · Imp 4 · Diff 3 · T9
- [ ] Impl `DocsAgentGrpcService` (Generate)
- [ ] `QuestPdfRenderer` service
- [ ] Template engine (Scriban) cho dynamic fields
- [ ] Templates: QUOTE-V1, BROCHURE-HSK, SLIDE-DEMO-5, ONBOARDING-KIT
- [ ] `POST /api/docs/generate` returns file URL (MinIO signed URL 7d)
- [ ] Branded header/footer/logo từ tenant settings
- [ ] QR code footer via `IQrGenerator`
- [ ] Read receipt tracker (open beacon)
- [ ] p95 <30s instrument

### M20 — Analytics KPI daily + Metabase · Imp 4 · Diff 3 · T11
- [ ] `KpiAggregator` service — daily roll-up vào `kpi_daily`
- [ ] Metabase docker service trong compose
- [ ] Metabase dashboard JSON checked-in `deploy/metabase/`
- [ ] `AnalyticsEndpoints.cs` (5 channel + funnel + agent perf)
- [ ] Anomaly alert qua `IAnomalyDetector` (CPL spike)
- [ ] 7-day forecast via `IForecaster`
- [ ] CSV/PDF export

### M21 — Test infra · Imp 4 · Diff 2 · T1 ongoing
- [ ] Add `Clawbot.Integration.Tests` project với Testcontainers.MsSql
- [ ] Add `Clawbot.Agents.Tests` project
- [ ] CI workflow `.github/workflows/test.yml` (build + test + coverage report)
- [ ] Coverage gate ≥80% in CI fail build dưới ngưỡng
- [ ] xUnit + FluentAssertions + NSubstitute conventions
- [ ] Sample test cho mỗi bounded context (smoke)

---

## P2 — Medium (2 module)

### M18 — Content + Research pipeline · Imp 3 · Diff 3 · T8
- [ ] Impl `ContentAgentGrpcService` + `ResearchAgentGrpcService`
- [ ] Brief CRUD endpoint
- [ ] Content gen per-platform (TikTok/IG/FB/YT/Zalo)
- [ ] Approve workflow (approved_by + approved_at)
- [ ] Schedule integration (Buffer/Later API)
- [ ] Weekly trend scan job (Monday 7am)
- [ ] Repurpose flow (TikTok → Reels + Shorts)

### M19 — Ads automation (Meta + TikTok) · Imp 3 · Diff 4 · T10
- [ ] Impl `AdsAgentGrpcService`
- [ ] Meta Marketing API connector
- [ ] TikTok Business API connector
- [ ] `ads_rules` CRUD endpoint
- [ ] Rule engine: pause when CPL>threshold, scale +20% when good
- [ ] Frequency rotation when freq>2
- [ ] Budget 90% alert
- [ ] Lookalike audience builder
- [ ] Weekly ads report job

---

## Progress summary (update mỗi sprint)

| Tuần | Modules in-flight | Modules done | Notes |
|:-:|---|---|---|
| T0 | — | (skeleton only) | Domain entities + proto + grpc stubs |
| T1 | M03 | **M01** (EF wire), **M02** (RBAC stack), **M04** (KB endpoints) | Build xanh 0/0. Content seed + RAG-backed test exec defer M09. |
| T2 | | | |
| T3 | | | |
| T4 | | | |
| T5 | | | |
| T6 | | | |
| T7 | | | |
| T8 | | | |
| T9 | | | |
| T10 | | | |
| T11 | | | |
| T12 | | | |
| T13 | | | |

---

## Cross-cutting deferred items (Phase 2 / nice-to-have)

- [ ] Multi-region replication
- [ ] GDPR data export per contact
- [ ] White-label tenant branding
- [ ] Python alternative AgentService
- [ ] Mobile app (React Native)
- [ ] A/B test framework (UC-K10) full impl
- [ ] Pixel agents office UI (SW-043)
