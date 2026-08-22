using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Clawbot.Domain.Observability;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/system-logs (perm system.logs). SystemLogEntry là read model chỉ có ctor private
/// (ghi bằng SqlBulkCopy ngoài đời) nên test seed qua reflection. Nhánh q dùng EF.Functions.Like
/// (InMemory không hỗ trợ) nên không phủ.
/// </summary>
public sealed class AdminSystemLogsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AdminSystemLogsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    /// <summary>Dựng SystemLogEntry qua reflection (read model không có factory công khai).</summary>
    private static SystemLogEntry BuildLog(
        Guid? tenantId, string level, string source, string message,
        DateTimeOffset occurredAt, int? statusCode = null, string? path = null, string? category = null)
    {
        var ctor = typeof(SystemLogEntry).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null)!;
        var entry = (SystemLogEntry)ctor.Invoke(null);
        Set(entry, nameof(SystemLogEntry.TenantId), tenantId);
        Set(entry, nameof(SystemLogEntry.Level), level);
        Set(entry, nameof(SystemLogEntry.Source), source);
        Set(entry, nameof(SystemLogEntry.Message), message);
        Set(entry, nameof(SystemLogEntry.OccurredAt), occurredAt);
        Set(entry, nameof(SystemLogEntry.StatusCode), statusCode);
        Set(entry, nameof(SystemLogEntry.Path), path);
        Set(entry, nameof(SystemLogEntry.Category), category);
        Set(entry, nameof(SystemLogEntry.Method), "GET");
        return entry;
    }

    private static void Set(SystemLogEntry target, string name, object? value) =>
        typeof(SystemLogEntry).GetProperty(name)!.SetValue(target, value);

    private async Task SeedLogAsync(SystemLogEntry entry)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SystemLogs.Add(entry);
        await db.SaveChangesAsync();
    }

    private async Task SeedStatsAsync(DateTimeOffset bucketHour, Guid tenantId, string statusClass, long count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RequestStatsHourly.Add(RequestStatsHourly.Create(bucketHour, tenantId, statusClass, count));
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsLogs_WithSummaryCounts()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedLogAsync(BuildLog(tenantId, "Error", "api", "Loi nghiem trong", now.AddMinutes(-5), statusCode: 500));
        await SeedLogAsync(BuildLog(tenantId, "Warning", "api", "Canh bao nhe", now.AddMinutes(-4), statusCode: 404));

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/system-logs", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("summary").GetProperty("errors24h").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("summary").GetProperty("warnings24h").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task List_FilterByLevel_ReturnsOnlyMatching()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedLogAsync(BuildLog(tenantId, "Error", "api", "Chi error", now.AddMinutes(-3)));
        await SeedLogAsync(BuildLog(tenantId, "Information", "api", "Chi info", now.AddMinutes(-2)));

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/admin/system-logs?level=Error", UriKind.Relative));

        body.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("level").GetString() == "Error");
    }

    [Fact]
    public async Task List_FilterByStatusGroup_Splits4xxAnd5xx()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedLogAsync(BuildLog(tenantId, "Error", "api", "Client error", now.AddMinutes(-6), statusCode: 404));
        await SeedLogAsync(BuildLog(tenantId, "Error", "api", "Server error", now.AddMinutes(-5), statusCode: 503));

        var fourXx = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/admin/system-logs?statusGroup=4xx", UriKind.Relative));
        fourXx.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("statusCode").GetInt32())
            .Should().OnlyContain(code => code >= 400 && code < 500);

        var fiveXx = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/admin/system-logs?statusGroup=5xx", UriKind.Relative));
        fiveXx.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("statusCode").GetInt32())
            .Should().OnlyContain(code => code >= 500 && code < 600);
    }

    [Fact]
    public async Task List_FilterBySource_ReturnsOnlyThatSource()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        var marker = $"src-{Guid.NewGuid():N}"[..12];
        await SeedLogAsync(BuildLog(tenantId, "Error", marker, "Log nguon rieng", now.AddMinutes(-2)));
        await SeedLogAsync(BuildLog(tenantId, "Error", "nguon-khac", "Log nguon khac", now.AddMinutes(-1)));

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/system-logs?source={marker}", UriKind.Relative));

        body.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("source").GetString() == marker);
    }

    [Fact]
    public async Task List_CursorPagination_DropsTotalAndSummaryOnNextPage()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
            await SeedLogAsync(BuildLog(tenantId, "Error", "api", $"Log phan trang {i}", now.AddMinutes(-10 + i)));

        var page1 = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/admin/system-logs?pageSize=2", UriKind.Relative));
        page1.GetProperty("items").GetArrayLength().Should().Be(2);
        page1.GetProperty("total").ValueKind.Should().Be(JsonValueKind.Number);
        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNull();

        var page2 = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/system-logs?pageSize=2&cursor={Uri.EscapeDataString(cursor!)}", UriKind.Relative));
        page2.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        page2.GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null);
        page2.GetProperty("summary").GetProperty("errors24h").GetInt32().Should().Be(0,
            "trang cursor không đếm lại summary");
    }

    // ------------------------------------------------------------------
    // GET detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsDetail_WithExceptionAndProperties()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var entry = BuildLog(tenantId, "Error", "api", "Log chi tiet", DateTimeOffset.UtcNow.AddMinutes(-2),
            statusCode: 500, path: "/api/test");
        await SeedLogAsync(entry);

        // Đọc id do InMemory sinh sau khi lưu.
        long id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = await db.SystemLogs.IgnoreQueryFilters()
                .Where(l => l.Message == "Log chi tiet")
                .Select(l => l.Id).FirstAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/system-logs/{id}", UriKind.Relative));

        body.GetProperty("id").GetInt64().Should().Be(id);
        body.GetProperty("message").GetString().Should().Be("Log chi tiet");
        body.GetProperty("path").GetString().Should().Be("/api/test");
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/system-logs/999999999", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("system_log_not_found");
    }

    // ------------------------------------------------------------------
    // GET stats/hourly
    // ------------------------------------------------------------------

    [Fact]
    public async Task StatsHourly_AggregatesByStatusClass()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var bucket = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedStatsAsync(bucket, tenantId, "2xx", 10);
        await SeedStatsAsync(bucket, tenantId, "4xx", 3);
        await SeedStatsAsync(bucket, tenantId, "5xx", 2);

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/admin/system-logs/stats/hourly?hours=24", UriKind.Relative));

        var point = body.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("ok2xx").GetInt64() >= 10);
        point.GetProperty("client4xx").GetInt64().Should().BeGreaterThanOrEqualTo(3);
        point.GetProperty("server5xx").GetInt64().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task StatsHourly_ClampsHours_ReturnsOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // hours vượt trần bị kẹp về 168 — vẫn 200 chứ không lỗi binding.
        var response = await client.GetAsync(new Uri("/api/admin/system-logs/stats/hourly?hours=99999", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
