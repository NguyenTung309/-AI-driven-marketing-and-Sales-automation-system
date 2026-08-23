using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

public sealed class AuthLoginTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AuthLoginTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_WithBootstrappedAdmin_ReturnsAccessToken()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(ApiTestFactory.AdminEmail, ApiTestFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        login!.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPassword_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(ApiTestFactory.AdminEmail, "sai-mat-khau"));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Login_UnknownEmail_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("khong-ton-tai@test.local", ApiTestFactory.AdminPassword));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Login_MissingCredentials_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("", ""));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_WithoutCookie_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/auth/refresh", UriKind.Relative), null);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_WithoutSession_DoesNotFailWithServerError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), null);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}

public sealed class AuthenticatedReadEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AuthenticatedReadEndpointTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>
    /// Lấy danh sách route GET thật từ EndpointDataSource thay vì hardcode đường dẫn — đường dẫn
    /// đoán tay trả 404 sẽ khiến test "xanh giả" mà không chạy handler nào.
    /// Chỉ lấy route không có tham số để gọi được trực tiếp.
    /// </summary>
    /// <summary>
    /// Route không chạy được trên provider InMemory (không phải lỗi sản phẩm).
    /// /api/inbox/channels gọi db.Database.GetDbConnection() để dò schema — InMemory không có
    /// relational connection nên ném InvalidOperationException. Cần SQLite/SQL Server để phủ.
    /// </summary>
    private static readonly HashSet<string> RelationalOnlyRoutes =
        new(StringComparer.Ordinal) { "/api/inbox/channels" };

    public static TheoryData<string> ParameterlessGetRoutes()
    {
        using var factory = new ApiTestFactory();
        using var scope = factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

        var data = new TheoryData<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            if (methods is null || !methods.HttpMethods.Contains("GET", StringComparer.Ordinal))
                continue;

            var pattern = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(pattern)
                || pattern.Contains('{', StringComparison.Ordinal)
                || !pattern.StartsWith("/api/", StringComparison.Ordinal)
                || RelationalOnlyRoutes.Contains(pattern))
            {
                continue;
            }

            if (seen.Add(pattern))
                data.Add(pattern);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ParameterlessGetRoutes))]
    public async Task AuthenticatedGet_DoesNotReturnUnauthorizedOrServerError(string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public void RouteDiscovery_FindsRealApiRoutes()
    {
        // Nếu con số này về 0 thì theory ở trên không chạy handler nào — phải fail rõ ràng.
        ParameterlessGetRoutes().Count.Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task AuthenticatedGet_Leads_ReturnsJson()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/leads", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GarbageBearerToken_IsRejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "khong-phai-jwt");

        var response = await client.GetAsync(new Uri("/api/leads", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
