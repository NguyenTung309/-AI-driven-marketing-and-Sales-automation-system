using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NSubstitute;

namespace Clawbot.AgentService.Tests;

internal sealed class AgentServiceTestAppDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AgentServiceTestAppDb(Guid tenantId)
    {
        TenantId = tenantId;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var tenants = Substitute.For<ITenantAccessor>();
        var ctx = new TenantContext(TenantId, "test");
        tenants.Current.Returns(ctx);
        tenants.Require().Returns(ctx);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;

        Db = new AppDbContext(options, tenants);
        Db.Database.EnsureCreated();
    }

    public AppDbContext Db { get; }

    public Guid TenantId { get; }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

internal sealed class SqliteFriendlyModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnType() is not null)
                    property.SetColumnType(null);
            }
        }
    }
}
