using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentGenerationTraceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChainRunId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var contentItemId = Guid.NewGuid();
        var briefId = Guid.NewGuid();

        var trace = ContentGenerationTrace.Create(
            TenantId, ChainRunId, "step-1", "v2.0", "gpt-4o",
            1500, 800, 0.05m, 3200, "pass", "{\"key\":\"val\"}",
            Now, contentItemId, briefId);

        trace.TenantId.Should().Be(TenantId);
        trace.ChainRunId.Should().Be(ChainRunId);
        trace.ContentItemId.Should().Be(contentItemId);
        trace.BriefId.Should().Be(briefId);
        trace.StepId.Should().Be("step-1");
        trace.PromptVersion.Should().Be("v2.0");
        trace.Model.Should().Be("gpt-4o");
        trace.InputTokens.Should().Be(1500);
        trace.OutputTokens.Should().Be(800);
        trace.UsdCost.Should().Be(0.05m);
        trace.LatencyMs.Should().Be(3200);
        trace.GateResult.Should().Be("pass");
        trace.PayloadJson.Should().Be("{\"key\":\"val\"}");
        trace.CreatedAt.Should().Be(Now.ToUniversalTime());
    }

    [Fact]
    public void Create_ThrowsOnEmptyTenantId()
    {
        var act = () => ContentGenerationTrace.Create(
            Guid.Empty, ChainRunId, "s", "v", "m", 0, 0, 0m, 0, "r", null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ClampsStringsToMaxLength()
    {
        var longStep = new string('a', 100);
        var longModel = new string('b', 200);

        var trace = ContentGenerationTrace.Create(
            TenantId, ChainRunId, longStep, "v", longModel, 0, 0, 0m, 0, "r", null, Now);

        trace.StepId.Should().HaveLength(ContentGenerationTrace.StepIdMaxLength);
        trace.Model.Should().HaveLength(ContentGenerationTrace.ModelMaxLength);
    }

    [Fact]
    public void Create_ClampsNegativeTokensAndCostToZero()
    {
        var trace = ContentGenerationTrace.Create(
            TenantId, ChainRunId, "s", "v", "m", -10, -5, -0.01m, -100, "r", null, Now);

        trace.InputTokens.Should().Be(0);
        trace.OutputTokens.Should().Be(0);
        trace.UsdCost.Should().Be(0m);
        trace.LatencyMs.Should().Be(0);
    }

    [Fact]
    public void Create_AllowsNullPayloadJson()
    {
        var trace = ContentGenerationTrace.Create(
            TenantId, ChainRunId, "s", "v", "m", 0, 0, 0m, 0, "r", null, Now);

        trace.PayloadJson.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsWhitespaceFromStrings()
    {
        var trace = ContentGenerationTrace.Create(
            TenantId, ChainRunId, "  step  ", "  v1  ", "  model  ", 0, 0, 0m, 0, "  pass  ", null, Now);

        trace.StepId.Should().Be("step");
        trace.PromptVersion.Should().Be("v1");
        trace.Model.Should().Be("model");
        trace.GateResult.Should().Be("pass");
    }
}
