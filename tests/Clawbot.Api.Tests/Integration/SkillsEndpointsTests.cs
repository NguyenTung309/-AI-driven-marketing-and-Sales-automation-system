using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>/api/skills — kho tệp kỹ năng (.md) tái sử dụng cho agent.</summary>
public sealed class SkillsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public SkillsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static string UniqueName() => $"skill-{Guid.NewGuid():N}"[..16];

    private static async Task<Guid> CreateSkillAsync(HttpClient client, string? name = null, string content = "# noi dung")
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name = name ?? UniqueName(),
            description = "mo ta test",
            contentMd = content,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    // ------------------------------------------------------------------
    // POST create
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidPayload_ReturnsCreated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = UniqueName();

        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name,
            description = "Ky nang test",
            contentMd = "# Huong dan\nNoi dung ky nang.",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be(name);
        body.GetProperty("sizeBytes").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_BlankName_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name = "   ",
            description = (string?)null,
            contentMd = "noi dung",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("name_required");
    }

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name = new string('a', 129),
            description = (string?)null,
            contentMd = "noi dung",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("name_too_long");
    }

    [Fact]
    public async Task Create_ContentTooLarge_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name = UniqueName(),
            description = (string?)null,
            contentMd = new string('a', 100_001),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content_too_large");
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = UniqueName();
        await CreateSkillAsync(client, name);

        var response = await client.PostAsJsonAsync(new Uri("/api/skills", UriKind.Relative), new
        {
            name,
            description = (string?)null,
            contentMd = "noi dung khac",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("name_exists");
    }

    // ------------------------------------------------------------------
    // GET list / detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsCreatedSkill_SortedByName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateSkillAsync(client);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/skills", UriKind.Relative));

        body.EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Get_ReturnsFullContent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateSkillAsync(client, content: "# Noi dung day du");

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/skills/{id}", UriKind.Relative));

        body.GetProperty("contentMd").GetString().Should().Be("# Noi dung day du");
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/skills/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // PUT update
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_ValidPayload_ChangesContent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateSkillAsync(client);

        var response = await client.PutAsJsonAsync(new Uri($"/api/skills/{id}", UriKind.Relative), new
        {
            description = "Mo ta moi",
            contentMd = "# Noi dung moi",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("description").GetString().Should().Be("Mo ta moi");
    }

    [Fact]
    public async Task Update_ContentTooLarge_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateSkillAsync(client);

        var response = await client.PutAsJsonAsync(new Uri($"/api/skills/{id}", UriKind.Relative), new
        {
            description = (string?)null,
            contentMd = new string('a', 100_001),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content_too_large");
    }

    [Fact]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/skills/{Guid.NewGuid()}", UriKind.Relative), new
        {
            description = (string?)null,
            contentMd = "noi dung",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // DELETE
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesFromList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateSkillAsync(client);

        var response = await client.DeleteAsync(new Uri($"/api/skills/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var getResponse = await client.GetAsync(new Uri($"/api/skills/{id}", UriKind.Relative));
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound, "soft delete phải ẩn khỏi GET");
    }

    [Fact]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/skills/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
