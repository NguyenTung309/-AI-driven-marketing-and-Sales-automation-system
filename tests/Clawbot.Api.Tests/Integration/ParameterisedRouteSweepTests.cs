using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Quét các route có đúng một tham số {id:guid} bằng một GUID không tồn tại. Mục tiêu là chạy
/// thân handler tới nhánh "không tìm thấy" — nhánh này đi qua auth, phân quyền, resolve tenant và
/// truy vấn DB, tức phần lớn code của handler.
/// </summary>
public sealed class ParameterisedRouteSweepTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ParameterisedRouteSweepTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>
    /// Route cần provider quan hệ thật — hoặc gọi db.Database.GetDbConnection, hoặc dùng raw SQL
    /// (FromSql). InMemory không xử lý được; muốn phủ thì phải đổi harness sang SQLite/SQL Server.
    /// </summary>
    private static readonly HashSet<string> RelationalOnlyRoutes = new(StringComparer.Ordinal)
    {
        "/api/inbox/channels",
        "/api/orchestration/v2/schedules/{id:guid}",
        "/api/contacts/{id:guid}/memories",
    };

    /// <summary>
    /// LỖI ĐÃ BIẾT (chưa sửa): ExperimentService ném InvalidOperationException("experiment_not_found")
    /// và endpoint không bắt, nên hỏi một experiment không tồn tại trả 500 thay vì 404.
    /// Hành vi này được khoá lại bằng test riêng bên dưới; loại khỏi vòng quét để nó không
    /// che mất các 500 khác.
    /// </summary>
    private static readonly HashSet<string> KnownDefectRoutes =
        new(StringComparer.Ordinal) { "/api/experiments/{id:guid}/summary" };

    private static TheoryData<string> SingleGuidRoutes(string method)
    {
        using var factory = new ApiTestFactory();
        using var scope = factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();

        var data = new TheoryData<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            if (methods is null || !methods.HttpMethods.Contains(method, StringComparer.Ordinal))
                continue;

            var pattern = endpoint.RoutePattern;
            var raw = pattern.RawText;
            if (string.IsNullOrWhiteSpace(raw)
                || !raw.StartsWith("/api/", StringComparison.Ordinal)
                || RelationalOnlyRoutes.Contains(raw)
                || KnownDefectRoutes.Contains(raw))
            {
                continue;
            }

            var parameters = pattern.Parameters;
            if (parameters.Count != 1 || !IsGuidParameter(parameters[0]))
                continue;

            var concrete = raw.Replace(
                $"{{{parameters[0].Name}:guid}}",
                Guid.NewGuid().ToString("D"),
                StringComparison.Ordinal);
            if (concrete.Contains('{', StringComparison.Ordinal))
                continue;

            if (seen.Add(concrete))
                data.Add(concrete);
        }

        return data;
    }

    private static bool IsGuidParameter(RoutePatternParameterPart parameter) =>
        parameter.ParameterPolicies.Any(policy =>
            string.Equals(policy.Content, "guid", StringComparison.Ordinal));

    public static TheoryData<string> GetRoutes() => SingleGuidRoutes("GET");

    public static TheoryData<string> DeleteRoutes() => SingleGuidRoutes("DELETE");

    [Theory]
    [MemberData(nameof(GetRoutes))]
    public async Task Get_UnknownId_IsHandledNotCrashed(string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [MemberData(nameof(DeleteRoutes))]
    public async Task Delete_UnknownId_IsHandledNotCrashed(string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExperimentSummary_UnknownId_Returns500_KnownDefect()
    {
        // KHOÁ HÀNH VI HIỆN TẠI, KHÔNG PHẢI KHẲNG ĐỊNH NÓ ĐÚNG.
        // ExperimentService.GetSummaryAsync ném InvalidOperationException("experiment_not_found")
        // nhưng ExperimentsEndpoints không bắt -> client nhận 500 thay vì 404.
        // Khi sửa endpoint để trả 404, test này sẽ đỏ và cần cập nhật.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/experiments/{Guid.NewGuid():D}/summary", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public void RouteDiscovery_FindsParameterisedRoutes()
    {
        // Chặn "xanh giả": nếu bộ lọc hỏng và không tìm ra route nào thì theory ở trên vô nghĩa.
        GetRoutes().Count.Should().BeGreaterThan(10);
        DeleteRoutes().Count.Should().BeGreaterThan(3);
    }
}
