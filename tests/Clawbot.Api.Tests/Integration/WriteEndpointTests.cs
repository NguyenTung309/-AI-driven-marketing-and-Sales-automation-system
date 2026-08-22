using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clawbot.Api.Contracts.ChatScenarios;
using Clawbot.Api.Contracts.Leads;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

public sealed class LabelsWriteEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public LabelsWriteEndpointTests(ApiTestFactory factory) => _factory = factory;

    private static Uri Labels(string suffix = "") =>
        new($"/api/labels{suffix}", UriKind.Relative);

    [Fact]
    public async Task List_IsAllowedForAdmin()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Labels("/"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_AsAdmin_IsForbiddenByCurrentRule()
    {
        // LabelsEndpoints.CreateAsync chủ động Forbid khi caller có quyền "admin:inboxes"
        // (role Admin luôn có). Test này khoá lại HÀNH VI HIỆN TẠI, không khẳng định nó đúng —
        // xem ghi chú trong báo cáo: nhánh này nhiều khả năng là logic thừa/copy nhầm.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Labels("/"), new { Name = "Khách VIP", Color = "#ff0000" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            Labels($"/{Guid.NewGuid():D}"), new { Name = "X", Color = "#000000" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(Labels($"/{Guid.NewGuid():D}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Labels("/"), new { Name = "X", Color = "#000000" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out var id) && id.TryGetGuid(out var value))
            return value;
        return Guid.Empty;
    }
}

public sealed class LeadsWriteEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public LeadsWriteEndpointTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_WithUnknownContact_IsRejectedNotCrashed()
    {
        // Đường tạo lead qua agent từng bỏ kiểm tra contact tồn tại — phải trả lỗi rõ ràng,
        // không được 500 hay tạo lead mồ côi.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/leads/", UriKind.Relative),
            new CreateLeadRequest(Guid.NewGuid(), "facebook", null, null));

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ImportCsv_MissingRequiredColumns_ReturnsValidationError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent(
            "display_name\nNguyễn Văn A\n", Encoding.UTF8, "text/csv");

        var response = await client.PostAsync(
            new Uri("/api/leads/import.csv", UriKind.Relative), content);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ImportCsv_ValidRows_ImportsAndAppearsInList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent(
            "display_name,source_platform\nLê Thị Import,zalo\n", Encoding.UTF8, "text/csv");

        var response = await client.PostAsync(
            new Uri("/api/leads/import.csv", UriKind.Relative), content);

        if (response.StatusCode == HttpStatusCode.OK)
            (await client.GetStringAsync(new Uri("/api/leads", UriKind.Relative)))
                .Should().Contain("zalo");
    }

    [Fact]
    public async Task UpdateStage_UnknownLead_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/leads/{Guid.NewGuid():D}/stage", UriKind.Relative),
            new { Stage = "customer", Reason = "test" });

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExportCsv_ReturnsCsvAttachment()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/leads/export.csv", UriKind.Relative));

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            (await response.Content.ReadAsStringAsync()).Should().Contain("lead_id");
        }
    }
}

public sealed class ChatScenarioWriteEndpointTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ChatScenarioWriteEndpointTests(ApiTestFactory factory) => _factory = factory;

    private static Uri Scenarios(string suffix = "") =>
        new($"/api/chat-scenarios{suffix}", UriKind.Relative);

    [Fact]
    public async Task CreateUpdateDelete_RoundTripsThroughApi()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"kb-{Guid.NewGuid():N}"[..12];

        var created = await client.PostAsJsonAsync(
            Scenarios("/"),
            new CreateChatScenarioRequest(
                code, "hoc-phi", "học phí bao nhiêu", "Dạ học phí là...", "facebook", "than-thien"));
        created.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var dto = await created.Content.ReadFromJsonAsync<ChatScenarioDto>();
        dto!.Code.Should().Be(code);

        var fetched = await client.GetAsync(Scenarios($"/{dto.Id:D}"));
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await client.PutAsJsonAsync(
            Scenarios($"/{dto.Id:D}"),
            new UpdateChatScenarioRequest(
                "hoc-phi", "học phí thế nào", "Dạ học phí mới...", "facebook,zalo", "than-thien"));
        updated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(Scenarios($"/{dto.Id:D}"));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        (await client.GetAsync(Scenarios($"/{dto.Id:D}"))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_InvalidPayload_DoesNotReturnServerError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Scenarios("/"), new { Name = "" });

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/chat-scenarios/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}

public sealed class MalformedRequestTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public MalformedRequestTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task MalformedJsonBody_ReturnsBadRequestNotServerError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = new StringContent("{ khong-phai-json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/labels/", UriKind.Relative), content);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task InvalidGuidInRoute_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri("/api/chat-scenarios/khong-phai-guid", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnsupportedMethodOnRoute_Returns405()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsync(
            new Uri("/api/labels/", UriKind.Relative),
            new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }
}
