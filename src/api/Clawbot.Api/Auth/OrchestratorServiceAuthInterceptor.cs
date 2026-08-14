using System.Security.Claims;
using Clawbot.Infrastructure.Security;
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
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, WithCallerAuthorization(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) =>
        continuation(request, WithCallerAuthorization(context));

    private ClientInterceptorContext<TRequest, TResponse> WithCallerAuthorization<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "orchestrator_caller_required"));
        }

        var userId = ReadRequiredGuid(httpContext.User, ClaimTypes.NameIdentifier);
        var tenantId = ReadRequiredGuid(httpContext.User, "tenant_id");
        // Orchestration runs with the role of the session that asked for it, so the caller's role
        // travels with the call instead of being re-derived downstream.
        var roleId = ReadRequiredGuid(httpContext.User, "role_id");
        var headers = CopyHeadersWithoutAuthorization(context.Options.Headers);
        headers.Add("authorization", $"Bearer {tokenIssuer.Issue(userId, tenantId, roleId)}");
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));
    }

    private static Metadata CopyHeadersWithoutAuthorization(Metadata? source)
    {
        var headers = new Metadata();
        if (source is null)
            return headers;

        foreach (var header in source)
        {
            if (string.Equals(header.Key, "authorization", StringComparison.OrdinalIgnoreCase))
                continue;
            if (header.IsBinary)
                headers.Add(header.Key, header.ValueBytes);
            else
                headers.Add(header.Key, header.Value);
        }

        return headers;
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
