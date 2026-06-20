using Clawbot.Agents.Core.Chat;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Clawbot.AgentService.Services;

// Maps the domain LlmConfigNotConfiguredException (thrown by the resolver when an agent has no
// active LlmConfig bound, D1) onto a typed gRPC status so clients see `llm_config_not_configured`
// instead of a generic Internal error. FailedPrecondition = caller must bind/activate a config first.
// Covers every agent service uniformly (unary + server-streaming) — no per-service catch needed.
public sealed class LlmConfigGrpcInterceptor : Interceptor
{
    private const string Code = "llm_config_not_configured";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (LlmConfigNotConfiguredException ex)
        {
            throw ToRpc(ex);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        catch (LlmConfigNotConfiguredException ex)
        {
            throw ToRpc(ex);
        }
    }

    private static RpcException ToRpc(LlmConfigNotConfiguredException ex) =>
        new(new Status(StatusCode.FailedPrecondition, Code, ex));
}
