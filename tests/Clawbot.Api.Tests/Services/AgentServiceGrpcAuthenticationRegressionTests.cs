using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clawbot.Agents.Contracts.Chat;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Auth;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.Api.Tests.Services;

public sealed class AgentServiceGrpcAuthenticationRegressionTests
{
    private static readonly string SigningKey =
        Convert.ToBase64String(Enumerable.Repeat((byte)0xA5, 64).ToArray());

    [Fact]
    public void ApiAgentClients_RegisterExpectedAuthenticationInterceptors()
    {
        // Arrange
        var services = CreateServices();
        services.AddApiAgentServiceGrpcClients(
            new Uri("http://localhost:15875"),
            new AgentServiceGrpcHandlerFactory(new AgentServiceTlsOptions()));
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        // Act / Assert
        AssertInterceptor<Clawbot.Agents.Contracts.SaleAssist.SaleAssistAgent.SaleAssistAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Content.ContentAgent.ContentAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Research.ResearchAgent.ResearchAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Lead.LeadAgent.LeadAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Report.ReportAgent.ReportAgentClient,
            AgentServiceClientAuthInterceptor>(provider, options);
        AssertInterceptor<Clawbot.Agents.Contracts.Orchestrator.Orchestrator.OrchestratorClient,
            OrchestratorServiceAuthInterceptor>(provider, options);
    }

    [Fact]
    public void UnaryCall_WithoutHttpContext_UsesServiceIdentityForRequestTenant()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateClientInterceptor(httpContext: null);
        Metadata? capturedHeaders = null;
        var request = new DraftRequest { TenantId = tenantId.ToString("D") };
        var context = CreateUnaryContext<DraftRequest, DraftResponse>();

        // Act
        using var call = interceptor.AsyncUnaryCall(
            request,
            context,
            (_, forwardedContext) =>
            {
                capturedHeaders = forwardedContext.Options.Headers;
                return CompletedUnaryCall(new DraftResponse());
            });

        // Assert
        var principal = ValidateBearer(capturedHeaders);
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            .Should().Be(AgentServiceAuthenticationOptions.ServiceUserId.ToString("D"));
        principal.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst("role_id")?.Value
            .Should().Be(AgentServiceAuthenticationOptions.ServiceRoleId.ToString("D"));
        principal.FindFirst("client_id")?.Value.Should().Be(AgentServiceAuthenticationOptions.ClientId);
    }

    [Fact]
    public void ServerStreamingCall_WithoutHttpContext_UsesServiceIdentityForRequestTenant()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var interceptor = CreateClientInterceptor(httpContext: null);
        Metadata? capturedHeaders = null;
        var request = new ChatRequest { TenantId = tenantId.ToString("D") };
        var context = CreateStreamingContext<ChatRequest, ChatToken>();

        // Act
        using var call = interceptor.AsyncServerStreamingCall(
            request,
            context,
            (_, forwardedContext) =>
            {
                capturedHeaders = forwardedContext.Options.Headers;
                return CompletedStreamingCall<ChatToken>();
            });

        // Assert
        var principal = ValidateBearer(capturedHeaders);
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            .Should().Be(AgentServiceAuthenticationOptions.ServiceUserId.ToString("D"));
        principal.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst("role_id")?.Value
            .Should().Be(AgentServiceAuthenticationOptions.ServiceRoleId.ToString("D"));
    }

    [Fact]
    public void UnaryCall_WithAuthenticatedHttpContext_PreservesCallerIdentity()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedHttpContext(userId, tenantId, roleId);
        var interceptor = CreateClientInterceptor(httpContext);
        Metadata? capturedHeaders = null;

        // Act
        using var call = interceptor.AsyncUnaryCall(
            new DraftRequest { TenantId = tenantId.ToString("D") },
            CreateUnaryContext<DraftRequest, DraftResponse>(),
            (_, forwardedContext) =>
            {
                capturedHeaders = forwardedContext.Options.Headers;
                return CompletedUnaryCall(new DraftResponse());
            });

        // Assert
        var principal = ValidateBearer(capturedHeaders);
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(userId.ToString("D"));
        principal.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst("role_id")?.Value.Should().Be(roleId.ToString("D"));
    }

    [Fact]
    public void UnaryCall_WithAnonymousHttpContext_FailsClosedBeforeContinuation()
    {
        // Arrange
        var continuationCalled = false;
        var interceptor = CreateClientInterceptor(new DefaultHttpContext());

        // Act
        Action act = () => interceptor.AsyncUnaryCall(
            new DraftRequest { TenantId = Guid.NewGuid().ToString("D") },
            CreateUnaryContext<DraftRequest, DraftResponse>(),
            (_, _) =>
            {
                continuationCalled = true;
                return CompletedUnaryCall(new DraftResponse());
            });

        // Assert
        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "agent_service_caller_required");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public void UnaryCall_WithAuthenticatedTenantMismatch_FailsBeforeContinuation()
    {
        // Arrange
        var continuationCalled = false;
        var httpContext = CreateAuthenticatedHttpContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var interceptor = CreateClientInterceptor(httpContext);

        // Act
        Action act = () => interceptor.AsyncUnaryCall(
            new DraftRequest { TenantId = Guid.NewGuid().ToString("D") },
            CreateUnaryContext<DraftRequest, DraftResponse>(),
            (_, _) =>
            {
                continuationCalled = true;
                return CompletedUnaryCall(new DraftResponse());
            });

        // Assert
        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.PermissionDenied &&
                exception.Status.Detail == "agent_service_tenant_mismatch");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public void UnaryCall_WithoutHttpContextAndRequestTenant_FailsBeforeContinuation()
    {
        // Arrange
        var continuationCalled = false;
        var interceptor = CreateClientInterceptor(httpContext: null);

        // Act
        Action act = () => interceptor.AsyncUnaryCall(
            new DraftRequest(),
            CreateUnaryContext<DraftRequest, DraftResponse>(),
            (_, _) =>
            {
                continuationCalled = true;
                return CompletedUnaryCall(new DraftResponse());
            });

        // Assert
        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "agent_service_tenant_required");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public void OrchestratorCall_WithoutHttpContext_FailsClosedBeforeContinuation()
    {
        // Arrange
        var continuationCalled = false;
        var interceptor = new OrchestratorServiceAuthInterceptor(
            new HttpContextAccessor(),
            CreateIssuer());

        // Act
        Action act = () => interceptor.AsyncUnaryCall(
            new Clawbot.Agents.Contracts.Orchestrator.SubmitRequest(),
            CreateUnaryContext<
                Clawbot.Agents.Contracts.Orchestrator.SubmitRequest,
                Clawbot.Agents.Contracts.Orchestrator.SessionResponse>(),
            (_, _) =>
            {
                continuationCalled = true;
                return CompletedUnaryCall(
                    new Clawbot.Agents.Contracts.Orchestrator.SessionResponse());
            });

        // Assert
        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "orchestrator_caller_required");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public void OrchestratorCall_WithAnonymousIdentity_FailsClosedBeforeContinuation()
    {
        // Arrange
        var continuationCalled = false;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim("tenant_id", tenantId.ToString("D")),
                new Claim("role_id", roleId.ToString("D")),
            ])),
        };
        var interceptor = new OrchestratorServiceAuthInterceptor(
            new HttpContextAccessor { HttpContext = httpContext },
            CreateIssuer());

        // Act
        Action act = () => interceptor.AsyncUnaryCall(
            new Clawbot.Agents.Contracts.Orchestrator.SubmitRequest(),
            CreateUnaryContext<
                Clawbot.Agents.Contracts.Orchestrator.SubmitRequest,
                Clawbot.Agents.Contracts.Orchestrator.SessionResponse>(),
            (_, _) =>
            {
                continuationCalled = true;
                return CompletedUnaryCall(
                    new Clawbot.Agents.Contracts.Orchestrator.SessionResponse());
            });

        // Assert
        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "orchestrator_caller_required");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public void OrchestratorServerStreamingCall_WithAuthenticatedIdentity_AddsCallerToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var httpContext = CreateAuthenticatedHttpContext(userId, tenantId, roleId);
        var interceptor = new OrchestratorServiceAuthInterceptor(
            new HttpContextAccessor { HttpContext = httpContext },
            CreateIssuer());
        Metadata? capturedHeaders = null;

        // Act
        using var call = interceptor.AsyncServerStreamingCall(
            new ChatRequest { TenantId = tenantId.ToString("D") },
            CreateStreamingContext<ChatRequest, ChatToken>(),
            (_, forwardedContext) =>
            {
                capturedHeaders = forwardedContext.Options.Headers;
                return CompletedStreamingCall<ChatToken>();
            });

        // Assert
        var principal = ValidateBearer(capturedHeaders);
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(userId.ToString("D"));
        principal.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst("role_id")?.Value.Should().Be(roleId.ToString("D"));
    }

    [Fact]
    public void ApiProgram_UsesGuardedAgentServiceClientRegistration()
    {
        // Arrange
        var programPath = FindRepositoryFile("src/api/Clawbot.Api/Program.cs");

        // Act
        var source = File.ReadAllText(programPath);

        // Assert
        source.Should().Contain("builder.Services.AddApiAgentServiceGrpcClients(");
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSingleton(Options.Create(new AgentServiceAuthenticationOptions
        {
            SigningKey = SigningKey,
            TokenLifetimeMinutes = 2,
        }));
        services.AddSingleton<AgentServiceTokenIssuer>();
        services.AddTransient<AgentServiceClientAuthInterceptor>();
        services.AddTransient<OrchestratorServiceAuthInterceptor>();
        return services;
    }

    private static DefaultHttpContext CreateAuthenticatedHttpContext(
        Guid userId,
        Guid tenantId,
        Guid roleId) =>
        new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim("tenant_id", tenantId.ToString("D")),
                new Claim("role_id", roleId.ToString("D")),
            ], "test")),
        };

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{relativePath}'.",
            relativePath);
    }

    private static AgentServiceClientAuthInterceptor CreateClientInterceptor(HttpContext? httpContext) =>
        new(
            new HttpContextAccessor { HttpContext = httpContext },
            CreateIssuer());

    private static AgentServiceTokenIssuer CreateIssuer() =>
        new(Options.Create(new AgentServiceAuthenticationOptions
        {
            SigningKey = SigningKey,
            TokenLifetimeMinutes = 2,
        }));

    private static void AssertInterceptor<TClient, TInterceptor>(
        IServiceProvider provider,
        IOptionsMonitor<GrpcClientFactoryOptions> options)
        where TClient : class
        where TInterceptor : Interceptor
    {
        var registrations = options.Get(typeof(TClient).Name).InterceptorRegistrations;
        registrations
            .Select(registration => registration.Creator(provider))
            .Should().Contain(interceptor => interceptor is TInterceptor);
    }

    private static ClaimsPrincipal ValidateBearer(Metadata? headers)
    {
        headers.Should().NotBeNull();
        var authorization = headers!.Single(entry => entry.Key == "authorization").Value;
        authorization.Should().StartWith("Bearer ");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(
            authorization["Bearer ".Length..],
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = AgentServiceAuthenticationOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = AgentServiceAuthenticationOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    AgentServiceAuthenticationOptions.GetSigningKeyBytes(SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5),
            },
            out _);
    }

    private static ClientInterceptorContext<TRequest, TResponse> CreateUnaryContext<TRequest, TResponse>()
        where TRequest : class, new()
        where TResponse : class, new() =>
        new(
            new Method<TRequest, TResponse>(
                MethodType.Unary,
                "test",
                "unary",
                CreateMarshaller<TRequest>(),
                CreateMarshaller<TResponse>()),
            "localhost",
            new CallOptions());

    private static ClientInterceptorContext<TRequest, TResponse> CreateStreamingContext<TRequest, TResponse>()
        where TRequest : class, new()
        where TResponse : class, new() =>
        new(
            new Method<TRequest, TResponse>(
                MethodType.ServerStreaming,
                "test",
                "stream",
                CreateMarshaller<TRequest>(),
                CreateMarshaller<TResponse>()),
            "localhost",
            new CallOptions());

    private static Marshaller<T> CreateMarshaller<T>() where T : class, new() =>
        Marshallers.Create<T>(_ => [], _ => new T());

    private static AsyncUnaryCall<T> CompletedUnaryCall<T>(T response) where T : class =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncServerStreamingCall<T> CompletedStreamingCall<T>() where T : class =>
        new(
            new EmptyAsyncStreamReader<T>(),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private sealed class EmptyAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => throw new InvalidOperationException("No stream items are available.");

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
