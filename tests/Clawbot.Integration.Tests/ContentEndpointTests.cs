using System.Net;
using System.Net.Http.Json;
using System.Text;
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
        listed.RootElement.GetProperty("items").EnumerateArray()
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
        rows.RootElement.GetProperty("items").EnumerateArray()
            .Should().NotContain(e => e.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData(" FACEBOOK ", "facebook")]
    [InlineData(" Zalo ", "zalo")]
    [InlineData("INSTAGRAM", "instagram")]
    public async Task Create_brief_normalizes_canonical_platform(string input, string expected)
    {
        var response = await _client.PostAsJsonAsync("/api/content/briefs", new
        {
            platform = input,
            brief = "Canonical platform campaign",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("platform").GetString().Should().Be(expected);
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    [InlineData("fb")]
    public async Task Create_brief_rejects_platform_outside_canonical_writable_set(string platform)
    {
        var response = await _client.PostAsJsonAsync("/api/content/briefs", new
        {
            platform,
            brief = "Unsupported platform campaign",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.platform_unsupported");
    }

    [Fact]
    public async Task Update_brief_allows_text_only_change_when_legacy_platform_is_preserved()
    {
        var briefId = Guid.NewGuid();
        await InsertContentBriefAsync(briefId, "tiktok", "Historical brief");

        var response = await _client.PutAsJsonAsync($"/api/content/briefs/{briefId}", new
        {
            platform = " TIKTOK ",
            brief = "Updated historical text",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("platform").GetString().Should().Be("tiktok");
        json.RootElement.GetProperty("brief").GetString().Should().Be("Updated historical text");
    }

    [Fact]
    public async Task Update_brief_rejects_switching_legacy_platform_to_another_unsupported_value()
    {
        var briefId = Guid.NewGuid();
        await InsertContentBriefAsync(briefId, "tiktok", "Historical brief");

        var response = await _client.PutAsJsonAsync($"/api/content/briefs/{briefId}", new
        {
            platform = "youtube",
            brief = "Must not rewrite historical platform",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.platform_unsupported");
    }

    [Fact]
    public async Task Generate_normalizes_canonical_platform_before_enqueuing_job()
    {
        var response = await _client.PostAsJsonAsync("/api/content/items/generate", new
        {
            briefId = (Guid?)null,
            platform = " Instagram ",
            briefText = "Generate a campaign draft",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var responseJson = await ReadJsonAsync(response);
        var payloadJson = await ReadBackgroundJobPayloadAsync(responseJson.RootElement.GetProperty("jobId").GetGuid());
        payloadJson.RootElement.GetProperty("Platform").GetString().Should().Be("instagram");
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    public async Task Generate_rejects_platform_outside_canonical_writable_set(string platform)
    {
        var response = await _client.PostAsJsonAsync("/api/content/items/generate", new
        {
            briefId = (Guid?)null,
            platform,
            briefText = "Generate a campaign draft",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.platform_unsupported");
    }

    [Fact]
    public async Task Generate_does_not_fall_back_to_legacy_brief_platform()
    {
        var briefId = Guid.NewGuid();
        await InsertContentBriefAsync(briefId, "tiktok", "Historical brief");

        var response = await _client.PostAsJsonAsync("/api/content/items/generate", new
        {
            briefId,
            platform = (string?)null,
            briefText = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.platform_unsupported");
    }

    [Fact]
    public async Task Repurpose_normalizes_canonical_platforms_before_enqueuing_job()
    {
        var itemId = Guid.NewGuid();
        await InsertContentItemAsync(itemId, "facebook", "Source draft");

        var response = await _client.PostAsJsonAsync($"/api/content/items/{itemId}/repurpose", new
        {
            targetPlatforms = new[] { " ZALO ", "Instagram" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var responseJson = await ReadJsonAsync(response);
        var payloadJson = await ReadBackgroundJobPayloadAsync(responseJson.RootElement.GetProperty("jobId").GetGuid());
        payloadJson.RootElement.GetProperty("TargetPlatforms").EnumerateArray()
            .Select(value => value.GetString())
            .Should().Equal("zalo", "instagram");
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    public async Task Repurpose_rejects_platform_outside_canonical_writable_set(string platform)
    {
        var itemId = Guid.NewGuid();
        await InsertContentItemAsync(itemId, "facebook", "Source draft");

        var response = await _client.PostAsJsonAsync($"/api/content/items/{itemId}/repurpose", new
        {
            targetPlatforms = new[] { "zalo", platform },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.platform_unsupported");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repurpose_rejects_null_or_missing_target_platforms(bool includeNullProperty)
    {
        var itemId = Guid.NewGuid();
        await InsertContentItemAsync(itemId, "facebook", "Source draft");
        using var content = new StringContent(
            includeNullProperty ? "{\"targetPlatforms\":null}" : "{}",
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync($"/api/content/items/{itemId}/repurpose", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("code").GetString().Should().Be("content.repurpose_invalid");
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

        var read = await _client.GetAsync($"/api/content/items/{itemId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var readJson = await ReadJsonAsync(read);
        readJson.RootElement.GetProperty("id").GetGuid().Should().Be(itemId);

        var missing = await _client.GetAsync($"/api/content/items/{Guid.NewGuid()}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var revision = updated.RootElement.GetProperty("contentRevision").GetInt32();
        await MarkContentItemReviewedAsync(itemId);
        var approve = await _client.PostAsJsonAsync($"/api/content/items/{itemId}/approve", new
        {
            expectedRevision = revision,
        });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await ReadJsonAsync(approve);
        approved.RootElement.GetProperty("status").GetString().Should().Be("scheduled");

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

    private async Task<JsonDocument> ReadBackgroundJobPayloadAsync(Guid jobId)
    {
        await using var conn = await _sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM background_jobs WHERE id = @id AND tenant_id = @tenantId;";
        cmd.Parameters.Add(new SqlParameter("@id", jobId));
        cmd.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        var payload = (string?)await cmd.ExecuteScalarAsync();
        payload.Should().NotBeNullOrWhiteSpace();
        return JsonDocument.Parse(payload!);
    }

    private async Task InsertContentBriefAsync(Guid id, string platform, string brief)
    {
        await using var conn = await _sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO content_briefs
                (id, tenant_id, platform, brief, status, created_at, updated_at)
            VALUES
                (@id, @tenantId, @platform, @brief, 'pending', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """;
        cmd.Parameters.Add(new SqlParameter("@id", id));
        cmd.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        cmd.Parameters.Add(new SqlParameter("@platform", platform));
        cmd.Parameters.Add(new SqlParameter("@brief", brief));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task MarkContentItemReviewedAsync(Guid id)
    {
        await using var conn = await _sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE content_items
            SET agent_review_status = 'passed',
                agent_reviewed_revision = content_revision,
                agent_reviewed_at = SYSDATETIMEOFFSET(),
                image_review_status = 'not_applicable',
                reviewed_image_count = 0
            WHERE id = @id AND tenant_id = @tenantId;
            """;
        cmd.Parameters.Add(new SqlParameter("@id", id));
        cmd.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        await cmd.ExecuteNonQueryAsync();
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
