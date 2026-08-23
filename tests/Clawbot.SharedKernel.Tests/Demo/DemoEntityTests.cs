using Clawbot.SharedKernel.Demo;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Demo;

public sealed class DemoOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new DemoOptions();

        opts.Mode.Should().BeFalse();
        opts.MaskPii.Should().BeTrue();
        opts.SkipHmac.Should().BeFalse();
        opts.SseReplayCount.Should().Be(10);
        opts.WatchdogIntervalSeconds.Should().Be(60);
        opts.TraceTtlMinutes.Should().Be(60);
    }

    [Fact]
    public void EffectiveTtlMinutes_ClampsToRange()
    {
        new DemoOptions { TraceTtlMinutes = 1 }.EffectiveTtlMinutes.Should().Be(5);
        new DemoOptions { TraceTtlMinutes = 9999 }.EffectiveTtlMinutes.Should().Be(1440);
        new DemoOptions { TraceTtlMinutes = 300 }.EffectiveTtlMinutes.Should().Be(300);
    }

    [Fact]
    public void WatchdogInterval_MinimumIs10Seconds()
    {
        new DemoOptions { WatchdogIntervalSeconds = 1 }.WatchdogInterval.Should().Be(TimeSpan.FromSeconds(10));
        new DemoOptions { WatchdogIntervalSeconds = 120 }.WatchdogInterval.Should().Be(TimeSpan.FromSeconds(120));
    }
}

public sealed class DemoRuntimeConfigTests
{
    [Fact]
    public void IsTokenConfigured_TrueWhenSet()
    {
        var config = new DemoRuntimeConfig { PancakeAccessToken = "tok" };
        config.IsTokenConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsTokenConfigured_FalseWhenEmpty()
    {
        new DemoRuntimeConfig().IsTokenConfigured.Should().BeFalse();
    }

    [Fact]
    public void EffectiveAutoReplyText_DefaultFallback()
    {
        new DemoRuntimeConfig().EffectiveAutoReplyText.Should().Contain("Cảm ơn");
    }

    [Fact]
    public void EffectiveAutoReplyText_CustomValue()
    {
        var config = new DemoRuntimeConfig { AutoReplyText = "Custom reply" };
        config.EffectiveAutoReplyText.Should().Be("Custom reply");
    }

    [Fact]
    public void IsSecretConfigured_ReflectsWebhookSecret()
    {
        new DemoRuntimeConfig().IsSecretConfigured.Should().BeFalse();
        new DemoRuntimeConfig { PancakeWebhookSecret = "s3cr3t" }
            .IsSecretConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsPageTokenConfigured_ReflectsPageAccessToken()
    {
        new DemoRuntimeConfig().IsPageTokenConfigured.Should().BeFalse();
        new DemoRuntimeConfig { PancakePageAccessToken = "page-token" }
            .IsPageTokenConfigured.Should().BeTrue();
    }

    [Fact]
    public void PancakeEndpointFields_RoundTrip()
    {
        var config = new DemoRuntimeConfig
        {
            PancakePageId = "page-1",
            PancakeBaseUrl = "https://pages.fm/api",
        };

        config.PancakePageId.Should().Be("page-1");
        config.PancakeBaseUrl.Should().Be("https://pages.fm/api");
    }
}

public sealed class DemoTraceTests
{
    [Fact]
    public void AddStep_SetsTimestampAndAppends()
    {
        var trace = new DemoTrace { TraceId = "t-1" };

        trace.AddStep(new DemoTraceStep { Layer = "agent", Status = DemoTraceStepStatus.Success });

        trace.Steps.Should().HaveCount(1);
        trace.Steps[0].Layer.Should().Be("agent");
        trace.Steps[0].TimestampUtc.Should().NotBeNull();
    }

    [Fact]
    public void DefaultStatus_IsPending()
    {
        var trace = new DemoTrace { TraceId = "t-2" };
        trace.Status.Should().Be(DemoTraceStatus.Pending);
    }

    [Fact]
    public void AddStep_PreservesCallerSuppliedTimestamp()
    {
        var trace = new DemoTrace { TraceId = "t-3" };
        var stamped = new DateTime(2026, 8, 17, 5, 0, 0, DateTimeKind.Utc);

        trace.AddStep(new DemoTraceStep { Layer = "gateway", TimestampUtc = stamped });

        trace.Steps[0].TimestampUtc.Should().Be(stamped);
    }

    [Fact]
    public void AddStep_KeepsInsertionOrder()
    {
        var trace = new DemoTrace { TraceId = "t-4" };

        trace.AddStep(new DemoTraceStep { Layer = "webhook" });
        trace.AddStep(new DemoTraceStep { Layer = "agent" });
        trace.AddStep(new DemoTraceStep { Layer = "channel" });

        trace.Steps.Select(step => step.Layer).Should().Equal("webhook", "agent", "channel");
    }

    [Fact]
    public void Defaults_HaveEmptyStepsAndErrors()
    {
        var trace = new DemoTrace { TraceId = "t-5" };

        trace.Steps.Should().BeEmpty();
        trace.Errors.Should().BeEmpty();
        trace.CompletedAtUtc.Should().BeNull();
        trace.TotalDurationMs.Should().BeNull();
    }

    [Fact]
    public void Completion_RecordsStatusAndDuration()
    {
        var trace = new DemoTrace { TraceId = "t-6" };
        var completedAt = new DateTime(2026, 8, 17, 6, 0, 0, DateTimeKind.Utc);

        trace.Status = DemoTraceStatus.Completed;
        trace.CompletedAtUtc = completedAt;
        trace.TotalDurationMs = 1250;
        trace.Errors.Add("cảnh báo nhỏ");

        trace.Status.Should().Be(DemoTraceStatus.Completed);
        trace.CompletedAtUtc.Should().Be(completedAt);
        trace.TotalDurationMs.Should().Be(1250);
        trace.Errors.Should().ContainSingle();
    }
}

public sealed class DemoTraceStepTests
{
    [Fact]
    public void Defaults_ArePendingWithEmptyOutput()
    {
        var step = new DemoTraceStep { Layer = "agent" };

        step.Layer.Should().Be("agent");
        step.Status.Should().Be(DemoTraceStepStatus.Pending);
        step.DurationMs.Should().BeNull();
        step.TimestampUtc.Should().BeNull();
        step.Reason.Should().BeNull();
        step.LinkedTraceId.Should().BeNull();
        step.Output.Should().BeEmpty();
    }

    [Fact]
    public void FailedStep_CarriesReasonAndLinkedTrace()
    {
        var step = new DemoTraceStep
        {
            Layer = "channel",
            Status = DemoTraceStepStatus.Failed,
            DurationMs = 42,
            Reason = "token_expired",
            LinkedTraceId = "agent-trace-9",
        };
        step.Output["httpStatus"] = 401;

        step.Status.Should().Be(DemoTraceStepStatus.Failed);
        step.DurationMs.Should().Be(42);
        step.Reason.Should().Be("token_expired");
        step.LinkedTraceId.Should().Be("agent-trace-9");
        step.Output["httpStatus"].Should().Be(401);
    }
}
