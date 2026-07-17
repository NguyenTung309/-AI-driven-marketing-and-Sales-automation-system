using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Jobs;

public sealed record AgentSandboxJobPayload(Guid AgentConfigId, string AgentCode, string SystemPrompt, string Message);

// "Chạy thử" agent trong màn cấu hình: 1 lượt chat thật qua LLM.
// NotifyOnSuccess=false — user đang ngồi trong sandbox chờ câu trả lời hiện ra.
public sealed class AgentSandboxJobHandler(
    AppDbContext db,
    IClaudeChatClient chatClient,
    ILlmCallScope llmScope,
    IPiiRedactor pii,
    IClock clock) : IJobHandler
{
    public const string JobType = "agents.sandbox";

    public string Type => JobType;

    public bool NotifyOnSuccess => false;

    public async Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AgentSandboxJobPayload>(ctx.PayloadJson)
            ?? throw new InvalidOperationException("Thiếu dữ liệu chạy thử agent.");

        var agent = await db.AgentConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == payload.AgentConfigId && a.TenantId == ctx.TenantId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy agent.");

        var now = clock.UtcNow;
        var redactedMessage = (await pii.RedactAsync(payload.Message, ct).ConfigureAwait(false)).RedactedText;

        var session = AgentSession.Start(ctx.TenantId, agent.Id, null, "Agent configuration sandbox", now, ctx.UserId);
        session.AppendTrace("sandbox", agent.DisplayName, "input", redactedMessage, now);

        ClaudeReply reply;
        using (llmScope.Begin(ctx.TenantId, agent.Code, now))
        {
            // Sandbox bọc guardrail giống chat thật để "Chạy thử" phản ánh đúng hành vi production.
            var systemPrompt = Clawbot.Agents.Core.AgentPromptDefaults.Compose(payload.SystemPrompt);
            reply = await chatClient.CompleteAsync(systemPrompt, Array.Empty<ChatTurn>(), redactedMessage, ct)
                .ConfigureAwait(false);
        }

        var redactedReply = (await pii.RedactAsync(reply.Text, ct).ConfigureAwait(false)).RedactedText;
        session.AppendTrace("sandbox", agent.DisplayName, "reply", redactedReply, now.AddMilliseconds(1));
        session.Finish(now.AddMilliseconds(2));
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var result = new AgentSandboxResponse(session.Id, redactedReply, now.AddMilliseconds(1));
        return new JobResult($"/agents/runs/{session.Id}", JsonSerializer.Serialize(result, JobResultJson.Web));
    }
}
