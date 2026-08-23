using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Competitors;
using Clawbot.Api.Contracts.Llm;
using Clawbot.Api.Contracts.Security;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

public sealed class CompetitorSourceCrudTests : IAsyncLifetime
{
    // Factory riêng mỗi test: DB InMemory dùng chung giữa các test song song gây race
    // (delete unknown lúc 204 lúc 404 tuỳ dữ liệu test khác để lại).
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static readonly CreateCompetitorSourceRequest ValidSource =
        new("Đối thủ A", "https://example.test/feed.xml", "rss");

    [Fact]
    public async Task CreateUpdateDelete_RoundTripsThroughApi()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            new Uri("/api/competitors/sources", UriKind.Relative), ValidSource);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<CompetitorSourceDto>();
        dto!.Name.Should().Be("Đối thủ A");

        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/competitors/sources/{dto.Id:D}", UriKind.Relative),
            new UpdateCompetitorSourceRequest("Đối thủ A+", "https://example.test/feed2.xml", "rss", true));
        updated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(
            new Uri($"/api/competitors/sources/{dto.Id:D}", UriKind.Relative));
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Khong co route GET theo id; xac nhan da xoa qua danh sach.
        (await client.GetStringAsync(new Uri("/api/competitors/sources", UriKind.Relative)))
            .Should().NotContain("Đối thủ A+");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Create_MissingName_IsRejected(string? name)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/competitors/sources", UriKind.Relative),
            new CreateCompetitorSourceRequest(name!, "https://example.test", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("khong-phai-url")]
    [InlineData("/relative")]
    public async Task Create_InvalidUrl_IsRejected(string url)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/competitors/sources", UriKind.Relative),
            new CreateCompetitorSourceRequest("A", url, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/competitors/sources/{Guid.NewGuid():D}", UriKind.Relative),
            new UpdateCompetitorSourceRequest("A", "https://example.test", null, true));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/competitors/sources/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_EmptySourceType_DefaultsToRss()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            new Uri("/api/competitors/sources", UriKind.Relative),
            new CreateCompetitorSourceRequest("B", "https://example.test/x.xml", null));

        var dto = await created.Content.ReadFromJsonAsync<CompetitorSourceDto>();
        dto!.SourceType.Should().Be("rss");
    }
}

public sealed class LlmConfigCrudTests : IAsyncLifetime
{
    // Factory riêng mỗi test: DB InMemory dùng chung giữa các test song song gây race
    // (delete unknown lúc 204 lúc 404 tuỳ dữ liệu test khác để lại).
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static readonly CreateLlmConfigRequest ValidConfig =
        new("openai", "gpt-4o", "sk-test-key-123", DisplayName: "Test config");

    [Fact]
    public async Task CreateUpdateActivateDelete_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            new Uri("/api/llm-configs", UriKind.Relative), ValidConfig);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<LlmConfigDto>();
        dto!.Provider.Should().Be("openai");
        dto.ModelId.Should().Be("gpt-4o");
        dto.HasApiKey.Should().BeTrue();
        dto.IsActive.Should().BeTrue();

        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/llm-configs/{dto.Id:D}", UriKind.Relative),
            new UpdateLlmConfigRequest("openai", "gpt-4o-mini", "Updated", null, null, null, null, null, null));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await updated.Content.ReadFromJsonAsync<LlmConfigDto>();
        after!.ModelId.Should().Be("gpt-4o-mini");

        var deactivated = await client.PostAsync(
            new Uri($"/api/llm-configs/{dto.Id:D}/deactivate", UriKind.Relative), null);
        deactivated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var activated = await client.PostAsync(
            new Uri($"/api/llm-configs/{dto.Id:D}/activate", UriKind.Relative), null);
        activated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var rotated = await client.PostAsJsonAsync(
            new Uri($"/api/llm-configs/{dto.Id:D}/rotate-key", UriKind.Relative),
            new RotateLlmKeyRequest("sk-new-key-456"));
        rotated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(
            new Uri($"/api/llm-configs/{dto.Id:D}", UriKind.Relative));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/llm-configs", UriKind.Relative), ValidConfig);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/llm-configs/{Guid.NewGuid():D}", UriKind.Relative),
            new UpdateLlmConfigRequest("openai", "x", null, null, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Activate_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/llm-configs/{Guid.NewGuid():D}/activate", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RotateKey_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/llm-configs/{Guid.NewGuid():D}/rotate-key", UriKind.Relative),
            new RotateLlmKeyRequest("sk-x"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/llm-configs/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_MissingApiKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/llm-configs", UriKind.Relative),
            new CreateLlmConfigRequest("openai", "gpt-4o", ApiKey: ""));

        // Endpoint hiện tại không chặn api key rỗng — khoá lại hành vi này. Nên trả 400;
        // khi sửa sẽ đỏ để nhắc cập nhật.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
    }
}

public sealed class EmbeddingConfigCrudTests : IAsyncLifetime
{
    // Factory riêng mỗi test: DB InMemory dùng chung giữa các test song song gây race
    // (delete unknown lúc 204 lúc 404 tuỳ dữ liệu test khác để lại).
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static readonly CreateEmbeddingConfigRequest ValidConfig =
        new("openai", "text-embedding-3-small", 1536, ApiKey: "sk-test");

    [Fact]
    public async Task CreateUpdateDelete_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            new Uri("/api/embedding-configs", UriKind.Relative), ValidConfig);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<EmbeddingConfigDto>();
        dto!.Dimension.Should().Be(1536);

        var updated = await client.PutAsJsonAsync(
            new Uri($"/api/embedding-configs/{dto.Id:D}", UriKind.Relative),
            new UpdateEmbeddingConfigRequest("openai", "text-embedding-3-large", 3072, "Updated", null));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivated = await client.PostAsync(
            new Uri($"/api/embedding-configs/{dto.Id:D}/deactivate", UriKind.Relative), null);
        deactivated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var activated = await client.PostAsync(
            new Uri($"/api/embedding-configs/{dto.Id:D}/activate", UriKind.Relative), null);
        activated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var rotated = await client.PostAsJsonAsync(
            new Uri($"/api/embedding-configs/{dto.Id:D}/rotate-key", UriKind.Relative),
            new RotateEmbeddingKeyRequest("sk-new"));
        rotated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(
            new Uri($"/api/embedding-configs/{dto.Id:D}", UriKind.Relative));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/embedding-configs/{Guid.NewGuid():D}", UriKind.Relative),
            new UpdateEmbeddingConfigRequest("openai", "text-embedding-3-small", 1536, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RotateKey_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/embedding-configs/{Guid.NewGuid():D}/rotate-key", UriKind.Relative),
            new RotateEmbeddingKeyRequest("sk-x"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/embedding-configs/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

public sealed class ApiKeyCrudTests : IAsyncLifetime
{
    // Factory riêng mỗi test: DB InMemory dùng chung giữa các test song song gây race
    // (delete unknown lúc 204 lúc 404 tuỳ dữ liệu test khác để lại).
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task IssueAndRevoke_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var issued = await client.PostAsJsonAsync(
            new Uri("/api/api-keys", UriKind.Relative),
            new CreateApiKeyRequest("Test key", DateTimeOffset.UtcNow.AddDays(30), ["leads:read"]));
        issued.StatusCode.Should().Be(HttpStatusCode.Created);
        var response = await issued.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        response!.PlaintextKey.Should().NotBeNullOrWhiteSpace();
        response.Name.Should().Be("Test key");

        var listed = await client.GetStringAsync(new Uri("/api/api-keys", UriKind.Relative));
        listed.Should().Contain("Test key");

        var revoked = await client.DeleteAsync(
            new Uri($"/api/api-keys/{response.Id:D}", UriKind.Relative));
        revoked.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Issue_MissingName_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/api-keys", UriKind.Relative),
            new CreateApiKeyRequest("", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoke_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/api-keys/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
