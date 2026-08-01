using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakeBootstrapSeederTests
{
    [Fact]
    public async Task BootstrapAsync_UsesConfiguredTenantAndListedPagePlatform_WhenMinting()
    {
        // Arrange
        await using var fixture = await SeederFixture.CreateAsync("tenant-a");
        fixture.PageList.ListAsync("user-token", Arg.Any<CancellationToken>())
            .Returns([
                new PancakePageSummary("page-1", "Facebook page", "facebook"),
            ]);
        fixture.TokenService.MintAndStoreAsync(
                fixture.TenantId,
                "page-1",
                "Facebook page",
                "facebook",
                "user-token",
                Arg.Any<CancellationToken>())
            .Returns(new PancakePageToken(
                "page-token",
                "page-1",
                "Facebook page",
                "facebook"));
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PANCAKE_TENANT_SLUG"] = "tenant-a",
            ["PANCAKE_PAGE_ID"] = "page-1",
            ["PANCAKE_USER_ACCESS_TOKEN"] = "user-token",
        });

        // Act
        await PancakeBootstrapSeeder.BootstrapAsync(
            fixture.Services,
            configuration);

        // Assert
        await fixture.TokenService.Received(1).MintAndStoreAsync(
            fixture.TenantId,
            "page-1",
            "Facebook page",
            "facebook",
            "user-token",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BootstrapAsync_RequiresPlatform_WhenStoringDirectPageToken()
    {
        // Arrange
        await using var fixture = await SeederFixture.CreateAsync("tenant-a");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PANCAKE_TENANT_SLUG"] = "tenant-a",
            ["PANCAKE_PAGE_ID"] = "page-1",
            ["PANCAKE_PAGE_ACCESS_TOKEN"] = "page-token",
        });

        // Act
        var action = () => PancakeBootstrapSeeder.BootstrapAsync(
            fixture.Services,
            configuration);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("pancake_bootstrap_platform_required");
        await fixture.TokenService.DidNotReceiveWithAnyArgs()
            .StorePageTokenDirectAsync(default, default!, default!, default!, default!);
    }

    [Fact]
    public async Task BootstrapAsync_IgnoresPlaceholderUserToken_AndStoresValidDirectToken()
    {
        // Arrange
        await using var fixture = await SeederFixture.CreateAsync("tenant-a");
        fixture.TokenService.StorePageTokenDirectAsync(
                fixture.TenantId,
                "page-1",
                "Bootstrapped",
                "zalo",
                "page-token",
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PANCAKE_TENANT_SLUG"] = "tenant-a",
            ["PANCAKE_PLATFORM"] = "zalo",
            ["PANCAKE_PAGE_ID"] = "page-1",
            ["PANCAKE_USER_ACCESS_TOKEN"] = "replace-with-user-token",
            ["PANCAKE_PAGE_ACCESS_TOKEN"] = "page-token",
        });

        // Act
        await PancakeBootstrapSeeder.BootstrapAsync(
            fixture.Services,
            configuration);

        // Assert
        await fixture.TokenService.Received(1).StorePageTokenDirectAsync(
            fixture.TenantId,
            "page-1",
            "Bootstrapped",
            "zalo",
            "page-token",
            Arg.Any<CancellationToken>());
        await fixture.TokenService.DidNotReceiveWithAnyArgs()
            .MintAndStoreAsync(default, default!, default!, default!, default!);
    }

    private static IConfiguration BuildConfiguration(
        Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class SeederFixture(
        SqliteConnection connection,
        ServiceProvider services,
        Guid tenantId,
        IPancakePageTokenService tokenService,
        IPageListGateway pageList) : IAsyncDisposable
    {
        public IServiceProvider Services { get; } = services;
        public Guid TenantId { get; } = tenantId;
        public IPancakePageTokenService TokenService { get; } = tokenService;
        public IPageListGateway PageList { get; } = pageList;

        public static async Task<SeederFixture> CreateAsync(string tenantSlug)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE tenants (
                    id TEXT NOT NULL PRIMARY KEY,
                    slug TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    plan_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    brand_name TEXT NULL,
                    logo_url TEXT NULL,
                    primary_color TEXT NULL,
                    accent_color TEXT NULL,
                    support_name TEXT NULL,
                    widget_greeting TEXT NULL,
                    require_orchestration_approval INTEGER NOT NULL,
                    require_content_review INTEGER NOT NULL,
                    content_publishing_approval_policy TEXT NOT NULL,
                    content_publishing_policy_version INTEGER NOT NULL,
                    content_publishing_policy_updated_at TEXT NOT NULL,
                    require_chat_reply_approval INTEGER NOT NULL,
                    skip_chat_reply_review INTEGER NOT NULL,
                    require_kb_human_review INTEGER NOT NULL,
                    monthly_cost_cap_usd TEXT NULL,
                    ai_auto_reply_resume_minutes INTEGER NOT NULL,
                    idle_alert_minutes INTEGER NOT NULL,
                    lead_lost_after_days INTEGER NOT NULL,
                    auto_approve_lead_revenue INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
                """);
            var tenantId = Guid.NewGuid();
            var createdAt = new DateTimeOffset(
                2026,
                7,
                30,
                0,
                0,
                0,
                TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenants (
                    id,
                    slug,
                    display_name,
                    plan_name,
                    is_active,
                    require_orchestration_approval,
                    require_content_review,
                    content_publishing_approval_policy,
                    content_publishing_policy_version,
                    content_publishing_policy_updated_at,
                    require_chat_reply_approval,
                    skip_chat_reply_review,
                    require_kb_human_review,
                    ai_auto_reply_resume_minutes,
                    idle_alert_minutes,
                    lead_lost_after_days,
                    auto_approve_lead_revenue,
                    created_at)
                VALUES (
                    {tenantId},
                    {tenantSlug},
                    {"Tenant"},
                    {"free"},
                    {true},
                    {false},
                    {false},
                    {"human_required"},
                    {1L},
                    {createdAt},
                    {false},
                    {false},
                    {false},
                    {5},
                    {5},
                    {60},
                    {false},
                    {createdAt});
                """);

            var tokenService = Substitute.For<IPancakePageTokenService>();
            var pageList = Substitute.For<IPageListGateway>();
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(db)
                .AddSingleton(tokenService)
                .AddSingleton(pageList)
                .BuildServiceProvider();
            return new SeederFixture(
                connection,
                services,
                tenantId,
                tokenService,
                pageList);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
