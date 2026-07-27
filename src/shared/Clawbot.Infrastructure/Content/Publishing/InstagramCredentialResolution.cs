namespace Clawbot.Infrastructure.Content.Publishing;

public enum InstagramCredentialResolutionStatus
{
    Absent,
    Disabled,
    Resolved,
    Invalid,
}

public sealed record InstagramCredential(
    string InstagramUserId,
    string AccessToken);

public sealed record InstagramCredentialResolution(
    InstagramCredentialResolutionStatus Status,
    InstagramCredential? Credential = null);

public interface IInstagramCredentialResolver
{
    Task<InstagramCredentialResolution> ResolveAsync(
        Guid tenantId,
        CancellationToken ct = default);
}
