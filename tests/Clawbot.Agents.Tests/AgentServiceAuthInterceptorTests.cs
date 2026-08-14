using Clawbot.Agents.Contracts.Chat;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.AgentService.Services;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Tests;

public sealed class AgentServiceAuthInterceptorTests
{
    private static readonly string SigningKey =
        Convert.ToBase64String(Enumerable.Repeat((byte)0xC3, 64).ToArray());

    [Fact]
    public async Task UnaryServerHandler_AcceptsMatchingSignedTenantAndSetsPrincipal()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var context = CreateContext(IssueToken(tenantId));
        var interceptor = CreateInterceptor();
        var continuationCalled = false;

        // Act
        var response = await interceptor.UnaryServerHandler(
            new DraftRequest { TenantId = tenantId.ToString("D") },
            context,
            (_, _) =>
            {
                continuationCalled = true;
                return Task.FromResult(new DraftResponse());
            });

        // Assert
        response.Should().NotBeNull();
        continuationCalled.Should().BeTrue();
        context.HttpContext.User.Identity?.IsAuthenticated.Should().BeTrue();
        context.HttpContext.User.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
    }

    [Fact]
    public async Task UnaryServerHandler_RejectsSignedTenantMismatchBeforeContinuation()
    {
        // Arrange
        var context = CreateContext(IssueToken(Guid.NewGuid()));
        var interceptor = CreateInterceptor();
        var continuationCalled = false;

        // Act
        Func<Task> act = () => interceptor.UnaryServerHandler(
            new DraftRequest { TenantId = Guid.NewGuid().ToString("D") },
            context,
            (_, _) =>
            {
                continuationCalled = true;
                return Task.FromResult(new DraftResponse());
            });

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.PermissionDenied &&
                exception.Status.Detail == "agent_service_tenant_mismatch");
        continuationCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ServerStreamingServerHandler_RejectsSignedTenantMismatchBeforeContinuation()
    {
        // Arrange
        var context = CreateContext(IssueToken(Guid.NewGuid()));
        var interceptor = CreateInterceptor();
        var continuationCalled = false;

        // Act
        Func<Task> act = () => interceptor.ServerStreamingServerHandler(
            new ChatRequest { TenantId = Guid.NewGuid().ToString("D") },
            new NoopServerStreamWriter<ChatToken>(),
            context,
            (_, _, _) =>
            {
                continuationCalled = true;
                return Task.CompletedTask;
            });

        // Assert
        await act.Should().ThrowAsync<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.PermissionDenied &&
                exception.Status.Detail == "agent_service_tenant_mismatch");
        continuationCalled.Should().BeFalse();
    }

    private static AgentServiceAuthInterceptor CreateInterceptor() =>
        new(Options.Create(new AgentServiceAuthenticationOptions
        {
            SigningKey = SigningKey,
            TokenLifetimeMinutes = 2,
        }));

    private static string IssueToken(Guid tenantId) =>
        new AgentServiceTokenIssuer(Options.Create(new AgentServiceAuthenticationOptions
        {
            SigningKey = SigningKey,
            TokenLifetimeMinutes = 2,
        })).Issue(Guid.NewGuid(), tenantId, Guid.NewGuid());

    private static TestServerCallContext CreateContext(string token) =>
        new(new Metadata
        {
            { "authorization", $"Bearer {token}" },
        });

    private sealed class NoopServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly Metadata _requestHeaders;
        private readonly Metadata _responseTrailers = [];
        private readonly Dictionary<object, object> _userState;
        private Status _status;
        private WriteOptions? _writeOptions;

        public TestServerCallContext(Metadata requestHeaders)
        {
            _requestHeaders = requestHeaders;
            HttpContext = new DefaultHttpContext();
            _userState = new Dictionary<object, object>
            {
                ["__HttpContext"] = HttpContext,
            };
        }

        public HttpContext HttpContext { get; }

        protected override string MethodCore => "/test.Agent/Call";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:12345";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => _requestHeaders;
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => _responseTrailers;
        protected override Status StatusCore { get => _status; set => _status = value; }
        protected override WriteOptions? WriteOptionsCore { get => _writeOptions; set => _writeOptions = value; }
        protected override AuthContext AuthContextCore => throw new NotSupportedException();
        protected override IDictionary<object, object> UserStateCore => _userState;

        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
