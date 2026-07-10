using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// Review-gate P5 (biên adapter, critique #1): mọi call-site IChannelAdapter.SendAsync là một đường
// content rời hệ thống — TỪNG chỗ phải có gate (review/manual-mode/toxicity/template-approved).
// Sender MỚI gọi adapter => test này đỏ => buộc thêm gate rồi mới được whitelist.
// Đây là guard chống drift thay cho wrapper DI (các call-site hiện tại đã gate xong ở P2/P3/P5).
public sealed class OutboundSenderBoundaryTests
{
    // File được phép chạm IChannelAdapter.SendAsync + gate tương ứng:
    private static readonly string[] AllowedCallSites =
    [
        // gate: tiered LLM review + manual-mode hold + toxicity (P2/P3)
        "src/agents/Clawbot.AgentService/Services/ChatAgentGrpcService.cs",
        // gate: OutboundMessageSafetyService (sale gõ tay - QĐ5 miễn agent review) + draft approve (P3)
        "src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs",
        // gate: template tĩnh duyệt-1-lần, biến nội suy chỉ URL/ngày hệ thống sinh (QĐ6)
        "src/api/Clawbot.Api/Services/DocumentDeliveryService.cs",
        // gate: manual-mode/resolved skip (P3) + template tĩnh 100% (QĐ6) + action reply_comment/private_replies
        "src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs",
        // gate: manual-mode hold (P3) + toxicity trên bản render vì interpolate tên khách (P5)
        "src/shared/Clawbot.Infrastructure/Jobs/DripSequenceJob.cs",
        // implementer + contracts — không phải call-site nghiệp vụ
        "src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs",
        "src/shared/Clawbot.SharedKernel/Channels/IChannelAdapter.cs",
        "src/shared/Clawbot.SharedKernel/Channels/ICommentChannelAdapter.cs",
    ];

    [Fact]
    public void Every_channel_send_call_site_is_gated_and_whitelisted()
    {
        var root = FindRepoRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var source = File.ReadAllText(f);
                var inboxSend = source.Contains("IChannelAdapter", StringComparison.Ordinal)
                    && source.Contains(".SendAsync(", StringComparison.Ordinal);
                var commentSend = source.Contains("ICommentChannelAdapter", StringComparison.Ordinal)
                    && (source.Contains("SendCommentReplyAsync(", StringComparison.Ordinal)
                        || source.Contains("SendPrivateReplyAsync(", StringComparison.Ordinal));
                return inboxSend || commentSend;
            })
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Except(AllowedCallSites, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        offenders.Should().BeEmpty(
            "call-site IChannelAdapter.SendAsync mới phải qua review-gate (manual-mode + review/toxicity/template policy) " +
            "rồi mới thêm vào whitelist — xem docs/superpowers/plans/2026-07-10-mandatory-review-gate-plan.md Phase 5");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Clawbot.sln")))
            dir = dir.Parent!;
        dir.Should().NotBeNull("repo root (Clawbot.sln) must be locatable from test bin dir");
        return dir!.FullName;
    }
}
