using Clawbot.Domain.Common;

namespace Clawbot.Domain.KnowledgeBase;

// Tệp kỹ năng (.md) dùng lại được cho agent: nội dung markdown ngắn mô tả cách agent xử lý một kỹ năng.
// Agent tham chiếu theo Name (AgentConfig.SkillFilesJson). Khác KB module (RAG/Qdrant) — skill file
// được nối thẳng vào system prompt khi agent chạy.
public sealed class SkillFile : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;      // vd: ky-nang-tu-van.md, unique theo tenant
    public string? Description { get; private set; }
    public string ContentMd { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private SkillFile() { }

    public static SkillFile Create(Guid tenantId, string name, string? description, string contentMd, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            ContentMd = contentMd,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Update(string? description, string contentMd, DateTimeOffset now)
    {
        Description = description;
        ContentMd = contentMd;
        UpdatedAt = now;
    }

    public void SoftDelete(DateTimeOffset now)
    {
        DeletedAt = now;
        UpdatedAt = now;
    }
}
