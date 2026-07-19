using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Observability;
using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Observability;

public sealed class SystemLogSinkTests
{
    [Fact]
    public void Map_truncates_message_and_redacts_pii()
    {
        var pii = new RegexPiiRedactor();
        using var sink = new SystemLogSink(
            "Server=localhost;Database=clawbot;TrustServerCertificate=True",
            "api",
            pii,
            batchSize: 1000,
            flushInterval: TimeSpan.FromHours(1));

        // Phone at the start so truncation (2048) still leaves PII for redaction.
        var message = "call 0912345678 " + new string('x', 3000);
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            exception: new InvalidOperationException("boom"),
            messageTemplate: new MessageTemplateParser().Parse(message.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)),
            properties:
            [
                new LogEventProperty("StatusCode", new ScalarValue(500)),
                new LogEventProperty("RequestMethod", new ScalarValue("GET")),
                new LogEventProperty("RequestPath", new ScalarValue("/api/test")),
                new LogEventProperty("TraceId", new ScalarValue("req-abc")),
            ]);

        var row = sink.MapForTests(logEvent);
        row.Message.Length.Should().BeLessThanOrEqualTo(SystemLogSink.MaxMessageLength);
        row.Message.Should().NotContain("0912345678");
        row.Message.Should().Contain("[PHONE]");
        row.StatusCode.Should().Be(500);
        row.Method.Should().Be("GET");
        row.Path.Should().Be("/api/test");
        row.TraceId.Should().Be("req-abc");
        row.Exception.Should().Contain("InvalidOperationException");
        row.Level.Should().Be("Error");
        row.Source.Should().Be("api");
    }

    [Fact]
    public void Map_ignores_information_level_via_emit_filter()
    {
        // Emit drops < Warning before mapping — verified by no exception when conn string is invalid
        // because Information never reaches Map/flush.
        using var sink = new SystemLogSink(
            "Server=invalid;Database=x;TrustServerCertificate=True",
            "api",
            flushInterval: TimeSpan.FromHours(1));

        var info = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: new MessageTemplateParser().Parse("ok"),
            properties: Array.Empty<LogEventProperty>());

        var act = () => sink.Emit(info);
        act.Should().NotThrow();
    }
}
