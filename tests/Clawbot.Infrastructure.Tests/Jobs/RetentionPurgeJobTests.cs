using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class RetentionPurgeJobTests
{
    [Fact]
    public async Task RunAsync_removes_notifications_older_than_90_days_only()
    {
        using var t = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        var expired = Notification.Create(t.TenantId, null, "system", "expired", now.AddDays(-91));
        var boundary = Notification.Create(t.TenantId, null, "system", "boundary", now.AddDays(-90));
        var fresh = Notification.Create(t.TenantId, null, "system", "fresh", now.AddDays(-5));
        t.Db.Notifications.AddRange(expired, boundary, fresh);
        await t.Db.SaveChangesAsync();

        var sut = new RetentionPurgeJob(t.Db, IdentityRedactor(), NullLogger<RetentionPurgeJob>.Instance, new FixedTimeProvider(now));
        await sut.RunAsync();

        var remainingTitles = await t.Db.Notifications
            .IgnoreQueryFilters()
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Title)
            .ToListAsync();
        remainingTitles.Should().Equal("boundary", "fresh");
    }

    [Fact]
    public async Task RunAsync_redacts_legacy_message_content_before_dropping_raw_value()
    {
        using var t = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
        var conversation = Conversation.Open(t.TenantId, "widget", "widget-1", now.AddDays(-45));
        conversation.AppendMessage("in", "visitor", "Goi 0912345678", "text", now.AddDays(-45));
        t.Db.Conversations.Add(conversation);
        await t.Db.SaveChangesAsync();

        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new RedactionResult(
                call.ArgAt<string>(0).Replace("0912345678", "[PHONE]", StringComparison.Ordinal),
                Array.Empty<PiiSpan>()));
        var sut = new RetentionPurgeJob(t.Db, pii, NullLogger<RetentionPurgeJob>.Instance, new FixedTimeProvider(now));

        await sut.RunAsync();

        var message = await t.Db.Messages.IgnoreQueryFilters().SingleAsync();
        message.Content.Should().Be("Goi [PHONE]");
        message.RedactedContent.Should().Be("Goi [PHONE]");
        message.OriginalContent.Should().BeNull();
    }

    private static IPiiRedactor IdentityRedactor()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new RedactionResult(call.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        return pii;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
