
# Hocba Company — AI-Driven Marketing & Sales Automation System
Đây là một hệ thống AI-powered marketing automation platform được xây dựng theo kiến trúc microservices với multi-tenant, sử dụng .NET 8 và React. Hệ thống tích hợp nhiều AI agents để tự động hóa các tác vụ marketing như chat, tạo content, và chấm điểm leads,...
## Company
**Hocba** — công ty bán khóa học tiếng Trung online tại Việt Nam.
Quy mô hiện tại: đang ở giai đoạn validation & growth.
Thị trường: B2C, khách hàng người Việt học tiếng Trung.

---

## Project: ClawBot — AI Marketing & Sales Automation

### Mục tiêu tổng thể
Xây dựng hệ thống AI tự động hóa toàn bộ vòng đời khách hàng:
- **Marketing**: thu hút lead từ 3 kênh, qualify tự động
- **Content**. Tự động đăng bài trên các kênh theo đúng lịch trình được lên plan sẵn từ Agent cha
- **Sales**: tư vấn 24/7, follow-up, chốt deal, sync CRM

### 3 vấn đề cốt lõi cần giải quyết
1. Lead bị bỏ lỡ do reply chậm (30 phút–vài tiếng, thủ công 100%)
2. Sale bị ngốn sức vào FAQ lặp lại (học phí, lộ trình, khai giảng)
3. Không có follow-up → khách nguội → conversion thấp
4. Không có số liệu → không biết đang mất tiền ở đâu

---

## Kênh đầu vào
- **Zalo OA** — kênh chính, khách Việt dùng nhiều nhất
- **Facebook Messenger** — Page comment + DM
- **Telegram Bot** — kênh phụ + internal admin/alert

## CRM
- **Pancake** — source of truth duy nhất cho toàn bộ deal, contact, stage

---

## Tech Stack
-**Frontend**. React + vite
- **Backend**: ASP.NET Core, Semantic Kernel, RabbitMQ, Hangfire
- **AI Models**: DeepSeek-V3 (chat/full), DeepSeek-R1 (reasoning)
- **Vector DB**: Qdrant — 4 collections: product_kb, skill_kb, conversation_kb, competitor_kb
- **Cache**: Redis (3 instance độc lập: hot cache, SignalR backplane, queue buffer)
- **DB**: SQL Server Always On AG
- **Gateway**: YARP reverse proxy
- **Observability**: OpenTelemetry + Jaeger, Serilog, Sentry

## Agent System (Semantic Kernel orchestrated)
| Agent | Vai trò |
|---|---|
| Agent-Chat | Tư vấn 24/7, RAG từ Qdrant |
| Agent-Lead | Lead scoring, tạo deal Pancake |
| Agent-SaleAssist | Draft reply cho sale người thật (human-in-loop) |
| Agent-Content | Tự động đăng bài trên các kênh với nội dung dựa trên 5 platform sẵn
| Agent-Docs | Transcribe audio, capture skill |
| Agent-Report | KPI daily, Telegram alert |
| Agent-Research | Competitor tracking |

## Human-in-loop Rule
- Agent tự xử lý khi confidence > 0.85
- Escalate khi: lead value > 5tr / khách than phiền / discount / pháp lý
- Sale review qua SignalR Dashboard, timeout 60s → safe fallback

---

## Security Layer
- Webhook signature verify (HMAC-SHA256) từng platform
- JWT short-lived (15 phút) cho internal services
- Prompt injection guard (scan cả input lẫn output)
- PII masking trong logs
- API key trong Vault, auto-rotate 30 ngày
- Tenant isolation: namespace riêng Redis/Qdrant/SQL per OA

---

## Roadmap ưu tiên (theo thứ tự)
1. Thiết kế phễu sale + script từng stage
2. Tracking cơ bản (đo số thực trước khi build)
3. Bot FAQ 24/7
4. Auto follow-up theo lịch
5. Onboarding & upsell sequence

---

## 
- Thiết kế kỹ thuật: architecture, agent design, data schema, API contracts
- Chiến lược marketing & growth: funnel, content, kênh, campaign
- Quy trình sale: script, objection handling, conversion optimization
- Phân tích vấn đề: debug logic, đề xuất cải tiến, so sánh approach
- Viết code: ASP.NET Core, Semantic Kernel plugin, Qdrant integration, Pancake API
- Khi được hỏi về hệ thống, mặc định hiểu đây là context Hocba/ClawBot trừ khi nói khác

