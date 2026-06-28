using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Clawbot.Integration.Tests;

public sealed class ContentEndpointTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private static readonly Guid TenantId = Guid.Parse(TestAuthHandler.TenantId);
    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ContentEndpointTests(SqlServerFixture sql)
    {
        _sql = sql;
        _factory = new ClawbotWebApplicationFactory(sql);
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Brief_crud_roundtrip_uses_http_contract()
    {
        var create = await _client.PostAsJsonAsync("/api/content/briefs", new
        {
            platform = "facebook",
            brief = "Launch HSK4 webinar campaign",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadJsonAsync(create);
        var id = created.RootElement.GetProperty("id").GetGuid();
        created.RootElement.GetProperty("platform").GetString().Should().Be("facebook");

        var list = await _client.GetAsync("/api/content/briefs?platform=facebook");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await ReadJsonAsync(list);
        listed.RootElement.EnumerateArray()
            .Should().Contain(e => e.GetProperty("id").GetGuid() == id);

        var update = await _client.PutAsJsonAsync($"/api/content/briefs/{id}", new
        {
            platform = "zalo",
            brief = "Updated Zalo campaign",
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(update);
        updated.RootElement.GetProperty("platform").GetString().Should().Be("zalo");
        updated.RootElement.GetProperty("brief").GetString().Should().Be("Updated Zalo campaign");

        var delete = await _client.DeleteAsync($"/api/content/briefs/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await _client.GetAsync("/api/content/briefs?platform=zalo");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ReadJsonAsync(afterDelete);
        rows.RootElement.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Item_approval_schedule_calendar_and_cancel_roundtrip_uses_http_contract()
    {
        var itemId = Guid.NewGuid();
        await InsertContentItemAsync(itemId, "instagram", "Initial carousel draft");

        var update = await _client.PutAsJsonAsync($"/api/content/items/{itemId}", new
        {
            body = "Updated carousel draft",
            assetsJson = """[{"type":"image","url":"https://cdn.example/1.png"}]""",
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(update);
        updated.RootElement.GetProperty("body").GetString().Should().Be("Updated carousel draft");

        var approve = await _client.PostAsync($"/api/content/items/{itemId}/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await ReadJsonAsync(approve);
        approved.RootElement.GetProperty("status").GetString().Should().Be("approved");

        var scheduledAt = DateTimeOffset.UtcNow.AddDays(2);
        var schedule = await _client.PostAsJsonAsync($"/api/content/items/{itemId}/schedule", new
        {
            scheduledAt,
        });
        schedule.StatusCode.Should().Be(HttpStatusCode.Created);
        var scheduled = await ReadJsonAsync(schedule);
        var scheduleId = scheduled.RootElement.GetProperty("id").GetGuid();
        scheduled.RootElement.GetProperty("contentItemId").GetGuid().Should().Be(itemId);

        var calendar = await _client.GetAsync($"/api/content/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(3).ToString("O"))}");
        calendar.StatusCode.Should().Be(HttpStatusCode.OK);
        var calendarJson = await ReadJsonAsync(calendar);
        calendarJson.RootElement.GetProperty("items").EnumerateArray()
            .Should().Contain(e => e.GetProperty("scheduleId").GetGuid() == scheduleId);

        var queue = await _client.GetAsync("/api/content/queue?status=scheduled&platform=instagram");
        queue.StatusCode.Should().Be(HttpStatusCode.OK);
        var queueJson = await ReadJsonAsync(queue);
        queueJson.RootElement.GetProperty("items").EnumerateArray()
            .Should().Contain(e => e.GetProperty("id").GetGuid() == itemId);

        var cancel = await _client.DeleteAsync($"/api/content/schedule/{scheduleId}");
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var queueAfterCancel = await _client.GetAsync("/api/content/queue?status=approved&platform=instagram");
        queueAfterCancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var reverted = await ReadJsonAsync(queueAfterCancel);
        reverted.RootElement.GetProperty("items").EnumerateArray()
            .Should().Contain(e => e.GetProperty("id").GetGuid() == itemId);
    }

    [Fact]
    public async Task Image_prompt_validation_returns_http_400()
    {
        var response = await _client.PostAsJsonAsync("/api/content/image-prompts", new
        {
            brief = "",
            platform = "facebook",
            style = "editorial",
            brandTokens = Array.Empty<string>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.image_prompt_invalid");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private async Task InsertContentItemAsync(Guid id, string platform, string body)
    {
        await using var conn = await _sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO content_items
                (id, tenant_id, platform, status, body, assets_json, created_at, updated_at)
            VALUES
                (@id, @tenantId, @platform, 'draft', @body, '[]', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """;
        cmd.Parameters.Add(new SqlParameter("@id", id));
        cmd.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        cmd.Parameters.Add(new SqlParameter("@platform", platform));
        cmd.Parameters.Add(new SqlParameter("@body", body));
        await cmd.ExecuteNonQueryAsync();
    }
}
