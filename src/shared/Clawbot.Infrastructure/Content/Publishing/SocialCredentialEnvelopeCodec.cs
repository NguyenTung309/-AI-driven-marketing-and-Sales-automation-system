using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Content.Publishing;

public enum SocialCredentialEnvelopeStatus
{
    Resolved,
    Invalid,
}

public sealed record SocialCredentialEnvelopeResult(
    SocialCredentialEnvelopeStatus Status,
    GraphChannelOptions? Options = null);

/// <summary>
/// Stores admin-managed social channel credentials inside an authenticated, context-bound envelope.
/// The underlying encryptor authenticates the entire plaintext, including tenant/provider/row target,
/// so ciphertext lifted from one tenant, provider, or row cannot be replayed into another.
/// </summary>
public static class SocialCredentialEnvelopeCodec
{
    private const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(
        IEncryptor encryptor,
        Guid tenantId,
        string provider,
        string? pageId,
        GraphChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        ArgumentNullException.ThrowIfNull(options);
        if (encryptor is not IAuthenticatedEncryptor)
            throw new InvalidOperationException("Social credential envelopes require authenticated encryption.");
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        var normalized = Normalize(options);
        var envelope = new StoredSocialCredentialEnvelope(
            CurrentVersion,
            tenantId,
            NormalizeProvider(provider),
            NormalizePageId(pageId),
            normalized);
        return encryptor.Encrypt(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public static SocialCredentialEnvelopeResult Decode(
        IEncryptor encryptor,
        Guid tenantId,
        string provider,
        string? pageId,
        string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        if (encryptor is not IAuthenticatedEncryptor authenticatedEncryptor
            || tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(provider)
            || string.IsNullOrEmpty(ciphertext))
        {
            return Invalid();
        }

        try
        {
            var plaintext = authenticatedEncryptor.DecryptAuthenticated(ciphertext);
            using var document = JsonDocument.Parse(plaintext);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid();

            // Unversioned legacy payloads are not bound to tenant/provider/row context.
            // Reject them so an admin must explicitly disable or replace the stored credentials.
            if (!document.RootElement.TryGetProperty("version", out _))
                return Invalid();

            var envelope = JsonSerializer.Deserialize<StoredSocialCredentialEnvelope>(plaintext, JsonOptions);
            if (envelope is null
                || envelope.Version != CurrentVersion
                || envelope.TenantId != tenantId
                || !string.Equals(envelope.Provider, NormalizeProvider(provider), StringComparison.Ordinal)
                || !string.Equals(envelope.PageId, NormalizePageId(pageId), StringComparison.Ordinal)
                || envelope.Options is null)
            {
                return Invalid();
            }

            return new SocialCredentialEnvelopeResult(
                SocialCredentialEnvelopeStatus.Resolved,
                Normalize(envelope.Options));
        }
        catch (JsonException)
        {
            return Invalid();
        }
        catch (FormatException)
        {
            return Invalid();
        }
        catch (CryptographicException)
        {
            return Invalid();
        }
    }

    public static GraphChannelOptions Normalize(GraphChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new GraphChannelOptions
        {
            Enabled = options.Enabled,
            Endpoint = options.Endpoint?.Trim() ?? string.Empty,
            PageAccessToken = options.PageAccessToken?.Trim() ?? string.Empty,
            PageId = options.PageId?.Trim() ?? string.Empty,
            OaAccessToken = options.OaAccessToken?.Trim() ?? string.Empty,
            OaId = options.OaId?.Trim() ?? string.Empty,
        };
    }

    private static string NormalizeProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("provider is required.", nameof(provider));
        return provider.Trim().ToLowerInvariant();
    }

    private static string? NormalizePageId(string? pageId) =>
        string.IsNullOrWhiteSpace(pageId) ? null : pageId.Trim();

    private static SocialCredentialEnvelopeResult Invalid() =>
        new(SocialCredentialEnvelopeStatus.Invalid);

    private sealed record StoredSocialCredentialEnvelope(
        int Version,
        Guid TenantId,
        string Provider,
        string? PageId,
        GraphChannelOptions Options);
}
