# ClawBot SaleMkt

**Nền tảng tự động hoá bán hàng & marketing đa kênh (omnichannel) cho trung tâm dạy tiếng Trung.**

5 nhân sự thật + 8 AI agent + Knowledge Base tiếng Trung chuyên sâu → tư vấn 24/7, một sale chăm gấp 3× khách hàng. Tổng hợp DM + comment từ 5 kênh (Zalo, Facebook, TikTok, Instagram, YouTube/Google Business) vào một luồng inbox duy nhất — không miss tin nhắn.

> Phiên bản kiến trúc 2.1 · SQL Server 2022 · 34 bảng · 12 bounded context · 8 AI agent · 31 skill. Chi tiết: [docs/arch.md](docs/arch.md).

---

## ✨ Tính năng chính

- **Omnichannel Inbox** — gộp DM + comment 5 kênh về một luồng; tích hợp qua [Pancake](https://pancake.vn) làm proxy đa kênh, cấu hình per-tenant không cần redeploy.
- **Sale Assist** — AI draft câu trả lời + context panel + cảnh báo khách chờ > 5 phút, giúp 1 sale chăm 3× khách.
- **AI Agents** — 8 agent gRPC (Chat, SaleAssist, Lead, Content, Docs, Ads, Report, Research) chạy 24/7.
- **Knowledge Base tiếng Trung** — 6 module (giáo trình HSK, lộ trình, giá, FAQ, GV) + versioning + 20-câu test set, lưu vector trên Qdrant.
- **Lead CRM** — scoring 5 kênh weighted, dedup theo embedding, drip sequence, auto-assign.
- **Content & Ads automation** — sinh content theo từng platform + auto pause/scale Meta/TikTok ads.
- **Multi-tenant** — shared database, cách ly bằng `tenant_id` + EF Core global query filter.
- **Cost discipline** — hard cap Claude API $200/tháng/tenant, cảnh báo ở mức 80%.

## 🧱 Công nghệ

| Lớp | Công nghệ |
|-----|-----------|
| Backend API | .NET 8, ASP.NET Core minimal APIs, EF Core 8, MediatR, FluentValidation, SignalR, Serilog, Polly |
| AI Agent Service | .NET 8 gRPC, Microsoft Semantic Kernel, Anthropic Claude Sonnet 4.6 |
| Database | SQL Server 2022 (snake_case, soft-delete, immutable append logs) |
| Vector store | Qdrant (SQL Server giữ JSON snapshot embedding) |
| Hạ tầng | RabbitMQ (MassTransit), Redis 7, MinIO (S3-compatible) |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Frontend | React 19, Vite, TypeScript, Tailwind, Zustand, TanStack Query |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers |
| Dev infra | Docker Compose (5 service) |

## 🏛️ Kiến trúc

Clean Architecture + DDD bounded context:

- **Domain** — zero external dependency (không EF, không MediatR), 12 bounded context.
- **Application** — MediatR commands/queries + FluentValidation.
- **Infrastructure** — EF Core SqlServer + Identity + Polly + Qdrant + AES encryptor.
- **API / AgentService** — entry point HTTP / gRPC, tách process để LLM call không làm chậm API.

AgentService là microservice gRPC độc lập — có thể scale riêng hoặc viết lại bằng Python qua interface `.proto`.

## 📂 Cấu trúc thư mục

```
Clawbot.sln                       # Solution — 12 .NET project
proto/                            # gRPC contracts (.proto)
src/
├── shared/
│   ├── Clawbot.Domain/           # 12 bounded context (zero deps)
│   ├── Clawbot.Application/       # MediatR handlers + validators
│   ├── Clawbot.SharedKernel/      # Abstractions
│   └── Clawbot.Infrastructure/    # EF Core, Identity, Redis, RabbitMQ, Qdrant
├── api/
│   ├── Clawbot.Api/               # ASP.NET Core + SignalR + Webhooks
│   └── Clawbot.Api.Contracts/     # Public DTOs
├── agents/
│   ├── Clawbot.Agents.Core/       # IAgent + Orchestrator + Skills/
│   └── Clawbot.AgentService/      # gRPC host (8 agent)
└── frontend/clawbot-web/          # React 19 + Vite + TS + Tailwind
tests/                            # xUnit (Domain + Application)
deploy/                           # docker-compose + migrations DDL
docs/                             # Kiến trúc, ERD, project plan
.sdd/                             # Spec-Driven Development artifacts
```

## 🚀 Khởi động dev

**Yêu cầu:** .NET SDK 8.0.x · Node.js 20+ · Docker Desktop 4.x+

```bash
# 1. Hạ tầng (sqlserver, redis, rabbitmq, qdrant, minio)
cd deploy
copy .env.example .env
docker compose up -d

# 2. Apply DDL
docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "Clawbot!2026" -C -d clawbot \
  -i /var/opt/mssql/migrations/0001_init.sql

# 3. Backend API
cd ..
dotnet run --project src/api/Clawbot.Api --launch-profile http

# 4. Agent Service
dotnet run --project src/agents/Clawbot.AgentService

# 5. Frontend
cd src/frontend/clawbot-web
npm install
npm run dev
```

## 🤖 AI Agents & Skills

8 AI agent (gRPC service), điều phối qua **31 skill** chia 2 nhóm:

- **Phase 1 — 9 prompt/process knowledge skill**: KB tiếng Trung, 50 kịch bản chat, tư vấn Zalo, platform specs, ads/content/doc/lead/trend.
- **Phase 2 — 22 utility/library-backed skill**: intent & sentiment (PhoBERT), language detection, PII redaction (Presidio), lead dedup (Qdrant), forecast (ML.NET), PDF render (QuestPDF), prompt-injection defender, claude-cost-tracker…

Mỗi skill là một `SKILL.md` (+ tuỳ chọn C# adapter trong `Clawbot.Agents.Core/Skills/`). Agent đọc skill từ Skill Catalog thay vì hardcode prompt. Chi tiết: [.sdd/skills/_index.md](.sdd/skills/_index.md).

## 📐 Spec-Driven Development (SDD)

Mọi feature lớn đi qua pipeline 5 phase với traceability: **CONTEXT → SPEC (EARS notation) → AI review → PLAN → TASKS → Implement + Validate**. Toàn bộ artifact nằm dưới [.sdd/](.sdd/), ràng buộc bởi `constitution.md` (7 articles).

## 🔒 Bảo mật (trước khi production)

- Đổi `Jwt:SigningKey` (≥32 ký tự random) và `Encryption:Base64Key` (AES 32-byte) — lưu secret manager / env var, **không** commit.
- Đổi mật khẩu SA của SQL Server trong `.env`.
- Bật HTTPS/TLS 1.3, rate limit `/auth/*` và `/webhooks/*`, verify HMAC webhook.
- PII retention 30 ngày (purge job qua skill `pii-redaction`); mọi inbound qua `prompt-injection-defender`.

---

_Tài liệu chi tiết: [docs/arch.md](docs/arch.md) · ERD: [docs/erd-notion.md](docs/erd-notion.md) · Project plan: `docs/ClawBot_SaleMkt_ProjectPlan.docx`_
