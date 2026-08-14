using System.IdentityModel.Tokens.Jwt;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.AgentService.Services;

// Validates the JWT issued by AgentServiceTokenIssuer (via OrchestratorServiceAuthInterceptor)
// and populates HttpContext.User so downstream authorization policies and OrchestratorCallerAuthorizer
// can read tenant_id, user_id, and role_id claims. Rejects unauthenticated or malformed tokens.
public sealed class AgentServiceAuthInterceptor : Interceptor
{
    private readonly AgentServiceAuthenticationOptions _options;
    private readonly TokenValidationParameters _validationParameters;

    // Program.cs binds this section with Configure<T>, so only IOptions<T> is in the container;
    // taking the bare options type made gRPC interceptor activation throw at call time.
    public AgentServiceAuthInterceptor(IOptions<AgentServiceAuthenticationOptions> options)
    {
        _options = options.Value;
        var signingKeyBytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes(_options.SigningKey);
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
        AuthenticateCall(request, context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AuthenticateCall(request, context);
        return continuation(request, responseStream, context);
    }

    private void AuthenticateCall(object request, ServerCallContext context)
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
            AgentServiceTenantBinding.EnsurePrincipalMatchesRequest(principal, request);
            context.GetHttpContext().User = principal;
        }
        catch (SecurityTokenException)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "invalid_token"));
        }
    }
}
