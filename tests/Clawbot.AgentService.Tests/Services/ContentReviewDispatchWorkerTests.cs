using Clawbot.AgentService.Services;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ContentReviewDispatchWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    public ContentReviewDispatchWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var setup = CreateDb();
        setup.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task DispatchOnceAsync_EnumeratesActiveTenantIdsOnly()
    {
        var activeA = Tenant.Create("active-a", "Active A", "free", Now);
        var activeB = Tenant.Create("active-b", "Active B", "free", Now);
        var inactive = Tenant.Create("inactive", "Inactive", "free", Now);
        typeof(Tenant).GetProperty(nameof(Tenant.IsActive))!
            .SetValue(inactive, false);

        await using (var setup = CreateDb())
        {
            setup.Tenants.AddRange(activeA, activeB, inactive);
            await setup.SaveChangesAsync();
        }

        var recorder = new RecordingTenantRunner();
        var worker = CreateWorker(recorder);

        await worker.DispatchOnceAsync();

        recorder.TenantIds.Should().BeEquivalentTo(
            [activeA.Id, activeB.Id],
            options => options.WithoutStrictOrdering());
        recorder.TenantIds.Should().NotContain(inactive.Id);
    }

    [Fact]
    public async Task DispatchOnceAsync_UsesStableTenantOrdering()
    {
        var first = Tenant.Create("zzz", "Z", "free", Now);
        var second = Tenant.Create("aaa", "A", "free", Now);
        await using (var setup = CreateDb())
        {
            setup.Tenants.AddRange(first, second);
            await setup.SaveChangesAsync();
        }

        var recorder = new RecordingTenantRunner();
        var worker = CreateWorker(recorder);

        await worker.DispatchOnceAsync();

        recorder.TenantIds.Should().Equal(
            recorder.TenantIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task DispatchOnceAsync_CreatesFreshScopeForEachTenant()
    {
        var tenantA = Tenant.Create("scope-a", "Scope A", "free", Now);
        var tenantB = Tenant.Create("scope-b", "Scope B", "free", Now);
        await using (var setup = CreateDb())
        {
            setup.Tenants.AddRange(tenantA, tenantB);
            await setup.SaveChangesAsync();
        }

        var recorder = new RecordingTenantRunner();
        var worker = CreateWorker(recorder);

        await worker.DispatchOnceAsync();

        recorder.ScopeIds.Should().HaveCount(2);
        recorder.ScopeIds.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task DispatchOnceAsync_ContinuesAfterOneTenantFails()
    {
        var first = Tenant.Create("fail-first", "Fail First", "free", Now);
        var second = Tenant.Create("ok-second", "Ok Second", "free", Now);
        await using (var setup = CreateDb())
        {
            setup.Tenants.AddRange(first, second);
            await setup.SaveChangesAsync();
        }

        var ordered = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();
        var failingTenant = ordered[0];
        var recorder = new RecordingTenantRunner(
            tenantId => tenantId == failingTenant
                ? throw new InvalidOperationException("tenant_failed")
                : Task.CompletedTask);
        var worker = CreateWorker(recorder);

        await worker.DispatchOnceAsync();

        recorder.TenantIds.Should().Equal(ordered);
    }

    [Fact]
    public async Task DispatchOnceAsync_PropagatesCancellationAndStopsLaterTenants()
    {
        var first = Tenant.Create("cancel-first", "Cancel First", "free", Now);
        var second = Tenant.Create("cancel-second", "Cancel Second", "free", Now);
        await using (var setup = CreateDb())
        {
            setup.Tenants.AddRange(first, second);
            await setup.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource();
        var ordered = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();
        var recorder = new RecordingTenantRunner(
            _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        var worker = CreateWorker(recorder);

        var act = async () => await worker.DispatchOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        recorder.TenantIds.Should().ContainSingle()
            .Which.Should().Be(ordered[0]);
    }

    private ContentReviewDispatchWorker CreateWorker(RecordingTenantRunner recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped(_ => CreateDb());
        services.AddScoped<IReviewTenantRunner>(sp =>
        {
            recorder.RegisterScope(Guid.NewGuid());
            return recorder;
        });

        var provider = services.BuildServiceProvider();
        return new ContentReviewDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContentReviewWorkerOptions
            {
                PollInterval = TimeSpan.FromSeconds(1)
            }),
            NullLogger<ContentReviewDispatchWorker>.Instance);
    }

    private AppDbContext CreateDb()
    {
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Current.Returns((TenantContext?)null);
        tenants.Require().Returns(_ => throw new InvalidOperationException("tenant_required"));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;
        return new AppDbContext(options, tenants);
    }

    private sealed class RecordingTenantRunner(
        Func<Guid, Task>? handler = null) : IReviewTenantRunner
    {
        private readonly Func<Guid, Task> _handler =
            handler ?? (_ => Task.CompletedTask);

        public List<Guid> TenantIds { get; } = [];
        public List<Guid> ScopeIds { get; } = [];

        public void RegisterScope(Guid scopeId) => ScopeIds.Add(scopeId);

        public async Task RunTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            TenantIds.Add(tenantId);
            await _handler(tenantId);
        }
    }
}
