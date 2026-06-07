using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Clawbot.Infrastructure.Content.Publishing;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class HttpSocialPublisherTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContentItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset ScheduledAt = new(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishAsync_sends_buffer_shaped_payload_with_bearer_token()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"post_url":"https://social.example/posts/1"}"""),
        });
        var publisher = new HttpSocialPublisher(
            new HttpClient(handler),
            Options.Create(new PublisherOptions
            {
                Endpoint = "https://publisher.example/create",
                Token = "secret-token",
            }));

        var result = await publisher.PublishAsync(Request(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://social.example/posts/1");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri.Should().Be(new Uri("https://publisher.example/create"));
        handler.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "secret-token"));
        using var payload = JsonDocument.Parse(handler.Body);
        payload.RootElement.GetProperty("profile_ids")[0].GetString().Should().Be("facebook");
        payload.RootElement.GetProperty("text").GetString().Should().Be("Learn HSK today");
        payload.RootElement.GetProperty("scheduled_at").GetInt64().Should().Be(ScheduledAt.ToUnixTimeSeconds());
        payload.RootElement.GetProperty("metadata").GetProperty("tenant_id").GetString().Should().Be(TenantId.ToString());
        payload.RootElement.GetProperty("metadata").GetProperty("content_item_id").GetString().Should().Be(ContentItemId.ToString());
    }

    [Fact]
    public async Task PublishAsync_returns_failure_without_endpoint_or_token()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var publisher = new HttpSocialPublisher(
            new HttpClient(handler),
            Options.Create(new PublisherOptions()));

        var result = await publisher.PublishAsync(Request(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("publisher_not_configured");
        handler.Calls.Should().Be(0);
    }

    private static PublishRequest Request() =>
        new(TenantId, ContentItemId, "facebook", "Learn HSK today", "[]", ScheduledAt);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response;
        }
    }
}
