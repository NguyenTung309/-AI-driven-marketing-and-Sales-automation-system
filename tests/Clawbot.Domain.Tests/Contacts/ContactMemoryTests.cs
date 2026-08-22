using Clawbot.Domain.Contacts;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Contacts;

public sealed class ContactMemoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ContactId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var convId = Guid.NewGuid();
        var memory = ContactMemory.Create(TenantId, ContactId, "Học viên lớp IELTS", "profile", 0.9m, convId, Now);

        memory.TenantId.Should().Be(TenantId);
        memory.ContactId.Should().Be(ContactId);
        memory.Fact.Should().Be("Học viên lớp IELTS");
        memory.Category.Should().Be("profile");
        memory.Confidence.Should().Be(0.9m);
        memory.SourceConversationId.Should().Be(convId);
        memory.IsActive.Should().BeTrue();
        memory.SupersededById.Should().BeNull();
        memory.CreatedAt.Should().Be(Now);
        memory.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_ThrowsOnBlankFact()
    {
        var act = () => ContactMemory.Create(TenantId, ContactId, "   ", "profile", 0.5m, null, Now);

        act.Should().Throw<ArgumentException>().WithParameterName("fact");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("PROFILE")]
    public void Create_ThrowsOnInvalidCategory(string category)
    {
        var act = () => ContactMemory.Create(TenantId, ContactId, "fact", category, 0.5m, null, Now);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(category));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("preference")]
    [InlineData("commitment")]
    [InlineData("history")]
    public void Create_AcceptsValidCategories(string category)
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "some fact", category, 0.5m, null, Now);

        memory.Category.Should().Be(category);
    }

    [Fact]
    public void Create_ClampsConfidenceToZeroOne()
    {
        var low = ContactMemory.Create(TenantId, ContactId, "f", "profile", -0.5m, null, Now);
        var high = ContactMemory.Create(TenantId, ContactId, "f", "profile", 1.5m, null, Now);

        low.Confidence.Should().Be(0m);
        high.Confidence.Should().Be(1m);
    }

    [Fact]
    public void Create_TrimsFact()
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "  trimmed fact  ", "profile", 0.5m, null, Now);

        memory.Fact.Should().Be("trimmed fact");
    }

    [Fact]
    public void Supersede_DeactivatesAndPointsToReplacement()
    {
        var original = ContactMemory.Create(TenantId, ContactId, "old fact", "profile", 0.8m, null, Now);
        var replacementId = Guid.NewGuid();

        original.Supersede(replacementId, Now.AddMinutes(5));

        original.IsActive.Should().BeFalse();
        original.SupersededById.Should().Be(replacementId);
        original.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Supersede_AllowsNullReplacementForDelete()
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "deleted fact", "history", 0.3m, null, Now);

        memory.Supersede(null, Now.AddMinutes(1));

        memory.IsActive.Should().BeFalse();
        memory.SupersededById.Should().BeNull();
    }

    [Fact]
    public void Supersede_ThrowsWhenAlreadySuperseded()
    {
        var memory = ContactMemory.Create(TenantId, ContactId, "fact", "profile", 0.5m, null, Now);
        memory.Supersede(Guid.NewGuid(), Now);

        var act = () => memory.Supersede(Guid.NewGuid(), Now.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }
}
