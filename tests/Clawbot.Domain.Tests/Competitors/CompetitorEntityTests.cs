using Clawbot.Domain.Competitors;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Competitors;

public sealed class CompetitorSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var source = CompetitorSource.Create(TenantId, "Competitor RSS", "https://example.com/rss", "rss", Now);

        source.TenantId.Should().Be(TenantId);
        source.Name.Should().Be("Competitor RSS");
        source.Url.Should().Be("https://example.com/rss");
        source.SourceType.Should().Be("rss");
        source.IsActive.Should().BeTrue();
        source.CreatedAt.Should().Be(Now);
        source.LastScannedAt.Should().BeNull();
        source.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_BlankSourceType_DefaultsToRss()
    {
        var source = CompetitorSource.Create(TenantId, "n", "u", "", Now);

        source.SourceType.Should().Be("rss");
    }

    [Fact]
    public void Update_ChangesFields()
    {
        var source = CompetitorSource.Create(TenantId, "old", "old-url", "rss", Now);

        source.Update("new", "new-url", "fanpage", false);

        source.Name.Should().Be("new");
        source.Url.Should().Be("new-url");
        source.SourceType.Should().Be("fanpage");
        source.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Update_BlankSourceType_KeepsOriginal()
    {
        var source = CompetitorSource.Create(TenantId, "n", "u", "fanpage", Now);

        source.Update("n2", "u2", "", true);

        source.SourceType.Should().Be("fanpage");
    }

    [Fact]
    public void MarkScanned_SetsLastScannedAt()
    {
        var source = CompetitorSource.Create(TenantId, "n", "u", "rss", Now);

        source.MarkScanned(Now.AddHours(1));

        source.LastScannedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void SoftDelete_DeactivatesAndSetsDeletedAt()
    {
        var source = CompetitorSource.Create(TenantId, "n", "u", "rss", Now);

        source.SoftDelete(Now.AddDays(1));

        source.IsActive.Should().BeFalse();
        source.DeletedAt.Should().Be(Now.AddDays(1));
    }
}

public sealed class CompetitorPostTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var sourceId = Guid.NewGuid();
        var post = CompetitorPost.Create(TenantId, sourceId, "https://comp.com/post", "Big Promo", "Snippet text",
            Now.AddHours(-1), Now, "abc123hash");

        post.TenantId.Should().Be(TenantId);
        post.SourceId.Should().Be(sourceId);
        post.Url.Should().Be("https://comp.com/post");
        post.Title.Should().Be("Big Promo");
        post.Snippet.Should().Be("Snippet text");
        post.PublishedAt.Should().Be(Now.AddHours(-1));
        post.DetectedAt.Should().Be(Now);
        post.ContentHash.Should().Be("abc123hash");
    }
}
