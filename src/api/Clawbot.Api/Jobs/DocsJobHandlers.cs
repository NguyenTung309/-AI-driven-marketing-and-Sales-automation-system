using System.Text.Json;
using Clawbot.Agents.Contracts.Docs;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record DocsGenerateJobPayload(
    string TemplateCode,
    Guid? ContactId,
    IReadOnlyDictionary<string, string>? Vars,
    string? SentVia);

public sealed record DocsKitJobPayload(
    IReadOnlyList<string> TemplateCodes,
    Guid? ContactId,
    IReadOnlyDictionary<string, string>? Vars,
    string? SentVia);

// Sinh 1 tài liệu bằng DocsAgent.
public sealed class DocsGenerateJobHandler(
    DocsAgent.DocsAgentClient grpc,
    DocumentDeliveryService delivery) : IJobHandler
{
    public const string JobType = "docs.generate";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<DocsGenerateJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu đầu vào cho việc sinh tài liệu.");

        var doc = await DocumentsEndpoints.GenerateOneAsync(
            ctx.TenantId, payload.TemplateCode, payload.ContactId, payload.Vars, payload.SentVia,
            grpc, delivery, ct).ConfigureAwait(false);

        return new JobResult("/documents", $"Đã sinh tài liệu {payload.TemplateCode} ({doc.SizeBytes} bytes).");
    }
}

// Bộ tài liệu: nhiều doc trong 1 việc — đây là luồng nặng nhất (1-5 phút), có tiến độ theo từng doc.
public sealed class DocsKitJobHandler(
    DocsAgent.DocsAgentClient grpc,
    DocumentDeliveryService delivery) : IJobHandler
{
    public const string JobType = "docs.generate-kit";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<DocsKitJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu đầu vào cho việc sinh bộ tài liệu.");

        var total = payload.TemplateCodes.Count;
        long totalBytes = 0;

        for (var i = 0; i < total; i++)
        {
            var code = payload.TemplateCodes[i];
            await ctx.Progress.ReportAsync(i * 100 / total, $"Đang sinh {code} ({i + 1}/{total})", ct)
                .ConfigureAwait(false);

            var doc = await DocumentsEndpoints.GenerateOneAsync(
                ctx.TenantId, code, payload.ContactId, payload.Vars, payload.SentVia,
                grpc, delivery, ct).ConfigureAwait(false);
            totalBytes += doc.SizeBytes;
        }

        return new JobResult("/documents", $"Đã sinh {total} tài liệu ({totalBytes} bytes).");
    }
}
