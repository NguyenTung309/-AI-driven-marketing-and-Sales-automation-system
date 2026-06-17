using System.Security.Cryptography;
using System.Text;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Docs;
using Clawbot.Agents.Core.Docs;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Documents;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using CoreDocs = Clawbot.Agents.Core.Docs;

namespace Clawbot.AgentService.Tests.Services;

public sealed class DocsAgentGrpcServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Generate_extracts_phone_from_recent_conversation_when_contact_phone_is_missing()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var template = DocumentTemplate.Create(
            TenantId,
            "quote-v1",
            "quote",
            "Bao gia cho {{customer_name}}\nPhone={{contact_phone}}",
            Now);
        var contact = Contact.Create(TenantId, "Visitor 123", Now);
        var conversation = Conversation.Open(TenantId, "zalo", "thread-1", Now, contact.Id);
        conversation.AppendMessage(
            "in",
            "contact",
            "So dien thoai cua em la 0912345678, tu van giup em goi HSK4.",
            "text",
            Now.AddMinutes(1));
        fx.Db.AddRange(template, contact, conversation);
        await fx.Db.SaveChangesAsync();

        var renderer = new CapturingRenderer();
        var storage = new CapturingStorage();
        var service = new DocsAgentGrpcService(
            new CoreDocs.DocsAgent(new SimpleTemplateEngine(), renderer),
            storage,
            fx.Db,
            new FixedClock(Now));

        var response = await service.Generate(new DocGenerateRequest
        {
            TenantId = TenantId.ToString(),
            TemplateCode = "quote-v1",
            ContactId = contact.Id.ToString(),
        }, TestServerCallContext.Create());

        response.FileUrl.Should().StartWith("https://docs.example/");
        renderer.ResolvedBody.Should().Contain("Phone=0912345678");
        var saved = await fx.Db.GeneratedDocuments.IgnoreQueryFilters().SingleAsync();
        saved.ContactId.Should().Be(contact.Id);
        saved.ExpiresAt.Should().Be(Now.AddDays(GeneratedDocument.LinkValidityDays));
    }

    [Fact]
    public async Task Generate_merges_latest_deployed_kb_content_into_brochure_template()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var template = DocumentTemplate.Create(
            TenantId,
            "brochure-hsk4",
            "brochure",
            "Brochure\n{{knowledge}}\nCodes={{kb_module_codes}}",
            Now);
        var module = KbModule.Create(TenantId, "hsk4", "HSK 4", Now);
        var oldVersion = KbVersion.Create(module.Id, 1, "Old HSK4 pricing", Now.AddDays(-2));
        oldVersion.Deploy(Now.AddDays(-2));
        var deployedVersion = KbVersion.Create(module.Id, 2, "HSK4 intensive course: 8 weeks, small group.", Now.AddDays(-1));
        deployedVersion.Deploy(Now.AddDays(-1));
        var draftVersion = KbVersion.Create(module.Id, 3, "Draft-only content must not render.", Now);
        fx.Db.AddRange(template, module, oldVersion, deployedVersion, draftVersion);
        await fx.Db.SaveChangesAsync();

        var renderer = new CapturingRenderer();
        var service = new DocsAgentGrpcService(
            new CoreDocs.DocsAgent(new SimpleTemplateEngine(), renderer),
            new CapturingStorage(),
            fx.Db,
            new FixedClock(Now));

        await service.Generate(new DocGenerateRequest
        {
            TenantId = TenantId.ToString(),
            TemplateCode = "brochure-hsk4",
        }, TestServerCallContext.Create());

        renderer.ResolvedBody.Should().Contain("HSK4 intensive course: 8 weeks, small group.");
        renderer.ResolvedBody.Should().Contain("Codes=hsk4");
        renderer.ResolvedBody.Should().NotContain("Old HSK4 pricing");
        renderer.ResolvedBody.Should().NotContain("Draft-only content must not render.");
    }

    private sealed class CapturingRenderer : IDocumentRenderer
    {
        public string ResolvedBody { get; private set; } = string.Empty;

        public byte[] Render(string resolvedBody, DocBranding branding, string docType)
        {
            ResolvedBody = resolvedBody;
            return Encoding.ASCII.GetBytes("%PDF-1.7\nRendered test document");
        }
    }

    private sealed class CapturingStorage : IDocumentStorage
    {
        public Task<string> SaveAsync(byte[] content, string fileName, CancellationToken ct = default)
        {
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            return Task.FromResult($"https://docs.example/{fileName}?hash={hash}");
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
