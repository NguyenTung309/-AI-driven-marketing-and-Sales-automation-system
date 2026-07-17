using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class AiAutoReplyResumeJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resumes_PastWindow_AndTriggersHangingReply()
    {
        // Arrange: cửa sổ nhường sale đã hết -> job phải bật lại AI + gọi resumer
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now.AddHours(-1));
        conv.AppendMessage("in", "contact", "khách hỏi", "text", Now.AddMinutes(-10));
        conv.PauseAiAutoReplyUntil(DateTimeOffset.UtcNow.AddMinutes(-1));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var resumer = Substitute.For<IAiAutoReplyResumer>();
        var sut = new AiAutoReplyResumeJob(fx.Db, resumer, NullLogger<AiAutoReplyResumeJob>.Instance);

        // Act
        await sut.RunAsync();

        // Assert
        var reloaded = await fx.Db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == conv.Id);
        reloaded.AiAutoReplyEnabled.Should().BeTrue();
        reloaded.AiAutoReplyResumeAt.Should().BeNull();
        await resumer.Received(1).ReplyToHangingCustomerMessageAsync(fx.TenantId, conv.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_WindowStillOpen()
    {
        // Arrange: chưa hết cửa sổ — sale vẫn đang cầm hội thoại
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now.AddHours(-1));
        conv.PauseAiAutoReplyUntil(DateTimeOffset.UtcNow.AddMinutes(10));
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var resumer = Substitute.For<IAiAutoReplyResumer>();
        var sut = new AiAutoReplyResumeJob(fx.Db, resumer, NullLogger<AiAutoReplyResumeJob>.Instance);

        // Act
        await sut.RunAsync();

        // Assert
        var reloaded = await fx.Db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == conv.Id);
        reloaded.AiAutoReplyEnabled.Should().BeFalse();
        await resumer.DidNotReceiveWithAnyArgs().ReplyToHangingCustomerMessageAsync(default, default, default);
    }

    [Fact]
    public async Task Ignores_ManuallyDisabled_NoResumeAt()
    {
        // Arrange: toggle tay/escalate = tắt vĩnh viễn (resume_at NULL) — job không được tự bật lại
        using var fx = new TestAppDb();
        var conv = Conversation.Open(fx.TenantId, "zalo", "page1:conv1", Now.AddHours(-1));
        conv.SetAiAutoReply(false);
        fx.Db.Conversations.Add(conv);
        await fx.Db.SaveChangesAsync();

        var resumer = Substitute.For<IAiAutoReplyResumer>();
        var sut = new AiAutoReplyResumeJob(fx.Db, resumer, NullLogger<AiAutoReplyResumeJob>.Instance);

        // Act
        await sut.RunAsync();

        // Assert
        var reloaded = await fx.Db.Conversations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == conv.Id);
        reloaded.AiAutoReplyEnabled.Should().BeFalse();
        await resumer.DidNotReceiveWithAnyArgs().ReplyToHangingCustomerMessageAsync(default, default, default);
    }
}
