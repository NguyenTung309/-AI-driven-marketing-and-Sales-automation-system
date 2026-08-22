using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Contacts;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/contacts/*: thuần EF. GET/DELETE {id}/memories nằm trong RelationalOnlyRoutes của
/// ParameterisedRouteSweepTests vì DeleteAllMemoriesAsync dùng ExecuteDeleteAsync (không chắc chạy
/// được trên InMemory) — kiểm chứng trực tiếp ở đây thay vì đoán theo sweep.
/// </summary>
public sealed class ContactsEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> DefaultTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> SeedContactAsync(Guid tenantId, string displayName = "Khách test")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = Contact.Create(tenantId, displayName, DateTimeOffset.UtcNow);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        return contact.Id;
    }

    private async Task<Guid> SeedMemoryAsync(Guid tenantId, Guid contactId, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var memory = ContactMemory.Create(
            tenantId, contactId, "Học viên trình độ trung cấp", ContactMemory.CategoryProfile,
            0.9m, null, DateTimeOffset.UtcNow);
        db.ContactMemories.Add(memory);
        if (!active)
            memory.Supersede(null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        return memory.Id;
    }

    // ------------------------------------------------------------------
    // Memories
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListMemories_ReturnsOnlyActiveOnes()
    {
        var tenantId = await DefaultTenantIdAsync();
        var contactId = await SeedContactAsync(tenantId);
        var activeId = await SeedMemoryAsync(tenantId, contactId, active: true);
        var inactiveId = await SeedMemoryAsync(tenantId, contactId, active: false);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/contacts/{contactId:D}/memories", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<ContactsEndpoints.ContactMemoryDto>>();
        items!.Select(m => m.Id).Should().Contain(activeId);
        items!.Select(m => m.Id).Should().NotContain(inactiveId);
    }

    [Fact]
    public async Task ListMemories_UnknownContact_ReturnsEmptyList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/contacts/{Guid.NewGuid():D}/memories", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<ContactsEndpoints.ContactMemoryDto>>();
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMemory_UnknownId_ReturnsNotFound()
    {
        var tenantId = await DefaultTenantIdAsync();
        var contactId = await SeedContactAsync(tenantId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/contacts/{contactId:D}/memories/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMemory_KnownId_SupersedesRatherThanHardDelete()
    {
        var tenantId = await DefaultTenantIdAsync();
        var contactId = await SeedContactAsync(tenantId);
        var memoryId = await SeedMemoryAsync(tenantId, contactId, active: true);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/contacts/{contactId:D}/memories/{memoryId:D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var memory = await db.ContactMemories.IgnoreQueryFilters().FirstAsync(m => m.Id == memoryId);
        memory.IsActive.Should().BeFalse("supersede hạ cờ, không xóa cứng");
    }

    // DeleteAllMemoriesAsync (bulk DELETE /{id}/memories) dùng ExecuteDeleteAsync — InMemory provider
    // không dịch được biểu thức này (InvalidOperationException khi build LINQ), dù SQL Server chạy
    // bình thường. Cùng lý do route này đã bị loại khỏi ParameterisedRouteSweepTests.RelationalOnlyRoutes.
    // Không có cách test happy-path của nhánh này qua harness InMemory hiện tại.

    // ------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------

    [Fact]
    public async Task Export_UnknownContact_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/contacts/{Guid.NewGuid():D}/export.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_KnownContact_ReturnsJsonFile()
    {
        var tenantId = await DefaultTenantIdAsync();
        var contactId = await SeedContactAsync(tenantId, "Khách xuất dữ liệu");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/contacts/{contactId:D}/export.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        // ExportJsonOptions không set Encoder -> Unicode escape thành \uXXXX; parse JSON thay vì so
        // khớp chuỗi thô để không phụ thuộc cách encode.
        var doc = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        doc.GetProperty("contact").GetProperty("displayName").GetString().Should().Be("Khách xuất dữ liệu");
        doc.GetProperty("contact").GetProperty("id").GetGuid().Should().Be(contactId);
    }

    // ------------------------------------------------------------------
    // Merge
    // ------------------------------------------------------------------

    [Fact]
    public async Task Merge_SameSourceAndTarget_IsRejected()
    {
        var tenantId = await DefaultTenantIdAsync();
        var contactId = await SeedContactAsync(tenantId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/contacts/merge", UriKind.Relative),
            new MergeContactsRequest(contactId, contactId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Merge_UnknownContacts_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/contacts/merge", UriKind.Relative),
            new MergeContactsRequest(Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Merge_ValidContacts_TransfersExternalIdsAndSoftDeletesSource()
    {
        var tenantId = await DefaultTenantIdAsync();
        var sourceId = await SeedContactAsync(tenantId, "Nguồn");
        var targetId = await SeedContactAsync(tenantId, "Đích");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ContactExternalIds.Add(ContactExternalId.Create(sourceId, "zalo", "zalo-1", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/contacts/merge", UriKind.Relative),
            new MergeContactsRequest(sourceId, targetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"external_ids_transferred\":1");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transferred = await verifyDb.ContactExternalIds.FirstAsync(e => e.ExternalId == "zalo-1");
        transferred.ContactId.Should().Be(targetId);

        var source = await verifyDb.Contacts.IgnoreQueryFilters().FirstAsync(c => c.Id == sourceId);
        source.DeletedAt.Should().NotBeNull("nguồn phải bị soft-delete sau khi gộp");
    }

    [Fact]
    public async Task Merge_DuplicateExternalIdOnTarget_RemovesSourceDuplicateInsteadOfTransferring()
    {
        var tenantId = await DefaultTenantIdAsync();
        var sourceId = await SeedContactAsync(tenantId, "Nguồn");
        var targetId = await SeedContactAsync(tenantId, "Đích");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ContactExternalIds.Add(ContactExternalId.Create(sourceId, "zalo", "zalo-dup", DateTimeOffset.UtcNow));
            db.ContactExternalIds.Add(ContactExternalId.Create(targetId, "zalo", "zalo-dup", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/contacts/merge", UriKind.Relative),
            new MergeContactsRequest(sourceId, targetId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"external_ids_transferred\":0");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.ContactExternalIds.CountAsync(e => e.ExternalId == "zalo-dup")).Should().Be(1);
    }
}
