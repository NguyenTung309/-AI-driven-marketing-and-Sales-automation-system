using Clawbot.Domain.Contacts;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Contacts;

public sealed class ContactMemoryTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContactId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_trims_fact_and_clamps_confidence()
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "  Học viên trình độ HSK3  ", ContactMemory.CategoryProfile, 1.7m, null, CreatedAt);

        memory.Fact.Should().Be("Học viên trình độ HSK3");
        memory.Confidence.Should().Be(1m);
        memory.IsActive.Should().BeTrue();
        memory.UpdatedAt.Should().Be(CreatedAt);
    }

    [Fact]
    public void Create_rejects_empty_fact_and_unknown_category()
    {
        var emptyFact = () => ContactMemory.Create(TenantId, ContactId, " ", ContactMemory.CategoryProfile, 0.9m, null, CreatedAt);
        var badCategory = () => ContactMemory.Create(TenantId, ContactId, "f", "mood", 0.9m, null, CreatedAt);

        emptyFact.Should().Throw<ArgumentException>().WithMessage("fact_required*");
        badCategory.Should().Throw<ArgumentException>().WithMessage("invalid_category*");
    }

    [Fact]
    public void Supersede_deactivates_links_replacement_and_is_one_way()
    {
        var old = ContactMemory.Create(TenantId, ContactId, "Thích ca tối 2-4-6", ContactMemory.CategoryPreference, 0.8m, null, CreatedAt);
        var replacement = ContactMemory.Create(TenantId, ContactId, "Đổi sang ca tối 3-5-7", ContactMemory.CategoryPreference, 0.9m, null, CreatedAt.AddDays(1));

        old.Supersede(replacement.Id, CreatedAt.AddDays(1));

        old.IsActive.Should().BeFalse();
        old.SupersededById.Should().Be(replacement.Id);
        old.UpdatedAt.Should().Be(CreatedAt.AddDays(1));

        var again = () => old.Supersede(null, CreatedAt.AddDays(2));
        again.Should().Throw<InvalidOperationException>().WithMessage("memory_already_superseded");
    }

    [Fact]
    public void Supersede_without_replacement_acts_as_delete()
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "Đã hẹn học thử", ContactMemory.CategoryCommitment, 0.9m, null, CreatedAt);

        memory.Supersede(null, CreatedAt.AddDays(1));

        memory.IsActive.Should().BeFalse();
        memory.SupersededById.Should().BeNull();
    }
}
