namespace Clawbot.Domain.Common;

public interface ITenantOwned
{
    Guid TenantId { get; }
}
