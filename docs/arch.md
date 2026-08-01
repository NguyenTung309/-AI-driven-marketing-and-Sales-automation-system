CLAWBOT SALEMKT
Tài liệu Kiến trúc & Triển khai
Omnichannel Sales Automation cho trung tâm tiếng Trung
Phiên bản 2.1  |  Tháng 5/2026
Cập nhật: SQL Server 2022 · 34 tables · 12 bounded contexts · 8 AI agents · 31 skills · SDD artifacts

## ⚠ Strategy update 2026-05-29 — Pancake unified channel

Toàn bộ tích hợp 5 kênh (Facebook Page/Messenger/Comments, Instagram, Zalo OA, TikTok Shop, WhatsApp, Google Business) chuyển sang dùng **Pancake (pancake.vn / pages.fm)** làm omnichannel proxy.

- M06 = tích hợp 1 lần với Pancake API; M07 (TikTok/IG/YT native) bị superseded.
- Mọi tham số runtime (BaseUrl, AccessToken, WebhookSecret, SignatureHeader, SignatureAlgo, SignatureEncoding, SendPathTemplate, AuthMode) cấu hình per-tenant qua `PUT /api/channels/pancake/config` — không hard-code, không redeploy khi đổi.
- Secret AES-encrypted trong `pancake_configs` (existing entity reused).
- Lý do: tránh vendor SDK churn × 5, OAuth refresh × 5, comment-vs-DM routing × 5. Pancake giải xong rồi.
- Fallback path: nếu Pancake outage / disagreement, mỗi native adapter implement riêng theo `IChannelAdapter` — schema (`conversations`, `messages`, `contact_external_ids`) + ingestor pipeline M08 không đổi.


1. Tổng quan Hệ thống
ClawBot SaleMkt là nền tảng tự động hoá bán hàng đa nền tảng (Zalo, Facebook, TikTok, Instagram, YouTube) cho trung tâm dạy tiếng Trung. 5 nhân sự thật + 8 AI agent + Knowledge Base tiếng Trung chuyên sâu → tư vấn 24/7, một sale chăm gấp 3× khách hàng.
1.1 Mục tiêu thiết kế
Omnichannel inbox: tổng hợp DM + comment từ 5 kênh vào 1 luồng. Không miss tin nhắn.
Sale Assist: 1 sale chăm 3× khách nhờ AI draft + context panel + alert.
Spec-driven + Agent-driven Development (SDD/ADD): mọi feature đi qua SPEC → PLAN → TASKS → CODE → TESTS với traceability.
Multi-tenant: shared database SQL Server, cách ly bằng tenant_id + EF Core global query filter.
Khả năng mở rộng: API, AgentService, DB scale độc lập. Agent service là gRPC riêng — có thể viết lại bằng Python.
Cost discipline: hard cap Claude API $200/tháng/tenant, alert 80%.
1.2 Công nghệ sử dụng
2. Cấu trúc Source Code
Solution gồm 12 .NET project + 1 frontend React + .sdd/ artifacts + deploy/. Tuân theo Clean Architecture + DDD bounded context.
2.1 Sơ đồ thư mục
d:\Clawbot\
|-- Clawbot.sln                          # Solution -- 12 projects
|-- Directory.Build.props                # MSBuild props chung
|-- Directory.Packages.props             # Quan ly NuGet tap trung
|-- proto/                               # gRPC contracts (9 file .proto)
|-- src/
|   |-- shared/
|   |   |-- Clawbot.Domain/              # 12 bounded contexts
|   |   |-- Clawbot.Application/         # MediatR handlers + validators
|   |   |-- Clawbot.SharedKernel/        # Abstractions
|   |   `-- Clawbot.Infrastructure/      # EF Core (SqlServer), Identity, Redis, RabbitMQ, Qdrant
|   |-- api/
|   |   |-- Clawbot.Api/                 # ASP.NET Core + SignalR + Webhooks
|   |   `-- Clawbot.Api.Contracts/       # Public DTOs
|   |-- agents/
|   |   |-- Clawbot.Agents.Contracts/    # Proto-generated gRPC stubs
|   |   |-- Clawbot.Agents.Core/         # IAgent + Orchestrator + Skills/ (22 adapter)
|   |   `-- Clawbot.AgentService/        # gRPC host (9 service)
|   `-- frontend/clawbot-web/            # React 19 + Vite + TS + Tailwind
|-- tests/
|   |-- Clawbot.Domain.Tests/
|   `-- Clawbot.Application.Tests/
|-- deploy/
|   |-- docker-compose.yml               # 5 service
|   |-- .env.example
|   `-- migrations/0001_init.sql         # DDL SQL Server (source of truth)
|-- docs/                                # ProjectPlan docx + spec-audit + erd
`-- .sdd/                                # Spec-Driven Development artifacts
|-- constitution.md                  # 7 articles
|-- AGENTS.md                        # Context cho AI coding agents
|-- specs/01..10/SPEC.md             # 10 SPEC files (EARS notation)
`-- skills/_index.md + 31 SKILL.md
2.2 12 bounded context trong Clawbot.Domain
3. Kiến trúc & Lý do Thiết kế
3.1 Clean Architecture + DDD
Domain: zero external dependencies (không EF, không MediatR). 12 bounded context.
Application: phụ thuộc Domain + SharedKernel. MediatR commands/queries + FluentValidation.
Infrastructure: phụ thuộc Application. EF Core SqlServer + Identity + Polly + Qdrant + AesEncryptor.
API + AgentService: phụ thuộc Application + Infrastructure. Entry point HTTP / gRPC.
3.2 Spec-Driven Development (SDD)
Theo PDF Spec-Driven & Agent-Driven Development 2026. Mọi feature lớn đi qua pipeline 5 phase:
Phase 0 — CONTEXT.md: team viết bối cảnh, ràng buộc, câu hỏi mở.
Phase 1 — SPEC.md: 8 section + Acceptance Criteria theo EARS notation.
Phase 2 — AI review SPEC: Claude phát hiện gap, mâu thuẫn.
Phase 3 — PLAN.md: AI sinh, human approve.
Phase 4 — TASKS.md: atomic task ≤ 4h.
Phase 5 — Implement + Validate: traceability comment liên kết code → SPEC.
3.3 Agent-Driven Development + Skill System
Skill = SKILL.md file (frontmatter OpenClaw + custom clawbot) + tuỳ chọn C# adapter.
9 skill Phase 1 = prompt/process knowledge.
22 skill Phase 2 = utility/library-backed (intent, sentiment, PII, PDF, forecast…).
Agent đọc skill từ Skill Catalog thay vì hardcode prompt trong code.
3.4 Multi-tenant
Shared database; cách ly bằng tenant_id column trên mọi tenant-scoped table. ITenantOwned interface đánh dấu domain entity; AppDbContext áp HasQueryFilter global. JWT mang claim tenant_id + tenant_slug.
3.5 gRPC Agent Microservice
4. Cơ sở dữ liệu — SQL Server 2022
Source of truth: deploy/migrations/0001_init.sql. EF Core 8 + Microsoft.EntityFrameworkCore.SqlServer chỉ map. ERD: docs/erd.md.
4.1 Conventions
snake_case (auto map qua SnakeCaseConventions cho entity không thuộc Identity).
PK = UNIQUEIDENTIFIER DEFAULT NEWID(); timestamp = DATETIMEOFFSET DEFAULT SYSDATETIMEOFFSET().
tenant_id UNIQUEIDENTIFIER NOT NULL + index (tenant_id, created_at DESC).
Soft-delete cho aggregate root mutable; immutable append-only cho log tables.
JSON: NVARCHAR(MAX) (SQL Server không có jsonb).
Vector lưu Qdrant; SQL Server cột embedding chỉ là JSON snapshot.
FK action: SQL Server cấm multi-path cascade → dùng NO ACTION ở nhánh con.
4.2 Toàn bộ 33 bảng
5. 8 AI Agent & 31 Skill Catalog
5.1 8 AI Agent — gRPC service
5.2 Phase 1 — 9 prompt/process knowledge skill
5.3 Phase 2 — 22 utility/library-backed skill — giải thích từng skill
Mỗi skill có C# adapter interface trong src/agents/Clawbot.Agents.Core/Skills/{Nlp,Lead,Content,Ops}/. Các adapter mặc định đã được đăng ký bằng implementation cụ thể; tích hợp ngoài vẫn config-gated/fallback theo SPEC.
NLP — 6 skill (Clawbot.Agents.Core.Skills.Nlp.*)
Lead Intelligence — 4 skill (Clawbot.Agents.Core.Skills.Lead.*)
Content Marketing — 6 skill (Clawbot.Agents.Core.Skills.Content.*)
Ops / Cross-cutting — 6 skill (Clawbot.Agents.Core.Skills.Ops.*)
5.4 Wiring DI
Mỗi skill: interface I<Name> + DTO record + internal sealed implementation/fallback class.
SkillsModule.AddClawbotSkills() đăng ký 22 service singleton.
AgentService/Program.cs gọi builder.Services.AddClawbotSkills() khi start.
SkillsModule.AddClawbotSkills() đăng ký implementation mặc định cho từng skill; service cần API key hoặc vendor ngoài degrade/fallback theo cấu hình.
6. SDD Artifacts (.sdd/)
Theo PDF "Spec-Driven & Agent-Driven Development" 2026. Artifact luôn dưới .sdd/, là contract giữa human intent và agent execution.
6.1 Cấu trúc .sdd/
.sdd/
|-- constitution.md                # 7 articles, immutable
|-- AGENTS.md                      # Context cho AI agent
|-- specs/
|   |-- _template.md
|   |-- 01-omnichannel-inbox/SPEC.md      (FR-01)
|   |-- 02-knowledge-base/SPEC.md         (FR-02)
|   |-- 03-ai-agent-management/SPEC.md    (FR-03)
|   |-- 04-sale-assist/SPEC.md            (FR-04)
|   |-- 05-lead-crm/SPEC.md               (FR-05)
|   |-- 06-content-management/SPEC.md     (FR-06)
|   |-- 07-document-generation/SPEC.md    (FR-07)
|   |-- 08-analytics/SPEC.md              (FR-08)
|   |-- 09-ads-automation/SPEC.md         (FR-09)
|   `-- 10-admin-security/SPEC.md         (FR-10)
`-- skills/
|-- _index.md                  # Catalog 31 skill + Agent x Skill matrix
`-- 31 SKILL.md
6.2 EARS notation cho Acceptance Criteria
Ubiquitous: THE SYSTEM SHALL <requirement>.
Event: WHEN <trigger> THE SYSTEM SHALL <action>.
State: WHILE <state> THE SYSTEM SHALL <restriction>.
Optional: WHERE <feature included> THE SYSTEM SHALL <behavior>.
Unwanted: IF <unwanted condition> THEN THE SYSTEM SHALL <safe response>.
6.3 Constitution — 7 articles
7. Triển khai (Deployment)
7.1 Yêu cầu môi trường
7.2 Khởi động dev environment
Bước 1 — Infrastructure
cd d:\Clawbot\deploy
copy .env.example .env
docker compose up -d
5 container: sqlserver (1433), redis (6379), rabbitmq (5672/15672), qdrant (6333), minio (9000/9001).
Bước 2 — Apply DDL
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
-S localhost -U sa -P "Clawbot!2026" -C -d clawbot \
-i /var/opt/mssql/migrations/0001_init.sql
Bước 3 — Backend API
cd d:\Clawbot
dotnet run --project src/api/Clawbot.Api --launch-profile http
Bước 4 — Agent Service
dotnet run --project src/agents/Clawbot.AgentService
Bước 5 — Frontend
cd d:\Clawbot\src\frontend\clawbot-web
npm install
npm run dev
7.3 Cấu hình quan trọng (appsettings.json)
8. Lưu ý Bảo mật
9. Roadmap 13 tuần
Lớp | Công nghệ
Backend API | .NET 8, ASP.NET Core minimal APIs, EF Core 8, MediatR, FluentValidation, Serilog, SignalR, Polly
AI Agent Service | .NET 8 gRPC, Microsoft Semantic Kernel, Anthropic Claude Sonnet 4.6
Database | Microsoft SQL Server 2022 (33 tables, snake_case, soft-delete, immutable append logs)
Vector store | Qdrant (kb_versions collection); SQL Server giữ JSON snapshot embedding
Message queue | RabbitMQ (MassTransit)
Cache | Redis 7
Object storage | MinIO (S3-compatible)
Auth | ASP.NET Core Identity + JWT Bearer (HS256)
Frontend | React 19, Vite, TypeScript, Tailwind, Zustand, TanStack Query
Observability | Serilog structured JSON + OpenTelemetry (gen_ai.* semconv) + Metabase BI
Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers.MsSql
Dev infra | Docker Compose (5 service)
Folder | Aggregate / Entity | Mô tả
Common/ | AggregateRoot, Entity, IDomainEvent, ITenantOwned | Base types DDD
Tenants/ | Tenant | Multi-tenant root
Contacts/ | Contact + ContactExternalId | Khách unified cross-platform
Conversations/ | Conversation + Message | Inbox đa kênh; Message immutable log
KnowledgeBase/ | KbModule + KbVersion + KbTestCase | 6 module + versioning + 20-câu test
ChatScenarios/ | ChatScenario | 50 kịch bản (KB-001..KB-050)
Agents/ | AgentConfig + AgentSession + AgentTrace | 8 agent registry + run history
Leads/ | Lead + LeadActivity + LeadScoringRule | Lead scoring 5-channel weighted
SaleAssist/ | QuickReplyTemplate | Template library 1-click cho sale
Documents/ | DocumentTemplate + GeneratedDocument | PDF/brochure/slide + read receipt
Content/ | ContentBrief + ContentItem + ContentSchedule | Content pipeline 5-platform
Ads/ | AdsCampaign + AdsRule + AdsAction | Meta + TikTok automation
Analytics/ | KpiDaily | KPI snapshot/ngày/platform
Security/ | Role + Permission + AuditLog + ApiKey | RBAC tenant-scoped + audit
Vấn đề | Giải pháp
Semantic Kernel/LLM call tốn RAM | AgentService chạy process riêng — API không bị chậm
Streaming token từ Claude tới client | Server-streaming gRPC tự nhiên
Muốn thử Python cho AI | Implement .proto interface là xong
Agent cần scale riêng | Deploy nhiều AgentService instance
Nhóm | Bảng | Mục đích chính
Tenancy & RBAC | tenants | Multi-tenant root + plan_name + settings JSON
Tenancy & RBAC | users | AspNet Identity extended với tenant_id, display_name
Tenancy & RBAC | roles + permissions + role_permissions + AspNetUserRoles | Permission RBAC tenant-scoped; user assignments use ASP.NET Identity
Tenancy & RBAC | api_keys | API key per tenant
Tenancy & RBAC | audit_logs | Append-only: action + resource + diff_json + ip
Contacts | contacts | Khách unified, lifetime_score, soft-delete
Contacts | contact_external_ids | (platform, external_id) UNIQUE — cross-platform stitching
Conversations | conversations | Thread/platform; status open/pending/resolved/escalated
Conversations | messages | Append-only — direction/sender_type/content/sent_at
Leads | leads | score + stage cold/warm/hot/customer/lost
Leads | lead_scoring_rules | Channel-weighted weight per event_code
Leads | lead_activities | Append-only timeline
KnowledgeBase | kb_modules | 6 KB module
KnowledgeBase | kb_versions | Versioning + accuracy_score + embedding JSON
KnowledgeBase | kb_test_cases | 20-câu test set per module
ChatScenarios | chat_scenarios | 50 kịch bản KB-001..KB-050
Agents | agents | 8 agent config: skill_files_json + kb_modules_json
Agents | agent_sessions | Per-run goal + plan_json + status
Agents | agent_traces | Append-only trace events
SaleAssist | quick_reply_templates | Template library
Documents | document_templates | QUOTE-V1, BROCHURE-HSK, SLIDE-DEMO-5…
Documents | generated_documents | File đã tạo + sent_via + opened_at
Content | content_briefs | Brief input từ MKT
Content | content_items | 5 platform + status draft/approved/published
Content | content_schedule | Lịch post + posted_at + post_url
Ads | ads_campaigns | Meta + TikTok mirror
Ads | ads_rules | metric/comparator/threshold/action
Ads | ads_actions | Append-only: action_taken + payload_json
Analytics | kpi_daily | (tenant, date, platform) snapshot
Channel & LLM | pancake_configs | Webhook secret + access token (AES)
Channel & LLM | llm_configs | Per-tenant LLM provider/model/key
Agent | gRPC service | .proto | Trách nhiệm
Agent-Chat | ChatAgentGrpcService | agent_chat.proto | Tư vấn 24/7 5 kênh
Agent-SaleAssist | SaleAssistAgentGrpcService | agent_sale_assist.proto | Draft + summary + alert >5 phút
Agent-Lead | LeadAgentGrpcService | agent_lead.proto | Score, dedup, drip, assign
Agent-Content | ContentAgentGrpcService | agent_content.proto | Sinh content per platform
Agent-Docs | DocsAgentGrpcService | agent_docs.proto | PDF quote/brochure/slide <30s
Agent-Ads | AdsAgentGrpcService | agent_ads.proto | Meta + TikTok auto pause/scale
Agent-Report | ReportAgentGrpcService | agent_report.proto | Daily 7h30 + weekly + alert
Agent-Research | ResearchAgentGrpcService | agent_research.proto | Weekly trend tiếng Trung VN
Skill | Agent dùng | Mục đích — giải thích
knowledge-base-tieng-trung | Chat, SaleAssist | 6 KB module (giáo trình HSK/lộ trình/giá/FAQ/GV/đặc thù kênh). Mọi câu trả lời agent phải lấy nguồn từ KB này. KB chính xác hơn = agent đáng tin hơn.
50-chat-scenarios | Chat | 50 kịch bản KB-001..KB-050. Mỗi tin nhắn inbound được match trigger → áp template + tone theo platform. Bao trùm chào hỏi, hỏi giá, objection, đặt lịch, follow-up.
zalo-sales-consultation | Chat, SaleAssist | 5 bước Zalo (chào → hỏi mục tiêu → đề xuất lộ trình → giới thiệu giá → chốt). 5 objection chính. Giờ vàng. Dấu hiệu lead nóng (+score).
platform-specs | Chat, Content | Char limit, format, hashtag rule, tone-of-voice cho từng platform (Zalo/Facebook/TikTok/Instagram/YouTube). Tránh dùng tone TikTok trên Zalo.
ads-optimization | Ads | CPL/frequency/CTR threshold + scaling rule (+20% khi tốt) + dayparting (pause 2-5h). Định nghĩa rule cho bảng ads_rules.
content-copywriting | Content | Hook formula per platform (TikTok 3s question / Facebook storytelling / YouTube SEO title). Brand voice, banned phrase ("rẻ nhất", "đảm bảo đậu").
doc-templates | Docs | Catalog template QUOTE/BROCHURE/SLIDE/ONBOARDING + required fields per template. Định nghĩa schema cho bảng document_templates.
lead-scoring | Lead | Stage threshold (cold/warm/hot 30/70) + bảng weight default per event (asks_price +10, shares_phone +20...). Seed cho bảng lead_scoring_rules.
trend-tieng-trung | Research | Weekly trend source (TikTok/YouTube/Baidu/Google Trends VN) + topic filter (include HSK / exclude political). Cadence thứ 2 sáng.
Skill | Library backing | Phục vụ việc gì
intent-classification | vinai/phobert-base-v2 (HuggingFace) | Phân loại 1 trong 8 intent: greeting, price_inquiry, objection, schedule_request, farewell, spam, escalation_request, other. Agent-Chat dùng intent label để chọn đúng chat_scenarios template. Giúp tự động routing không cần if-else hardcode.
sentiment-analysis | wonrax/phobert-base-vietnamese-sentiment | Polarity 3-class (neg/neu/pos). Phát hiện khách bực bội → escalate sale. Track sentiment drift over time → cảnh báo churn risk. Report dùng để dashboard "sale nào tăng/giảm sentiment khách nhất".
language-detection | fasttext lid.176 | Phát hiện ngôn ngữ (vi/en/zh/...) trong tin nhắn để route tone-voice phù hợp. Khách viết English → Chat dùng tone EN. Khách viết 中文 → activate skill vi-zh-translation tự động.
pii-redaction | microsoft/presidio (REST sidecar) | Mask phone (0[3|5|7|8|9]xxx)/email/CCCD trong message log trước khi append vào audit_logs hoặc gửi external sink. Compliance NFR-03 (không giữ PII thô >30 ngày).
toxicity-filter | unitaryai/detoxify (REST sidecar) | Block từ ngữ xúc phạm/off-brand. Filter cả output Claude lẫn tin sale tự gõ. Constitution Art.6 cấm "đảm bảo đậu HSK", "rẻ nhất". Threshold > 0.7 → block, < 0.7 → warn.
conversation-summarization | Claude Sonnet 4.6 (via Semantic Kernel) | Tóm tắt thread > 20 turn thành 3-5 câu + 3 key points. Sale mới vào ca nhanh grasp context. Cache theo (conversation_id, last_msg_id) Redis 6h.
Skill | Library backing | Phục vụ việc gì
lead-deduplication | Qdrant cosine similarity | Đi xa hơn (platform, external_id) exact match: dùng embedding similarity trên (display_name + phone tail + email) → tìm 5 candidate gần nhất → confirm dedup ≥ 0.92. Khách nhắn Zalo + Facebook cùng 1 lúc → merge vào 1 contact.
contact-enrichment | Hunter.io + Apollo.io | Tra ngược email/phone để biết company/jobTitle/LinkedIn. Lead phụ huynh hỏi cho con → biết phụ huynh làm gì → personalize tư vấn business Chinese. Cache 24h.
timezone-detection | NodaTime + libphonenumber | Đoán timezone từ phone country code/locale. +84 → Asia/Ho_Chi_Minh, +86 → Asia/Shanghai. Drip sequence + reminder gửi đúng giờ local. 100% sync, in-process.
spam-detection | Akismet REST | Filter bot/scam/repeated emoji trước khi enqueue cho Agent-Chat. NFR-04: handle ≥500 chat song song — spam waste agent budget. Heuristic fallback offline khi Akismet down.
Skill | Library backing | Phục vụ việc gì
hashtag-research-vn | TikTok Creative Center + Google Trends VN | Top 20 hashtag trending VN trên 5 platform mỗi thứ 2. Feed Agent-Research weekly trend list (UC-E01). Cache 6h Redis.
zh-script-validation | OpenCC (s2t/t2s) | Đảm bảo KB tiếng Trung không mix 简体/繁體. Mỗi kb_versions deploy check consistency. Content nhắm học viên VN → default Simplified. 100% deterministic.
image-prompt-generation | Claude → Replicate FLUX | Sinh prompt visual cho content brief. Brief "post Reels mời học HSK3" → prompt FLUX style: bright, Asian learner, classroom, no text overlay. Negative prompt block watermark + low-quality mặc định.
short-video-script-formula | Claude (Hook+Value+CTA schema) | Brief platform=tiktok → script chuẩn 3 phần (Hook 3s + Value ~25s + CTA 2s) + shot list 5-7 cảnh. Output JSON schema-enforced.
vi-zh-translation | Claude Sonnet 4.6 + glossary KB | Dịch VI↔ZH với glossary giữ thuật ngữ chuẩn (HSK level, tên giáo trình riêng). Học viên hỏi 1 cụm 中文 → trả lời VI + 中文. Content phòng MKT VN↔Trung.
competitor-monitor | System.ServiceModel.Syndication + AngleSharp | Weekly scan 5-10 trung tâm tiếng Trung VN — title + snippet + URL → feed Agent-Research để propose counter-content. ETag + content hash dedupe.
Skill | Library backing | Phục vụ việc gì
pdf-table-renderer | QuestPDF NuGet (.NET native) | Render bảng giá / lộ trình → PDF branded. Bảng quote nhiều dòng (lộ trình HSK3 16 buổi). FR-07 yêu cầu <30s. Native, không cần Python subprocess.
qr-code-generator | QRCoder NuGet | Sinh QR PNG cho Zalo OA / Messenger / link đăng ký, nhúng vào PDF footer hoặc Story IG. 100% sync, p95 < 50 ms. ECC level Q (resilient).
anomaly-detection | Math.NET Numerics z-score | Z-score trên time series KPI. UC-I05: CPL spike trên bất kỳ kênh nào → Telegram alert. Rolling window 14 ngày, threshold |z| > 2.5. Output kèm reason cho alert message.
forecast-7day | ML.NET TimeSeries SSA | Forecast 7 ngày lead/spend/conversion. Input: kpi_daily 60 ngày. Output: forecast + lower/upper band. UC-D10 pipeline forecast tuần. Train per-tenant per-platform.
prompt-injection-defender | Lakera Guard / protectai/llm-guard | Phát hiện jailbreak / "Ignore previous instructions" / data-exfil pattern trong inbound text TRƯỚC khi pass tới Claude. Constitution Art.3 + NFR-03. IsMalicious=true → 422 + log audit.
claude-cost-tracker | OpenTelemetry gen_ai.* + SQLite ledger | Ghi mọi gọi Claude với input/output tokens + USD cost theo (tenant, agent, model). Hard cap $200/tháng/tenant + soft alert 80% (Constitution Art.6). Daily roll-up vào kpi_daily.
Article | Nội dung
1. Tech stack | STRICT — không deviate. Adding dep cần RFC trong .sdd/rfcs/
2. Coding standards | DDD layered, Domain zero deps, snake_case SQL
3. Security | No secrets in source, AES at-rest, TLS 1.3, 30-day raw retention, parameterized SQL only
4. Git | Conventional commits, squash on merge, no force-push main
5. Testing | TDD, 80% coverage floor, Testcontainers cho integration
6. AI agent | Mỗi agent có code trong agents table + SKILL.md. Claude budget cap $200/tháng/tenant
7. Review | CI green + 1 reviewer + SPEC ID link; security PR cần 2 reviewer
Công cụ | Phiên bản | Mục đích
.NET SDK | 8.0.x | Build + run backend
Node.js | 20.x+ | Build frontend
Docker Desktop | 4.x+ | Chạy SQL Server, Redis, RabbitMQ, Qdrant, MinIO
Git | Bất kỳ | Clone source
Key | Mô tả | Mặc định
ConnectionStrings:SqlServer | SQL Server connection | Server=localhost,1433;Database=clawbot;User Id=sa;...
ConnectionStrings:Redis | Redis endpoint | localhost:6379
ConnectionStrings:RabbitMq | AMQP URL | amqp://guest:guest@localhost:5672
Jwt:SigningKey | JWT signing — đổi production | (≥32 ký tự)
Encryption:Base64Key | AES 32-byte base64 — đổi production | (base64 32 bytes)
Vector:Backend | Vector store | qdrant (SQL Server không hỗ trợ vector index)
Hạng mục | Việc cần làm
JWT Signing Key | Đổi Jwt:SigningKey ≥32 ký tự random; lưu secret manager / env var
AES Key | Đổi Encryption:Base64Key 32 bytes random thực sự
SQL Server SA password | Đổi MSSQL_SA_PASSWORD trong .env
Webhook HMAC verify | VerifyWebhookSignatureAsync cho Pancake unified webhook; per-channel native HMAC chỉ là fallback nếu bỏ broker
HTTPS / TLS 1.3 | Kestrel SSL cert HOẶC reverse proxy nginx/Caddy
Rate limit | ASP.NET RateLimiter cho /auth/* và /webhooks/*
PII retention | Purge job: messages.content > 30 ngày qua pii-redaction skill
Claude cost cap | Anthropic console hard cap $200/tháng + soft alert 80%
Prompt injection | Mọi inbound qua prompt-injection-defender skill
Tuần | Giai đoạn | Người thật | AI Agent | Deliverable
T1 | KB + Infra | P1 VPS/Docker. P3 export log. | — | Stack live, KB draft
T2 | KB hoàn thiện | P3+P4. P1 tạo Pancake tenant/account. | Agent-Code | KB v1
T3 | 50 kịch bản | P3 viết. P4 validate. | Agent-Chat: test KB | KB ≥85%, 50 scenarios
T4 | Chat live qua Pancake | P3 monitor. P4 QA. | Agent-Chat 24/7 | Chatbot Pancake unified live
T5 | Sale Assist | P3 setup. P4 test draft. | Agent-SaleAssist | Sale Assist live
T6 | Pancake omnichannel hardening | P1 capture live webhook. P2 verify reply/DM semantics. | Agent-Chat: Pancake unified broker | 5-channel broker verified
T7 | Lead+Marketing | P5 dashboard. P3 drip. | Agent-Lead | Pipeline tự động
T8 | Content MKT | P2 trend. P4 QA. | Agent-Research + Content | Content 5 platform
T9 | Document auto | P3 template. P4 QA PDF. | Agent-Docs | Tài liệu <30s
T10 | Ads automation | P2 ngưỡng. P4 QA. | Agent-Ads | Ads tự tối ưu
T11 | KB refinement | P3+P4 update từ data. | KB v2 | KB v2
T12 | QA tổng thể | P4 test 240 UC. P5 SOP. | 240 UC | QA report + SOP
T13 | Go-live | War room 3 ngày. | Full system | Go-live
