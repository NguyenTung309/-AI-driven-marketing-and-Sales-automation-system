using System.Net;
using System.Reflection;
using Clawbot.Infrastructure.Content.Publishing;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests.Content.Publishing;

public sealed class InstagramPublishingTests
{
    [Fact]
    public void InstagramPublishingGate_IsBlocked_ReturnsFalseForInstagram()
    {
        InstagramPublishingGate.IsBlocked("instagram").Should().BeFalse();
        InstagramPublishingGate.IsBlocked("facebook").Should().BeFalse();
        InstagramPublishingGate.IsBlocked("").Should().BeFalse();
    }

    [Fact]
    public void ResolveInstagramImage_WithPublicBaseUrl_ResolvesRelativeAssetUrl()
    {
        // Arrange
        var options = Options.Create(new GraphPublisherOptions
        {
            InstagramPublishingEnabled = true,
            PublicBaseUrl = "https://example.ngrok-free.app"
        });
        using var http = new HttpClient();
        var publisher = new GraphSocialPublisher(http, options);

        var resolveMethod = typeof(GraphSocialPublisher).GetMethod(
            "ResolveInstagramImage",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ResolveInstagramImage not found");

        var assetId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var assetsJson = $@"[
            {{
                ""type"": ""image"",
                ""url"": ""/api/content/items/{itemId}/assets/{assetId}"",
                ""assetId"": ""{assetId}"",
                ""contentType"": ""image/jpeg""
            }}
        ]";

        // Act
        var result = resolveMethod.Invoke(publisher, [assetsJson]);

        // Assert
        result.Should().NotBeNull();
        var tuple = ((string? ImageUrl, string? Error))result!;
        tuple.Error.Should().BeNull();
        tuple.ImageUrl.Should().Be($"https://example.ngrok-free.app/api/content/items/{itemId}/assets/{assetId}.jpg");
    }

    [Fact]
    public void ResolveInstagramImage_WithDirectPublicJpeg_ReturnsDirectUrl()
    {
        // Arrange
        var options = Options.Create(new GraphPublisherOptions
        {
            InstagramPublishingEnabled = true,
            PublicBaseUrl = "https://example.ngrok-free.app"
        });
        using var http = new HttpClient();
        var publisher = new GraphSocialPublisher(http, options);

        var resolveMethod = typeof(GraphSocialPublisher).GetMethod(
            "ResolveInstagramImage",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ResolveInstagramImage not found");

        var assetsJson = @"[
            {
                ""type"": ""image"",
                ""url"": ""https://images.unsplash.com/photo-123456789.jpg"",
                ""contentType"": ""image/jpeg""
            }
        ]";

        // Act
        var result = resolveMethod.Invoke(publisher, [assetsJson]);

        // Assert
        result.Should().NotBeNull();
        var tuple = ((string? ImageUrl, string? Error))result!;
        tuple.Error.Should().BeNull();
        tuple.ImageUrl.Should().Be("https://images.unsplash.com/photo-123456789.jpg");
    }
}
