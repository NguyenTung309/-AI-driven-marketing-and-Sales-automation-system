using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Clawbot.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tools (dotnet ef).
/// Reads connection string from appsettings.json.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Find appsettings.json by walking up from current directory
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "appsettings.json")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(dir ?? throw new InvalidOperationException("Could not find appsettings.json"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = config.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer not configured");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        var tenants = new DesignTimeTenantAccessor();
        return new AppDbContext(optionsBuilder.Options, tenants);
    }

    private sealed class DesignTimeTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() => throw new InvalidOperationException("Design-time context");
    }
}
