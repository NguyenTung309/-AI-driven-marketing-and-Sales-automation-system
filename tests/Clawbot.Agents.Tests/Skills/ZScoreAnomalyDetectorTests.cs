using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Z-score anomaly: cần >=3 điểm lịch sử mới chấm; stddev 0 => không anomaly; vượt ngưỡng => anomaly.
public sealed class ZScoreAnomalyDetectorTests
{
    private static ZScoreAnomalyDetector NewDetector() => new();

    private static (DateTimeOffset, double) Point(int dayOffset, double value)
        => (new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset), value);

    [Fact]
    public void Name_IsAnomalyDetection()
    {
        NewDetector().Name.Should().Be("anomaly-detection");
    }

    [Fact]
    public async Task Score_NullSeries_Throws()
    {
        var act = async () => await NewDetector().ScoreAsync(null!, 3d, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public async Task Score_NonPositiveThreshold_Throws(double threshold)
    {
        var series = new[] { Point(0, 1d) };

        var act = async () => await NewDetector().ScoreAsync(series, threshold, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Score_FewerThan3History_NoAnomaly()
    {
        // 3 điểm đầu không đủ window (previous.Length < 3) => IsAnomaly false, ZScore 0.
        var series = new[] { Point(0, 10d), Point(1, 11d), Point(2, 12d) };

        var result = await NewDetector().ScoreAsync(series, 3d, CancellationToken.None);

        result.Should().OnlyContain(p => !p.IsAnomaly && p.ZScore == 0d);
    }

    [Fact]
    public async Task Score_FlatSeries_ZeroStdDev_NoAnomaly()
    {
        var series = Enumerable.Range(0, 8).Select(i => Point(i, 5d)).ToList();

        var result = await NewDetector().ScoreAsync(series, 2d, CancellationToken.None);

        result.Should().OnlyContain(p => !p.IsAnomaly);
    }

    [Fact]
    public async Task Score_Spike_FlaggedAnomaly()
    {
        var series = new List<(DateTimeOffset, double)>
        {
            Point(0, 10d), Point(1, 10d), Point(2, 11d), Point(3, 9d), Point(4, 10d),
            Point(5, 100d), // spike lớn so với lịch sử ổn định ~10
        };

        var result = await NewDetector().ScoreAsync(series, 2d, CancellationToken.None);

        var spike = result[^1];
        spike.IsAnomaly.Should().BeTrue();
        Math.Abs(spike.ZScore).Should().BeGreaterThan(2d);
    }

    [Fact]
    public async Task Score_OrdersByTimestamp()
    {
        // Truyền lộn xộn thứ tự thời gian; kết quả phải sắp theo At tăng dần.
        var series = new[] { Point(3, 9d), Point(0, 10d), Point(2, 11d), Point(1, 10d) };

        var result = await NewDetector().ScoreAsync(series, 3d, CancellationToken.None);

        result.Select(p => p.At).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Score_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var series = Enumerable.Range(0, 8).Select(i => Point(i, i)).ToList();

        var act = async () => await NewDetector().ScoreAsync(series, 3d, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
