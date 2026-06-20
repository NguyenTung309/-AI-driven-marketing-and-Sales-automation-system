using System.Text;
using System.Text.Json;
using Clawbot.Api.Middleware;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class GrpcErrorTranslationMiddlewareTests
{
    [Fact]
    public async Task FailedPrecondition_maps_to_422_with_detail_as_error_code()
    {
        var ctx = NewContext();
        var sut = new GrpcErrorTranslationMiddleware(
            _ => throw new RpcException(new Status(StatusCode.FailedPrecondition, "llm_config_not_configured")));

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        (await ReadBodyAsync(ctx)).Should().Contain("llm_config_not_configured");
    }

    [Fact]
    public async Task Other_grpc_status_is_rethrown_unchanged()
    {
        var ctx = NewContext();
        var sut = new GrpcErrorTranslationMiddleware(
            _ => throw new RpcException(new Status(StatusCode.Internal, "boom")));

        var act = async () => await sut.InvokeAsync(ctx);

        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.StatusCode == StatusCode.Internal);
    }

    [Fact]
    public async Task Success_passes_through()
    {
        var ctx = NewContext();
        var sut = new GrpcErrorTranslationMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; });

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    private static DefaultHttpContext NewContext() =>
        new() { Response = { Body = new MemoryStream() } };

    private static async Task<string> ReadBodyAsync(HttpContext ctx)
    {
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
