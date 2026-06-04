using Clawbot.Agents.Contracts.Docs;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using CoreDocs = Clawbot.Agents.Core.Docs;

namespace Clawbot.AgentService.Services;

public sealed class DocsAgentGrpcService(
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
            FooterNote: $"{tenantName} · Tài liệu tạo tự động bởi ClawBot");

        var vars = new Dictionary<string, string>(request.Vars, StringComparer.Ordinal);
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
        var fileUrl = await _storage.SaveAsync(result.Pdf, fileName, ct).ConfigureAwait(false);

        Guid? contactId = Guid.TryParse(request.ContactId, out var cid) && cid != Guid.Empty ? cid : null;

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
}
