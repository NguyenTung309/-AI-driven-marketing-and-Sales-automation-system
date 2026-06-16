# Plan Thong Nhat Luong Zalo Inbound/Outbound

> **Cho agent thuc thi:** SKILL BAT BUOC: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Muc tieu:** Ket noi vong luong hoan chinh: nhan tin Zalo -> DB -> hien thi UI, va gui tin tu UI -> Zalo trong che do demo.

**Kien truc:** Luong inbound (Zalo polling -> IngestAsync -> DB -> API -> Frontend) da chay. Luong outbound (Frontend -> InboxEndpoints.SendOutboundAsync -> PancakeChannelAdapter.SendAsync -> Pancake API -> Zalo) bi hong vi PancakeConfigResolver khong tim thay token demo (luu trong env vars / DemoRuntimeConfigStore) va PancakeChannelAdapter.SendAsync khong phan giai duoc page ID tu external thread ID dang phang. Sua: them fallback env-var trong config resolver, them PageId vao runtime config, va dung PageId tu config khi SplitThread tra ve pagePart rong.

**Cong nghe:** .NET 8, EF Core + SQL Server, React 19 + TypeScript + Tailwind + TanStack Query + Axios, SignalR, Pancake Public API V2 (poll) / V1 (send).

---


## Cau truc File

### Cac file can sua:

| File | Chuc nang | Thay doi |
|------|-----------|---------|
| src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeRuntimeConfig.cs | Config record runtime cho Pancake adapter | Add PageId property |
| src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeConfigResolver.cs | Giai config Pancake tu DB / appsettings / env | Add env var fallback path + PageId |
| src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs | Trien khai SendAsync | Fallback pagePart to cfg.PageId when composite parsing fails |
| tests/Clawbot.Infrastructure.Tests/Channels/PancakeAdapterSendTests.cs | Kiem thu tich hop | File moi |

### Task 1: Them PageId vao PancakeRuntimeConfig

**Files:**
- Sua: src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeRuntimeConfig.cs

- [ ] Buoc 1.1: Them thuoc tinh PageId

  Them vao record:

  ```csharp
  public sealed record PancakeRuntimeConfig(
      string BaseUrl,
      string AccessToken,
      string WebhookSecret,
      string SignatureHeader,
      string SignatureAlgo,
      string SignatureEncoding,
      string SendPathTemplate,
      string AuthMode,
      string PageId   // NEW
  );
  ```

- [ ] Buoc 1.2: Cap nhat tat ca noi goi

  Tim cac cho dung `new PancakeRuntimeConfig(`:
  1. PancakeConfigResolver.cs (duong dan DB + fallback appsettings)
  2. Code test

  Add `PageId: row.PageId ?? string.Empty` (DB path) and `PageId: section["PageId"] ?? string.Empty` (appsettings path).

- [ ] Buoc 1.3: Build de kiem tra

  ```
  dotnet build src/Clawbot.sln
  ```

- [ ] Buoc 1.4: Commit

  ```
  git add src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeRuntimeConfig.cs
  git commit -m "feat: add PageId to PancakeRuntimeConfig"
  ```

---


> **Ghi chu kien truc (Task 2):** Giai phap doc env var la tam thoi cho demo.
> Ve dai han, nen Seed mot ban ghi Demo Tenant trong DB de PancakeConfigResolver 
> chi doc tu mot nguon duy nhat (DB), tranh xung dot ENV khi co nhieu tenant.
> TODO: Tao seed data + migration cho demo config sau khi demo on dinh.

### Task 2: Cau noi fallback env-var trong PancakeConfigResolver

**Files:**
- Sua: src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeConfigResolver.cs

- [ ] Buoc 2.1: Them fallback env-var sau fallback appsettings

  Sau khi kiem tra section IConfiguration (tra ve null neu thieu section), them fallback thu ba doc truc tiep tu env vars. Trong .NET, IConfiguration bao gom env vars duoi ten goc cua chung (PANCAKE_*).

  ```csharp
  // 3rd fallback: env vars (demo mode)
  var envPageToken = _cfg["PANCAKE_PAGE_ACCESS_TOKEN"];
  var envPageId = _cfg["PANCAKE_PAGE_ID"];
  var envWebhookSecret = _cfg["PANCAKE_WEBHOOK_SECRET"];

  if (!string.IsNullOrEmpty(envPageToken))
  {
      LogEnvFallback(_logger);
      return new PancakeRuntimeConfig(
          BaseUrl: DefaultBaseUrl,
          AccessToken: envPageToken,
          WebhookSecret: envWebhookSecret ?? string.Empty,
          SignatureHeader: DefaultSigHeader,
          SignatureAlgo: "hmac-sha256",
          SignatureEncoding: "hex",
          SendPathTemplate: DefaultSendPath,
          AuthMode: "query",
          PageId: envPageId ?? string.Empty);
  }
  ```

  Them truong logger vao PancakeConfigResolver:

  ```csharp
  private readonly ILogger<PancakeConfigResolver> _logger;

  public PancakeConfigResolver(
      AppDbContext db,
      IEncryptor encryptor,
      IConfiguration cfg,
      ILogger<PancakeConfigResolver> logger)   // NEW
  {
      _db = db;
      _encryptor = encryptor;
      _cfg = cfg;
      _logger = logger;
  }
  }
  ```

  Them helper log message:

  ```csharp
  [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "PancakeConfigResolver: using env-var fallback (demo mode)")]
  private static partial void LogEnvFallback(ILogger logger);
  ```

- [ ] Buoc 2.2: Build de kiem tra

  ```
  dotnet build src/Clawbot.sln
  ```

- [ ] Buoc 2.3: Commit

  ```
  git add src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeConfigResolver.cs
  git commit -m "feat: add env-var fallback to PancakeConfigResolver for demo mode"
  ```

---

### Task 3: Sua phan giai page ID trong SendAsync khi thread ID dang phang

**Files:**
- Sua: src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs

- [ ] Buoc 3.1: Fallback pagePart ve cfg.PageId khi split composite ra rong

  Trong SendAsync, sau SplitThread, kiem tra neu pagePart rong thi dung PageId tu config:

  ```csharp
  var (threadPart, pagePart) = SplitThread(externalThreadId);
  // Fallback: if thread ID is not in composite format,
  // resolve page ID from config.
  if (string.IsNullOrEmpty(pagePart) && !string.IsNullOrEmpty(cfg.PageId))
      pagePart = cfg.PageId;
  ```

  Doan nay dat ngay sau SplitThread va truoc cac lenh replace SendPathTemplate. Phan con lai cua method giu nguyen.

> **Ghi chu tuong lai (Task 3):** _SplitThread_ la design yeu - no buoc identity cua conversation
> phai mang thong tin cua channel config. Ve dai: _SendOutboundAsync_ hoac tang goi phia truoc
> nen truyen xuong ca _externalThreadId_ + _PageId_ lay tu navigation _Conversation -> Channel -> PageId_,
> thay vi bat _PancakeChannelAdapter_ phai tu mo mam tu chuoi externalThreadId.
> Hien tai domain chua co lien ket Conversation -> ChannelId - can bo sung sau.



- [ ] Buoc 3.2: Build de kiem tra

  ```
  dotnet build src/Clawbot.sln
  ```

- [ ] Buoc 3.3: Commit

  ```
  git add src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs
  git commit -m "fix: fallback pagePart to cfg.PageId when SplitThread returns empty"
  ```

---

### Task 4: Viet kiem thu tich hop cho luong gui

**Files:**
- Tao: tests/Clawbot.Infrastructure.Tests/Channels/PancakeAdapterSendTests.cs

- [ ] Buoc 4.1: Viet test

  ```csharp
  using Clawbot.Infrastructure.Channels.Pancake;
  using Clawbot.SharedKernel.Channels;
  using Clawbot.SharedKernel.Multitenancy;
  using NSubstitute;

  namespace Clawbot.Infrastructure.Tests.Channels;

  public sealed class PancakeAdapterSendTests
  {
      private readonly HttpClient _http = new(new PancakeSendTestHandler());
      private readonly IPancakeConfigResolver _resolver;
      private readonly ITenantAccessor _tenants = Substitute.For<ITenantAccessor>();

      public PancakeAdapterSendTests()
      {
          var resolver = Substitute.For<IPancakeConfigResolver>();
          resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(new PancakeRuntimeConfig(
                  BaseUrl: "https://pages.fm/api/public_api/v2",
                  AccessToken: "test_page_token",
                  WebhookSecret: "",
                  SignatureHeader: "x-pancake-signature",
                  SignatureAlgo: "hmac-sha256",
                  SignatureEncoding: "hex",
                  SendPathTemplate: "/pages/{page_id}/conversations/{thread_id}/messages",
                  AuthMode: "query",
                  PageId: "pzl_test_page_123"));
          _resolver = resolver;
      }

      [Fact]
      public async Task SendAsync_WithFlatThreadId_ShouldUseConfigPageId()
      {
          var adapter = new PancakeChannelAdapter(_http, _resolver, _tenants);
          var ex = await Record.ExceptionAsync(() =>
              adapter.SendAsync("conv_abc_456", "Hello from test", CancellationToken.None));
          Assert.Null(ex);
      }
  }

  public sealed class PancakeSendTestHandler : HttpClientHandler
  {
      protected override Task<HttpResponseMessage> SendAsync(
          HttpRequestMessage request, CancellationToken ct)
      {
          Assert.Contains("pzl_test_page_123", request.RequestUri!.ToString());
          Assert.Contains("conv_abc_456", request.RequestUri!.ToString());
          Assert.Contains("page_access_token=test_page_token", request.RequestUri!.ToString());
          return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
      }
  }
  ```


- [ ] Buoc 4.1b: Unit test cho PancakeConfigResolver env-var fallback

  Them test kiem tra rieng PancakeConfigResolver voi IConfiguration gia lap chua bien moi truong:

  `csharp
  using Clawbot.Infrastructure.Channels.Pancake;
  using Clawbot.Infrastructure.Persistence;
  using Clawbot.SharedKernel.Security;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.Logging.Abstractions;
  using NSubstitute;

  namespace Clawbot.Infrastructure.Tests.Channels;

  public sealed class PancakeConfigResolverTests
  {
      [Fact]
      public async Task ResolveAsync_WithEnvVars_ShouldReturnConfig()
      {
          var cfgData = new Dictionary<string, string?>
          {
              ["PANCAKE_PAGE_ACCESS_TOKEN"] = "env_page_token",
              ["PANCAKE_PAGE_ID"] = "env_page_123",
              ["PANCAKE_WEBHOOK_SECRET"] = "env_secret",
          };
          var config = new ConfigurationBuilder()
              .AddInMemoryCollection(cfgData!)
              .Build();

          var db = Substitute.For<AppDbContext>();
          var encryptor = Substitute.For<IEncryptor>();
          var resolver = new PancakeConfigResolver(db, encryptor, config,
              NullLogger<PancakeConfigResolver>.Instance);

          var result = await resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);
          Assert.NotNull(result);
          Assert.Equal("env_page_token", result.AccessToken);
          Assert.Equal("env_page_123", result.PageId);
      }
  }
  `

  Chay: dotnet test tests/Clawbot.Infrastructure.Tests --filter PancakeConfigResolverTests
  Ky vong: PASS.

- [ ] Buoc 4.2: Chay test de xac nhan pass sau Task 3

  ```
  dotnet test tests/Clawbot.Infrastructure.Tests --filter PancakeAdapterSendTests
  ```

  Ky vong: PASS.

- [ ] Buoc 4.3: Chay toan bo test suite

  ```
  dotnet test tests/Clawbot.Infrastructure.Tests
  ```

- [ ] Buoc 4.4: Commit

  ```
  git add tests/Clawbot.Infrastructure.Tests/Channels/PancakeAdapterSendTests.cs
  git commit -m "test: add integration test for SendAsync with flat thread ID"
  ```


---

### Task 5: Kiem thu E2E thu cong

- [ ] Buoc 5.1: Khoi dong backend + frontend

  `
  # Terminal 1: Backend
  dotnet run --project src/api/Clawbot.Api --launch-profile Demo

  # Terminal 2: Frontend
  cd src/frontend/clawbot-web
  npm run dev
  `

- [ ] Buoc 5.2: Kiem tra luong inbound
  1. Gui tin nhan tu Zalo vao page
  2. Cho toi da 10s cho PancakePollingService poll
  3. Mo http://localhost:5173/conversations
  4. Xac nhan hoi thoai + tin nhan hien trong danh sach
  5. Click hoi thoai, xac nhan tin nhan hien trong ChatPane

- [ ] Buoc 5.3: Kiem tra luong outbound
  1. Trong hoi thoai dang mo, go tin nhan tra loi
  2. Click Gui hoac nhan Enter
  3. Xac nhan:
     - Tin nhan hien trong ChatPane dang bubble gui di
     - Nhan duoc tin tra loi tren Zalo
     - Kiem tra log backend co outbound OK

- [ ] Buoc 5.4: Kiem tra cac truong hop bien
  1. Gui tin rong - bi chan boi disabled state
  2. Gui khi mutation dang chay - nut gui bi disable
  3. Refresh trang - lich su hoi thoai van con tu DB

---

## Kiem Tra Bao Phu

| Yeu cau | Task | Trang thai |
|-------------|------|--------|
| Config resolver tim thay token trong demo mode | Task 2 | Planned |
| SendAsync phan giai page ID dung | Task 3 | Planned |
| Dinh dang thread ID phang hoat dong khi gui | Task 3 | Planned |
| E2E inbound + outbound da kiem tra | Task 5 | Manual |
| Kiem thu tich hop for send path | Task 4 | Planned |

---

## Tu Soat

- Soat placeholder: Khong co TBD, TODO, hay code placeholder. Toan bo code block hoan chinh.
- Nhat quan kieu: Constructor PancakeRuntimeConfig duoc cap nhat nhat quan o Task 1 + tat ca noi goi o Buoc 1.2.
- Soat pham vi: Mot moi quan tam duy nhat - luong demo inbound/outbound thong nhat. Khong refactor khong lien quan.
- Soat mo ho: Ten env var PANCAKE_PAGE_ACCESS_TOKEN, PANCAKE_PAGE_ID khop voi file .env va DemoRuntimeConfigStore.
