using Clawbot.Infrastructure.Observability;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Observability;

public sealed class TelemetryModuleTests
{
    [Fact]
    public void Http_server_duration_histogram_has_30_second_slo_bucket_for_p95_tracking()
    {
        TelemetryModule.HttpServerRequestDurationInstrumentName.Should().Be("http.server.request.duration");
        TelemetryModule.HttpServerDurationSloSeconds.Should().Be(30d);

        var config = TelemetryModule.CreateHttpServerDurationHistogramConfiguration();
        var boundaries = config.Boundaries.Should().NotBeNull().And.Subject!;

        boundaries.Should().BeInAscendingOrder();
        boundaries.Should().Contain(TelemetryModule.HttpServerDurationSloSeconds);
        boundaries.Should().Contain(value => value > TelemetryModule.HttpServerDurationSloSeconds);
    }
}
