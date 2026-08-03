using Clawbot.Agents.Contracts.Docs;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using CoreDocs = Clawbot.Agents.Core.Docs;

namespace Clawbot.AgentService.Services;

public sealed partial class DocsAgentGrpcService(
    CoreDocs.DocsAgent agent,
    CoreDocs.IDocumentStorage storage,
    AppDbContext db,
    IClock clock) : DocsAgent.DocsAgentBase
{
    private readonly CoreDocs.DocsAgent _agent = agent;
    private readonly CoreDocs.IDocumentStorage _storage = storage;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public override async Task<DocGenerateResponse> Generate(DocGenerateRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var ct = context.CancellationToken;

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (string.IsNullOrWhiteSpace(request.TemplateCode))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "template_code required"));

        var template = await _db.DocumentTemplates
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Code == request.TemplateCode && t.DeletedAt == null)
            .Select(t => new { t.Id, t.Code, t.DocType, t.TemplateHtml })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"template '{request.TemplateCode}' not found"));

        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.DisplayName)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "ClawBot";

        var branding = new CoreDocs.DocBranding(
            tenantName,
            LogoText: null,
            FooterNote: $"{tenantName} · Tài liệu tạo tự động bởi ClawBot",
            QrPayload: $"clawbot://doc/{tenantId:N}/{request.TemplateCode}");

        var vars = new Dictionary<string, string>(request.Vars, StringComparer.Ordinal);

        // Docs-1: auto-fill customer info from the linked contact (caller-supplied vars win).
        Guid? contactId = Guid.TryParse(request.ContactId, out var cid) && cid != Guid.Empty ? cid : null;
        if (contactId is not null)
        {
            var contact = await _db.Contacts.IgnoreQueryFilters()
                .Where(c => c.Id == contactId.Value && c.TenantId == tenantId)
                .Select(c => new { c.DisplayName, c.Phone, c.Email })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (contact is not null)
            {
                TryAddVar(vars, "contact_name", contact.DisplayName);
                TryAddVar(vars, "customer_name", contact.DisplayName);
                TryAddVar(vars, "contact_phone", contact.Phone);
                TryAddVar(vars, "contact_email", contact.Email);
            }

            var conversationVars = await ExtractConversationVarsAsync(tenantId, contactId.Value, ct).ConfigureAwait(false);
            TryAddVar(vars, "contact_name", conversationVars.Name);
            TryAddVar(vars, "customer_name", conversationVars.Name);
            TryAddVar(vars, "contact_phone", conversationVars.Phone);
            TryAddVar(vars, "contact_email", conversationVars.Email);
        }

        await AddKnowledgeVarsAsync(tenantId, vars, ct).ConfigureAwait(false);

        var renderRequest = new CoreDocs.DocsRenderRequest(
            tenantId, template.Code, template.DocType, template.TemplateHtml, vars, branding);

        CoreDocs.DocsRenderResult result;
        try
        {
            result = await _agent.RenderAsync(renderRequest, ct).ConfigureAwait(false);
        }
        catch (CoreDocs.DocsTemplateException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var now = _clock.UtcNow;
        var fileName = $"{template.Code}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}.pdf".ToLowerInvariant();
        var fileUrl = await _storage.SaveAsync(result.Pdf, fileName, ct: ct).ConfigureAwait(false);

        var doc = GeneratedDocument.Create(
            tenantId, template.Id, fileUrl, now,
            contactId: contactId, generatedBy: null, fileHash: result.Sha256);

        _db.GeneratedDocuments.Add(doc);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new DocGenerateResponse
        {
            DocumentId = doc.Id.ToString(),
            FileUrl = fileUrl,
            FileHash = result.Sha256,
            SizeBytes = result.SizeBytes,
            LatencyMs = result.LatencyMs,
        };
    }

    private static void TryAddVar(Dictionary<string, string> vars, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !vars.ContainsKey(key))
            vars[key] = value;
    }

    private async Task AddKnowledgeVarsAsync(Guid tenantId, Dictionary<string, string> vars, CancellationToken ct)
    {
        if (vars.ContainsKey("knowledge") && vars.ContainsKey("kb_content") && vars.ContainsKey("kb_module_codes"))
            return;

        var deployedVersions = await _db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.DeletedAt == null && m.Status == "active")
            .Join(_db.KbVersions.IgnoreQueryFilters().Where(v => v.Status == "deployed"),
                m => m.Id,
                v => v.KbModuleId,
                (m, v) => new
                {
                    m.Code,
                    m.Name,
                    v.Version,
                    v.ContentMd,
                    v.DeployedAt,
                    v.CreatedAt,
                })
            .ToListAsync(ct).ConfigureAwait(false);

        var latestByModule = deployedVersions
            .GroupBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(v => v.Version)
                .ThenByDescending(v => v.DeployedAt ?? v.CreatedAt)
                .First())
            .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (latestByModule.Count == 0)
            return;

        var knowledge = string.Join(
            "\n\n",
            latestByModule.Select(v => $"## {v.Name} ({v.Code})\n{v.ContentMd.Trim()}"));
        var moduleCodes = string.Join(",", latestByModule.Select(v => v.Code));

        TryAddVar(vars, "knowledge", knowledge);
        TryAddVar(vars, "kb_content", knowledge);
        TryAddVar(vars, "kb_module_codes", moduleCodes);
    }

    private async Task<ConversationVars> ExtractConversationVarsAsync(Guid tenantId, Guid contactId, CancellationToken ct)
    {
        var messages = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.ContactId == contactId && c.DeletedAt == null)
            .Join(_db.Messages.IgnoreQueryFilters(),
                c => c.Id,
                m => m.ConversationId,
                (_, m) => m)
            .Select(m => new { m.SentAt, Text = m.OriginalContent ?? m.Content })
            .ToListAsync(ct).ConfigureAwait(false);

        return ExtractConversationVars(messages
            .OrderByDescending(m => m.SentAt)
            .Take(20)
            .Select(m => m.Text));
    }

    private static ConversationVars ExtractConversationVars(IEnumerable<string> texts)
    {
        string? name = null;
        string? phone = null;
        string? email = null;

        foreach (var text in texts.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            phone ??= NormalizePhone(PhoneRegex().Match(text));
            email ??= NormalizeMatch(EmailRegex().Match(text));
            name ??= NormalizeName(NameRegex().Match(text));

            if (name is not null && phone is not null && email is not null)
                break;
        }

        return new ConversationVars(name, phone, email);
    }

    private static string? NormalizePhone(Match match)
    {
        if (!match.Success) return null;
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length >= 9 ? digits : null;
    }

    private static string? NormalizeMatch(Match match) =>
        match.Success ? match.Value.Trim() : null;

    private static string? NormalizeName(Match match)
    {
        if (!match.Success) return null;
        var name = match.Groups["name"].Value.Trim(' ', '.', ',', ';', ':', '-', '!');
        return name.Length >= 2 ? name : null;
    }

    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[\s.\-]?\d){8,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?:ten|tên|em la|em là|minh la|mình là|toi la|tôi là)\s+(?<name>[A-Za-zÀ-ỹ][A-Za-zÀ-ỹ\s]{1,60})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    private sealed record ConversationVars(string? Name, string? Phone, string? Email);
}
