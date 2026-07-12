using System.Text.Json;
using Clawbot.Infrastructure.Jobs;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class MetaEngagementSyncJobTests
{
    [Theory]
    [InlineData("https://www.facebook.com/1122334455_9988776655", "1122334455_9988776655")]
    [InlineData("https://www.facebook.com/1122334455_9988776655/", "1122334455_9988776655")]
    [InlineData("https://zalo.me/p/sometoken", null)]  // no underscore -> not a FB post id
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractPostId_returns_underscore_tail_only(string? url, string? expected)
    {
        Assert.Equal(expected, MetaEngagementSyncJob.ExtractPostId(url));
    }

    [Fact]
    public void ReadCounts_reads_summary_totals()
    {
        using var doc = JsonDocument.Parse(
            """{"likes":{"summary":{"total_count":42}},"comments":{"summary":{"total_count":7}},"id":"x"}""");

        var (likes, comments) = MetaEngagementSyncJob.ReadCounts(doc.RootElement);

        Assert.Equal(42, likes);
        Assert.Equal(7, comments);
    }

    [Fact]
    public void ReadCounts_returns_null_when_summary_missing()
    {
        using var doc = JsonDocument.Parse("""{"id":"x"}""");

        var (likes, comments) = MetaEngagementSyncJob.ReadCounts(doc.RootElement);

        Assert.Null(likes);
        Assert.Null(comments);
    }
}
