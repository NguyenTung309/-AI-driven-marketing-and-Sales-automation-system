using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class HangfireModuleTests
{
    [Fact]
    public void ScheduleClawbotJobs_schedules_kpi_rollup_at_0030()
    {
        var recurring = Substitute.For<IRecurringJobManager>();
        using var services = new ServiceCollection()
            .AddSingleton(recurring)
            .BuildServiceProvider();

        Clawbot.Infrastructure.Jobs.HangfireModule.ScheduleClawbotJobs(services);

        recurring.Received().AddOrUpdate(
            "kpi-daily-rollup",
            Arg.Any<Job>(),
            "30 0 * * *",
            Arg.Any<RecurringJobOptions>());
    }

    [Fact]
    public void ScheduleClawbotJobs_schedules_analytics_alert_and_forecast_jobs()
    {
        var recurring = Substitute.For<IRecurringJobManager>();
        using var services = new ServiceCollection()
            .AddSingleton(recurring)
            .BuildServiceProvider();

        Clawbot.Infrastructure.Jobs.HangfireModule.ScheduleClawbotJobs(services);

        recurring.Received().AddOrUpdate(
            "kpi-anomaly-alert",
            Arg.Any<Job>(),
            "0 * * * *",
            Arg.Any<RecurringJobOptions>());
        recurring.Received().AddOrUpdate(
            "kpi-forecast-precompute",
            Arg.Any<Job>(),
            "45 0 * * *",
            Arg.Any<RecurringJobOptions>());
    }

    [Fact]
    public void ScheduleClawbotJobs_schedules_daily_report_push_at_0730_vietnam_time()
    {
        var recurring = Substitute.For<IRecurringJobManager>();
        using var services = new ServiceCollection()
            .AddSingleton(recurring)
            .BuildServiceProvider();

        Clawbot.Infrastructure.Jobs.HangfireModule.ScheduleClawbotJobs(services);

        recurring.Received().AddOrUpdate(
            "daily-report-push",
            Arg.Any<Job>(),
            "30 7 * * *",
            Arg.Is<RecurringJobOptions>(o => o.TimeZone.BaseUtcOffset == TimeSpan.FromHours(7)));
    }

    [Fact]
    public void ScheduleClawbotJobs_schedules_ads_rule_evaluation_hourly()
    {
        var recurring = Substitute.For<IRecurringJobManager>();
        using var services = new ServiceCollection()
            .AddSingleton(recurring)
            .BuildServiceProvider();

        Clawbot.Infrastructure.Jobs.HangfireModule.ScheduleClawbotJobs(services);

        recurring.Received().AddOrUpdate(
            "ads-rule-evaluation",
            Arg.Any<Job>(),
            "0 * * * *",
            Arg.Any<RecurringJobOptions>());
    }
}
