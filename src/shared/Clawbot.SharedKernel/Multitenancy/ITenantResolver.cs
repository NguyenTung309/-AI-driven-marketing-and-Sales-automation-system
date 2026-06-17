namespace Clawbot.SharedKernel.Multitenancy;

public interface ITenantResolver
{
    Task<Guid> ResolveTenantIdAsync(CancellationToken ct = default);
}
