using Clawbot.Domain.SaleAssist;
using FluentAssertions;

namespace Clawbot.Domain.Tests.SaleAssist;

public sealed class QuickReplyTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var tpl = QuickReplyTemplate.Create(TenantId, "greeting", "Chào bạn!", Now);

        tpl.TenantId.Should().Be(TenantId);
        tpl.Code.Should().Be("greeting");
        tpl.Body.Should().Be("Chào bạn!");
        tpl.Category.Should().BeNull();
        tpl.Platforms.Should().BeNull();
        tpl.CreatedAt.Should().Be(Now);
        tpl.UpdatedAt.Should().Be(Now);
    }
}

public sealed class UpsellSuggestionCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var convId = Guid.NewGuid();
        var cache = UpsellSuggestionCache.Create(TenantId, convId, true, "Gợi ý khóa nâng cao", "Khách quan tâm", 75, Now, Now.AddMinutes(-5));

        cache.TenantId.Should().Be(TenantId);
        cache.ConversationId.Should().Be(convId);
        cache.Eligible.Should().BeTrue();
        cache.Suggestion.Should().Be("Gợi ý khóa nâng cao");
        cache.Reason.Should().Be("Khách quan tâm");
        cache.LeadScore.Should().Be(75);
        cache.GeneratedAt.Should().Be(Now);
        cache.SourceLastMessageAt.Should().Be(Now.AddMinutes(-5));
    }

    [Fact]
    public void Update_ReplacesAllFields()
    {
        var cache = UpsellSuggestionCache.Create(TenantId, Guid.NewGuid(), true, "old", "old reason", 50, Now, Now);

        cache.Update(false, "new suggestion", "new reason", 90, Now.AddHours(1), Now.AddMinutes(30));

        cache.Eligible.Should().BeFalse();
        cache.Suggestion.Should().Be("new suggestion");
        cache.Reason.Should().Be("new reason");
        cache.LeadScore.Should().Be(90);
        cache.GeneratedAt.Should().Be(Now.AddHours(1));
        cache.SourceLastMessageAt.Should().Be(Now.AddMinutes(30));
    }
}
