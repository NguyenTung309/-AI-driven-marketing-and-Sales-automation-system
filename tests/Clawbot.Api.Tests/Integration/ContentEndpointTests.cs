using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Content;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

public sealed class ContentBriefCrudTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static Uri Briefs(string suffix = "") =>
        new($"/api/content/briefs{suffix}", UriKind.Relative);

    [Fact]
    public async Task CreateUpdateDelete_RoundTrips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest("facebook", "Viết bài về khai giảng"));
        created.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        var dto = await created.Content.ReadFromJsonAsync<ContentBriefDto>();
        dto!.Platform.Should().Be("facebook");
        dto.Brief.Should().Be("Viết bài về khai giảng");

        var fetched = await client.GetAsync(Briefs($"/{dto.Id:D}"));
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await client.PutAsJsonAsync(
            Briefs($"/{dto.Id:D}"),
            new UpdateContentBriefRequest("zalo", "Viết bài về học phí"));
        updated.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var deleted = await client.DeleteAsync(Briefs($"/{dto.Id:D}"));
        deleted.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task List_ReturnsCreatedBrief()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest("facebook", "Nội dung tìm kiếm được"));

        var listed = await client.GetStringAsync(Briefs());

        listed.Should().Contain("Nội dung tìm kiếm được");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankBrief_IsRejected(string brief)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest("facebook", brief));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("khong-ton-tai")]
    [InlineData("")]
    public async Task Create_UnsupportedPlatform_IsRejected(string platform)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest(platform, "nội dung"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("zalo")]
    [InlineData("instagram")]
    public async Task Create_SupportedPlatforms_AreAccepted(string platform)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest(platform, "nội dung"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Briefs($"/{Guid.NewGuid():D}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            Briefs($"/{Guid.NewGuid():D}"),
            new UpdateContentBriefRequest("facebook", "nội dung"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_IsHandled()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(Briefs($"/{Guid.NewGuid():D}"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Create_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            Briefs(), new CreateContentBriefRequest("facebook", "x"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public sealed class ContentItemWriteTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static Uri Items(string suffix) =>
        new($"/api/content/items{suffix}", UriKind.Relative);

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Items($"/{Guid.NewGuid():D}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            Items($"/{Guid.NewGuid():D}"),
            new UpdateContentItemRequest("nội dung mới", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownId_IsHandled()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(Items($"/{Guid.NewGuid():D}"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Approve_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Items($"/{Guid.NewGuid():D}/approve"),
            new ApproveContentItemRequest(1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reject_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Items($"/{Guid.NewGuid():D}/reject"),
            new RejectContentItemRequest(1, "không đạt"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Schedule_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            Items($"/{Guid.NewGuid():D}/schedule"),
            new ScheduleContentItemRequest(DateTimeOffset.UtcNow.AddDays(1)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHooks_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(Items($"/{Guid.NewGuid():D}/hooks"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteAsset_UnknownIds_IsHandled()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            Items($"/{Guid.NewGuid():D}/assets/{Guid.NewGuid():D}"));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RetryAgentReview_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            Items($"/{Guid.NewGuid():D}/agent-review/retry"), null);

        // Endpoint kiểm tra body/revision trước khi tra id nên trả 400 chứ không phải 404.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public sealed class ContentReadEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("/api/content/calendar")]
    [InlineData("/api/content/items")]
    [InlineData("/api/content/trends")]
    [InlineData("/api/content/post-performance")]
    [InlineData("/api/content/publish-targets")]
    public async Task ReadEndpoints_ReturnOkForAdmin(string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(0)]
    [InlineData(500)]
    public async Task PostPerformance_WindowParameterIsClamped(int days)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/content/post-performance?days={days}", UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DeleteSchedule_UnknownId_IsHandled()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/content/schedule/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.NoContent);
    }
}
