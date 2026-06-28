using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Tests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakeBootstrapSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    private static ServiceProvider BuildProvider(TestAppDb fx, IPancakePageTokenService service)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(fx.Db);
        sc.AddSingleton(service);
        sc.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return sc.BuildServiceProvider();
    }

    private static async Task SeedDefaultTenantAsync(TestAppDb fx)
    {
        fx.Db.Tenants.Add(Tenant.Create("default", "Default Tenant", "free", Now));
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Bootstrap_StoresPageTokenDirect_WhenPageTokenEnvPresent()
    {
        // EARS[WHEN PANCAKE_PAGE_ACCESS_TOKEN + PANCAKE_PAGE_ID env are present THE SYSTEM SHALL store the page
        // token directly (no mint) for the default tenant]
        using var fx = new TestAppDb();
        await SeedDefaultTenantAsync(fx);
        var service = Substitute.For<IPancakePageTokenService>();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PANCAKE_PAGE_ACCESS_TOKEN"] = "pgt_env",
            ["PANCAKE_PAGE_ID"] = "pzl_page_1",
        }).Build();
        var sp = BuildProvider(fx, service);

        await PancakeBootstrapSeeder.BootstrapAsync(sp, cfg, CancellationToken.None);

        await service.Received(1).StorePageTokenDirectAsync(
            fx.Db.Tenants.First().Id, "pzl_page_1", "Bootstrapped", "pancake", "pgt_env", Arg.Any<CancellationToken>());
        await service.DidNotReceive().MintAndStoreAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bootstrap_MintsPageToken_WhenUserTokenEnvPresent()
    {
        // EARS[WHEN PANCAKE_USER_ACCESS_TOKEN env is present THE SYSTEM SHALL mint + store a page token from it]
        using var fx = new TestAppDb();
        await SeedDefaultTenantAsync(fx);
        var service = Substitute.For<IPancakePageTokenService>();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PANCAKE_USER_ACCESS_TOKEN"] = "user_env",
            ["PANCAKE_PAGE_ID"] = "pzl_page_1",
        }).Build();
        var sp = BuildProvider(fx, service);

        await PancakeBootstrapSeeder.BootstrapAsync(sp, cfg, CancellationToken.None);

        await service.Received(1).MintAndStoreAsync(
            fx.Db.Tenants.First().Id, "pzl_page_1", "Bootstrapped", "pancake", "user_env", Arg.Any<CancellationToken>());
        await service.DidNotReceive().StorePageTokenDirectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bootstrap_Noops_WhenEnvVarsAbsent()
    {
        // EARS[WHEN no Pancake env vars are present THE SYSTEM SHALL do nothing (admin connect flow is the runtime path)]
        using var fx = new TestAppDb();
        await SeedDefaultTenantAsync(fx);
        var service = Substitute.For<IPancakePageTokenService>();
        var cfg = new ConfigurationBuilder().Build();
        var sp = BuildProvider(fx, service);

        await PancakeBootstrapSeeder.BootstrapAsync(sp, cfg, CancellationToken.None);

        await service.DidNotReceive().StorePageTokenDirectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().MintAndStoreAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bootstrap_Noops_WhenNoDefaultTenant()
    {
        using var fx = new TestAppDb();
        // No tenant seeded.
        var service = Substitute.For<IPancakePageTokenService>();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PANCAKE_PAGE_ACCESS_TOKEN"] = "pgt_env",
            ["PANCAKE_PAGE_ID"] = "pzl_page_1",
        }).Build();
        var sp = BuildProvider(fx, service);

        var act = async () => await PancakeBootstrapSeeder.BootstrapAsync(sp, cfg, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await service.DidNotReceive().StorePageTokenDirectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bootstrap_SwallowsServiceFailure_DoesNotCrash()
    {
        // Best-effort: a failed mint/store must not crash startup.
        using var fx = new TestAppDb();
        await SeedDefaultTenantAsync(fx);
        var service = Substitute.For<IPancakePageTokenService>();
        service.StorePageTokenDirectAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PANCAKE_PAGE_ACCESS_TOKEN"] = "pgt_env",
            ["PANCAKE_PAGE_ID"] = "pzl_page_1",
        }).Build();
        var sp = BuildProvider(fx, service);

        var act = async () => await PancakeBootstrapSeeder.BootstrapAsync(sp, cfg, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
