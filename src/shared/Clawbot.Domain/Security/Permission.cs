using System.Diagnostics.CodeAnalysis;
using Clawbot.Domain.Common;

namespace Clawbot.Domain.Security;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Domain term — permission entity in the RBAC bounded context.")]
public sealed class Permission : AggregateRoot<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private Permission() { }

    public static Permission Create(string code, string? description = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Description = description,
        };
}
