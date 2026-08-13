using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Api.Auth;

// Gắn token AgentService cho 6 gRPC client còn lại (report/content/lead/sale-assist/docs/research)
// sau khi phía AgentService khoá toàn bộ service sau policy "orchestrator-service".
// Hai chế độ:
//  - Có HttpContext đã xác thực (endpoint HTTP): phát token bằng danh tính phiên — giữ nguyên truy vết.
//  - Không có HttpContext (job nền Hangfire): phát service token với danh tính service cố định.
//    6 service này đọc tenant/user từ field của message chứ không đọc claim, nên service token
//    không nới cửa quyền nào; nó chỉ chứng minh cuộc gọi xuất phát từ chính API host.
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
        var token = IssueToken(request);
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

        headers.Add("authorization", $"Bearer {token}");
        var options = context.Options.WithHeaders(headers);
        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            options));
    }

    private string IssueToken<TRequest>(TRequest request) where TRequest : class
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = ReadRequiredGuid(user, ClaimTypes.NameIdentifier);
            var tenantId = ReadRequiredGuid(user, "tenant_id");
            var roleId = ReadRequiredGuid(user, "role_id");
            return tokenIssuer.Issue(userId, tenantId, roleId);
        }

        // Job nền: không có phiên — dùng danh tính service, tenant lấy từ message nếu có.
        return tokenIssuer.IssueServiceToken(ReadTenantIdFromMessage(request));
    }

    // Mọi request của 6 service đều mang trường TenantId (string) do proto sinh ra.
    private static Guid ReadTenantIdFromMessage<TRequest>(TRequest request) where TRequest : class
    {
        var value = request.GetType().GetProperty("TenantId")?.GetValue(request) as string;
        return Guid.TryParse(value, out var tenantId) ? tenantId : Guid.Empty;
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
