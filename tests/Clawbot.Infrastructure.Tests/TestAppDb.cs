using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

/// <summary>
/// Builds a real <see cref="AppDbContext"/> over an open in-memory SQLite connection.
/// SQLite faithfully models the relational multi-step write flow (insert then update)
/// that the EF InMemory provider cannot. A model customizer strips SQL-Server-specific
/// column types (e.g. nvarchar(max)) so SQLite's DDL generator can build the schema.
/// The connection stays open for the lifetime of the context so the schema persists.
/// </summary>
internal sealed class TestAppDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    public Guid TenantId { get; }

    public TestAppDb(Guid? tenantId = null, IInterceptor? interceptor = null)
    {
        TenantId = tenantId ?? Guid.NewGuid();
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var tenants = Substitute.For<ITenantAccessor>();
        var ctx = new TenantContext(TenantId, "test");
        tenants.Current.Returns(ctx);
        tenants.Require().Returns(ctx);

        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>();
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        Db = new AppDbContext(builder.Options, tenants);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// Clears explicit (SQL-Server) column types from every property so SQLite's DDL
/// generator picks a compatible affinity instead of failing on types like nvarchar(max).
/// </summary>
internal sealed class SqliteFriendlyModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetConverter =
        new(v => v.UtcTicks, v => new DateTimeOffset(new DateTime(v, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableDateTimeOffsetConverter =
        new(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(new DateTime(v.Value, DateTimeKind.Utc)) : null);

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(DateTimeOffsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(NullableDateTimeOffsetConverter);
                }

                if (property.GetColumnType() is not null)
                {
                    property.SetColumnType(null);
                }
            }
        }
    }
}
