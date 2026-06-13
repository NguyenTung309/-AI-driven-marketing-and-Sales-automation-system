using Clawbot.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Documents;

// Docs-1 — 7-day download link expiry.
public sealed class GeneratedDocumentTests
{
    [Fact]
    public void Create_sets_expiry_seven_days_after_creation()
    {
        var created = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);

        var doc = GeneratedDocument.Create(Guid.NewGuid(), Guid.NewGuid(), "https://x/y.pdf", created);

        doc.ExpiresAt.Should().Be(created.AddDays(GeneratedDocument.LinkValidityDays));
    }

    [Fact]
    public void IsExpired_is_false_within_window_true_after()
    {
        var created = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);
        var doc = GeneratedDocument.Create(Guid.NewGuid(), Guid.NewGuid(), "https://x/y.pdf", created);

        doc.IsExpired(created.AddDays(3)).Should().BeFalse();
        doc.IsExpired(created.AddDays(8)).Should().BeTrue();
    }
}
