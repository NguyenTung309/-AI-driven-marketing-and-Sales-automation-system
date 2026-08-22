using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Api.Tests.Services;

public sealed class ContentImagePromptServiceTests
{
    [Fact]
    public async Task GenerateAsync_NullBrief_ThrowsArgumentException()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();

        var act1 = async () => await service.GenerateAsync(tenantId, new GenerateImagePromptRequest(null, "facebook", null, null));
        var act2 = async () => await service.GenerateAsync(tenantId, new GenerateImagePromptRequest("  ", "facebook", null, null));

        (await act1.Should().ThrowAsync<ArgumentException>()).WithMessage("*brief*");
        (await act2.Should().ThrowAsync<ArgumentException>()).WithMessage("*brief*");
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedPlatform_ThrowsArgumentException()
    {
        var service = CreateService();
        var act = async () => await service.GenerateAsync(Guid.NewGuid(), new GenerateImagePromptRequest("nice brief", "tiktok", null, null));
        (await act.Should().ThrowAsync<ArgumentException>()).WithMessage("*platform*");
    }

    [Fact]
    public async Task GenerateAsync_ValidRequest_NormalizesAndDelegates()
    {
        var generator = Substitute.For<IImagePromptGenerator>();
        generator.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImagePromptResult("prompt X", "negative Y", new Dictionary<string, string>()));

        var service = CreateService(generator);
        var request = new GenerateImagePromptRequest("  my brief  ", "facebook", null, null);

        var result = await service.GenerateAsync(Guid.NewGuid(), request);

        result.Prompt.Should().Be("prompt X");
        result.NegativePrompt.Should().Be("negative Y");
        await generator.Received(1).GenerateAsync(
            Arg.Is<ImagePromptRequest>(r => r.Brief == "my brief" && r.Platform == "facebook" && r.Style == "brand-safe education marketing"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_CustomStyle_PreservesStyle()
    {
        var generator = Substitute.For<IImagePromptGenerator>();
        generator.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImagePromptResult("p", "n", new Dictionary<string, string>()));

        var service = CreateService(generator);

        await service.GenerateAsync(Guid.NewGuid(), new GenerateImagePromptRequest("brief", "zalo", "  neon vaporwave  ", null));

        await generator.Received(1).GenerateAsync(
            Arg.Is<ImagePromptRequest>(r => r.Style == "neon vaporwave"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_BrandTokens_DedupesCaseInsensitiveAndCapsAtTwelve()
    {
        var generator = Substitute.For<IImagePromptGenerator>();
        generator.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImagePromptResult("p", "n", new Dictionary<string, string>()));

        var service = CreateService(generator);
        var tokens = Enumerable.Range(0, 15).Select(i => $"token-{i}").ToList();
        tokens.Add("TOKEN-0"); // duplicate case-insensitive của token-0
        tokens.Add("  "); // whitespace-only bị lọc

        await service.GenerateAsync(Guid.NewGuid(), new GenerateImagePromptRequest("brief", "facebook", null, tokens));

        await generator.Received(1).GenerateAsync(
            Arg.Is<ImagePromptRequest>(r => r.BrandTokens.Count == 12 && r.BrandTokens.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 12),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService();
        var act = async () => await service.GenerateAsync(Guid.NewGuid(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GenerateAsync_PlatformCaseInsensitive_Succeeds()
    {
        var generator = Substitute.For<IImagePromptGenerator>();
        generator.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImagePromptResult("p", "n", new Dictionary<string, string>()));
        var service = CreateService(generator);

        var result = await service.GenerateAsync(Guid.NewGuid(), new GenerateImagePromptRequest("brief", "Facebook", null, null));

        result.Should().NotBeNull();
        await generator.Received(1).GenerateAsync(
            Arg.Is<ImagePromptRequest>(r => r.Platform == "facebook"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_HttpPath_UsesTenantAccessor()
    {
        var generator = Substitute.For<IImagePromptGenerator>();
        generator.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ImagePromptResult("p", "n", new Dictionary<string, string>()));
        var expectedTenant = Guid.NewGuid();
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Require().Returns(new TenantContext(expectedTenant, "test"));
        var llmScope = Substitute.For<ILlmCallScope>();
        llmScope.Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(Substitute.For<IDisposable>());
        var service = new ContentImagePromptService(generator, llmScope, tenants);

        await service.GenerateAsync(new GenerateImagePromptRequest("brief", "facebook", null, null));

        llmScope.Received(1).Begin(expectedTenant, Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>());
    }

    private static ContentImagePromptService CreateService(IImagePromptGenerator? generator = null)
    {
        var gen = generator ?? Substitute.For<IImagePromptGenerator>();
        // Mặc định stub generator để không throw khi test nhánh validation
        if (generator is null)
            gen.GenerateAsync(Arg.Any<ImagePromptRequest>(), Arg.Any<CancellationToken>())
                .Returns(new ImagePromptResult("p", "n", new Dictionary<string, string>()));
        var llmScope = Substitute.For<ILlmCallScope>();
        llmScope.Begin(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<Guid?>())
            .Returns(Substitute.For<IDisposable>());
        var tenants = Substitute.For<ITenantAccessor>();
        return new ContentImagePromptService(gen, llmScope, tenants);
    }
}
