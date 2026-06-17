using Clawbot.Api.Services;
using Clawbot.Domain.Documents;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class DocumentOpenReceiptServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordOpenAsync_marks_generated_document_opened_for_beacon()
    {
        using var fx = new TestApiAppDb(TenantId);
        var template = DocumentTemplate.Create(TenantId, "quote", "quote", "<p>Quote</p>", Now.AddDays(-1));
        var doc = GeneratedDocument.Create(TenantId, template.Id, "https://files.example/quote.pdf", Now.AddDays(-1));
        fx.Db.AddRange(template, doc);
        await fx.Db.SaveChangesAsync();
        var sut = new DocumentOpenReceiptService(fx.Db, new FixedClock(Now));

        var recorded = await sut.RecordOpenAsync(doc.Id, CancellationToken.None);

        recorded.Should().BeTrue();
        fx.Db.ChangeTracker.Clear();
        var saved = await fx.Db.GeneratedDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == doc.Id);
        saved.OpenedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RecordOpenAsync_returns_false_when_document_does_not_exist()
    {
        using var fx = new TestApiAppDb(TenantId);
        var sut = new DocumentOpenReceiptService(fx.Db, new FixedClock(Now));

        var recorded = await sut.RecordOpenAsync(Guid.NewGuid(), CancellationToken.None);

        recorded.Should().BeFalse();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
