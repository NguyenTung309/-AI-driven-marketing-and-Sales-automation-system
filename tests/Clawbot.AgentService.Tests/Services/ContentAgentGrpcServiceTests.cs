using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Content;
using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.Content;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CoreContent = Clawbot.Agents.Core.Content;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ContentAgentGrpcServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Generate_persists_content_item_and_returns_saved_variant()
    {
        var tenantId = Guid.NewGuid();
        var briefId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var service = BuildService(fx);

        var response = await service.Generate(
            new ContentRequest
            {
                TenantId = tenantId.ToString(),
                BriefId = briefId.ToString(),
                Channel = "facebook",
                Brief = "HSK launch",
            },
            TestServerCallContext.Create());

        response.ContentId.Should().NotBeEmpty();
        response.Title.Should().Be("facebook");
        response.Body.Should().Be("Draft for facebook: Brief=HSK launch");
        response.Variants.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ContentVariant
            {
                Platform = "facebook",
                Title = "facebook",
                Body = "Draft for facebook: Brief=HSK launch",
                ContentId = response.ContentId,
            });

        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Id.ToString().Should().Be(response.ContentId);
        saved.TenantId.Should().Be(tenantId);
        saved.BriefId.Should().Be(briefId);
        saved.Platform.Should().Be("facebook");
        saved.Body.Should().Be("Draft for facebook: Brief=HSK launch");
        saved.CreatedAt.Should().Be(Now);
        saved.UpdatedAt.Should().Be(Now);
        saved.Status.Should().Be("draft");
    }

    [Fact]
    public async Task Repurpose_persists_one_item_per_normalized_target_and_returns_variants()
    {
        var tenantId = Guid.NewGuid();
        var briefId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var source = ContentItem.Create(tenantId, "facebook", "Original source", createdBy: null, Now.AddDays(-1), briefId);
        fx.Db.ContentItems.Add(source);
        await fx.Db.SaveChangesAsync();
        var service = BuildService(fx);

        var response = await service.Repurpose(
            new RepurposeRequest
            {
                TenantId = tenantId.ToString(),
                ContentId = source.Id.ToString(),
                TargetChannels = { "tiktok", " youtube ", "TIKTOK" },
            },
            TestServerCallContext.Create());

        response.Title.Should().Be("tiktok");
        response.Body.Should().Be("Draft for tiktok: Brief=Original source");
        response.Variants.Select(v => v.Platform).Should().Equal("tiktok", "youtube");
        response.Variants.Select(v => v.Body).Should().Equal(
            "Draft for tiktok: Brief=Original source",
            "Draft for youtube: Brief=Original source");

        var saved = (await fx.Db.ContentItems.IgnoreQueryFilters().ToListAsync())
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Platform)
            .ToList();
        saved.Should().HaveCount(3);
        saved[0].Id.Should().Be(source.Id);
        var repurposed = saved.Where(i => i.Id != source.Id).OrderBy(i => i.Platform).ToList();
        repurposed.Select(i => i.Platform).Should().Equal("tiktok", "youtube");
        repurposed.Should().OnlyContain(i =>
            i.TenantId == tenantId
            && i.BriefId == briefId
            && i.CreatedAt == Now
            && i.UpdatedAt == Now
            && i.Status == "draft");
        repurposed.Select(i => i.Body).Should().BeEquivalentTo(
            ["Draft for tiktok: Brief=Original source", "Draft for youtube: Brief=Original source"]);
        response.ContentId.Should().Be(repurposed.Single(i => i.Platform == "tiktok").Id.ToString());
    }

    private static ContentAgentGrpcService BuildService(AgentServiceTestAppDb fx)
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var templates = Substitute.For<CoreContent.IPromptTemplateProvider>();
        templates.GetTemplate(Arg.Any<string>()).Returns("Brief={{brief}}");
        var llm = Substitute.For<CoreContent.IContentLlmClient>();
        llm.CompleteAsync(Arg.Any<CoreContent.ContentLlmRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<CoreContent.ContentLlmRequest>(0);
                return new CoreContent.ContentLlmResult($"Draft for {request.Platform}: {request.Prompt}", 11, 7);
            });
        var agent = new CoreContent.ContentAgent(rag, templates, llm);

        return new ContentAgentGrpcService(
            agent,
            fx.Db,
            new FixedClock(Now),
            NullLogger<ContentAgentGrpcService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
