using MediatR;
using Microsoft.Extensions.Logging;

namespace Clawbot.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        HandlingRequest(logger, name, null);
        var response = await next();
        HandledRequest(logger, name, null);
        return response;
    }

    private static readonly Action<ILogger, string, Exception?> HandlingRequest =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(HandlingRequest)), "Handling {Request}");

    private static readonly Action<ILogger, string, Exception?> HandledRequest =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(HandledRequest)), "Handled {Request}");
}
