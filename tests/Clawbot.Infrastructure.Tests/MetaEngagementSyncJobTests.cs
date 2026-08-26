using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class MetaEngagementSyncJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MetaEngagementSyncJobTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateDb();
        var createScript = db.Database.GenerateCreateScript()
            .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
            .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
            .Replace("N'", "'", StringComparison.Ordinal);
        db.Database.ExecuteSqlRaw(createScript);
    }

    private AppDbContext CreateDb(Guid? tenantId = null) =>
        new(
            _options,
            new FakeTenantAccessor(tenantId.HasValue ? new TenantContext(tenantId.Value, "test") : null));

    [Fact]
    public async Task RunForTenantAsync_SyncsOnlyTargetTenantSchedules()
    {
        var now = DateTimeOffset.UtcNow;
        var tenant1 = Tenant.Create("Tenant 1", "t1", "standard", now);
        var tenant2 = Tenant.Create("Tenant 2", "t2", "standard", now);
        var assetId1 = Guid.NewGuid();

        using (var db = CreateDb())
        {
            db.Tenants.AddRange(tenant1, tenant2);
            await db.SaveChangesAsync();

            var item1 = ContentItem.Create(tenant1.Id, "facebook", "Bài viết FB T1", Guid.NewGuid(), now);
            var item2 = ContentItem.Create(tenant2.Id, "facebook", "Bài viết FB T2", Guid.NewGuid(), now);
            db.ContentItems.AddRange(item1, item2);
            await db.SaveChangesAsync();

            var schedule1 = ContentSchedule.Schedule(
                tenant1.Id,
                item1.Id,
                1,
                "facebook",
                now.AddHours(-1),
                now.AddHours(-1),
                assetId1);
            schedule1.MarkPublishing(now.AddHours(-1));
            schedule1.MarkPosted("https://facebook.com/123456_789012", "123456_789012", now.AddHours(-1));

            var schedule2 = ContentSchedule.Schedule(
                tenant2.Id,
                item2.Id,
                1,
                "facebook",
                now.AddHours(-1),
                now.AddHours(-1),
                Guid.NewGuid());
            schedule2.MarkPublishing(now.AddHours(-1));
            schedule2.MarkPosted("https://facebook.com/999999_888888", "999999_888888", now.AddHours(-1));

            db.ContentSchedules.AddRange(schedule1, schedule2);
            await db.SaveChangesAsync();
        }

        var meta = Substitute.For<IMetaIntegrationService>();
        var credential1 = new MetaPageCredential(assetId1, "123456", "My Page", "mock-token");
        meta.ResolvePageForEngagementAsync(tenant1.Id, assetId1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MetaPageCredential?>(credential1));

        var graphJson = """
            {
                "shares": { "count": 2 },
                "likes": { "summary": { "total_count": 25 } },
                "comments": { "summary": { "total_count": 7 } }
            }
            """;
        var graph = Substitute.For<IMetaGraphClient>();
        graph.GetAsync(
            tenant1.Id,
            "123456_789012",
            Arg.Any<IReadOnlyDictionary<string, string?>>(),
            "mock-token",
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(JsonDocument.Parse(graphJson)));

        var instagram = Substitute.For<IInstagramCredentialResolver>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);

        using (var db = CreateDb(tenant1.Id))
        {
            var job = new MetaEngagementSyncJob(
                db,
                meta,
                graph,
                instagram,
                clock,
                NullLogger<MetaEngagementSyncJob>.Instance);

            var syncedCount = await job.RunForTenantAsync(tenant1.Id, CancellationToken.None);
            syncedCount.Should().Be(1);
        }

        using (var db = CreateDb())
        {
            var s1 = await db.ContentSchedules.IgnoreQueryFilters().FirstAsync(s => s.ExternalPostId == "123456_789012");
            s1.LikeCount.Should().Be(25);
            s1.CommentCount.Should().Be(7);
            s1.EngagementSyncedAt.Should().Be(now);

            var s2 = await db.ContentSchedules.IgnoreQueryFilters().FirstAsync(s => s.ExternalPostId == "999999_888888");
            s2.EngagementSyncedAt.Should().BeNull("Tenant 2 không bị đồng bộ trong lần gọi này");
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private sealed class FakeTenantAccessor(TenantContext? current) : ITenantAccessor
    {
        public TenantContext? Current => current;
        public TenantContext Require() => current ?? throw new InvalidOperationException("No tenant context");
    }
}
