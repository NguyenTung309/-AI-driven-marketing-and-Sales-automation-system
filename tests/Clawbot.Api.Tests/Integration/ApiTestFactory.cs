using System.Net.Http.Headers;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Boot toàn bộ host API trong test. Ba điều kiện bắt buộc để host lên được mà không cần
/// SQL Server / RabbitMQ thật:
///   1. Environment KHÔNG phải "Development" — Program.cs chạy DevDataSeeder.EnsureSchemaAsync
///      trước Build() và sẽ ghi DDL thẳng vào SQL Server thật.
///   2. Clawbot:StartupMode = passive — không đăng ký Hangfire server nên không mở kết nối lúc boot.
///   3. Thay AppDbContext bằng InMemory provider.
/// Ngoài Development, gRPC agent-service bắt buộc https (cert chỉ bắt ở Production).
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Test-Admin-Password-1!";

    private static readonly string AgentServiceKey =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i + 7)).ToArray());

    private static readonly string EncryptionKey =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i + 61)).ToArray());

    private readonly string _databaseName = $"api-int-{Guid.NewGuid():N}";

    private readonly Lazy<Task<string>> _accessToken;

    public ApiTestFactory() =>
        _accessToken = new Lazy<Task<string>>(
            LoginAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Staging");
        builder.ConfigureHostConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clawbot:StartupMode"] = "passive",
                ["ConnectionStrings:SqlServer"] =
                    "Server=(local);Database=clawbot_test;Trusted_Connection=True;TrustServerCertificate=True",
                ["AgentServiceAuthentication:SigningKey"] = AgentServiceKey,
                ["Jwt:SigningKey"] = "test-public-jwt-signing-key-that-is-long-enough-1234567890",
                ["AgentService:Url"] = "https://localhost:5001",
                ["Bootstrap:InitialAdminEmail"] = AdminEmail,
                ["Bootstrap:InitialAdminPassword"] = AdminPassword,
                ["Encryption:Base64Key"] = EncryptionKey,
                // Bộ quét gọi hàng trăm endpoint; để log mặc định thì stdout phình tới mức
                // test host chết khi deserialize kết quả.
                ["Logging:LogLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Serilog:MinimumLevel:Override:Microsoft"] = "Error",
                ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Error",
            }));

        builder.ConfigureServices(services =>
        {
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                    || d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var descriptor in dbDescriptors)
                services.Remove(descriptor);

            // InMemory không có transaction; không bỏ qua cảnh báo này thì mọi handler dùng
            // BeginTransaction sẽ ném và trả 500, che mất hành vi thật của endpoint.
            services.AddDbContext<AppDbContext>(opt => opt
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Đăng nhập bằng admin gốc và trả về client đã gắn bearer token. Token cache lại một lần:
    /// /auth/login bị rate-limit 30 request/phút mỗi IP nên đăng nhập lại ở từng test sẽ ăn 429.
    /// </summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var token = await _accessToken.Value.ConfigureAwait(false);
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<string> LoginAsync()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(AdminEmail, AdminPassword));
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("login_response_empty");
        return login.AccessToken;
    }
}
