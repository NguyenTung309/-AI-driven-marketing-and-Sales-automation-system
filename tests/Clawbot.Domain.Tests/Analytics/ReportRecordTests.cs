using Clawbot.Domain.Analytics;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Analytics;

public sealed class ReportRecordTests
{
    [Fact]
    public void ReportColumn_SetsAllProperties()
    {
        var col = new ReportColumn("revenue", "Doanh thu", "number");

        col.Key.Should().Be("revenue");
        col.Label.Should().Be("Doanh thu");
        col.Type.Should().Be("number");
    }

    [Fact]
    public void ReportChart_SetsXAndSeries()
    {
        var chart = new ReportChart("date", ["leads", "customers"]);

        chart.X.Should().Be("date");
        chart.Series.Should().Equal("leads", "customers");
    }

    [Fact]
    public void ReportArtifactPayload_SetsAllFields()
    {
        var columns = new[] { new ReportColumn("k", "l", "text") };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["k"] = "v" }
        };
        var chart = new ReportChart("x", ["s"]);

        var payload = new ReportArtifactPayload("snapshot", columns, rows, chart);

        payload.Kind.Should().Be("snapshot");
        payload.Columns.Should().HaveCount(1);
        payload.Rows.Should().HaveCount(1);
        payload.Chart.Should().NotBeNull();
    }

    [Fact]
    public void ReportArtifactPayload_NullChart_Allowed()
    {
        var payload = new ReportArtifactPayload("anomaly", [], [], null);

        payload.Chart.Should().BeNull();
    }
}
