using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Services;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Integration.Tests;

public sealed class FailedMessageRetryConcurrencyTests : IClassFixture<SqlServerFixture>
{
    private static readonly Guid TenantId = Guid.Parse("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("f2222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly SqlServerFixture _sql;

    public FailedMessageRetryConcurrencyTests(SqlServerFixture sql) => _sql = sql;

    [Fact]
    public async Task Concurrent_retries_call_channel_once_and_second_request_conflicts()
    {
        await using var setupDb = CreateDb();
        await EnsureTenantAndActorAsync(setupDb);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:concurrent-thread", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Chỉ gửi một lần", "text", Now.AddMinutes(-4), status: "send_failed");
        setupDb.Conversations.Add(conversation);
        await setupDb.SaveChangesAsync();

        var adapter = new BlockingChannelAdapter();
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var firstService = CreateService(firstDb, adapter);
        var secondService = CreateService(secondDb, adapter);

        var firstTask = firstService.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);
        await adapter.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = await secondService.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        second.Outcome.Should().Be(FailedMessageRetryOutcome.NotAvailable);
        adapter.Release.TrySetResult(true);
        var first = await firstTask;
        first.Outcome.Should().Be(FailedMessageRetryOutcome.Sent);
        adapter.CallCount.Should().Be(1);

        await using var verifyDb = CreateDb();
        var persisted = await verifyDb.Messages.SingleAsync(m => m.Id == message.Id);
        persisted.Status.Should().Be("sent");
        persisted.Content.Should().Be("Chỉ gửi một lần");
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.ConnectionString)
            .Options;
        return new AppDbContext(options, new FixedTenantAccessor(TenantId));
    }

    private static FailedMessageRetryService CreateService(AppDbContext db, IChannelAdapter adapter)
    {
        var toxicity = Substitute.For<IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(false);
        return new FailedMessageRetryService(
            db,
            adapter,
            new OutboundMessageSafetyService(toxicity, Options.Create(new ToxicityOptions())),
            Substitute.For<IInboxNotifier>(),
            new FixedClock(Now),
            NullLogger<FailedMessageRetryService>.Instance);
    }

    private static async Task EnsureTenantAndActorAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            IF COL_LENGTH('messages', 'attachment_url') IS NULL
                ALTER TABLE messages ADD attachment_url nvarchar(2048) NULL;
            IF NOT EXISTS (SELECT 1 FROM tenants WHERE id = {TenantId})
            BEGIN
                INSERT INTO tenants (id, slug, display_name, plan_name, is_active, settings_json, created_at, updated_at)
                VALUES ({TenantId}, 'retry-test', 'Retry Test', 'free', 1, NCHAR(123) + NCHAR(125), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            END;
            IF NOT EXISTS (SELECT 1 FROM users WHERE id = {ActorId})
            BEGIN
                INSERT INTO users (id, tenant_id, display_name, email, password_hash, security_stamp, access_failed_count, is_active, created_at, updated_at, user_name, normalized_user_name, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled)
                VALUES ({ActorId}, {TenantId}, 'Retry Actor', 'retry-actor@clawbot.local', 'hash', 'stamp', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'retry-actor@clawbot.local', 'RETRY-ACTOR@CLAWBOT.LOCAL', 'RETRY-ACTOR@CLAWBOT.LOCAL', 1, 0, 0, 1);
            END;
            """);
    }

    private sealed class BlockingChannelAdapter : IChannelAdapter
    {
        private int _callCount;
        public string Name => "blocking";
        public int CallCount => _callCount;
        public TaskCompletionSource<bool> SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> VerifyWebhookSignatureAsync(
            Guid tenantId,
            string rawBody,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<ChannelMessage>> ParseAsync(
            string rawBody,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChannelMessage>>(Array.Empty<ChannelMessage>());

        public async Task<string?> SendAsync(
            Guid tenantId,
            string externalThreadId,
            string text,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            SendStarted.TrySetResult(true);
            await Release.Task.WaitAsync(ct);
            return "concurrent-provider-id";
        }
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        private readonly TenantContext _context = new(tenantId, "retry-test");
        public TenantContext? Current => _context;
        public TenantContext Require() => _context;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
