# ClawBot SaleMkt — Spec Audit

Doc gốc: `docs/ClawBot_SaleMkt_ProjectPlan.docx`
Reference: `docs/spec-driven-&-agent-driven-development.pdf`
Sinh ngày: 2026-05-27

---

## 1. Functional Requirements → Bounded Context → Project location

| FR | Tên | Bounded Context (Domain) | Project chính | SPEC artifact |
|----|---|---|---|---|
| FR-01 | Omnichannel Inbox | `Contacts`, `Conversations` | `Clawbot.Domain`, `Clawbot.Infrastructure.Channels`, `Clawbot.Api` | `.sdd/specs/01-omnichannel-inbox/SPEC.md` |
| FR-02 | Knowledge Base | `KnowledgeBase` | `Clawbot.Domain`, `Clawbot.Infrastructure.Vectors` (Qdrant), `Clawbot.AgentService` | `.sdd/specs/02-knowledge-base/SPEC.md` |
| FR-03 | AI Agent Management | `Agents` | `Clawbot.Agents.Core`, `Clawbot.AgentService` | `.sdd/specs/03-ai-agent-management/SPEC.md` |
| FR-04 | Sale Assist | `SaleAssist` | `Clawbot.Domain`, `Clawbot.Api` | `.sdd/specs/04-sale-assist/SPEC.md` |
| FR-05 | Lead & CRM | `Leads` | `Clawbot.Domain`, `Clawbot.Api` | `.sdd/specs/05-lead-crm/SPEC.md` |
| FR-06 | Content Management | `Content` | `Clawbot.Domain`, `Clawbot.AgentService` (Content/Research agents) | `.sdd/specs/06-content-management/SPEC.md` |
| FR-07 | Document Generation | `Documents` | `Clawbot.Domain`, `Clawbot.AgentService` (Docs agent) | `.sdd/specs/07-document-generation/SPEC.md` |
| FR-08 | Analytics | `Analytics` | `Clawbot.Domain`, Metabase | `.sdd/specs/08-analytics/SPEC.md` |
| FR-09 | Ads Automation | `Ads` | `Clawbot.Domain`, `Clawbot.AgentService` (Ads agent) | `.sdd/specs/09-ads-automation/SPEC.md` |
| FR-10 | Admin & Security | `Security`, `Tenants` | `Clawbot.Infrastructure.Identity`, `Clawbot.Api.Auth` | `.sdd/specs/10-admin-security/SPEC.md` |

## 2. Non-Functional Requirements → enforcement point

| NFR | Yêu cầu | Enforcement |
|-----|---|---|
| NFR-01 | API p95 < 200 ms, chat reply < 3 s, PDF gen < 30 s | Serilog metrics + Metabase dashboard; gRPC deadlines in proto |
| NFR-02 | Uptime ≥ 99.5%, auto-restart, 5 channel không offline cùng lúc | Docker `restart: unless-stopped`, channel health monitor (`SW-020`, `SW-095`) |
| NFR-03 | TLS 1.3, encrypt PII, rate-limit, không lưu chat thô > 30 ngày | `IEncryptor`, ASP.NET rate-limit middleware, scheduled `messages` purge job |
| NFR-04 | ≥ 500 concurrent chat, horizontal scale | Stateless API + Redis + RabbitMQ MassTransit |
| NFR-05 | KB accuracy ≥ 85%, alert on drop | `kb_versions.accuracy_score`, `SW-032`, `SW-120` |

## 3. 8 AI Agent → Service → Proto → Domain

| Agent | gRPC Service (`Clawbot.AgentService.Services`) | `.proto` (`proto/`) | Domain table | Status |
|---|---|---|---|---|
| Agent-Chat | `ChatAgentGrpcService.cs` ✅ | `agent_chat.proto` ✅ | `agents`, `agent_sessions`, `chat_scenarios` | Stub exists |
| Agent-SaleAssist | `SaleAssistAgentGrpcService.cs` ➕ | `agent_sale_assist.proto` ➕ | `agents`, `quick_reply_templates` | New |
| Agent-Lead | `LeadAgentGrpcService.cs` ✅ | `agent_lead.proto` ✅ | `agents`, `leads`, `lead_scoring_rules` | Stub exists |
| Agent-Content | `ContentAgentGrpcService.cs` ✅ | `agent_content.proto` ✅ | `agents`, `content_briefs`, `content_items` | Stub exists |
| Agent-Docs | `DocsAgentGrpcService.cs` ➕ | `agent_docs.proto` ➕ | `agents`, `document_templates`, `generated_documents` | New |
| Agent-Ads | `AdsAgentGrpcService.cs` ➕ | `agent_ads.proto` ➕ | `agents`, `ads_campaigns`, `ads_rules`, `ads_actions` | New |
| Agent-Report | `ReportAgentGrpcService.cs` ➕ | `agent_report.proto` ➕ | `agents`, `kpi_daily` | New |
| Agent-Research | `ResearchAgentGrpcService.cs` ➕ | `agent_research.proto` ➕ | `agents`, `content_briefs` | New |

## 4. 31 SKILL.md → `.sdd/skills/` (Phase 1: 9 process knowledge + Phase 2: 22 utility/library-backed)

### Phase 1 — Prompt / Process knowledge (9)

| SKILL file | Phục vụ agent | Output target |
|---|---|---|
| `knowledge-base-tieng-trung.md` | Chat, SaleAssist | `kb_modules`, `kb_versions` seed |
| `50-chat-scenarios.md` | Chat | `chat_scenarios` seed (KB-001..KB-050) |
| `zalo-sales-consultation.md` | Chat, SaleAssist | tone-of-voice prompt |
| `platform-specs.md` | Chat, Content | tone/format rule cho 5 platform |
| `ads-optimization.md` | Ads | rule template |
| `content-copywriting.md` | Content | format theo platform |
| `doc-templates.md` | Docs | `document_templates` seed |
| `lead-scoring.md` | Lead | `lead_scoring_rules` seed |
| `trend-tieng-trung.md` | Research | weekly trend prompt |

### Phase 2 — Utility / library-backed (22) — C# adapter trong `src/agents/Clawbot.Agents.Core/Skills/`

| SKILL file | Agents | C# adapter | Library / source |
|---|---|---|---|
| `intent-classification.md` | Chat, SaleAssist | `Nlp.IIntentClassifier` | vinai/phobert-base-v2 |
| `sentiment-analysis.md` | Chat, SaleAssist, Report | `Nlp.ISentimentAnalyzer` | wonrax/phobert-base-vietnamese-sentiment |
| `language-detection.md` | Chat, SaleAssist | `Nlp.ILanguageDetector` | fasttext lid.176 |
| `pii-redaction.md` | Chat, SaleAssist, Report | `Nlp.IPiiRedactor` | microsoft/presidio |
| `toxicity-filter.md` | SaleAssist, Chat | `Nlp.IToxicityFilter` | unitaryai/detoxify |
| `conversation-summarization.md` | SaleAssist, Chat | `Nlp.IConversationSummarizer` | Claude Sonnet 4.6 |
| `lead-deduplication.md` | Lead | `Lead.ILeadDeduplicator` | Qdrant cosine |
| `contact-enrichment.md` | Lead, SaleAssist | `Lead.IContactEnricher` | Hunter.io + Apollo.io |
| `timezone-detection.md` | Lead, Chat | `Lead.ITimezoneDetector` | NodaTime |
| `spam-detection.md` | Chat, Lead | `Lead.ISpamDetector` | Akismet |
| `hashtag-research-vn.md` | Research, Content | `Content.IHashtagResearcher` | TikTok CC + Google Trends |
| `zh-script-validation.md` | Content, Chat | `Content.IZhScriptValidator` | OpenCC |
| `image-prompt-generation.md` | Content | `Content.IImagePromptGenerator` | Claude + Replicate FLUX |
| `short-video-script-formula.md` | Content | `Content.IVideoScriptComposer` | Hook/Value/CTA prompt |
| `vi-zh-translation.md` | Chat, Content | `Content.IViZhTranslator` | Claude + glossary KB |
| `competitor-monitor.md` | Research | `Content.ICompetitorMonitor` | RSS + scrape |
| `pdf-table-renderer.md` | Docs | `Ops.IPdfTableRenderer` | QuestPDF |
| `qr-code-generator.md` | Docs, Content | `Ops.IQrGenerator` | QRCoder |
| `anomaly-detection.md` | Report, Ads | `Ops.IAnomalyDetector` | Math.NET z-score |
| `forecast-7day.md` | Report | `Ops.IForecaster` | ML.NET TimeSeries SSA |
| `prompt-injection-defender.md` | Chat, SaleAssist, Lead | `Ops.IPromptInjectionDefender` | Lakera Guard / llm-guard |
| `claude-cost-tracker.md` | ALL 8 agents | `Ops.IClaudeCostTracker` | OTel gen_ai + SQLite |

DI wiring: `Clawbot.Agents.Core.Skills.SkillsModule.AddClawbotSkills()` → called từ `Clawbot.AgentService/Program.cs`. Full Agent↔Skill matrix: [`.sdd/skills/_index.md`](../.sdd/skills/_index.md).

## 5. 120 UC nghiệp vụ (A..K) → Agent + Table

> Tham chiếu `docs/ClawBot_SaleMkt_ProjectPlan.docx` mục 7. Cột "Implemented in" trỏ tới file source khi đã có code, hoặc `🔲 backlog` khi chưa.

### Nhóm A — Omnichannel Inbox & Routing
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-A01 | Channel adapter | `messages`, `conversations` | `Clawbot.Infrastructure.Channels.PancakeChannelAdapter` (Zalo only — 🔲 4 channel còn lại) |
| UC-A02 | Agent-Chat | `chat_scenarios` | 🔲 backlog |
| UC-A03 | Agent-Chat | `chat_scenarios` | 🔲 backlog |
| UC-A04 | Agent-Chat | `messages` | 🔲 backlog |
| UC-A05 | Agent-Lead | `contact_external_ids` | 🔲 backlog (table mới) |
| UC-A06 | Agent-Lead | `leads.score` | 🔲 backlog |
| UC-A07 | Agent-Chat | scheduled job | 🔲 backlog |
| UC-A08 | Agent-Chat | `agents.escalation_rules_json` | 🔲 backlog |
| UC-A09 | Agent-Lead | `conversations.contact_id` | 🔲 backlog |
| UC-A10 | Agent-Report | scheduled job | 🔲 backlog |

### Nhóm B — Knowledge Base & Tư vấn AI
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-B01..B07 | Agent-Chat | `kb_modules`, `kb_versions`, `chat_scenarios` | 🔲 backlog |
| UC-B08 | QA admin | `kb_versions` | 🔲 backlog |
| UC-B09..B12 | Agent-Docs | `document_templates`, `generated_documents` | 🔲 backlog (Agent-Docs mới) |

### Nhóm C — Sale Assist (10 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-C01..C10 | Agent-SaleAssist | `quick_reply_templates`, `messages`, `leads` | 🔲 backlog (Agent-SaleAssist mới) |

### Nhóm D — Lead Scoring & Marketing Data (10 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-D01..D04 | Agent-Lead | `lead_scoring_rules`, `leads`, `lead_activities` | 🔲 backlog |
| UC-D05..D10 | Agent-Lead/Report | `leads`, `kpi_daily` | 🔲 backlog |

### Nhóm E — Content Marketing (10 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-E01..E10 | Research + Content | `content_briefs`, `content_items`, `content_schedule` | 🔲 backlog |

### Nhóm F — Nurture Sequences (8 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-F01..F08 | Agent-Lead | `leads`, `lead_activities`, `messages` | 🔲 backlog |

### Nhóm G — Chăm sóc học viên (8 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-G01..G08 | Chat/Lead/Docs | `messages`, `generated_documents`, `lead_activities` | 🔲 backlog |

### Nhóm H — Ads & Paid Media (8 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-H01..H08 | Agent-Ads | `ads_campaigns`, `ads_rules`, `ads_actions` | 🔲 backlog |

### Nhóm I — Báo cáo tổng hợp (8 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-I01..I08 | Agent-Report | `kpi_daily` + Metabase | 🔲 backlog |

### Nhóm J — Internal Operations (6 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-J01..J06 | Agent-Report/Admin | `audit_logs`, `kb_versions`, `llm_configs` | partial — `audit_logs` mới |

### Nhóm K — Phát sinh T8–T13 (10 UC)
| UC | Agent | Table | Implemented in |
|---|---|---|---|
| UC-K01..K10 | Content/Chat/Lead | nhiều bảng | 🔲 backlog |

## 6. 120 UC hệ thống (SW-01..SW-12) → REST endpoint group

| SW Group | Tên | Endpoint file (`src/api/Clawbot.Api/Endpoints/`) | Notes |
|---|---|---|---|
| SW-01 (SW-001..010) | Auth & RBAC | `AuthEndpoints.cs` ✅, `RolesEndpoints.cs` ➕ | JWT đã có; cần `/auth/2fa`, `/auth/reset` |
| SW-02 (SW-011..022) | Omnichannel Inbox | `InboxEndpoints.cs` ➕ | unified inbox; merge dedup |
| SW-03 (SW-023..034) | Knowledge Base | `KbEndpoints.cs` ➕ | CRUD module/version, deploy, rollback |
| SW-04 (SW-035..046) | AI Agent Management | `AgentsEndpoints.cs` ➕ | start/stop/clone/test |
| SW-05 (SW-047..056) | Sale Assist | `SaleAssistEndpoints.cs` ➕ | draft, summary, alert |
| SW-06 (SW-057..068) | Lead & CRM | `LeadsEndpoints.cs` ➕ | scoring, pipeline, import/export |
| SW-07 (SW-069..078) | Content & Document | `ContentEndpoints.cs`, `DocumentsEndpoints.cs` ➕ | brief, queue, template |
| SW-08 (SW-079..088) | Analytics | `AnalyticsEndpoints.cs` ➕ | KPI 5 channel |
| SW-09 (SW-089..096) | Integrations | `IntegrationsEndpoints.cs` ➕ | health check 5 platform |
| SW-10 (SW-097..106) | Admin & System | `AdminEndpoints.cs` ➕ | config, backup, audit |
| SW-11 (SW-107..114) | Document Engine | `DocumentsEndpoints.cs` ➕ | template + render |
| SW-12 (SW-115..120) | KB Accuracy | `KbAccuracyEndpoints.cs` ➕ | test set, A/B, alert |

## 7. 50 Chat Scenarios — KB-001..KB-050

→ Lưu vào bảng `chat_scenarios` (seed). Mỗi row: `code` (KB-001), `trigger`, `platform[]`, `response_template`, `tone_voice`, `group_name`.

Phân nhóm theo doc (mục 4):
- Group 1 (KB-001..008): First message
- Group 2 (KB-009..016): Lộ trình & giáo trình
- Group 3 (KB-017..026): Objection handling
- Group 4 (KB-027..034): Dẫn dắt action
- Group 5 (KB-035..042): Platform-specific
- Group 6 (KB-043..050): Follow-up

## 8. Database tables (25 chính + 2 cấu hình đã có)

Xem `docs/erd.md` cho diagram đầy đủ.

Bảng mới so với ERD hiện tại:
1. `contacts` — unified contact
2. `contact_external_ids` — cross-platform unification
3. `kb_modules` — 6 module
4. `kb_versions` — versioning + accuracy
5. `kb_test_cases` — test set 20 câu
6. `chat_scenarios` — 50 scenarios
7. `agents` — config registry (config 8 agent)
8. `quick_reply_templates`
9. `document_templates`
10. `generated_documents`
11. `content_briefs`
12. `content_schedule`
13. `ads_campaigns`
14. `ads_rules`
15. `ads_actions`
16. `kpi_daily`
17. `lead_scoring_rules`
18. `lead_activities`
19. `audit_logs`
20. `roles` (thay AspNetRoles, có `tenant_id`)
21. `permissions`
22. `role_permissions`
23. `api_keys`

Bảng giữ nguyên (đã có trong ERD nháp): `tenants`, `conversations`, `messages`, `leads`, `agent_sessions`, `agent_traces`, `content_items`, `pancake_configs`, `llm_configs`, AspNetUsers.

Bảng đổi tên / refactor: `knowledge_items` → tách thành `kb_modules` + `kb_versions` (versioning + accuracy explicit).

## 9. Gaps / Risk

| # | Gap | Mức ảnh hưởng | Hành động |
|---|---|---|---|
| G1 | Chỉ có 1/5 channel adapter (Pancake/Zalo). Còn FB Graph, TikTok Business, IG, YT chưa có | Cao | Backlog T6 (theo roadmap doc) |
| G2 | Chưa có Semantic Kernel + RAG wire | Cao | Stub `IVectorStore` → cần triển khai trong `PgVectorStore`/`QdrantVectorStore` |
| G3 | AspNet Identity chưa thay bằng custom RBAC tenant-scoped | TB | Migrate roles vào bảng `roles` mới; giữ `AppUser/AppRole` cho session |
| G4 | Chưa có scheduled job runner (Hangfire/Quartz) | TB | Cần cho drip, nurture, daily KPI |
| G5 | Frontend chưa có UI cho 11/12 bounded context mới | Cao | Theo roadmap, T4–T9 |
| G6 | Chưa có data retention job (purge > 30 ngày) | TB (compliance) | Backlog NFR-03 |
| G7 | Chưa có rate-limit middleware | TB | Backlog NFR-03 |
| G8 | Test coverage gần 0% (chỉ skeleton test project) | Cao | Áp dụng TDD theo PDF chương 7 |

## 10. Spec-Driven workflow áp dụng (theo PDF)

Áp dụng pipeline 5 phase cho mỗi feature lớn:
1. **CONTEXT.md** — viết khi bắt đầu sprint (team)
2. **SPEC.md** — 8 section bắt buộc, dùng EARS cho Acceptance Criteria
3. **AI Spec Review** — Claude review draft
4. **PLAN.md** — AI decompose; human approve
5. **TASKS.md** — atomic task ≤ 4h, link tới EARS

Constitution + AGENTS.md viết một lần ở root, immutable trừ khi đồng thuận team.

Hybrid Core-Shell theo §13 của PDF:
- **Core (spec-driven full)**: FR-02 Knowledge Base, FR-03 Agent Mgmt, FR-05 Lead CRM, FR-10 Admin/Security
- **Shell (agent-driven)**: FR-04 Sale Assist UI, FR-06 Content gen, FR-08 Analytics UI

---

## Traceability sample (EARS)

Một acceptance criteria mẫu cho FR-01 / UC-A07:

> **WHEN** `messages.sent_at` ngoài giờ hành chính (06:00–22:00 GMT+7) **AND** `conversations.status = 'OPEN'` **THE SYSTEM SHALL** auto-reply theo template `chat_scenarios` group "out-of-hours" trong vòng 5 giây.

Link code: `Clawbot.Domain.ChatScenarios.ChatScenario`, endpoint `POST /webhooks/{platform}`, agent `ChatAgentGrpcService.Reply`.
