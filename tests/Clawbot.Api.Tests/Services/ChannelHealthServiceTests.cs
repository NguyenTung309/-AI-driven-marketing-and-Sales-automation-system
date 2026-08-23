using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests.Services;

public sealed class ChannelHealthServiceTests
{
    [Fact]
    public async Task GetPancakeAsync_NoConfigs_ReturnsDegraded()
    {
        await using var fixture = await ChannelFixture.CreateAsync();

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Adapter.Should().Be("PancakeChannelAdapter");
        report.Status.Should().Be("degraded");
        report.TotalConfigs.Should().Be(0);
        report.ConfiguredTenants.Should().Be(0);
        report.Checks.Should().HaveCount(3);
        report.Checks.Should().OnlyContain(c => c.Status == "degraded");
    }

    [Fact]
    public async Task GetPancakeAsync_FullyConfiguredActive_ReturnsOk()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha256", encoding: "hex", withSecret: true);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Status.Should().Be("ok");
        report.TotalConfigs.Should().Be(1);
        report.ConfiguredTenants.Should().Be(1);
        report.Checks.Should().OnlyContain(c => c.Status == "ok");
    }

    [Fact]
    public async Task GetPancakeAsync_ActiveButMissingToken_OutboundDegraded()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: false, algo: "hmac-sha256", encoding: "hex", withSecret: true);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Status.Should().Be("degraded");
        report.Checks.First(c => c.Name == "outbound").Status.Should().Be("degraded");
        report.Checks.First(c => c.Name == "config").Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetPancakeAsync_UnsupportedSignatureAlgo_WebhookDegraded()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha512", encoding: "hex", withSecret: true);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Checks.First(c => c.Name == "webhook").Status.Should().Be("degraded");
        report.Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetPancakeAsync_UnsupportedEncoding_WebhookDegraded()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha256", encoding: "base64url", withSecret: true);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Checks.First(c => c.Name == "webhook").Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetPancakeAsync_Base64Encoding_WebhookOk()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha256", encoding: "base64", withSecret: true);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Checks.First(c => c.Name == "webhook").Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetPancakeAsync_MixedActiveAndInactive_OnlyActiveCountsForConfiguredTenants()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha256", encoding: "hex", withSecret: true);
        await fixture.SeedInactiveAsync();

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.TotalConfigs.Should().Be(2);
        report.ConfiguredTenants.Should().Be(1);
        report.Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetPancakeAsync_MissingWebhookSecret_WebhookDegraded()
    {
        await using var fixture = await ChannelFixture.CreateAsync();
        await fixture.SeedActiveAsync(withToken: true, algo: "hmac-sha256", encoding: "hex", withSecret: false);

        var service = new ChannelHealthService(fixture.Db);
        var report = await service.GetPancakeAsync();

        report.Checks.First(c => c.Name == "webhook").Status.Should().Be("degraded");
    }

    private sealed class ChannelFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public static async Task<ChannelFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new ChannelFixture(connection, db);
        }

        public async Task SeedActiveAsync(bool withToken, string algo, string encoding, bool withSecret)
        {
            var tenantId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var config = PancakeConfig.Create(tenantId, now);
            if (withToken) config.UpdateAccessToken("enc-token-value", now);
            if (withSecret) config.UpdateWebhookSecret("enc-secret-value", now);
            config.UpdateSignature("x-pancake-signature", algo, encoding, now);
            Db.PancakeConfigs.Add(config);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task SeedInactiveAsync()
        {
            var tenantId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var config = PancakeConfig.Create(tenantId, now);
            config.UpdateAccessToken("enc-token-value", now);
            config.UpdateWebhookSecret("enc-secret-value", now);
            config.Deactivate(now);
            Db.PancakeConfigs.Add(config);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() => throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
