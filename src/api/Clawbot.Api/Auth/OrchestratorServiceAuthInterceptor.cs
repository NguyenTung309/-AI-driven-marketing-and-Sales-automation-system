using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Api.Auth;

public sealed class OrchestratorServiceAuthInterceptor(
    IHttpContextAccessor httpContextAccessor,
    AgentServiceTokenIssuer tokenIssuer) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "orchestrator_caller_required"));
        var userId = ReadRequiredGuid(httpContext.User, ClaimTypes.NameIdentifier);
        var tenantId = ReadRequiredGuid(httpContext.User, "tenant_id");
        // Orchestration runs with the role of the session that asked for it, so the caller's role
        // travels with the call instead of being re-derived downstream.
        var roleId = ReadRequiredGuid(httpContext.User, "role_id");
        var headers = new Metadata();
        if (context.Options.Headers is not null)
        {
            foreach (var header in context.Options.Headers)
            {
                if (string.Equals(header.Key, "authorization", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (header.IsBinary)
                    headers.Add(header.Key, header.ValueBytes);
                else
                    headers.Add(header.Key, header.Value);
            }
        }

        headers.Add("authorization", $"Bearer {tokenIssuer.Issue(userId, tenantId, roleId)}");
        var options = context.Options.WithHeaders(headers);
        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            options));
    }

    private static Guid ReadRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        if (!Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value)
            || value == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "orchestrator_caller_required"));
        }

        return value;
    }
}
