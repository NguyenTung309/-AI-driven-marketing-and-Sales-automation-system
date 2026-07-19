using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Clawbot.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Api.Tests.Middleware;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_returns_safe_json_without_stack_and_sets_500()
    {
        var ctx = new DefaultHttpContext
        {
            TraceIdentifier = "req-test-123",
            Response = { Body = new MemoryStream() },
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tenant_id", Guid.NewGuid().ToString("D")),
                new Claim("sub", Guid.NewGuid().ToString("D")),
            ], "test")),
        };

        var sut = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await sut.TryHandleAsync(
            ctx,
            new InvalidOperationException("secret stack detail should not leak"),
            CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEndAsync();
        body.Should().Contain("internal_error");
        body.Should().Contain("req-test-123");
        body.Should().NotContain("secret stack detail");
        body.Should().NotContain("InvalidOperationException");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("internal_error");
        doc.RootElement.GetProperty("requestId").GetString().Should().Be("req-test-123");
        doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
