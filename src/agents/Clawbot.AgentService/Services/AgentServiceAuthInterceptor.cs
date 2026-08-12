using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.AgentService.Services;

// Validates the JWT issued by AgentServiceTokenIssuer (via OrchestratorServiceAuthInterceptor)
// and populates HttpContext.User so downstream authorization policies and OrchestratorCallerAuthorizer
// can read tenant_id, user_id, and role_id claims. Rejects unauthenticated or malformed tokens.
public sealed class AgentServiceAuthInterceptor : Interceptor
{
    private readonly AgentServiceAuthenticationOptions _options;
    private readonly TokenValidationParameters _validationParameters;

    public AgentServiceAuthInterceptor(AgentServiceAuthenticationOptions options)
    {
        _options = options;
        var signingKeyBytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes(options.SigningKey);
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = AgentServiceAuthenticationOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = AgentServiceAuthenticationOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        AuthenticateCall(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AuthenticateCall(context);
        return continuation(request, responseStream, context);
    }

    private void AuthenticateCall(ServerCallContext context)
    {
        var authHeader = context.RequestHeaders.GetValue("authorization");
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "orchestrator_caller_required"));
        }

        var token = authHeader.Substring("Bearer ".Length);
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParameters, out _);
            context.GetHttpContext().User = principal;
        }
        catch (SecurityTokenException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid_token"));
        }
    }
}
