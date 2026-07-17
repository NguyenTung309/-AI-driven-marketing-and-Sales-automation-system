namespace Clawbot.SharedKernel.Channels;

// Comment auto-reply (FB/IG qua Pancake): rep công khai dưới comment + nhắn riêng người comment.
// Tách khỏi IChannelAdapter vì chỉ kênh Pancake hỗ trợ (action reply_comment / private_replies);
// caller thiếu adapter này thì PHẢI skip, không được fallback reply_inbox (sai ngữ nghĩa FB).
public interface ICommentChannelAdapter
{
    /// <returns>Message id phía kênh (dedup echo), null khi kênh không trả id.</returns>
    Task<string?> SendCommentReplyAsync(Guid tenantId, string externalThreadId, string commentMessageId, string text, CancellationToken ct = default);

    /// <summary>Private reply từ comment — FB giới hạn 1 lần/comment trong window 7 ngày.</summary>
    Task<string?> SendPrivateReplyAsync(Guid tenantId, string externalThreadId, string postId, string commentMessageId, string fromId, string text, CancellationToken ct = default);
}
