using Clawbot.Agents.Core.Skills.Nlp;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace Clawbot.Infrastructure.Observability;

public static class SystemLogSinkExtensions
{
    /// <summary>
    /// Persist Warning+ log events into dbo.system_logs. Safe to call when connection string is missing (no-op).
    /// </summary>
    public static LoggerConfiguration SystemLogs(
        this LoggerSinkConfiguration writeTo,
        string? connectionString,
        string source,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Warning)
    {
        ArgumentNullException.ThrowIfNull(writeTo);
        if (string.IsNullOrWhiteSpace(connectionString))
            return writeTo.Sink(new NullSink(), restrictedToMinimumLevel);

        var sink = new SystemLogSink(connectionString, source, new RegexPiiRedactor());
        return writeTo.Sink(sink, restrictedToMinimumLevel);
    }

    private sealed class NullSink : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) { }
    }
}
