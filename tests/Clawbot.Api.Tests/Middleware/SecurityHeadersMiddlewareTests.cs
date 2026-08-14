using Clawbot.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Api.Tests.Middleware;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsSecurityHeadersAndDefersHsts()
    {
        var httpContext = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        httpContext.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        httpContext.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        httpContext.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("default-src 'self'");
        httpContext.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }
}