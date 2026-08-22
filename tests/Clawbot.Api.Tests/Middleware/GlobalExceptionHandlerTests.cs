using System.Text.Json;
using Clawbot.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Api.Tests.Middleware;

/// <summary>
/// GlobalExceptionHandler.TryHandleAsync: exception thường -> 500 + errorCode internal_error;
/// BadHttpRequestException -> 400 + errorCode request.invalid_parameter; request đã bị client
/// hủy (RequestAborted cancelled) -> 499 và không ghi body.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    private static GlobalExceptionHandler CreateHandler() =>
        new(NullLogger<GlobalExceptionHandler>.Instance);

    private static DefaultHttpContext CreateHttpContext()
    {
        // WriteAsJsonAsync đọc IOptions<JsonOptions> qua RequestServices; cấp một provider rỗng
        // để nó rơi về JsonSerializerOptions mặc định (camelCase) thay vì ném NullReferenceException.
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<JsonElement> ReadResponseBodyAsJsonAsync(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Returns500WithInternalErrorCode()
    {
        var handler = CreateHandler();
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        httpContext.Response.ContentType.Should().StartWith("application/json");
        var body = await ReadResponseBodyAsJsonAsync(httpContext);
        body.GetProperty("errorCode").GetString().Should().Be("internal_error");
        body.GetProperty("requestId").GetString().Should().Be(httpContext.TraceIdentifier);
    }

    [Fact]
    public async Task TryHandleAsync_BadHttpRequestException_Returns400WithInvalidParameterCode()
    {
        var handler = CreateHandler();
        var httpContext = CreateHttpContext();

        var handled = await handler.TryHandleAsync(
            httpContext, new BadHttpRequestException("bad payload"), CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadResponseBodyAsJsonAsync(httpContext);
        body.GetProperty("errorCode").GetString().Should().Be("request.invalid_parameter");
    }

    [Fact]
    public async Task TryHandleAsync_RequestAlreadyAborted_Returns499AndWritesNoBody()
    {
        var handler = CreateHandler();
        var httpContext = CreateHttpContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        httpContext.RequestAborted = cts.Token;

        var handled = await handler.TryHandleAsync(
            httpContext, new OperationCanceledException("client gone"), CancellationToken.None);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(499);
        httpContext.Response.Body.Position.Should().Be(0);
    }

    [Fact]
    public async Task TryHandleAsync_ResponseAlreadyStarted_ReturnsFalseWithoutOverwritingStatusCode()
    {
        var handler = CreateHandler();
        var httpContext = CreateHttpContext();
        // HttpResponseFeature mặc định của DefaultHttpContext (không có server thật đứng sau) luôn
        // báo HasStarted=false; phải tự cấp 1 feature giả báo true để mô phỏng response đã flush.
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var handled = await handler.TryHandleAsync(
            httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeFalse();
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
