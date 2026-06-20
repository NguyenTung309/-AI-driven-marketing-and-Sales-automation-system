using Clawbot.Agents.Core.Chat;
using Clawbot.AgentService.Services;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Clawbot.AgentService.Tests.Services;

public sealed class LlmConfigGrpcInterceptorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task UnaryServerHandler_maps_unbound_config_to_failed_precondition()
    {
        var sut = new LlmConfigGrpcInterceptor();

        var act = async () => await sut.UnaryServerHandler<string, string>(
            "req",
            TestServerCallContext.Create(),
            (_, _) => throw new LlmConfigNotConfiguredException(Tenant, "chat-agent"));

        var ex = (await act.Should().ThrowAsync<RpcException>()).Subject.First();
        ex.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        ex.Status.Detail.Should().Be("llm_config_not_configured");
    }

    [Fact]
    public async Task UnaryServerHandler_passes_through_unrelated_exceptions()
    {
        var sut = new LlmConfigGrpcInterceptor();

        var act = async () => await sut.UnaryServerHandler<string, string>(
            "req",
            TestServerCallContext.Create(),
            (_, _) => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task UnaryServerHandler_returns_response_when_no_exception()
    {
        var sut = new LlmConfigGrpcInterceptor();

        var result = await sut.UnaryServerHandler<string, string>(
            "req",
            TestServerCallContext.Create(),
            (req, _) => Task.FromResult(req + "-ok"));

        result.Should().Be("req-ok");
    }
}
