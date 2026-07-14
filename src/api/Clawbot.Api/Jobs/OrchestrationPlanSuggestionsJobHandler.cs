using System.Text.Encodings.Web;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Api.Endpoints;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;

namespace Clawbot.Api.Jobs;

// "Tự động xây dựng kế hoạch": LLM đọc snapshot tenant rồi đề xuất checklist kế hoạch định kỳ.
// Kết quả là dữ liệu để user tick chọn, không có trang riêng — nhét JSON vào ResultSummary,
// màn /agents đọc lại và mở dialog khi job xong.
public sealed class OrchestrationPlanSuggestionsJobHandler(
    AppDbContext db,
    IClaudeChatClient chatClient,
    ILlmCallScope llmScope,
    IClock clock,
    ILoggerFactory loggerFactory) : IJobHandler
{
    public const string JobType = "orchestration.plan-suggestions";

    // Frontend (/agents) JSON.parse ResultSummary và đọc field camelCase (result.items...); dùng Web defaults
    // để ra camelCase + UnsafeRelaxedJsonEscaping để tiếng Việt không bị escape thành \uXXXX.
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Type => JobType;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var suggestions = await OrchestrationV2Endpoints
            .BuildPlanSuggestionsAsync(ctx.TenantId, db, chatClient, llmScope, clock, loggerFactory, ct)
            .ConfigureAwait(false);

        // Link riêng (?planResult=) để "Mở kết quả" mở thẳng dialog checklist — khác ?job= (chỉ mở Job Center),
        // tránh dialog tự bung khi người dùng mới chỉ chọn job trong Job Center.
        return new JobResult(
            $"/agents?planResult={ctx.JobId}",
            JsonSerializer.Serialize(suggestions, ResultJson));
    }
}
