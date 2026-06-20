using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContentImagePromptServiceTests
{
    [Fact]
    public async Task GenerateAsync_validates_and_normalizes_request_before_calling_generator()
    {
        var generator = new CapturingImagePromptGenerator(new ImagePromptResult(
            "Hero visual prompt",
            "no clutter",
            new Dictionary<string, string> { ["composition"] = "centered student" }));
        var sut = new ContentImagePromptService(generator, new LlmCallScope(), new FixedTenantAccessor(Guid.NewGuid()));

        var result = await sut.GenerateAsync(new GenerateImagePromptRequest(
            Brief: "  HSK4 opening campaign  ",
            Platform: " TikTok ",
            Style: "  clean realistic classroom  ",
            BrandTokens: ["red", " Red ", "Học Bá", ""]));

        generator.Requests.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ImagePromptRequest(
                "HSK4 opening campaign",
                "tiktok",
                "clean realistic classroom",
                ["red", "Học Bá"]));
        result.Prompt.Should().Be("Hero visual prompt");
        result.NegativePrompt.Should().Be("no clutter");
        result.Hints.Should().Contain("composition", "centered student");
    }

    [Fact]
    public async Task GenerateAsync_rejects_unsupported_platform()
    {
        var sut = new ContentImagePromptService(new CapturingImagePromptGenerator(
            new ImagePromptResult("prompt", string.Empty, new Dictionary<string, string>())), new LlmCallScope(), new FixedTenantAccessor(Guid.NewGuid()));

        var act = async () => await sut.GenerateAsync(new GenerateImagePromptRequest(
            Brief: "Campaign",
            Platform: "blog",
            Style: null,
            BrandTokens: []));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*unsupported platform*");
    }

    private sealed class CapturingImagePromptGenerator(ImagePromptResult result) : IImagePromptGenerator
    {
        public string Name => "capturing-image-prompt";
        public List<ImagePromptRequest> Requests { get; } = [];

        public Task<ImagePromptResult> GenerateAsync(ImagePromptRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test");

        public TenantContext Require() => Current!;
    }
}
