namespace Clawbot.SharedKernel.Multitenancy;

public interface ITenantAccessor
{
    TenantContext? Current { get; }
    TenantContext Require();
}
