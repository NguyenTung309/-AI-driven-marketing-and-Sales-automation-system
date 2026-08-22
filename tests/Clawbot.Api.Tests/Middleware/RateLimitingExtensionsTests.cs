using Clawbot.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests.Middleware;

public sealed class RateLimitingExtensionsTests
{
    [Fact]
    public void PolicyNames_AreStableStrings()
    {
        RateLimitingExtensions.AuthPolicy.Should().Be("auth");
        RateLimitingExtensions.WebhookPolicy.Should().Be("webhook");
        RateLimitingExtensions.ChatPolicy.Should().Be("chat");
        RateLimitingExtensions.GeneralPolicy.Should().Be("general");
        RateLimitingExtensions.UploadPolicy.Should().Be("upload");
    }

    [Fact]
    public void AddClawbotRateLimiting_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        services.AddClawbotRateLimiting().Should().BeSameAs(services);
    }

    [Fact]
    public void AddClawbotRateLimiting_RegistersRateLimiterOptions()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddClawbotRateLimiting()
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        options.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        options.GlobalLimiter.Should().NotBeNull();
    }

    [Theory]
    [InlineData("auth", 30, "auth:30/min")]
    [InlineData("upload", 20, "upload:20/min")]
    [InlineData("general", 300, "general:300/min")]
    public void PolicyHeader_FormatsPolicyAndLimit(string policy, int permit, string expected)
    {
        RateLimitingExtensions.PolicyHeader(policy, permit).Should().Be(expected);
    }

    [Fact]
    public void PolicyHeader_UsesInvariantCultureForNumbers()
    {
        RateLimitingExtensions.PolicyHeader("chat", 1000).Should().Be("chat:1000/min");
    }
}
