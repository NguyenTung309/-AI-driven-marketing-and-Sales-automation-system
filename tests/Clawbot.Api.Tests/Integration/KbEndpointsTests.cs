using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/kb — module CRUD, version, upload, test case, accuracy dashboard.
/// Admin có kb:read + kb:write (RbacSeeder). Các endpoint chạy ngầm qua IJobLauncher
/// (HangfireJobLauncher → SQL storage thật) trả 202 + jobId; test host không có Qdrant
/// nên chỉ assert job được launch, không chờ kết quả embed/deploy.
/// Delete draft version an toàn: DeleteVectorsAsync thoát sớm khi version.Embedding trống.
/// </summary>
public sealed class KbEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public KbEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static string UniqueCode() => $"kb-{Guid.NewGuid():N}"[..12];

    /// <summary>Tạo module qua POST endpoint — trả về id để dùng cho các bước sau.</summary>
    private static async Task<Guid> CreateModuleAsync(HttpClient client, string? code = null, string name = "Module Test")
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/kb/modules/", UriKind.Relative), new
        {
            code = code ?? UniqueCode(),
            name,
            description = (string?)null,
            ownerRole = (string?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private static async Task<Guid> CreateVersionAsync(HttpClient client, Guid moduleId, string content)
    {
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions", UriKind.Relative),
            new { contentMd = content });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    /// <summary>Seed thẳng bản deployed (Deploy qua domain) cho các nhánh cần trạng thái deployed.</summary>
    private async Task<Guid> SeedDeployedVersionAsync(Guid moduleId, string content = "Noi dung deployed")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var next = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == moduleId).MaxAsync(v => (int?)v.Version) ?? 0;
        var version = KbVersion.Create(moduleId, next + 1, content, DateTimeOffset.UtcNow);
        version.Deploy(DateTimeOffset.UtcNow);
        db.KbVersions.Add(version);
        await db.SaveChangesAsync();
        return version.Id;
    }

    // ------------------------------------------------------------------
    // Module CRUD
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateModule_RoundTrips_AndAppearsInListAndDetail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = UniqueCode();
        var moduleId = await CreateModuleAsync(client, code, "Module Tao Moi");

        var detail = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}", UriKind.Relative));
        detail.GetProperty("code").GetString().Should().Be(code);
        detail.GetProperty("name").GetString().Should().Be("Module Tao Moi");
        detail.GetProperty("versionCount").GetInt32().Should().Be(0);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/kb/modules/", UriKind.Relative));
        list.EnumerateArray().Any(m => Guid.Parse(m.GetProperty("id").GetString()!) == moduleId).Should().BeTrue();
    }

    [Fact]
    public async Task CreateModule_MissingCodeOrName_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/kb/modules/", UriKind.Relative),
            new { code = "", name = "", description = (string?)null, ownerRole = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("code_and_name_required");
    }

    [Fact]
    public async Task CreateModule_DuplicateCode_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = UniqueCode();
        await CreateModuleAsync(client, code);

        var duplicate = await client.PostAsJsonAsync(new Uri("/api/kb/modules/", UriKind.Relative),
            new { code, name = "Trung ma", description = (string?)null, ownerRole = (string?)null });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("code_exists");
    }

    [Fact]
    public async Task GetModule_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/kb/modules/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateModule_ChangesNameAndDescription()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        var update = await client.PutAsJsonAsync(
            new Uri($"/api/kb/modules/{moduleId}", UriKind.Relative),
            new { name = "Ten moi", description = "Mo ta moi", ownerRole = "sale" });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}", UriKind.Relative));
        detail.GetProperty("name").GetString().Should().Be("Ten moi");
    }

    [Fact]
    public async Task UpdateModule_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid()}", UriKind.Relative),
            new { name = "x", description = (string?)null, ownerRole = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ArchiveModule_HidesFromListAndDetail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        var archive = await client.PostAsync(
            new Uri($"/api/kb/modules/{moduleId}/archive", UriKind.Relative), content: null);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Archive lần hai là no-op idempotent.
        (await client.PostAsync(new Uri($"/api/kb/modules/{moduleId}/archive", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync(new Uri($"/api/kb/modules/{moduleId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/kb/modules/", UriKind.Relative));
        list.EnumerateArray().Any(m => Guid.Parse(m.GetProperty("id").GetString()!) == moduleId).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Versions
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateVersion_IncrementsVersion_AndListsDescending()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        await CreateVersionAsync(client, moduleId, "Phien ban mot");
        await CreateVersionAsync(client, moduleId, "Phien ban hai");

        var versions = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}/versions", UriKind.Relative));
        versions.GetArrayLength().Should().Be(2);
        versions[0].GetProperty("version").GetInt32().Should().Be(2, "list sắp mới nhất trước");
        versions[1].GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task CreateVersion_EmptyContent_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions", UriKind.Relative),
            new { contentMd = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content_required");
    }

    [Fact]
    public async Task ListVersions_UnknownModule_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid()}/versions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVersionDetail_ReturnsContent_UnknownReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        var versionId = await CreateVersionAsync(client, moduleId, "Noi dung chi tiet");

        var detail = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}/versions/{versionId}", UriKind.Relative));
        detail.GetProperty("contentMd").GetString().Should().Be("Noi dung chi tiet");

        (await client.GetAsync(new Uri($"/api/kb/modules/{moduleId}/versions/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteVersion_Draft_Succeeds()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        var versionId = await CreateVersionAsync(client, moduleId, "Ban nhap de xoa");

        // includeRollbackTarget là query param bool bắt buộc (không nullable, không default).
        var response = await client.DeleteAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions/{versionId}?includeRollbackTarget=false", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var versions = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}/versions", UriKind.Relative));
        versions.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DeleteVersion_Deployed_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        var deployedId = await SeedDeployedVersionAsync(moduleId);

        var response = await client.DeleteAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions/{deployedId}?includeRollbackTarget=false", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("kb.version_deployed_not_deletable");
    }

    [Fact]
    public async Task DiffVersions_ComputesAddedAndRemovedLines()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        await CreateVersionAsync(client, moduleId, "dong giu lai\ndong bi xoa");
        await CreateVersionAsync(client, moduleId, "dong giu lai\ndong them moi");

        var diff = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}/diff?fromVersion=1&toVersion=2", UriKind.Relative));

        diff.GetProperty("linesAdded").GetInt32().Should().Be(1);
        diff.GetProperty("linesRemoved").GetInt32().Should().Be(1);
        diff.GetProperty("unifiedDiff").GetString().Should().Contain("+dong them moi").And.Contain("-dong bi xoa");
    }

    [Fact]
    public async Task DiffVersions_MissingParamsOrUnknownVersion_AreRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        await CreateVersionAsync(client, moduleId, "chi mot ban");

        (await client.GetAsync(new Uri($"/api/kb/modules/{moduleId}/diff", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(new Uri($"/api/kb/modules/{moduleId}/diff?fromVersion=1&toVersion=9", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Deploy / rollback — chạy ngầm qua job
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeployVersion_LaunchesJob_ReturnsAccepted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        var versionId = await CreateVersionAsync(client, moduleId, "Noi dung de deploy");

        var response = await client.PostAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions/{versionId}/deploy", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeployVersion_UnknownModuleOrVersion_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        (await client.PostAsync(new Uri($"/api/kb/modules/{Guid.NewGuid()}/versions/{Guid.NewGuid()}/deploy", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync(new Uri($"/api/kb/modules/{moduleId}/versions/{Guid.NewGuid()}/deploy", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RollbackVersion_LaunchesJob_ReturnsAccepted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);
        var versionId = await CreateVersionAsync(client, moduleId, "Noi dung de rollback");

        var response = await client.PostAsync(
            new Uri($"/api/kb/modules/{moduleId}/versions/{versionId}/rollback", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ------------------------------------------------------------------
    // Test cases + accuracy
    // ------------------------------------------------------------------

    [Fact]
    public async Task AddTestCase_RoundTrips_InList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        var create = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{moduleId}/test-cases", UriKind.Relative),
            new { question = "Gia san pham bao nhieu?", expectedAnswer = "San pham co gia 100k" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/kb/modules/{moduleId}/test-cases", UriKind.Relative));
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("question").GetString().Should().Be("Gia san pham bao nhieu?");
    }

    [Fact]
    public async Task AddTestCase_MissingFieldsOrUnknownModule_AreRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        (await client.PostAsJsonAsync(new Uri($"/api/kb/modules/{moduleId}/test-cases", UriKind.Relative),
            new { question = "", expectedAnswer = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(new Uri($"/api/kb/modules/{Guid.NewGuid()}/test-cases", UriKind.Relative),
            new { question = "q", expectedAnswer = "a" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GenerateTestCases_WithoutContent_IsRejected_WithContent_LaunchesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        (await client.PostAsJsonAsync(new Uri($"/api/kb/modules/{moduleId}/test-cases/generate", UriKind.Relative), new { count = (int?)null }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await CreateVersionAsync(client, moduleId, "Noi dung de sinh test case");
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/kb/modules/{moduleId}/test-cases/generate", UriKind.Relative),
            new { count = (int?)5 });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RunTest_ValidatesPreconditions_ThenLaunchesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        // Chua co ban deployed.
        (await client.PostAsync(new Uri($"/api/kb/modules/{moduleId}/test", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await SeedDeployedVersionAsync(moduleId);
        // Co deployed nhung chua co test case.
        var noCases = await client.PostAsync(new Uri($"/api/kb/modules/{moduleId}/test", UriKind.Relative), content: null);
        noCases.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noCases.Content.ReadAsStringAsync()).Should().Contain("no_test_cases");

        await client.PostAsJsonAsync(new Uri($"/api/kb/modules/{moduleId}/test-cases", UriKind.Relative),
            new { question = "Cau hoi?", expectedAnswer = "Tra loi" });
        (await client.PostAsync(new Uri($"/api/kb/modules/{moduleId}/test", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AccuracyDashboard_IncludesModuleWithLatestVersion()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = UniqueCode();
        var moduleId = await CreateModuleAsync(client, code);
        await CreateVersionAsync(client, moduleId, "Noi dung ban 1");

        var summaries = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/kb/accuracy", UriKind.Relative));

        var row = summaries.EnumerateArray()
            .First(s => Guid.Parse(s.GetProperty("kbModuleId").GetString()!) == moduleId);
        row.GetProperty("code").GetString().Should().Be(code);
        row.GetProperty("latestVersion").GetInt32().Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Upload file → draft version
    // ------------------------------------------------------------------

    [Fact]
    public async Task Upload_TextFile_CreatesDraftVersion()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Noi dung tai len tu tep"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "ghi-chu.txt");

        var response = await client.PostAsync(
            new Uri($"/api/kb/modules/{moduleId}/upload", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("contentMd").GetString().Should().Contain("Noi dung tai len tu tep");
        body.GetProperty("version").GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Upload_UnsupportedFormat_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var moduleId = await CreateModuleAsync(client);

        using var form = new MultipartFormDataContent();
        // Byte nhị phân có 0x00 — không phải text, không phải PDF/ZIP → hết ứng viên tên tệp.
        var fileContent = new ByteArrayContent([0x00, 0x01, 0x02, 0x03]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", "du-lieu.exe");

        var response = await client.PostAsync(
            new Uri($"/api/kb/modules/{moduleId}/upload", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("unsupported_format");
    }

    [Fact]
    public async Task Upload_UnknownModule_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Noi dung"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "tep.txt");

        var response = await client.PostAsync(
            new Uri($"/api/kb/modules/{Guid.NewGuid()}/upload", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Classify upload — stage file lên storage + launch job
    // ------------------------------------------------------------------

    [Fact]
    public async Task ClassifyUpload_NoFiles_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Form không có file nào: binding IFormFileCollection từ chối trước khi vào handler.
        using var form = new MultipartFormDataContent();
        var response = await client.PostAsync(new Uri("/api/kb/classify-upload", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClassifyUpload_StagesFileAndLaunchesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("Tai lieu san pham moi"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "files", "tai-lieu.txt");

        var response = await client.PostAsync(
            new Uri("/api/kb/classify-upload?autoDeploy=false&autoTest=false", UriKind.Relative), form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetString().Should().NotBeNullOrEmpty();
    }
}
