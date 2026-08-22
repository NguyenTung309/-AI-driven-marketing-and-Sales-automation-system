using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Api.Contracts.SaleAssist;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

public sealed class KbModuleCrudTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static string UniqueCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    [Fact]
    public async Task CreateModule_Update_Archive_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = UniqueCode("kb");

        var created = await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest(code, "Học phí", "Mô tả học phí", "Sale"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<KbModuleDto>();
        dto!.Name.Should().Be("Học phí");

        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/kb/modules/{dto.Id:D}", UriKind.Relative),
            new UpdateKbModuleRequest("Học phí mới", "Đã cập nhật", "Sale"));
        updated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var archived = await client.PostAsync(
            new Uri($"/api/kb/modules/{dto.Id:D}/archive", UriKind.Relative), null);
        archived.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateModule_MissingCodeOrName_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var noCode = await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest("", "Tên", null, null));
        var noName = await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest("code", "", null, null));

        noCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateModule_DuplicateCode_IsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = UniqueCode("dup");

        await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest(code, "Lần 1", null, null));
        var second = await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest(code, "Lần 2", null, null));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateModule_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid():D}", UriKind.Relative),
            new UpdateKbModuleRequest("X", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateVersion_And_TestCase_RoundTrip()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var module = await CreateModuleAsync(client, UniqueCode("ver"));

        var version = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{module.Id:D}/versions", UriKind.Relative),
            new CreateKbVersionRequest("# Học phí 2026\n5 triệu/khoá."));
        version.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var testCase = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{module.Id:D}/test-cases", UriKind.Relative),
            new CreateKbTestCaseRequest("Học phí bao nhiêu?", "5 triệu/khoá"));
        testCase.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateVersion_UnknownModule_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid():D}/versions", UriKind.Relative),
            new CreateKbVersionRequest("nội dung"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddTestCase_UnknownModule_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid():D}/test-cases", UriKind.Relative),
            new CreateKbTestCaseRequest("Q?", "A"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<KbModuleDto> CreateModuleAsync(HttpClient client, string code)
    {
        var created = await client.PostAsJsonAsync(
            new Uri("/api/kb/modules", UriKind.Relative),
            new CreateKbModuleRequest(code, "Module", null, null));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<KbModuleDto>())!;
    }
}

public sealed class QuickReplyCrudTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateUpdateDelete_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"qr-{Guid.NewGuid():N}"[..20];

        var created = await client.PostAsJsonAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative),
            new CreateQuickReplyRequest(code, "Cảm ơn anh/chị đã liên hệ", "tn", null));
        created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var listed = await client.GetStringAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative));
        listed.Should().Contain(code);

        var id = ExtractId(listed, code);
        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/sale-assist/quick-replies/{id:D}", UriKind.Relative),
            new UpdateQuickReplyRequest("Nội dung mới", "tn", null));
        updated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(
            new Uri($"/api/sale-assist/quick-replies/{id:D}", UriKind.Relative));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_MissingCodeOrBody_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var noCode = await client.PostAsJsonAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative),
            new CreateQuickReplyRequest("", "body", null, null));
        var noBody = await client.PostAsJsonAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative),
            new CreateQuickReplyRequest("code", "", null, null));

        noCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_DuplicateCode_IsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"dup-{Guid.NewGuid():N}"[..20];
        var payload = new CreateQuickReplyRequest(code, "body", null, null);

        await client.PostAsJsonAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative), payload);
        var second = await client.PostAsJsonAsync(
            new Uri("/api/sale-assist/quick-replies", UriKind.Relative), payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static Guid ExtractId(string listJson, string code)
    {
        using var doc = JsonDocument.Parse(listJson);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.GetProperty("items").EnumerateArray();
        foreach (var item in items)
        {
            if (item.TryGetProperty("code", out var c) && c.GetString() == code
                && item.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid))
                return guid;
        }

        throw new InvalidOperationException($"quick reply {code} not found in list");
    }
}

public sealed class DocumentTemplateCrudTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static CreateDocumentTemplateRequest Template(string code) => new(
        code,
        "contract",
        "<h1>Hợp đồng</h1><p>{{ten}}</p>",
        []);

    [Fact]
    public async Task CreateUpdateDelete_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"doc-{Guid.NewGuid():N}"[..20];

        var created = await client.PostAsJsonAsync(
            new Uri("/api/docs/templates", UriKind.Relative), Template(code));
        created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var body = await created.Content.ReadAsStringAsync();
        var id = ExtractId(body);

        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/docs/templates/{id:D}", UriKind.Relative),
            new UpdateDocumentTemplateRequest("contract", "<h1>Đã sửa</h1>", []));
        updated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(
            new Uri($"/api/docs/templates/{id:D}", UriKind.Relative));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/docs/templates/{Guid.NewGuid():D}", UriKind.Relative),
            new UpdateDocumentTemplateRequest("contract", "<p>x</p>", []));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Guid ExtractId(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out var id) && id.TryGetGuid(out var guid))
            return guid;
        throw new InvalidOperationException("no id in response");
    }
}

public sealed class NotificationWriteTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MarkRead_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/notifications/{Guid.NewGuid():D}/read", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAllRead_IsIdempotent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri("/api/notifications/read-all", UriKind.Relative), null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }
}

public sealed class NotificationPreferencesWriteTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PushSubscribe_InvalidEndpoint_IsRejectedWithoutServerError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/push/subscribe", UriKind.Relative),
            new { endpoint = "khong-phai-url", keys = new { } });

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PushUnsubscribe_WithoutSubscription_ReturnsNonError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri("/api/push/subscribe", UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
