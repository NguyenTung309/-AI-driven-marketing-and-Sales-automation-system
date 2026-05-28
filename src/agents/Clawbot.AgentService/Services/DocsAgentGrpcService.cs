using Clawbot.Agents.Contracts.Docs;
using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed class DocsAgentGrpcService : DocsAgent.DocsAgentBase
{
    public override Task<DocGenerateResponse> Generate(DocGenerateRequest request, ServerCallContext context)
    {
        _ = request;
        // TODO(student): load template by code, validate vars, render via PDF engine, upload to MinIO, persist generated_documents row.
        return Task.FromResult(new DocGenerateResponse
        {
            DocumentId = Guid.NewGuid().ToString(),
            FileUrl = "https://stub/doc.pdf"
        });
    }
}
