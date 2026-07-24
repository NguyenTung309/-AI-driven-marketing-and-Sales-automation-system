using System.Text.Json;
using Clawbot.Infrastructure.Leads;
using Clawbot.SharedKernel.Jobs;

namespace Clawbot.Api.Jobs;

public sealed record LeadRevenueEstimateJobPayload(Guid LeadId);

// Ước tính doanh thu AI khi lead → customer mà sale chưa nhập số tiền.
// NotifyOnSuccess=false: kết quả báo qua notification duyệt riêng (pending) hoặc im lặng (auto-approve).
public sealed class LeadRevenueEstimateJobHandler(LeadRevenueEstimateService service) : IJobHandler
{
    public const string JobType = "lead-revenue-estimate";

    public string Type => JobType;

    public bool NotifyOnSuccess => false;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<LeadRevenueEstimateJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu lead cần ước tính doanh thu.");

        var summary = await service
            .EstimateAndPersistAsync(ctx.TenantId, payload.LeadId, ct)
            .ConfigureAwait(false);

        return new JobResult($"/leads/{payload.LeadId}", summary);
    }
}
