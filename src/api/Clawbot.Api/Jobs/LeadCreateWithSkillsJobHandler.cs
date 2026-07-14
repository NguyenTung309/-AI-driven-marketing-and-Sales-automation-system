using System.Text.Json;
using Clawbot.Agents.Contracts.Lead;
using Clawbot.Api.Contracts.Leads;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record LeadCreateWithSkillsJobPayload(
    Guid ContactId,
    string DisplayName,
    string? Phone,
    string? Email,
    string SourcePlatform,
    string Locale);

// Tạo lead có chấm điểm/enrich/dedup bằng agent (LLM). Việc này báo khi xong: sale không ngồi chờ,
// kết quả có trang riêng để mở (/leads).
public sealed class LeadCreateWithSkillsJobHandler(LeadAgent.LeadAgentClient leadClient) : IJobHandler
{
    public const string JobType = "leads.create-with-skills";

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<LeadCreateWithSkillsJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu tạo lead.");

        var response = await leadClient.CreateWithSkillsAsync(new LeadCreateWithSkillsRequest
        {
            TenantId = ctx.TenantId.ToString("D"),
            ContactId = payload.ContactId.ToString("D"),
            DisplayName = payload.DisplayName,
            Phone = payload.Phone ?? string.Empty,
            Email = payload.Email ?? string.Empty,
            SourcePlatform = payload.SourcePlatform,
            Locale = payload.Locale,
            Country = string.Empty,
        }, cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        var result = new CreateWithSkillsResult(
            Guid.Parse(response.LeadId),
            response.SpamFlagged,
            response.SpamReason,
            response.Timezone,
            response.EnrichmentCompany,
            response.PossibleDup,
            response.DedupCandidates
                .Select(c => new LeadDedupCandidateDto(Guid.Parse(c.ContactId), c.Similarity))
                .ToList());

        return new JobResult($"/leads?lead={result.LeadId}", JsonSerializer.Serialize(result));
    }
}
