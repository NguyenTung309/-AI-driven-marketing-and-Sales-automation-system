namespace Clawbot.SharedKernel.Multitenancy;

public sealed record TenantContext(Guid TenantId, string TenantSlug);
