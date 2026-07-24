using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Integrations.Meta;

public static class MetaCredentialPurposes
{
    public const string AppConfiguration = "app_configuration";
    public const string ConnectionAccessToken = "connection_access_token";
    public const string PageAccessToken = "page_access_token";
}

public sealed record MetaCredentialEnvelopeContext(
    Guid TenantId,
    string Provider,
    string Purpose,
    Guid RowId,
    Guid? ParentId = null);

/// <summary>
/// Authenticated storage envelope for linked-Meta secrets. The authenticated plaintext binds the
/// secret to its tenant, provider, purpose, immutable row ID, and optional immutable parent row ID.
/// </summary>
public static class MetaCredentialEnvelopeCodec
{
    public const byte CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(
        IEncryptor encryptor,
        MetaCredentialEnvelopeContext context,
        string plaintext)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Plaintext is required.", nameof(plaintext));
        if (encryptor is not IAuthenticatedEncryptor)
            throw new InvalidOperationException("Meta credential envelopes require authenticated encryption.");

        var normalizedContext = Normalize(context);
        var envelope = new StoredMetaCredentialEnvelope(CurrentVersion, normalizedContext, plaintext);
        return encryptor.Encrypt(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public static bool TryDecode(
        IEncryptor encryptor,
        MetaCredentialEnvelopeContext expectedContext,
        string ciphertext,
        out string? plaintext)
    {
        plaintext = null;
        ArgumentNullException.ThrowIfNull(encryptor);
        ArgumentNullException.ThrowIfNull(expectedContext);
        if (encryptor is not IAuthenticatedEncryptor authenticatedEncryptor
            || string.IsNullOrEmpty(ciphertext))
        {
            return false;
        }

        try
        {
            var decrypted = authenticatedEncryptor.DecryptAuthenticated(ciphertext);
            var envelope = JsonSerializer.Deserialize<StoredMetaCredentialEnvelope>(decrypted, JsonOptions);
            if (envelope is null
                || envelope.Version != CurrentVersion
                || envelope.Context != Normalize(expectedContext)
                || string.IsNullOrWhiteSpace(envelope.Plaintext))
            {
                return false;
            }

            plaintext = envelope.Plaintext;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static MetaCredentialEnvelopeContext Normalize(MetaCredentialEnvelopeContext context)
    {
        if (context.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(context));
        if (context.RowId == Guid.Empty)
            throw new ArgumentException("RowId is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Provider))
            throw new ArgumentException("Provider is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.Purpose))
            throw new ArgumentException("Purpose is required.", nameof(context));

        return context with
        {
            Provider = context.Provider.Trim().ToLowerInvariant(),
            Purpose = context.Purpose.Trim().ToLowerInvariant(),
        };
    }

    private sealed record StoredMetaCredentialEnvelope(
        byte Version,
        MetaCredentialEnvelopeContext Context,
        string Plaintext);
}
