const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  HeadingLevel, AlignmentType, BorderStyle, WidthType, ShadingType,
  PageBreak, Header, Footer, PageNumber, ExternalHyperlink
} = require('docx');
const fs = require('fs');

// ─── Helpers ─────────────────────────────────────────────────────────────────
const border = { style: BorderStyle.SINGLE, size: 1, color: 'BBBBBB' };
const borders = { top: border, bottom: border, left: border, right: border };

function h1(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_1,
    spacing: { before: 360, after: 160 },
    children: [new TextRun({ text, bold: true, size: 32, font: 'Calibri' })],
  });
}
function h2(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_2,
    spacing: { before: 280, after: 120 },
    children: [new TextRun({ text, bold: true, size: 26, font: 'Calibri', color: '1F4E79' })],
  });
}
function h3(text) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_3,
    spacing: { before: 200, after: 80 },
    children: [new TextRun({ text, bold: true, size: 24, font: 'Calibri', color: '2E75B6' })],
  });
}
function para(text, opts = {}) {
  return new Paragraph({
    spacing: { before: 60, after: 60 },
    children: [new TextRun({ text, font: 'Calibri', size: 22, ...opts })],
  });
}
function bullet(text, level = 0) {
  return new Paragraph({
    numbering: { reference: 'bullets', level },
    spacing: { before: 40, after: 40 },
    children: [new TextRun({ text, font: 'Calibri', size: 22 })],
  });
}
function code(text) {
  return new Paragraph({
    spacing: { before: 40, after: 40 },
    indent: { left: 720 },
    children: [new TextRun({ text, font: 'Courier New', size: 18, color: '444444' })],
  });
}
function pageBreak() {
  return new Paragraph({ children: [new PageBreak()] });
}

// ─── Table helpers ────────────────────────────────────────────────────────────
function headerRow(cells) {
  return new TableRow({
    tableHeader: true,
    children: cells.map(text =>
      new TableCell({
        borders,
        shading: { fill: '1F4E79', type: ShadingType.CLEAR },
        margins: { top: 80, bottom: 80, left: 120, right: 120 },
        children: [new Paragraph({
          children: [new TextRun({ text, bold: true, color: 'FFFFFF', font: 'Calibri', size: 20 })],
        })],
      })
    ),
  });
}
function dataRow(cells, shade = false) {
  return new TableRow({
    children: cells.map(text =>
      new TableCell({
        borders,
        shading: shade ? { fill: 'EBF3FB', type: ShadingType.CLEAR } : undefined,
        margins: { top: 60, bottom: 60, left: 120, right: 120 },
        children: [new Paragraph({
          children: [new TextRun({ text: text || '', font: 'Calibri', size: 20 })],
        })],
      })
    ),
  });
}
function makeTable(headers, rows, widths) {
  return new Table({
    width: { size: 9360, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      headerRow(headers),
      ...rows.map((r, i) => dataRow(r, i % 2 === 0)),
    ],
  });
}

// ─── Content ─────────────────────────────────────────────────────────────────
const children = [];

// Cover
children.push(
  new Paragraph({ spacing: { before: 2000, after: 200 }, alignment: AlignmentType.CENTER, children: [new TextRun({ text: 'CLAWBOT', bold: true, size: 72, font: 'Calibri', color: '1F4E79' })] }),
  new Paragraph({ spacing: { before: 0, after: 100 }, alignment: AlignmentType.CENTER, children: [new TextRun({ text: 'Tài liệu Kiến trúc & Triển khai', size: 36, font: 'Calibri', color: '2E75B6' })] }),
  new Paragraph({ spacing: { before: 0, after: 60 }, alignment: AlignmentType.CENTER, children: [new TextRun({ text: 'Nền tảng Chatbot đa kênh Multi-Agent', size: 26, font: 'Calibri', italics: true, color: '666666' })] }),
  new Paragraph({ spacing: { before: 400, after: 60 }, alignment: AlignmentType.CENTER, children: [new TextRun({ text: 'Phiên bản 1.0  |  Tháng 5/2026', size: 22, font: 'Calibri', color: '888888' })] }),
  pageBreak(),
);

// ─── 1. Tổng quan ────────────────────────────────────────────────────────────
children.push(h1('1. Tổng quan Hệ thống'));
children.push(para('ClawBot là nền tảng chatbot đa kênh (Zalo, Facebook, Instagram, TikTok, WhatsApp) dựa trên kiến trúc multi-agent. Hệ thống cho phép doanh nghiệp triển khai các AI agent thông minh để tự động hóa cuộc hội thoại với khách hàng, quản lý lead, và tạo nội dung.'));

children.push(h2('1.1 Mục tiêu thiết kế'));
['Khả năng mở rộng (Scale-out): Mỗi lớp có thể scale độc lập — API server, Agent service, Database.',
 'Dễ bảo trì: Kiến trúc Clean Architecture giúp sinh viên join dự án nhanh, module rõ ràng.',
 'Thay thế linh hoạt: AI Agent layer là gRPC microservice riêng — có thể viết lại bằng Python/TypeScript mà không ảnh hưởng phần còn lại.',
 'Multi-tenant: Một deployment phục vụ nhiều doanh nghiệp, dữ liệu cách ly hoàn toàn qua EF Core global query filter.',
 'Resilience: Polly retry + circuit breaker + timeout trên mọi HTTP call ra ngoài.',
].forEach(t => children.push(bullet(t)));

children.push(h2('1.2 Công nghệ sử dụng'));
children.push(makeTable(
  ['Lớp', 'Công nghệ'],
  [
    ['Backend API', '.NET 8, ASP.NET Core, EF Core 8, MediatR, FluentValidation, Serilog, SignalR, Polly'],
    ['AI Agent Service', '.NET 8 gRPC, Microsoft.SemanticKernel, Grpc.AspNetCore'],
    ['Message Queue', 'RabbitMQ (MassTransit)'],
    ['Cache', 'Redis (StackExchange.Redis)'],
    ['Database', 'PostgreSQL 16 + pgvector extension'],
    ['Vector Store (alt)', 'Qdrant'],
    ['Object Storage', 'MinIO (S3-compatible)'],
    ['Auth', 'ASP.NET Core Identity + JWT Bearer'],
    ['Frontend', 'React 18, Vite, TypeScript, TanStack Query, React Router 6, Tailwind CSS v4'],
    ['Tests', 'xUnit, FluentAssertions, NSubstitute, Testcontainers'],
    ['Dev Infra', 'Docker Compose'],
  ],
  [2800, 6560]
));

children.push(pageBreak());

// ─── 2. Cấu trúc source code ─────────────────────────────────────────────────
children.push(h1('2. Cấu trúc Source Code'));
children.push(para('Toàn bộ solution nằm trong thư mục d:\\Clawbot\\. Cấu trúc tuân theo nguyên tắc Clean Architecture kết hợp với cách tổ chức module theo feature (Vertical Slice).'));

children.push(h2('2.1 Sơ đồ thư mục'));
[
  'd:\\Clawbot\\',
  '├── Clawbot.sln                    # Solution file — 11 projects',
  '├── global.json                    # Ghim .NET SDK 8.x',
  '├── Directory.Build.props          # MSBuild props chung (Nullable, LangVersion)',
  '├── Directory.Packages.props       # Quản lý phiên bản NuGet tập trung',
  '├── .editorconfig                  # Quy tắc code style',
  '├── proto/                         # gRPC contract (.proto files)',
  '├── src/',
  '│   ├── shared/',
  '│   │   ├── Clawbot.Domain/        # Entities, Value Objects, Domain Events',
  '│   │   ├── Clawbot.Application/   # Use cases, MediatR handlers, validators',
  '│   │   ├── Clawbot.SharedKernel/  # Abstraction interfaces',
  '│   │   └── Clawbot.Infrastructure/# EF Core, Identity, Redis, RabbitMQ...',
  '│   ├── api/',
  '│   │   ├── Clawbot.Api/           # ASP.NET Core + SignalR + Webhooks',
  '│   │   └── Clawbot.Api.Contracts/ # Public DTOs',
  '│   ├── agents/',
  '│   │   ├── Clawbot.Agents.Contracts/ # Proto-generated gRPC stubs',
  '│   │   ├── Clawbot.Agents.Core/      # Orchestrator + IAgent abstraction',
  '│   │   └── Clawbot.AgentService/     # gRPC host process',
  '│   └── frontend/',
  '│       └── clawbot-web/           # React + Vite + TypeScript SPA',
  '├── tests/                         # Test projects',
  '└── deploy/',
  '    ├── docker-compose.yml',
  '    └── .env.example',
].forEach(line => code(line));

children.push(h2('2.2 Chi tiết từng project'));

children.push(h3('Clawbot.Domain — Tầng Domain'));
children.push(para('Không phụ thuộc vào bất kỳ thư viện nào ngoài .NET BCL. Đây là trái tim của hệ thống.'));
children.push(makeTable(
  ['File / Thư mục', 'Mô tả'],
  [
    ['Common/Entity<TId>', 'Base class cho mọi entity. Override Equals/GetHashCode theo Id — tránh so sánh theo reference.'],
    ['Common/AggregateRoot<TId>', 'Kế thừa Entity, bổ sung danh sách DomainEvents. Mọi aggregate raise domain event khi có thay đổi trạng thái quan trọng.'],
    ['Common/IDomainEvent', 'Interface đánh dấu domain event, có thuộc tính OccurredOn.'],
    ['Common/ITenantOwned', 'Interface đánh dấu entity thuộc về một tenant. EF Core dùng để áp global query filter.'],
    ['Conversations/Conversation', 'Aggregate đầu tiên — đại diện một cuộc hội thoại đa kênh. Có factory method Open().'],
  ],
  [3000, 6360]
));

children.push(h3('Clawbot.SharedKernel — Abstractions'));
children.push(para('Chứa các interface dùng chung giữa Application và Infrastructure. Không có implementation.'));
children.push(makeTable(
  ['Interface', 'Mục đích'],
  [
    ['ITenantAccessor', 'Lấy TenantContext (TenantId, TenantSlug) từ HTTP request hiện tại.'],
    ['IClock', 'Bọc DateTimeOffset.UtcNow để dễ mock trong unit test.'],
    ['IVectorStore', 'Abstraction cho vector database: Upsert, Search (cosine), Delete.'],
    ['IChannelAdapter', 'Interface cho từng kênh (Pancake/Zalo/FB): xác thực webhook, parse message, gửi reply.'],
    ['IEncryptor', 'Mã hóa/giải mã symmetric (AES-CBC). Dùng cho API key của Pancake lưu ở DB.'],
  ],
  [3000, 6360]
));

children.push(h3('Clawbot.Application — Use Cases'));
children.push(para('Chứa toàn bộ business logic ở dạng MediatR commands/queries. Không phụ thuộc Infrastructure.'));
children.push(makeTable(
  ['Thành phần', 'Mô tả'],
  [
    ['Common/Result<T>', 'Result pattern: Success(value) hoặc Failure(error). Tránh throw exception trong business logic.'],
    ['Common/Behaviors/ValidationBehavior', 'MediatR pipeline behavior — tự động validate mọi command bằng FluentValidation.'],
    ['Common/Behaviors/LoggingBehavior', 'Pipeline behavior — log tên command trước/sau xử lý. Dùng LoggerMessage.Define cho zero-allocation logging.'],
    ['Abstractions/IAppDbContext', 'Interface lên EF Core DbContext, dùng IConversationSet thay DbSet để dễ mock trong unit test.'],
    ['Modules/Conversations/Commands/OpenConversation', 'Command + Handler mẫu: nhận channel + threadId, tạo Conversation, lưu DB, trả về Guid.'],
    ['DependencyInjection', 'Extension method AddApplication() — đăng ký MediatR, FluentValidation, behaviors.'],
  ],
  [3500, 5860]
));

children.push(h3('Clawbot.Infrastructure — Triển khai kỹ thuật'));
children.push(para('Triển khai các interface từ Application và SharedKernel. Đây là nơi duy nhất chứa code liên quan đến DB, cache, message queue.'));
children.push(makeTable(
  ['Thành phần', 'Mô tả'],
  [
    ['Persistence/AppDbContext', 'IdentityDbContext<AppUser, AppRole>. Áp tenant filter qua reflection cho mọi ITenantOwned entity.'],
    ['Identity/AppUser, AppRole', 'Mở rộng IdentityUser<Guid>/IdentityRole<Guid> với thuộc tính TenantId.'],
    ['Multitenancy/HttpTenantAccessor', 'Đọc claim tenant_id và tenant_slug từ JWT trong HttpContext.'],
    ['Security/AesEncryptor', 'AES-CBC, IV ngẫu nhiên 16 bytes được prepend vào ciphertext.'],
    ['Resilience/HttpResiliencePolicies', 'Polly: retry 3 lần (exponential backoff), circuit breaker 5 lỗi/30s, timeout 10s.'],
    ['Channels/Pancake/PancakeChannelAdapter', 'Stub — sinh viên sẽ implement xác thực HMAC, parse payload Pancake, gửi reply.'],
    ['Vectors/PgVectorStore, QdrantVectorStore', 'Stub — sinh viên implement cosine search. Chuyển đổi qua config "Vector:Backend".'],
    ['DependencyInjection', 'AddInfrastructure(cfg) — wires toàn bộ infrastructure services.'],
  ],
  [3500, 5860]
));

children.push(h3('Clawbot.Api — REST API Host'));
children.push(makeTable(
  ['Thành phần', 'Mô tả'],
  [
    ['Program.cs', 'Minimal API setup: Serilog, JWT Bearer auth, CORS (localhost:5173), Swagger, SignalR.'],
    ['Auth/JwtTokenIssuer', 'Phát JWT token với claims: sub, tenant_id, tenant_slug, roles. Dùng HS256.'],
    ['Endpoints/HealthEndpoints', 'GET /health/live và /health/ready — dùng cho container healthcheck.'],
    ['Endpoints/AuthEndpoints', 'POST /auth/login — kiểm tra password, issue JWT.'],
    ['Endpoints/WebhookEndpoints', 'POST /webhooks/pancake — verify signature, enqueue qua MassTransit.'],
    ['Hubs/DashboardHub', 'SignalR Hub [Authorize] — stream real-time updates về orchestrator plan, conversation events.'],
  ],
  [3200, 6160]
));

children.push(h3('Clawbot.AgentService — gRPC AI Microservice'));
children.push(para('Process riêng, lắng nghe trên port 5050 (Http2). Có thể deploy độc lập và sau này viết lại bằng Python mà không ảnh hưởng Clawbot.Api.'));
children.push(makeTable(
  ['Service', 'Proto', 'Mô tả'],
  [
    ['OrchestratorGrpcService', 'orchestrator.proto', 'Plan: nhận goal → trả danh sách tasks. Trace: stream trạng thái thực thi.'],
    ['ChatAgentGrpcService', 'agent_chat.proto', 'Reply: nhận conversation history → stream token phản hồi (streaming RPC).'],
    ['ContentAgentGrpcService', 'agent_content.proto', 'Generate: nhận brief → trả nội dung (title + body).'],
    ['LeadAgentGrpcService', 'agent_lead.proto', 'Score: nhận features của lead → trả điểm 0-100 + lý do.'],
  ],
  [3000, 2500, 3860]
));

children.push(h3('Frontend — clawbot-web'));
children.push(makeTable(
  ['Thư mục/File', 'Mô tả'],
  [
    ['src/app/routes.tsx', 'React Router 6 routes với RequireAuth guard. Redirect /login nếu chưa đăng nhập.'],
    ['src/app/providers.tsx', 'Wrap app với QueryClientProvider (TanStack Query) + AuthProvider.'],
    ['src/shared/api/client.ts', 'Axios instance với interceptor tự gắn JWT từ localStorage.'],
    ['src/shared/auth/AuthContext.tsx', 'Context quản lý token: lưu/xóa localStorage, expose useAuth() hook.'],
    ['src/features/auth/LoginPage.tsx', 'Form đăng nhập — POST /auth/login, lưu token, redirect /.'],
    ['src/features/dashboard/', 'Dashboard chính — sinh viên implement KPIs, orchestrator plan board.'],
    ['src/features/conversations/', 'Inbox đa kênh — sinh viên implement danh sách + chi tiết conversation.'],
  ],
  [3200, 6160]
));

children.push(pageBreak());

// ─── 3. Kiến trúc & Lý do ────────────────────────────────────────────────────
children.push(h1('3. Kiến trúc & Lý do Thiết kế'));

children.push(h2('3.1 Clean Architecture'));
children.push(para('Hệ thống chia làm 4 lớp, phụ thuộc hướng vào trong (Dependency Rule):'));
['Domain: Không phụ thuộc gì. Chứa entity, aggregate, domain event.',
 'Application: Phụ thuộc Domain + SharedKernel. Chứa use case (MediatR handler), không biết DB hay HTTP.',
 'Infrastructure: Phụ thuộc Application. Triển khai các port (interface) bằng EF Core, Redis, MassTransit...',
 'API: Phụ thuộc Application + Infrastructure. Nhận HTTP request, gọi MediatR, trả response.',
].forEach((t, i) => children.push(bullet(t)));

children.push(para('Lý do chọn:'));
children.push(bullet('Sinh viên có thể thêm module mới mà không sợ breaking change ở các module khác.'));
children.push(bullet('Unit test handler không cần khởi tạo DB thật — chỉ mock IAppDbContext.'));
children.push(bullet('Sau này có thể thay PostgreSQL bằng MySQL hoặc MongoDB mà không sửa Application/Domain.'));

children.push(h2('3.2 gRPC Microservice cho AI Agents'));
children.push(para('Lý do tách Agent layer ra process riêng:'));
children.push(makeTable(
  ['Vấn đề', 'Giải pháp'],
  [
    ['Semantic Kernel/LLM call tốn nhiều RAM, blocking', 'Agent service chạy process riêng, API server không bị ảnh hưởng.'],
    ['Muốn thử Python/TypeScript cho AI sau này', 'Chỉ cần implement .proto interface là xong, không đụng code C#.'],
    ['Agent cần scale riêng (nhiều GPU/LLM call hơn)', 'Deploy nhiều instance AgentService, API load-balance qua gRPC channel.'],
    ['Streaming token từ LLM ra client', 'Server-streaming gRPC tự nhiên hỗ trợ — không cần SSE hay long-polling.'],
  ],
  [3500, 5860]
));

children.push(h2('3.3 Multi-tenant'));
children.push(para('Chiến lược: Shared Database — một PostgreSQL, nhiều tenant, cách ly bằng TenantId column.'));
children.push(bullet('JWT token mang claim tenant_id (Guid) và tenant_slug.'));
children.push(bullet('HttpTenantAccessor đọc claim, trả TenantContext cho mọi request.'));
children.push(bullet('AppDbContext áp global query filter: e.TenantId == tenants.Current.TenantId cho mọi entity implement ITenantOwned.'));
children.push(bullet('Sinh viên thêm aggregate mới chỉ cần implement ITenantOwned — filter tự động áp.'));

children.push(h2('3.4 MediatR Pipeline'));
children.push(para('Mọi command/query đi qua pipeline: LoggingBehavior → ValidationBehavior → Handler.'));
children.push(para('Lợi ích: Thêm cross-cutting concern (tracing, caching, authorization) mà không sửa handler.'));

children.push(h2('3.5 Result Pattern'));
children.push(para('Handler trả Result<T> thay vì throw exception. Endpoint map Result → HTTP status code:'));
children.push(bullet('Result.Success(value) → 200 OK với payload.'));
children.push(bullet('Result.Failure(Error.NotFound(...)) → 404 Not Found.'));
children.push(bullet('Result.Failure(Error.Validation(...)) → 400 Bad Request.'));
children.push(para('Lợi ích: Flow control rõ ràng, không bị exception làm gián đoạn, dễ test.'));

children.push(h2('3.6 Vector Store Abstraction'));
children.push(para('IVectorStore có 2 implementation: PgVectorStore (PostgreSQL + pgvector) và QdrantVectorStore (dedicated vector DB).'));
children.push(para('Chuyển đổi bằng config "Vector:Backend": "qdrant" hoặc "pgvector" — không sửa code.'));
children.push(para('Phù hợp cho RAG (Retrieval-Augmented Generation) trong Knowledge module.'));

children.push(pageBreak());

// ─── 4. Hướng dẫn triển khai ─────────────────────────────────────────────────
children.push(h1('4. Hướng dẫn Triển khai'));

children.push(h2('4.1 Yêu cầu môi trường'));
children.push(makeTable(
  ['Công cụ', 'Phiên bản', 'Mục đích'],
  [
    ['.NET SDK', '8.0.x', 'Build và chạy backend'],
    ['Node.js', '20.x trở lên', 'Build frontend'],
    ['Docker Desktop', '4.x trở lên', 'Chạy PostgreSQL, Redis, RabbitMQ, Qdrant, MinIO'],
    ['Git', 'Bất kỳ', 'Clone source'],
  ],
  [2500, 2000, 4860]
));

children.push(h2('4.2 Chạy môi trường phát triển (Local)'));
children.push(h3('Bước 1: Khởi động infrastructure'));
[
  'cd d:\\Clawbot\\deploy',
  'copy .env.example .env',
  'docker compose up -d',
].forEach(l => code(l));
children.push(para('Lệnh này khởi động 5 container: postgres (port 5432), redis (6379), rabbitmq (5672 + management 15672), qdrant (6333), minio (9000 + console 9001).'));

children.push(h3('Bước 2: Chạy Backend API'));
[
  'cd d:\\Clawbot',
  'dotnet run --project src/api/Clawbot.Api --launch-profile http',
].forEach(l => code(l));
children.push(para('API lắng nghe tại http://localhost:5000. Swagger UI: http://localhost:5000/swagger'));

children.push(h3('Bước 3: Chạy Agent Service (terminal mới)'));
[
  'cd d:\\Clawbot',
  'dotnet run --project src/agents/Clawbot.AgentService',
].forEach(l => code(l));
children.push(para('gRPC server lắng nghe tại http://0.0.0.0:5050 (Http2).'));

children.push(h3('Bước 4: Chạy Frontend (terminal mới)'));
[
  'cd d:\\Clawbot\\src\\frontend\\clawbot-web',
  'npm install',
  'npm run dev',
].forEach(l => code(l));
children.push(para('Vite dev server tại http://localhost:5173. Vite proxy /api và /hubs về localhost:5000.'));

children.push(h2('4.3 Kiểm tra nhanh'));
children.push(makeTable(
  ['Endpoint / URL', 'Kết quả mong đợi'],
  [
    ['curl http://localhost:5000/health/live', '{"status":"live"}'],
    ['http://localhost:5000/swagger', 'Swagger UI hiển thị các endpoint'],
    ['http://localhost:5173', 'React app redirect về /login'],
    ['http://localhost:15672', 'RabbitMQ Management UI (guest/guest)'],
    ['http://localhost:9001', 'MinIO Console (minio/minio12345)'],
  ],
  [4500, 4860]
));

children.push(h2('4.4 Chạy Tests'));
code('dotnet test d:\\Clawbot\\Clawbot.sln');
children.push(para('Test mặc định chạy unit tests. Integration tests (Testcontainers) yêu cầu Docker đang chạy và có tag [Trait("Category","integration")].'));

children.push(h2('4.5 Cấu hình quan trọng'));
children.push(para('File cấu hình: src/api/Clawbot.Api/appsettings.json'));
children.push(makeTable(
  ['Key', 'Mô tả', 'Giá trị mặc định'],
  [
    ['ConnectionStrings:Postgres', 'PostgreSQL connection string', 'Host=localhost;Port=5432;Database=clawbot;...'],
    ['ConnectionStrings:Redis', 'Redis endpoint', 'localhost:6379'],
    ['ConnectionStrings:RabbitMq', 'AMQP URL', 'amqp://guest:guest@localhost:5672'],
    ['Jwt:SigningKey', 'Khóa ký JWT — phải đổi trong production!', '(chuỗi ≥32 ký tự)'],
    ['Encryption:Base64Key', 'AES key 32 bytes base64 — phải đổi trong production!', '(base64 của 32 bytes)'],
    ['Vector:Backend', 'Chọn vector store', '"pgvector" hoặc "qdrant"'],
  ],
  [3000, 3500, 2860]
));

children.push(pageBreak());

// ─── 5. Hướng dẫn thêm module mới ───────────────────────────────────────────
children.push(h1('5. Hướng dẫn Thêm Module Mới'));
children.push(para('Ví dụ: Thêm module "Leads" để quản lý danh sách khách hàng tiềm năng.'));

children.push(makeTable(
  ['Bước', 'File', 'Việc cần làm'],
  [
    ['1. Domain', 'src/shared/Clawbot.Domain/Leads/Lead.cs', 'Tạo aggregate Lead : AggregateRoot<Guid>, ITenantOwned'],
    ['2. EF Config', 'src/shared/Clawbot.Infrastructure/Persistence/Configurations/LeadConfiguration.cs', 'Implement IEntityTypeConfiguration<Lead> — định nghĩa table, index'],
    ['3. DbContext', 'IAppDbContext.cs + AppDbContext.cs', 'Thêm DbSet<Lead> / IConversationSet<Lead>'],
    ['4. Command', 'src/shared/Clawbot.Application/Modules/Leads/Commands/CreateLead/', 'Tạo CreateLeadCommand + Handler + Validator'],
    ['5. Endpoint', 'src/api/Clawbot.Api/Endpoints/LeadEndpoints.cs', 'MapPost("/leads", ...) — gọi MediatR, trả Result'],
    ['6. DTO', 'src/api/Clawbot.Api.Contracts/Leads/CreateLeadRequest.cs', 'Request/Response DTO'],
    ['7. Tests', 'tests/Clawbot.Application.Tests/Modules/Leads/', 'Unit test handler với NSubstitute'],
    ['8. Frontend', 'src/frontend/clawbot-web/src/features/leads/', 'LeadsPage.tsx + API calls + routes.tsx'],
  ],
  [800, 4000, 4560]
));

children.push(pageBreak());

// ─── 6. ERD ──────────────────────────────────────────────────────────────────
children.push(h1('6. ERD — Sơ đồ Quan hệ Cơ sở dữ liệu'));
children.push(para('ClawBot sử dụng PostgreSQL 16 với extension pgvector. Dưới đây là toàn bộ bảng hiện tại (skeleton) và bảng dự kiến khi hoàn thiện các module.'));

children.push(h2('6.1 Sơ đồ quan hệ (mô tả văn bản)'));
children.push(para('Do giới hạn của định dạng tài liệu, quan hệ được mô tả như sau:'));
children.push(bullet('AspNetUsers (AppUser) ←→ AspNetRoles qua AspNetUserRoles (many-to-many, Identity tự tạo)'));
children.push(bullet('AppUser.TenantId → tenants.Id (nhiều user thuộc một tenant)'));
children.push(bullet('conversations.tenant_id → tenants.Id'));
children.push(bullet('conversations.created_by → AspNetUsers.Id (tùy chọn)'));
children.push(bullet('messages.conversation_id → conversations.Id'));
children.push(bullet('leads.tenant_id → tenants.Id'));
children.push(bullet('knowledge_items.tenant_id → tenants.Id'));
children.push(bullet('llm_configs.tenant_id → tenants.Id'));
children.push(bullet('pancake_configs.tenant_id → tenants.Id'));

children.push(h2('6.2 Bảng: tenants'));
children.push(para('Lưu thông tin các tenant (doanh nghiệp sử dụng ClawBot).'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK, NOT NULL', 'Định danh duy nhất của tenant'],
    ['slug', 'varchar(64)', 'UNIQUE, NOT NULL', 'Tên ngắn dùng trong URL, claim JWT (vd: "acme-corp")'],
    ['display_name', 'varchar(256)', 'NOT NULL', 'Tên hiển thị đầy đủ của doanh nghiệp'],
    ['plan', 'varchar(32)', 'NOT NULL, DEFAULT "free"', 'Gói dịch vụ: free | starter | pro | enterprise'],
    ['is_active', 'boolean', 'NOT NULL, DEFAULT true', 'Tenant đang hoạt động hay đã bị suspend'],
    ['created_at', 'timestamptz', 'NOT NULL', 'Thời điểm tạo tenant'],
    ['settings_json', 'jsonb', 'NULL', 'Cài đặt tuỳ chỉnh (màu sắc widget, timezone, v.v.)'],
  ],
  [2000, 1600, 2500, 3260]
));

children.push(h2('6.3 Bảng: AspNetUsers (AppUser)'));
children.push(para('Kế thừa từ ASP.NET Core Identity. Các cột Identity chuẩn được bổ sung tenant_id và display_name.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['Id', 'uuid', 'PK', 'Identity mặc định'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant mà user này thuộc về'],
    ['display_name', 'varchar(256)', 'NOT NULL, DEFAULT ""', 'Tên hiển thị trong dashboard'],
    ['UserName', 'varchar(256)', 'UNIQUE (Identity)', 'Username đăng nhập'],
    ['Email', 'varchar(256)', 'UNIQUE (Identity)', 'Email đăng nhập'],
    ['PasswordHash', 'text', 'Identity', 'Bcrypt hash của password'],
    ['LockoutEnd', 'timestamptz', 'Identity, NULL', 'Thời điểm hết lockout sau nhiều lần đăng nhập sai'],
    ['AccessFailedCount', 'int', 'Identity', 'Số lần đăng nhập sai liên tiếp'],
  ],
  [2200, 1400, 2400, 3360]
));

children.push(h2('6.4 Bảng: conversations'));
children.push(para('Aggregate chính của module Conversations — đại diện một luồng hội thoại với khách hàng.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK, NOT NULL', 'Định danh conversation (Guid.NewGuid() trong domain)'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant sở hữu conversation này (global filter)'],
    ['channel', 'varchar(64)', 'NOT NULL', 'Kênh nguồn: facebook | zalo | instagram | tiktok | whatsapp'],
    ['external_thread_id', 'varchar(256)', 'NOT NULL', 'ID của thread/conversation phía Pancake'],
    ['created_at', 'timestamptz', 'NOT NULL', 'Thời điểm conversation được mở'],
    ['status', 'varchar(32)', 'NOT NULL, DEFAULT "open"', 'Trạng thái: open | resolved | escalated | spam'],
    ['assigned_to', 'uuid', 'FK → AspNetUsers.Id, NULL', 'Agent con người được assign (NULL = chưa assign)'],
  ],
  [2200, 1400, 2200, 3560]
));
children.push(para('Index duy nhất: (tenant_id, channel, external_thread_id) — đảm bảo không tạo trùng conversation cho cùng thread.'));

children.push(h2('6.5 Bảng: messages (dự kiến)'));
children.push(para('Lưu từng tin nhắn trong một conversation.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK', 'Định danh message'],
    ['conversation_id', 'uuid', 'FK → conversations.id, NOT NULL', 'Conversation chứa message này'],
    ['tenant_id', 'uuid', 'NOT NULL', 'Copy từ conversation cho global filter'],
    ['direction', 'varchar(16)', 'NOT NULL', 'inbound (từ khách) hoặc outbound (từ bot/agent)'],
    ['sender_type', 'varchar(16)', 'NOT NULL', 'customer | bot | human_agent'],
    ['content', 'text', 'NOT NULL', 'Nội dung tin nhắn (text hoặc JSON cho rich content)'],
    ['content_type', 'varchar(32)', 'NOT NULL, DEFAULT "text"', 'text | image | file | template'],
    ['sent_at', 'timestamptz', 'NOT NULL', 'Thời điểm tin nhắn được gửi'],
    ['metadata_json', 'jsonb', 'NULL', 'Dữ liệu bổ sung: attachment URL, reaction, v.v.'],
  ],
  [1800, 2500, 1400, 3660]
));

children.push(h2('6.6 Bảng: leads (dự kiến)'));
children.push(para('Lưu thông tin khách hàng tiềm năng được AI Lead Agent phân loại.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK', 'Định danh lead'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant sở hữu lead'],
    ['conversation_id', 'uuid', 'FK → conversations.id, NULL', 'Conversation nguồn (nếu có)'],
    ['external_user_id', 'varchar(256)', 'NOT NULL', 'ID khách hàng phía Pancake'],
    ['name', 'varchar(256)', 'NULL', 'Tên khách hàng (trích xuất từ hội thoại)'],
    ['phone', 'varchar(32)', 'NULL', 'Số điện thoại'],
    ['email', 'varchar(256)', 'NULL', 'Email'],
    ['score', 'int', 'NULL', 'Điểm lead 0-100 từ AI Lead Agent'],
    ['stage', 'varchar(32)', 'NOT NULL, DEFAULT "new"', 'Giai đoạn: new | qualified | contacted | converted | lost'],
    ['created_at', 'timestamptz', 'NOT NULL', 'Thời điểm tạo lead'],
  ],
  [2000, 1400, 1800, 4160]
));

children.push(h2('6.7 Bảng: knowledge_items (dự kiến)'));
children.push(para('Lưu tài liệu/FAQ dùng cho RAG (Retrieval-Augmented Generation) trong Chat Agent.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK', 'Định danh item'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant sở hữu'],
    ['title', 'varchar(512)', 'NOT NULL', 'Tiêu đề tài liệu'],
    ['content', 'text', 'NOT NULL', 'Nội dung văn bản đầy đủ'],
    ['embedding', 'vector(1536)', 'NULL', 'Vector embedding (pgvector) của content — dùng cho cosine search'],
    ['source_url', 'varchar(1024)', 'NULL', 'URL nguồn tài liệu gốc'],
    ['created_at', 'timestamptz', 'NOT NULL', 'Thời điểm import'],
    ['updated_at', 'timestamptz', 'NOT NULL', 'Lần cập nhật cuối'],
  ],
  [2000, 1400, 2000, 4000]
));

children.push(h2('6.8 Bảng: llm_configs (dự kiến)'));
children.push(para('Cấu hình LLM (Large Language Model) cho từng tenant. Mỗi tenant có thể dùng DeepSeek hoặc OpenAI riêng.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK', 'Định danh config'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant sở hữu'],
    ['provider', 'varchar(64)', 'NOT NULL', 'deepseek | openai | azure_openai | anthropic'],
    ['model_id', 'varchar(128)', 'NOT NULL', 'Tên model: deepseek-chat, gpt-4o, claude-sonnet-4-6...'],
    ['api_key_encrypted', 'text', 'NOT NULL', 'API key đã mã hóa AES (dùng AesEncryptor)'],
    ['base_url', 'varchar(512)', 'NULL', 'Custom base URL (cho Azure OpenAI hoặc self-hosted)'],
    ['is_active', 'boolean', 'NOT NULL, DEFAULT true', 'Config đang được dùng hay đã disable'],
    ['max_tokens', 'int', 'NOT NULL, DEFAULT 2048', 'Giới hạn token mỗi lần gọi'],
    ['temperature', 'float', 'NOT NULL, DEFAULT 0.7', 'Độ ngẫu nhiên của phản hồi (0.0 - 2.0)'],
  ],
  [2000, 1600, 1800, 3960]
));

children.push(h2('6.9 Bảng: pancake_configs (dự kiến)'));
children.push(para('Lưu thông tin tích hợp Pancake (webhook URL, access token) cho từng tenant và kênh.'));
children.push(makeTable(
  ['Cột', 'Kiểu', 'Constraint', 'Mô tả'],
  [
    ['id', 'uuid', 'PK', 'Định danh config'],
    ['tenant_id', 'uuid', 'FK → tenants.id, NOT NULL', 'Tenant sở hữu'],
    ['channel', 'varchar(64)', 'NOT NULL', 'facebook | zalo | instagram | tiktok | whatsapp'],
    ['page_id', 'varchar(128)', 'NOT NULL', 'Page ID phía Pancake/Facebook'],
    ['access_token_encrypted', 'text', 'NOT NULL', 'Access token đã mã hóa AES'],
    ['webhook_secret_encrypted', 'text', 'NOT NULL', 'HMAC secret để verify webhook signature'],
    ['is_active', 'boolean', 'NOT NULL, DEFAULT true', 'Kênh đang hoạt động'],
  ],
  [2000, 1600, 2000, 3760]
));

children.push(pageBreak());

// ─── 7. Lưu ý bảo mật ───────────────────────────────────────────────────────
children.push(h1('7. Lưu ý Bảo mật'));
children.push(para('Trước khi deploy production, bắt buộc thực hiện các bước sau:'));
children.push(makeTable(
  ['Hạng mục', 'Việc cần làm'],
  [
    ['JWT Signing Key', 'Đổi Jwt:SigningKey thành chuỗi ngẫu nhiên ≥32 ký tự, lưu trong secret manager hoặc env var.'],
    ['AES Encryption Key', 'Đổi Encryption:Base64Key thành key 32 bytes ngẫu nhiên thực sự. Không dùng key placeholder.'],
    ['Database Password', 'Đổi mật khẩu PostgreSQL trong deploy/.env và connection string.'],
    ['RabbitMQ Password', 'Đổi mật khẩu RABBITMQ_PASSWORD trong .env.'],
    ['MinIO Password', 'Đổi MINIO_PASSWORD thành chuỗi ≥8 ký tự.'],
    ['Pancake HMAC Verify', 'Implement VerifyWebhookSignatureAsync trong PancakeChannelAdapter trước khi expose webhook endpoint.'],
    ['HTTPS', 'Bật HTTPS cho Clawbot.Api bằng cách cấu hình Kestrel với SSL cert hoặc đặt sau reverse proxy (nginx/Caddy).'],
    ['Rate Limiting', 'Thêm rate limiting cho /auth/login và /webhooks/* để tránh brute force và abuse.'],
  ],
  [2800, 6560]
));

children.push(pageBreak());

// ─── 8. Roadmap ──────────────────────────────────────────────────────────────
children.push(h1('8. Roadmap Phát triển'));
children.push(makeTable(
  ['Phase', 'Module / Feature', 'Mô tả'],
  [
    ['Phase 1', 'Conversations (inbox)', 'Nhận webhook từ Pancake, lưu message, hiển thị real-time qua SignalR.'],
    ['Phase 1', 'Chat Agent', 'Implement ChatAgentGrpcService với Semantic Kernel + RAG từ knowledge_items.'],
    ['Phase 2', 'Lead Management', 'Lead capture tự động từ hội thoại, Lead Agent scoring, CRM view.'],
    ['Phase 2', 'Knowledge Base', 'Upload tài liệu, chunk + embed qua LLM, lưu pgvector cho RAG.'],
    ['Phase 3', 'Content Agent', 'Sinh nội dung marketing, post lên kênh qua Pancake API.'],
    ['Phase 3', 'Reporting', 'Dashboard KPIs, biểu đồ hội thoại, lead conversion rate.'],
    ['Phase 4', 'Multi-LLM', 'Cấu hình LLM per tenant, A/B test model, fallback khi LLM lỗi.'],
    ['Phase 4', 'Monitoring', 'Metrics (OpenTelemetry), alerting, agent trace viewer real-time.'],
  ],
  [1200, 2800, 5360]
));

// ─── Build document ───────────────────────────────────────────────────────────
const doc = new Document({
  numbering: {
    config: [{
      reference: 'bullets',
      levels: [{
        level: 0,
        format: 'bullet',
        text: '•',
        alignment: AlignmentType.LEFT,
        style: { paragraph: { indent: { left: 720, hanging: 360 } } },
      }],
    }],
  },
  styles: {
    default: {
      document: { run: { font: 'Calibri', size: 22 } },
    },
    paragraphStyles: [
      {
        id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 32, bold: true, font: 'Calibri', color: '1F4E79' },
        paragraph: { spacing: { before: 360, after: 160 }, outlineLevel: 0 },
      },
      {
        id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 28, bold: true, font: 'Calibri', color: '2E75B6' },
        paragraph: { spacing: { before: 280, after: 120 }, outlineLevel: 1 },
      },
      {
        id: 'Heading3', name: 'Heading 3', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 24, bold: true, font: 'Calibri', color: '2E75B6' },
        paragraph: { spacing: { before: 200, after: 80 }, outlineLevel: 2 },
      },
    ],
  },
  sections: [{
    properties: {
      page: {
        size: { width: 12240, height: 15840 },
        margin: { top: 1080, right: 1080, bottom: 1080, left: 1080 },
      },
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: '2E75B6', space: 1 } },
          children: [new TextRun({ text: 'ClawBot — T\xe0i liệu Kiến tr\xfac & Triển khai', font: 'Calibri', size: 18, color: '888888' })],
        })],
      }),
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          border: { top: { style: BorderStyle.SINGLE, size: 6, color: '2E75B6', space: 1 } },
          children: [
            new TextRun({ text: 'Trang ', font: 'Calibri', size: 18, color: '888888' }),
            new TextRun({ children: [PageNumber.CURRENT], font: 'Calibri', size: 18, color: '888888' }),
          ],
        })],
      }),
    },
    children,
  }],
});

Packer.toBuffer(doc).then(buffer => {
  fs.writeFileSync('d:\\Clawbot\\docs\\ClawBot-Architecture-VI.docx', buffer);
  console.log('Done: d:\\Clawbot\\docs\\ClawBot-Architecture-VI.docx');
}).catch(err => {
  console.error(err);
  process.exit(1);
});
