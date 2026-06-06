using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Clawbot.Application.Common.Behaviors;

public sealed partial class AuditBehavior<TRequest, TResponse>(ILogger<AuditBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger = logger;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            sw.Stop();
            LogCommandSucceeded(_logger, name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogCommandFailed(_logger, ex, name, sw.ElapsedMilliseconds);
            throw;
        }
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "Command {Command} completed in {ElapsedMs}ms")]
    private static partial void LogCommandSucceeded(ILogger logger, string command, long elapsedMs);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Command {Command} failed after {ElapsedMs}ms")]
    private static partial void LogCommandFailed(ILogger logger, Exception ex, string command, long elapsedMs);
}
