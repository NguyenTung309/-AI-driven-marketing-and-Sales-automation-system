using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;

namespace Clawbot.Domain.Tests.KnowledgeBase;

public sealed class SkillFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var sf = SkillFile.Create(TenantId, "tu-van.md", "Kỹ năng tư vấn", "# Nội dung", Now);

        sf.TenantId.Should().Be(TenantId);
        sf.Name.Should().Be("tu-van.md");
        sf.Description.Should().Be("Kỹ năng tư vấn");
        sf.ContentMd.Should().Be("# Nội dung");
        sf.CreatedAt.Should().Be(Now);
        sf.UpdatedAt.Should().Be(Now);
        sf.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Update_ChangesDescriptionAndContent()
    {
        var sf = SkillFile.Create(TenantId, "n.md", "old", "old content", Now);

        sf.Update("new desc", "new content", Now.AddHours(1));

        sf.Description.Should().Be("new desc");
        sf.ContentMd.Should().Be("new content");
        sf.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void SoftDelete_SetsDeletedAtAndUpdatedAt()
    {
        var sf = SkillFile.Create(TenantId, "n.md", null, "c", Now);

        sf.SoftDelete(Now.AddDays(1));

        sf.DeletedAt.Should().Be(Now.AddDays(1));
        sf.UpdatedAt.Should().Be(Now.AddDays(1));
    }
}
