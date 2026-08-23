using Clawbot.Domain.Common;

namespace Clawbot.Domain.Conversations;

public sealed class Conversation : AggregateRoot<Guid>, ITenantOwned
{
    private readonly List<Message> _messages = new();

    public Guid TenantId { get; private set; }
    public Guid? ContactId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string ExternalThreadId { get; private set; } = string.Empty;
    public string Status { get; private set; } = "open";
    // Co "AI dang chat": bat -> tin inbound duoc AI auto-reply. Tat khi sale nhay vao (gui tay/escalate).
    public bool AiAutoReplyEnabled { get; private set; } = true;
    // Sale gui tay -> tat AI tam thoi; moc nay la thoi diem AI tu bat lai o lan khach nhan tiep theo.
    // Null = tat vinh vien (toggle tay/escalate), khong bao gio tu bat lai.
    public DateTimeOffset? AiAutoReplyResumeAt { get; private set; }
    public Guid? AssignedTo { get; private set; }
    public DateTimeOffset? SnoozedUntil { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    // Watermark trích memory khách (ai-self-learning-memory Lớp 2): CHỈ set khi trích thành công —
    // fail giữ nguyên để lượt scan sau quét lại (không nuốt fail).
    public DateTimeOffset? MemoryExtractedAt { get; private set; }
    public Guid? InboxId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public byte[]? RowVersion { get; private set; }
    // Doi tac hoi thoai la nhom Zalo/FB (nhieu thanh vien), khong phai 1 khach ca nhan.
    // Duoc set tu cờ is_group cua kenh (Pancake) khi ingest — dung de loai nhom khoi dem/cham Lead.
    public bool IsGroup { get; private set; }

    public IReadOnlyCollection<Message> Messages => _messages.AsReadOnly();

    private Conversation() { }

    public static Conversation Open(
        Guid tenantId,
        string platform,
        string externalThreadId,
        DateTimeOffset createdAt,
        Guid? contactId = null,
        Guid? inboxId = null,
        bool isGroup = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            Platform = platform,
            ExternalThreadId = externalThreadId,
            CreatedAt = createdAt,
            InboxId = inboxId,
            IsGroup = isGroup,
        };

    public void SetInboxId(Guid inboxId) => InboxId = inboxId;

    // Self-heal mot chieu: chi bat len khi kenh xac nhan la nhom, khong bao gio tat lai
    // (tin sau thieu co is_group khong duoc xoa mat trang thai nhom da biet).
    public void MarkGroup() => IsGroup = true;

    public void Assign(Guid userId) => AssignedTo = userId;

    public void Resolve() => Status = "resolved";

    public void Escalate()
    {
        Status = "escalated";
        // Escalate = chuyen han cho nguoi, khong tu bat lai AI.
        AiAutoReplyEnabled = false;
        AiAutoReplyResumeAt = null;
        Raise(new Events.ConversationEscalated(TenantId, Id, DateTimeOffset.UtcNow));
    }

    // Toggle tay tu UI: bat/tat vinh vien, xoa moc tu bat lai.
    public void SetAiAutoReply(bool enabled)
    {
        AiAutoReplyEnabled = enabled;
        AiAutoReplyResumeAt = null;
    }

    // Sale gui tay -> tam tat AI, hen bat lai sau khoang thoi gian (khach im tiep thi AI cham lai).
    public void PauseAiAutoReplyUntil(DateTimeOffset resumeAt)
    {
        AiAutoReplyEnabled = false;
        AiAutoReplyResumeAt = resumeAt;
    }

    // Khach nhan tiep sau khi da qua moc hen: bat lai AI. Tra true neu vua bat lai (de caller luu + tiep tuc reply).
    public bool TryResumeAiAutoReply(DateTimeOffset now)
    {
        if (AiAutoReplyEnabled || AiAutoReplyResumeAt is null || AiAutoReplyResumeAt > now)
            return false;
        AiAutoReplyEnabled = true;
        AiAutoReplyResumeAt = null;
        return true;
    }

    public void MarkMemoryExtracted(DateTimeOffset at) => MemoryExtractedAt = at;

    public Message AppendMessage(string direction, string senderType, string content, string contentType, DateTimeOffset sentAt, Guid? senderUserId = null, string? externalMessageId = null, string? originalContent = null, string? redactedContent = null, string messageType = "text", string? parentPostId = null, string? senderDisplayName = null, string? senderAvatarUrl = null, string? attachmentUrl = null, string status = "sent", string? parentCommentId = null)
    {
        var msg = Message.Create(Id, TenantId, direction, senderType, content, contentType, sentAt, senderUserId, externalMessageId, originalContent, redactedContent, messageType, parentPostId, senderDisplayName, senderAvatarUrl, attachmentUrl, status, parentCommentId);
        _messages.Add(msg);
        LastMessageAt = sentAt;
        return msg;
    }

    // Claim ghi hụt (đụng unique index) phải rời khỏi navigation, nếu không lần
    // SaveChanges sau EF sẽ tự phát hiện lại và insert lần nữa.
    public void DiscardMessage(Message message) => _messages.Remove(message);

    public void Unassign() => AssignedTo = null;

    public void ReopenIfNeeded()
    {
        if (Status != "snoozed" && Status != "resolved") return;
        Status = "open";
        SnoozedUntil = null;
    }

    public void Snooze(DateTimeOffset until)
    {
        Status = "snoozed";
        SnoozedUntil = until;
    }

}





