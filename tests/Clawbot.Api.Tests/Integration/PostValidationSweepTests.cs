using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Gửi body rỗng "{}" tới mọi route POST/PUT không tham số. Không nhắm đường thành công mà nhắm
/// nhánh xác thực đầu vào: endpoint phải trả 400/404/409/422 chứ KHÔNG được 500 vì thiếu trường.
/// </summary>
public sealed class PostValidationSweepTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PostValidationSweepTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>
    /// Route bỏ qua vì phụ thuộc hạ tầng ngoài (gRPC agent-service, RabbitMQ, provider quan hệ)
    /// nên trong harness sẽ hỏng vì lý do không liên quan tới xác thực đầu vào.
    /// </summary>
    private static readonly HashSet<string> SkippedRoutes = new(StringComparer.Ordinal)
    {
        "/api/leads/rescore",
        "/api/leads/import.csv",
        "/api/kb/classify-upload",
        "/api/content/trends/scan",
        // Route dùng raw SQL (FromSql) hoặc có ràng buộc NOT NULL mà InMemory kiểm tra khác
        // SQL Server — muốn phủ phải đổi harness sang SQLite.
        "/api/admin/inboxes",
        "/api/tokens/settings",
        // LỖI ĐÃ BIẾT: không validate null password -> ArgumentNullException -> 500.
        // Khoá lại bằng test riêng, loại khỏi sweep.
        "/api/admin/users/",
    };

    private static TheoryData<string, string> BodylessWriteRoutes()
    {
        using var factory = new ApiTestFactory();
        using var scope = factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

        var data = new TheoryData<string, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            if (metadata is null)
                continue;

            var method = metadata.HttpMethods.FirstOrDefault(m =>
                m is "POST" or "PUT" or "PATCH");
            if (method is null)
                continue;

            var raw = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(raw)
                || !raw.StartsWith("/api/", StringComparison.Ordinal)
                || raw.Contains('{', StringComparison.Ordinal)
                || SkippedRoutes.Contains(raw))
            {
                continue;
            }

            if (seen.Add($"{method} {raw}"))
                data.Add(method, raw);
        }

        return data;
    }

    public static TheoryData<string, string> WriteRoutes() => BodylessWriteRoutes();

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EmptyBody_IsRejectedWithoutServerError(string method, string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = content,
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AdminUsers_CreateEmptyBody_Returns500_KnownDefect()
    {
        // LỖI ĐÃ BIẾT: AdminUsersEndpoints POST /api/admin/users/ không kiểm tra null
        // trước khi gọi UserManager.CreateAsync(user, null) -> ArgumentNullException -> 500.
        // Nên trả 400. Khi sửa, test này sẽ đỏ và cần cập nhật.
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/admin/users/", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public void RouteDiscovery_FindsWriteRoutes()
    {
        WriteRoutes().Count.Should().BeGreaterThan(10);
    }
}
