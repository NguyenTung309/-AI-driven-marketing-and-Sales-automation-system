using System.Reflection;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Common;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Identity;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantAccessor tenants)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    private readonly ITenantAccessor _tenants = tenants;

    IConversationSet IAppDbContext.Conversations => new EfConversationSet(Set<Conversation>());

    Task<int> IAppDbContext.SaveChangesAsync(CancellationToken ct) => base.SaveChangesAsync(ct);

    private sealed class EfConversationSet(DbSet<Conversation> set) : IConversationSet
    {
        public void Add(Conversation conversation) => set.Add(conversation);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
                method.MakeGenericMethod(entity.ClrType).Invoke(this, [builder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned
    {
        var tenantRef = _tenants;
        builder.Entity<TEntity>().HasQueryFilter(
            e => e.TenantId == (tenantRef.Current != null ? tenantRef.Current.TenantId : Guid.Empty));
    }
}
