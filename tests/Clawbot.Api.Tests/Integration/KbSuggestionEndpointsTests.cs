using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/kb/suggestions — duyệt tay đề xuất tri thức (ai-self-learning-memory 1.6).
/// Admin có kb:read + kb:write (RbacSeeder) nên chạy được cả 3 endpoint.
/// Nhánh approve gọi KbSuggestionMaterializer → KbDeployService.EmbedAndUpsertAsync (Qdrant thật);
/// test host không có Qdrant nên bước embed ném lỗi → endpoint trả 502 nhưng suggestion
/// đã Approve + KbVersion đã persist TRƯỚC bước embed, nên assert DB state thay vì status code.
/// </summary>
public sealed class KbSuggestionEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public KbSuggestionEndpointsTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>Seed một suggestion pending (op add — không cần module đích).</summary>
    private async Task<Guid> SeedSuggestionAsync(Guid tenantId, string title, string? op = null, Guid? targetModuleId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = KbSuggestion.Create(
            tenantId,
            op ?? KbSuggestion.OpAdd,
            targetModuleId,
            title,
            $"Noi dung tri thuc cho {title}",
            "Ly do de xuat",
            "[]",
            $"hash-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);
        db.KbSuggestions.Add(suggestion);
        await db.SaveChangesAsync();
        return suggestion.Id;
    }

    private async Task<Guid> SeedModuleAsync(Guid tenantId, string code, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var module = KbModule.Create(tenantId, code, name, DateTimeOffset.UtcNow);
        db.KbModules.Add(module);
        await db.SaveChangesAsync();
        return module.Id;
    }

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        // Admin bootstrap thuộc tenant "default" — cùng pattern AdminInboxEndpointsTests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsSuggestions_WithTotal()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id1 = await SeedSuggestionAsync(tenantId, "De xuat mot");
        var id2 = await SeedSuggestionAsync(tenantId, "De xuat hai");

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/kb/suggestions/", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!))
            .ToList();
        ids.Should().Contain(id1).And.Contain(id2);
    }

    [Fact]
    public async Task List_FiltersByStatus_AndResolvesModuleName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var moduleId = await SeedModuleAsync(tenantId, $"mod-{Guid.NewGuid():N}"[..12], "Module Muc Tieu");
        var updateId = await SeedSuggestionAsync(tenantId, "Cap nhat module", KbSuggestion.OpUpdate, moduleId);

        // Reject mot suggestion de co row khac status.
        var rejectId = await SeedSuggestionAsync(tenantId, "De xuat bi tu choi");
        var reject = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{rejectId}/reject", UriKind.Relative),
            new { reason = "Khong phu hop" });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/kb/suggestions/?status=rejected", UriKind.Relative));
        filtered.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("status").GetString() == "rejected");

        // Khong filter: suggestion update phai kem ten module dich.
        var all = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/kb/suggestions/", UriKind.Relative));
        var target = all.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == updateId);
        target.GetProperty("targetModuleName").GetString().Should().Be("Module Muc Tieu");
    }

    [Fact]
    public async Task List_InvalidPagination_ClampsToDefaults()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/kb/suggestions/?page=0&pageSize=999", UriKind.Relative));

        body.GetProperty("page").GetInt32().Should().Be(1, "page < 1 phải kẹp về 1");
        body.GetProperty("pageSize").GetInt32().Should().Be(50, "pageSize > 200 phải kẹp về mặc định 50");
    }

    // ------------------------------------------------------------------
    // POST approve
    // ------------------------------------------------------------------

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{Guid.NewGuid()}/approve", UriKind.Relative),
            new { contentMd = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Approve_Pending_MarksApprovedAndCreatesVersion()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedSuggestionAsync(tenantId, "Huong Dan Thanh Toan");

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/approve", UriKind.Relative),
            new { contentMd = "Noi dung da duoc nguoi duyet chinh sua" });

        // Qdrant khong co trong test host: embed fail -> 502; neu Qdrant san sang -> 200.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadGateway);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.KbSuggestions.IgnoreQueryFilters().FirstAsync(s => s.Id == id);
        suggestion.Status.Should().Be(KbSuggestion.StatusApproved);
        suggestion.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeHuman);
        suggestion.ContentMd.Should().Be("Noi dung da duoc nguoi duyet chinh sua", "người duyệt được sửa nội dung trước khi deploy");

        // Materializer tạo module từ slug hoá title + version deploy TRƯỚC bước embed.
        var module = await db.KbModules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Code == "huong-dan-thanh-toan");
        module.Should().NotBeNull("op add phải tạo module mới từ title slug hoá");
        var version = await db.KbVersions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.KbModuleId == module!.Id);
        version.Should().NotBeNull();
        version!.Status.Should().Be("deployed");
    }

    [Fact]
    public async Task Approve_AlreadyDecided_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedSuggestionAsync(tenantId, "De xuat duyet hai lan");

        await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/approve", UriKind.Relative),
            new { contentMd = (string?)null });

        var second = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/approve", UriKind.Relative),
            new { contentMd = (string?)null });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("suggestion_already_decided");
    }

    // ------------------------------------------------------------------
    // POST reject
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reject_EmptyReason_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedSuggestionAsync(tenantId, "Thieu ly do");

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/reject", UriKind.Relative),
            new { reason = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("reject_reason_required");
    }

    [Fact]
    public async Task Reject_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{Guid.NewGuid()}/reject", UriKind.Relative),
            new { reason = "Ly do" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reject_Pending_MarksRejectedWithReason()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedSuggestionAsync(tenantId, "De xuat bi tu choi boi test");

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/reject", UriKind.Relative),
            new { reason = "Trung lap voi tri thuc hien co" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be(KbSuggestion.StatusRejected);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suggestion = await db.KbSuggestions.IgnoreQueryFilters().FirstAsync(s => s.Id == id);
        suggestion.RejectedReason.Should().Be("Trung lap voi tri thuc hien co");
        suggestion.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reject_AlreadyDecided_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedSuggestionAsync(tenantId, "Tu choi hai lan");

        await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/reject", UriKind.Relative),
            new { reason = "Lan mot" });

        var second = await client.PostAsJsonAsync(
            new Uri($"/api/kb/suggestions/{id}/reject", UriKind.Relative),
            new { reason = "Lan hai" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
