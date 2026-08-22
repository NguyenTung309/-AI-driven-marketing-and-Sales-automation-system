using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/docs — template CRUD (thuần EF, soft delete qua DeletedAt), sinh tài liệu chạy ngầm qua
/// IJobLauncher (không cần AgentService/gRPC thật vì handler chỉ đẩy job vào hàng đợi), và beacon
/// mở tài liệu (AllowAnonymous). Nhánh DownloadAsync thành công cần MinIO/LocalDocumentStorage có
/// file thật nên KHÔNG cover ở đây — chỉ cover 404 sớm trước khi chạm IDocumentStorage.
/// </summary>
public sealed class DocumentsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public DocumentsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<DocumentTemplate> SeedTemplateAsync(
        Guid tenantId, string? code = null, string docType = "quote", string templateHtml = "<p>{{customer_name}}</p>")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tpl = DocumentTemplate.Create(
            tenantId, code ?? $"tpl-{Guid.NewGuid():N}"[..16], docType, templateHtml, DateTimeOffset.UtcNow);
        db.DocumentTemplates.Add(tpl);
        await db.SaveChangesAsync();
        return tpl;
    }

    private async Task<GeneratedDocument> SeedGeneratedDocumentAsync(
        Guid tenantId, Guid templateId, string fileUrl, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = GeneratedDocument.Create(tenantId, templateId, fileUrl, createdAt);
        db.GeneratedDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    // ------------------------------------------------------------------
    // Templates: POST create
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateTemplate_MissingCode_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/templates", UriKind.Relative),
            new CreateDocumentTemplateRequest("", "quote", "<p>hello</p>"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTemplate_MissingTemplateHtml_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/templates", UriKind.Relative),
            new CreateDocumentTemplateRequest($"tpl-{Guid.NewGuid():N}", "quote", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTemplate_DuplicateCode_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var existing = await SeedTemplateAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/templates", UriKind.Relative),
            new CreateDocumentTemplateRequest(existing.Code, "quote", "<p>khac noi dung</p>"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTemplate_Valid_ReturnsCreated_WithDefaultDocType()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"tpl-{Guid.NewGuid():N}"[..16];

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/templates", UriKind.Relative),
            new CreateDocumentTemplateRequest(code, "", "<p>Xin chao {{customer_name}}</p>"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<DocumentTemplateDto>();
        body.Should().NotBeNull();
        body!.Code.Should().Be(code);
        // DocType rỗng -> mặc định "quote" (đọc CreateTemplateAsync).
        body.DocType.Should().Be("quote");
        body.TemplateHtml.Should().Be("<p>Xin chao {{customer_name}}</p>");
    }

    [Fact]
    public async Task CreateTemplate_Valid_WithFields_ReturnsFieldsInDto()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"tpl-{Guid.NewGuid():N}"[..16];

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/templates", UriKind.Relative),
            new CreateDocumentTemplateRequest(code, "brochure", "<p>{{price}}</p>",
                [new TemplateFieldDto("price", "Gia", "currency", true, null, "1000000")]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<DocumentTemplateDto>();
        body!.DocType.Should().Be("brochure");
        body.Fields.Should().ContainSingle(f => f.Key == "price" && f.Type == "currency" && f.Required);
    }

    // ------------------------------------------------------------------
    // Templates: GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListTemplates_ReturnsCreatedTemplate()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/docs/templates", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var item = body.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == tpl.Id);
        item.GetProperty("code").GetString().Should().Be(tpl.Code);
    }

    // ------------------------------------------------------------------
    // Templates: PUT update
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateTemplate_BlankTemplateHtml_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/docs/templates/{tpl.Id}", UriKind.Relative),
            new UpdateDocumentTemplateRequest("quote", "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTemplate_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/docs/templates/{Guid.NewGuid()}", UriKind.Relative),
            new UpdateDocumentTemplateRequest("quote", "<p>noi dung moi</p>"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateTemplate_Valid_ReturnsNoContent_AndPersists()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/docs/templates/{tpl.Id}", UriKind.Relative),
            new UpdateDocumentTemplateRequest("slide", "<p>noi dung moi</p>"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/docs/templates", UriKind.Relative));
        var item = body.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == tpl.Id);
        item.GetProperty("docType").GetString().Should().Be("slide");
        item.GetProperty("templateHtml").GetString().Should().Be("<p>noi dung moi</p>");
    }

    // ------------------------------------------------------------------
    // Templates: DELETE
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteTemplate_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/docs/templates/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTemplate_Valid_ReturnsNoContent_AndHiddenFromList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);

        var response = await client.DeleteAsync(new Uri($"/api/docs/templates/{tpl.Id}", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/docs/templates", UriKind.Relative));
        body.GetProperty("items").EnumerateArray()
            .Should().NotContain(i => Guid.Parse(i.GetProperty("id").GetString()!) == tpl.Id);
    }

    // ------------------------------------------------------------------
    // GET /generated
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListGenerated_ReturnsSeededDocuments_OrderedByCreatedAtDesc()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);
        var older = await SeedGeneratedDocumentAsync(
            tenantId, tpl.Id, "/generated-docs/older.pdf", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = await SeedGeneratedDocumentAsync(
            tenantId, tpl.Id, "/generated-docs/newer.pdf", DateTimeOffset.UtcNow);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/docs/generated", UriKind.Relative));

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        ids.Should().Contain([older.Id, newer.Id]);
        // sắp CreatedAt desc -> newer đứng trước older trong danh sách.
        ids.IndexOf(newer.Id).Should().BeLessThan(ids.IndexOf(older.Id));
    }

    // ------------------------------------------------------------------
    // POST /generate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_BlankTemplateCode_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate", UriKind.Relative),
            new GenerateDocumentRequest("   ", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("templateCode required");
    }

    [Fact]
    public async Task Generate_EmailDelivery_MissingContactAndRecipient_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate", UriKind.Relative),
            new GenerateDocumentRequest("some-template", null, null, "email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("recipientEmail or contactId required for email delivery");
    }

    [Fact]
    public async Task Generate_EmailDelivery_InvalidRecipientEmail_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate", UriKind.Relative),
            new GenerateDocumentRequest("some-template", null, null, "email", "not-an-email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("recipientEmail invalid");
    }

    [Fact]
    public async Task Generate_Valid_ReturnsAccepted_WithJobId()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var tpl = await SeedTemplateAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate", UriKind.Relative),
            new GenerateDocumentRequest(tpl.Code, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------
    // POST /generate-kit
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateKit_NoTemplatesAtAll_ReturnsBadRequest()
    {
        // Dùng factory riêng để tenant chắc chắn không có DocumentTemplate nào can thiệp kết quả.
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate-kit", UriKind.Relative),
            new GenerateDocumentKitRequest(null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Chưa có mẫu tài liệu nào để tạo bộ.");
    }

    [Fact]
    public async Task GenerateKit_EmptyTemplateCodesArray_FallsBackToAllTemplates_ReturnsAccepted()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdForAsync(factory);
        await SeedTemplateForAsync(factory, tenantId);

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate-kit", UriKind.Relative),
            new GenerateDocumentKitRequest([], null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateKit_NullTemplateCodes_UsesExistingTemplate_ReturnsAccepted()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdForAsync(factory);
        await SeedTemplateForAsync(factory, tenantId);

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate-kit", UriKind.Relative),
            new GenerateDocumentKitRequest(null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task GenerateKit_EmailDelivery_MissingTarget_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/docs/generate-kit", UriKind.Relative),
            new GenerateDocumentKitRequest(["some-code"], null, null, "email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("recipientEmail or contactId required for email delivery");
    }

    private static async Task<Guid> GetAdminTenantIdForAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private static async Task SeedTemplateForAsync(ApiTestFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tpl = DocumentTemplate.Create(
            tenantId, $"tpl-{Guid.NewGuid():N}"[..16], "quote", "<p>hello</p>", DateTimeOffset.UtcNow);
        db.DocumentTemplates.Add(tpl);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // GET /{id}/download
    // ------------------------------------------------------------------

    [Fact]
    public async Task Download_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/docs/{Guid.NewGuid()}/download", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // GET /{id}/open.gif — AllowAnonymous, không kiểm tồn tại trước khi trả beacon.
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenBeacon_AnyId_ReturnsTransparentGif_Anonymously()
    {
        // Client KHÔNG gắn bearer token — endpoint AllowAnonymous.
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync(new Uri($"/api/docs/{Guid.NewGuid()}/open.gif", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/gif");
    }
}
