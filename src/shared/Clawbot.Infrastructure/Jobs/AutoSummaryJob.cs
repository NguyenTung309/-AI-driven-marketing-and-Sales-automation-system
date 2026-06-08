using Clawbot.Agents.Contracts.SaleAssist;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Hangfire background job: auto-summary on conversation resolve.
// Calls SaleAssistAgent gRPC AutoSummaryOnResolve so PII redaction + persistence
// happens server-side. Fire-and-forget from InboxEndpoints.ResolveAsync.
public sealed partial class AutoSummaryJob
{
    private readonly SaleAssistAgent.SaleAssistAgentClient _saleAssist;
    private readonly ILogger<AutoSummaryJob> _logger;

    public AutoSummaryJob(
        SaleAssistAgent.SaleAssistAgentClient saleAssist,
        ILogger<AutoSummaryJob> logger)
    {
        _saleAssist = saleAssist;
        _logger = logger;
    }

    public async Task RunAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
    {
        try
        {
            var response = await _saleAssist.AutoSummaryOnResolveAsync(
                new AutoSummaryRequest
                {
                    TenantId = tenantId.ToString("D"),
                    ConversationId = conversationId.ToString("D")
                }, cancellationToken: ct).ConfigureAwait(false);

            LogAutoSummaryJobCompleted(_logger, conversationId, response.KeyPoints.Count);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            LogAutoSummaryConversationNotFound(_logger, conversationId);
        }
        catch (Exception ex)
        {
            LogAutoSummaryJobFailed(_logger, ex, conversationId);
        }
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Auto-summary job completed for conversation {ConversationId} ({KeyPointCount} key points)")]
    private static partial void LogAutoSummaryJobCompleted(ILogger logger, Guid conversationId, int keyPointCount);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Warning, Message = "Auto-summary job: conversation {ConversationId} not found")]
    private static partial void LogAutoSummaryConversationNotFound(ILogger logger, Guid conversationId);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Error, Message = "Auto-summary job failed for conversation {ConversationId}")]
    private static partial void LogAutoSummaryJobFailed(ILogger logger, Exception ex, Guid conversationId);
}
