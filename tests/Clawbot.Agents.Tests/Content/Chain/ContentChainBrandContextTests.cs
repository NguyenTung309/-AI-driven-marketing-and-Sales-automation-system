using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Content.Chain;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Tests.Content.Chain;

// Bài "Cổ Loa" khách báo do chuỗi 4 bước viết ra mà không hề biết Học Bá bán khóa gì.
// Bối cảnh thương hiệu phải nằm trong system prompt của cả 4 bước, nhưng hợp đồng JSON phải đứng cuối
// để parser từng cổng không vỡ.
public sealed class ContentChainBrandContextTests
{
    [Fact]
    public void EveryStep_CarriesHocBaBrandContext()
    {
        // Arrange
        var context = SampleContext();

        // Act
        var prompts = BuildSteps().Select(step => step.BuildPrompt(context)).ToList();

        // Assert
        prompts.Should().HaveCount(4);
        prompts.Should().AllSatisfy(prompt =>
        {
            prompt.System.Should().Contain("BỐI CẢNH THƯƠNG HIỆU");
            prompt.System.Should().Contain("HSK từ 1 đến 6");
            prompt.System.Should().Contain("Tiếng Trung Công Xưởng");
        });
    }

    [Theory]
    [InlineData(PlanStep.Id, "\"objective\":\"awareness|lead_gen|nurture|promo\"")]
    [InlineData(OutlineStep.Id, "\"proofPoints\":[{\"claim\":\"string\",\"citationId\":1}]")]
    [InlineData(PackageStep.Id, "\"caption\":\"string\",\"hashtags\":[\"string\"]")]
    public void JsonSteps_KeepOutputContractAsTheLastInstruction(string stepId, string schemaFragment)
    {
        // Arrange
        var step = BuildSteps().Single(candidate => candidate.StepId == stepId);

        // Act
        var prompt = step.BuildPrompt(SampleContext());

        // Assert
        prompt.System.Should().Contain(schemaFragment);
        var brandIndex = prompt.System.IndexOf("BỐI CẢNH THƯƠNG HIỆU", StringComparison.Ordinal);
        brandIndex.Should().BeGreaterThanOrEqualTo(0);
        brandIndex.Should().BeLessThan(prompt.System.IndexOf(schemaFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void PromptOverride_StillWins_SoTenantCustomizationIsNotLost()
    {
        // Arrange
        var options = Options.Create(new ContentChainOptions
        {
            Enabled = true,
            Steps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [WriteStep.Id] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ContentChainOptions.DefaultKey] = "PERSONA_TUY_CHINH_CUA_TENANT",
                },
            },
        });

        // Act
        var prompt = new WriteStep(options).BuildPrompt(SampleContext());

        // Assert
        prompt.System.Should().Contain("PERSONA_TUY_CHINH_CUA_TENANT");
        prompt.System.Should().Contain(AgentPromptPacks.BrandContext);
    }

    private static IReadOnlyList<IContentChainStep> BuildSteps()
    {
        var options = Options.Create(new ContentChainOptions { Enabled = true });
        return [new PlanStep(options), new OutlineStep(options), new WriteStep(options), new PackageStep(options)];
    }

    private static ContentChainContext SampleContext() =>
        new(
            TenantId: Guid.NewGuid(),
            Platform: "facebook",
            Brief: "Viết bài về thành Cổ Loa",
            Knowledge: "[1] (module=kb, score=0.90) Khóa HSK 4 khai giảng tháng này",
            PlatformTemplate: "Giọng Facebook thân thiện",
            Limits: new ContentChainLimits(Min: 10, Max: 5000),
            ChunkCount: 1,
            Plan: new ContentPlan(
                Objective: "awareness",
                Audience: "người đi làm",
                KeyMessage: "Học tiếng Trung để đọc tư liệu lịch sử",
                Offer: null,
                Tone: "thân thiện",
                Cta: new ContentPlanCta("inbox", "Nhắn tin để được tư vấn"),
                MustInclude: [],
                MustAvoid: [],
                Language: "vi"),
            Body: "Thân bài mẫu.");
}
