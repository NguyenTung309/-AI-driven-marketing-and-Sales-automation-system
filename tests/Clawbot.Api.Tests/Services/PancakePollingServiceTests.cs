using System.Net;
using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Demo;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;

namespace Clawbot.Api.Tests.Services;

public sealed class PancakePollingServiceTests
{
    [Theory]
    [InlineData("facebook")]
    [InlineData("instagram")]
    [InlineData("tiktok")]
    [InlineData("pancake")]
    [InlineData("zalo")]
    public async Task PollInboxesAsync_PreservesInboxPlatformAcrossDedupPublishAndMarker(
        string platform)
    {
        // Arrange
        await using var fixture = await PollingFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var encryptor = CreateEncryptor(0x22);
        var inbox = Inbox.Create(tenantId, "Fixture page", platform, "page-one");
        inbox.SetAccessToken(encryptor.Encrypt("page-token"), DateTimeOffset.UtcNow);
        fixture.Db.ProcessedMessages.Add(new ProcessedMessage(
            tenantId,
            platform,
            "msg-seen",
            "conv-1"));
        await fixture.Db.SaveChangesAsync();
        var published = new List<ChannelInboundMessageReceived>();
        fixture.Publisher
            .Publish(
                Arg.Do<ChannelInboundMessageReceived>(message => published.Add(message)),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        using var client = new HttpClient(new StubHttpMessageHandler((request, _) =>
            Task.FromResult(PollResponse(request))));
        var service = CreateService(encryptor, fixture.ScopeFactory, fixture.Traces);

        // Act
        var result = await service.PollInboxesAsync(
            client,
            "https://pancake.test/api/public_api/v2",
            [inbox],
            sweep: false,
            CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        published.Should().ContainSingle();
        published[0].Message.Channel.Should().Be(platform);
        fixture.Db.ChangeTracker.Clear();
        var markers = await fixture.Db.ProcessedMessages
            .Where(message => message.TenantId == tenantId)
            .OrderBy(message => message.ExternalMessageId)
            .ToListAsync();
        markers.Should().HaveCount(2);
        markers.Should().OnlyContain(message => message.Platform == platform);
        markers.Select(message => message.ExternalMessageId)
            .Should().Equal("msg-new", "msg-seen");
    }

    [Fact]
    public async Task PollInboxesAsync_ContinuesAfterAuthenticatedTokenCannotBeDecrypted()
    {
        // Arrange
        var writer = CreateEncryptor(0x11);
        var reader = CreateEncryptor(0x22);
        var first = CreateInbox("page-one", "facebook", writer.Encrypt("first-token"));
        var second = CreateInbox("page-two", "instagram", reader.Encrypt("second-token"));
        var requestedPageIds = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestedPageIds.Add(PageId(request));
            return Task.FromResult(EmptyConversations());
        }));
        var service = CreateService(reader);

        // Act
        var result = await service.PollInboxesAsync(
            client,
            "https://pancake.test/api/public_api/v2",
            [first, second],
            sweep: false,
            CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        requestedPageIds.Should().Equal("page-two");
    }

    [Fact]
    public async Task PollInboxesAsync_ContinuesAfterOneInboxPollThrows()
    {
        // Arrange
        var encryptor = CreateEncryptor(0x22);
        var first = CreateInbox("page-one", "facebook", encryptor.Encrypt("first-token"));
        var second = CreateInbox("page-two", "instagram", encryptor.Encrypt("second-token"));
        var requestedPageIds = new List<string>();
        using var client = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            var pageId = PageId(request);
            requestedPageIds.Add(pageId);
            return pageId == "page-one"
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture failure"))
                : Task.FromResult(EmptyConversations());
        }));
        var service = CreateService(encryptor);

        // Act
        var result = await service.PollInboxesAsync(
            client,
            "https://pancake.test/api/public_api/v2",
            [first, second],
            sweep: false,
            CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        requestedPageIds.Should().Equal("page-one", "page-two");
    }

    [Fact]
    public async Task PollInboxesAsync_RethrowsCancellationWhenStoppingTokenIsCancelled()
    {
        // Arrange
        var encryptor = CreateEncryptor(0x22);
        var first = CreateInbox("page-one", "facebook", encryptor.Encrypt("first-token"));
        var second = CreateInbox("page-two", "instagram", encryptor.Encrypt("second-token"));
        var requestedPageIds = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requestedPageIds.Add(PageId(request));
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        }));
        var service = CreateService(encryptor);

        // Act
        var act = () => service.PollInboxesAsync(
            client,
            "https://pancake.test/api/public_api/v2",
            [first, second],
            sweep: false,
            cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        requestedPageIds.Should().Equal("page-one");
    }

    private static PancakePollingService CreateService(
        AesEncryptor encryptor,
        IServiceScopeFactory? scopeFactory = null,
        DemoTraceService? traces = null) =>
        new(
            traces!,
            null!,
            null!,
            NullLogger<PancakePollingService>.Instance,
            scopeFactory!,
            encryptor);

    private static Inbox CreateInbox(string pageId, string platform, string encryptedToken)
    {
        var inbox = Inbox.Create(Guid.NewGuid(), pageId, platform, pageId);
        inbox.SetAccessToken(encryptedToken, DateTimeOffset.UtcNow);
        return inbox;
    }

    private static AesEncryptor CreateEncryptor(byte fill) =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()),
        }));

    private static string PageId(HttpRequestMessage request)
    {
        var segments = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pagesIndex = Array.IndexOf(segments, "pages");
        return segments[pagesIndex + 1];
    }

    private static HttpResponseMessage PollResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var json = path.EndsWith("/messages", StringComparison.Ordinal)
            ? """
              {
                "success": true,
                "messages": [
                  {
                    "id": "msg-seen",
                    "message": "Already ingested",
                    "from": {
                      "id": "customer-1",
                      "name": "Fixture customer",
                      "avatar_url": null,
                      "is_group": false,
                      "admin_id": null,
                      "is_automated": false
                    },
                    "attachments": [],
                    "inserted_at": "2026-07-30T00:00:00Z"
                  },
                  {
                    "id": "msg-new",
                    "message": "New message",
                    "from": {
                      "id": "customer-1",
                      "name": "Fixture customer",
                      "avatar_url": null,
                      "is_group": false,
                      "admin_id": null,
                      "is_automated": false
                    },
                    "attachments": [],
                    "inserted_at": "2026-07-30T00:01:00Z"
                  }
                ]
              }
              """
            : """
              {
                "success": true,
                "conversations": [
                  {
                    "id": "conv-1",
                    "type": "INBOX",
                    "snippet": "Fixture conversation",
                    "message_count": 2,
                    "updated_at": "2099-01-01T00:00:00Z",
                    "inserted_at": "2026-07-30T00:00:00Z",
                    "page_id": "page-one",
                    "from": {
                      "id": "customer-1",
                      "name": "Fixture customer",
                      "avatar_url": null,
                      "is_group": false
                    },
                    "last_sent_by": null,
                    "customers": [
                      {
                        "id": "customer-1",
                        "name": "Fixture customer",
                        "avatar_url": null,
                        "fb_id": "customer-1"
                      }
                    ]
                  }
                ]
              }
              """;

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };
    }

    private static HttpResponseMessage EmptyConversations() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"success\":true,\"conversations\":[]}"),
    };

    private sealed class PollingFixture(
        SqliteConnection connection,
        AppDbContext db,
        ServiceProvider services,
        IPublishEndpoint publisher,
        DemoTraceService traces) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public IPublishEndpoint Publisher { get; } = publisher;
        public DemoTraceService Traces { get; } = traces;
        public IServiceScopeFactory ScopeFactory { get; } =
            services.GetRequiredService<IServiceScopeFactory>();

        public static async Task<PollingFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE processed_messages (
                    Id TEXT NOT NULL PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    Platform TEXT NOT NULL,
                    ExternalMessageId TEXT NOT NULL,
                    ConversationExternalId TEXT NOT NULL,
                    ProcessedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IX_processed_messages_Platform_ExternalMessageId
                    ON processed_messages (Platform, ExternalMessageId);
                """);

            var publisher = Substitute.For<IPublishEndpoint>();
            var services = new ServiceCollection()
                .AddSingleton(db)
                .AddSingleton(publisher)
                .BuildServiceProvider();
            var redis = Substitute.For<IDatabase>();
            var multiplexer = Substitute.For<IConnectionMultiplexer>();
            multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(redis);
            var traces = new DemoTraceService(
                multiplexer,
                Options.Create(new DemoOptions()));

            return new PollingFixture(connection, db, services, publisher, traces);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
