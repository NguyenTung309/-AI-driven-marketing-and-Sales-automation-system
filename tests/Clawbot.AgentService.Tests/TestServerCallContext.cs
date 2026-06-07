using Grpc.Core;

namespace Clawbot.AgentService.Tests;

internal sealed class TestServerCallContext : ServerCallContext
{
    private readonly CancellationToken _cancellationToken;
    private readonly Metadata _requestHeaders = [];
    private readonly Metadata _responseTrailers = [];
    private readonly Dictionary<object, object> _userState = [];
    private Status _status = new(StatusCode.OK, string.Empty);
    private WriteOptions? _writeOptions;

    private TestServerCallContext(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public static ServerCallContext Create(CancellationToken cancellationToken = default) =>
        new TestServerCallContext(cancellationToken);

    protected override string MethodCore => "test";

    protected override string HostCore => "localhost";

    protected override string PeerCore => "ipv4:127.0.0.1:0";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore => _requestHeaders;

    protected override CancellationToken CancellationTokenCore => _cancellationToken;

    protected override Metadata ResponseTrailersCore => _responseTrailers;

    protected override Status StatusCore
    {
        get => _status;
        set => _status = value;
    }

    protected override WriteOptions? WriteOptionsCore
    {
        get => _writeOptions;
        set => _writeOptions = value;
    }

    protected override AuthContext AuthContextCore =>
        new(string.Empty, new Dictionary<string, List<AuthProperty>>());

    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException("Propagation tokens are not used by these service tests.");

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}
