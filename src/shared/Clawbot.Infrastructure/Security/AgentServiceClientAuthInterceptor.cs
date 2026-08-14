using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Infrastructure.Security;

// Gắn token AgentService cho các gRPC client KHÔNG phải orchestrator (6 client bên API +
// ChatAgentClient ở shared DI). Đặt ở lớp shared vì GrpcChatAutoReplyGateway chạy ở cả API
// lẫn AgentService (AgentService tự gọi chính nó), nên interceptor phải có sẵn ở cả 2 host.
// Hai chế độ:
//  - Có HttpContext đã xác thực (endpoint HTTP, hoặc call gRPC đang được phục vụ): phát token
//    bằng danh tính phiên — giữ nguyên truy vết.
//  - Không có HttpContext (job Hangfire, consumer MassTransit): phát service token với danh tính
//    service cố định. Các service nhận đọc tenant/user từ field của message chứ không đọc claim,
//    nên service token không nới cửa quyền nào; nó chỉ chứng minh cuộc gọi xuất phát từ trong hệ thống.
// KHÔNG gắn interceptor này cho OrchestratorClient — đường đó phải fail-closed với danh tính phiên thật.
public sealed class AgentServiceClientAuthInterceptor(
    IHttpContextAccessor httpContextAccessor,
    AgentServiceTokenIssuer tokenIssuer) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, WithAuthorization(request, context));
    }

    // ChatAgent.Reply là server-streaming: thiếu override này thì auto-reply đi ra không có header.
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, WithAuthorization(request, context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithAuthorization<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
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

        headers.Add("authorization", $"Bearer {IssueToken(request)}");
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));
    }

    private string IssueToken<TRequest>(TRequest request) where TRequest : class
    {
        var requestTenantId = AgentServiceTenantBinding.ReadRequiredRequestTenantId(request);
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            // Ngữ cảnh nền thật sự không có phiên — dùng danh tính service, tenant lấy từ message.
            return tokenIssuer.IssueServiceToken(requestTenantId);
        }

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "agent_service_caller_required"));
        }

        var userId = ReadRequiredGuid(user, ClaimTypes.NameIdentifier);
        var tenantId = ReadRequiredGuid(user, "tenant_id");
        var roleId = ReadRequiredGuid(user, "role_id");
        if (tenantId != requestTenantId)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "agent_service_tenant_mismatch"));
        }

        return tokenIssuer.Issue(userId, tenantId, roleId);
    }

    private static Guid ReadRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        if (!Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value)
            || value == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "agent_service_caller_required"));
        }

        return value;
    }
}
