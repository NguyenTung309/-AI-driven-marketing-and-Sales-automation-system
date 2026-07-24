using System.Buffers;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Domain.Integrations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Integrations.Meta;

public sealed record MetaCredentialEnvelopeMigrationFailure(
    string EntityKind,
    Guid TenantId,
    Guid RowId,
    string Reason);

public sealed record MetaCredentialEnvelopeMigrationResult(
    int MigratedCount,
    int CurrentCount,
    int InvalidCount,
    IReadOnlyList<MetaCredentialEnvelopeMigrationFailure> Failures);

/// <summary>
/// Operator-approved legacy row from a trusted pre-migration snapshot. The ciphertext digest binds
/// the exact stored generation; the context digest binds entity, tenant, provider, purpose, row, and parent.
/// </summary>
public sealed record MetaCredentialEnvelopeMigrationApproval(
    string EntityKind = "",
    Guid TenantId = default,
    Guid RowId = default,
    string CiphertextSha256 = "",
    string Provider = "",
    string Purpose = "",
    Guid? ParentId = null,
    string ContextSha256 = "");

public sealed class MetaCredentialEnvelopeMigrationOptions
{
    public const string SectionName = "Operations:MetaCredentialEnvelopeMigration";

    public MetaCredentialEnvelopeMigrationApproval[] ApprovedRows { get; init; } = [];
}

/// <summary>
/// Explicit one-shot migration for linked-Meta ciphertext written before context-bound envelopes.
/// This type must only run in a dedicated migration process; normal runtime reads are strict.
/// </summary>
public static partial class MetaCredentialEnvelopeMigrator
{
    private const string ConfigurationEntityKind = "social_credentials";
    private const string ConnectionEntityKind = "meta_connections";
    private const string AssetEntityKind = "meta_assets";
    private const int ApprovalContextGeneration = 1;
    private const int MaximumLegacySecretLength = 16_384;

    public static async Task<MetaCredentialEnvelopeMigrationResult> MigrateAsync(
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        var options = serviceProvider.GetRequiredService<IConfiguration>()
            .GetSection(MetaCredentialEnvelopeMigrationOptions.SectionName)
            .Get<MetaCredentialEnvelopeMigrationOptions>()
            ?? new MetaCredentialEnvelopeMigrationOptions();
        var result = await MigrateLegacyAsync(
                serviceProvider.GetRequiredService<AppDbContext>(),
                serviceProvider.GetRequiredService<IEncryptor>(),
                serviceProvider.GetRequiredService<IClock>(),
                options.ApprovedRows,
                ct)
            .ConfigureAwait(false);
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MetaCredentialEnvelopeMigrator");
        LogCompleted(
            logger,
            result.MigratedCount,
            result.CurrentCount,
            result.InvalidCount);
        foreach (var failure in result.Failures)
        {
            LogInvalidRow(
                logger,
                failure.EntityKind,
                failure.TenantId,
                failure.RowId,
                failure.Reason);
        }
        return result;
    }

    internal static async Task<MetaCredentialEnvelopeMigrationResult> MigrateLegacyAsync(
        AppDbContext db,
        IEncryptor encryptor,
        IClock clock,
        MetaCredentialEnvelopeMigrationApproval[] approvals,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(encryptor);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(approvals);

        var approvalSet = LegacyMigrationApprovalSet.Create(approvals);
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);
        var migrated = 0;
        var current = 0;
        var failures = new List<MetaCredentialEnvelopeMigrationFailure>();
        var now = clock.UtcNow;

        var configurations = await db.SocialCredentials
            .IgnoreQueryFilters()
            .Where(row => row.Provider == MetaGraphConfigurationStore.Provider
                && row.PageId == null
                && row.CredentialsEncrypted != string.Empty)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var row in configurations)
        {
            var state = MigrateConfiguration(row, encryptor, approvalSet, now);
            Count(
                state,
                ConfigurationEntityKind,
                row.TenantId,
                row.Id,
                ref migrated,
                ref current,
                failures);
        }

        var connections = await db.MetaConnections
            .IgnoreQueryFilters()
            .Where(row => row.AccessTokenEncrypted != string.Empty)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var row in connections)
        {
            var state = MigrateConnection(row, encryptor, approvalSet, now);
            Count(
                state,
                ConnectionEntityKind,
                row.TenantId,
                row.Id,
                ref migrated,
                ref current,
                failures);
        }

        var assets = await db.MetaAssets
            .IgnoreQueryFilters()
            .Where(row => row.AccessTokenEncrypted != string.Empty)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var row in assets)
        {
            var state = MigrateAsset(row, encryptor, approvalSet, now);
            Count(
                state,
                AssetEntityKind,
                row.TenantId,
                row.Id,
                ref migrated,
                ref current,
                failures);
        }

        if (migrated > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return new MetaCredentialEnvelopeMigrationResult(
            migrated,
            current,
            failures.Count,
            failures);
    }

    private static MigrationState MigrateConfiguration(
        SocialCredential row,
        IEncryptor encryptor,
        LegacyMigrationApprovalSet approvalSet,
        DateTimeOffset now)
    {
        var context = new MetaCredentialEnvelopeContext(
            row.TenantId,
            MetaGraphConfigurationStore.Provider,
            MetaCredentialPurposes.AppConfiguration,
            row.Id);
        if (MetaCredentialEnvelopeCodec.TryDecode(encryptor, context, row.CredentialsEncrypted, out _))
            return MigrationState.Current;
        if (!approvalSet.IsApproved(
                ConfigurationEntityKind,
                context,
                row.CredentialsEncrypted)
            || !TryReadUnboundSecretForMigration(encryptor, row.CredentialsEncrypted, out var secret)
            || !IsValidLegacyConfiguration(secret))
        {
            return MigrationState.Invalid;
        }

        row.UpdateCredentials(MetaCredentialEnvelopeCodec.Encode(encryptor, context, secret), now);
        return MigrationState.Migrated;
    }

    private static MigrationState MigrateConnection(
        MetaConnection row,
        IEncryptor encryptor,
        LegacyMigrationApprovalSet approvalSet,
        DateTimeOffset now)
    {
        var context = new MetaCredentialEnvelopeContext(
            row.TenantId,
            MetaGraphConfigurationStore.Provider,
            MetaCredentialPurposes.ConnectionAccessToken,
            row.Id);
        if (MetaCredentialEnvelopeCodec.TryDecode(encryptor, context, row.AccessTokenEncrypted, out _))
            return MigrationState.Current;
        if (!approvalSet.IsApproved(
                ConnectionEntityKind,
                context,
                row.AccessTokenEncrypted)
            || !TryReadUnboundSecretForMigration(encryptor, row.AccessTokenEncrypted, out var secret)
            || !IsValidLegacyToken(secret))
        {
            return MigrationState.Invalid;
        }

        row.ReprotectAccessToken(MetaCredentialEnvelopeCodec.Encode(encryptor, context, secret), now);
        return MigrationState.Migrated;
    }

    private static MigrationState MigrateAsset(
        MetaAsset row,
        IEncryptor encryptor,
        LegacyMigrationApprovalSet approvalSet,
        DateTimeOffset now)
    {
        var context = new MetaCredentialEnvelopeContext(
            row.TenantId,
            MetaGraphConfigurationStore.Provider,
            MetaCredentialPurposes.PageAccessToken,
            row.Id,
            row.ConnectionId);
        if (MetaCredentialEnvelopeCodec.TryDecode(encryptor, context, row.AccessTokenEncrypted, out _))
            return MigrationState.Current;
        if (!approvalSet.IsApproved(
                AssetEntityKind,
                context,
                row.AccessTokenEncrypted)
            || !TryReadUnboundSecretForMigration(encryptor, row.AccessTokenEncrypted, out var secret)
            || !IsValidLegacyToken(secret))
        {
            return MigrationState.Invalid;
        }

        row.ReprotectAccessToken(MetaCredentialEnvelopeCodec.Encode(encryptor, context, secret), now);
        return MigrationState.Migrated;
    }

    private static bool TryReadUnboundSecretForMigration(
        IEncryptor encryptor,
        string ciphertext,
        out string secret)
    {
        secret = string.Empty;
        if (encryptor is IAuthenticatedEncryptor authenticatedEncryptor
            && TryDecryptAuthenticated(authenticatedEncryptor, ciphertext, out var authenticatedPlaintext))
        {
            if (LooksLikeVersionedEnvelope(authenticatedPlaintext)
                || string.IsNullOrWhiteSpace(authenticatedPlaintext))
            {
                return false;
            }

            secret = authenticatedPlaintext;
            return true;
        }

        if (encryptor is not ILegacyCiphertextDecryptor legacyDecryptor)
            return false;

        try
        {
            var legacyPlaintext = legacyDecryptor.DecryptLegacyForMigration(ciphertext);
            if (LooksLikeVersionedEnvelope(legacyPlaintext)
                || string.IsNullOrWhiteSpace(legacyPlaintext))
            {
                return false;
            }

            secret = legacyPlaintext;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDecryptAuthenticated(
        IAuthenticatedEncryptor encryptor,
        string ciphertext,
        out string plaintext)
    {
        plaintext = string.Empty;
        try
        {
            plaintext = encryptor.DecryptAuthenticated(ciphertext);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    internal static bool LooksLikeVersionedEnvelope(string plaintext)
    {
        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("version", out _)
                && root.TryGetProperty("context", out _)
                && root.TryGetProperty("plaintext", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string ComputeCiphertextSha256(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ciphertext)));
    }

    internal static string ComputeApprovalContextSha256(
        string entityKind,
        MetaCredentialEnvelopeContext context,
        string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return Convert.ToHexString(ComputeApprovalContextDigest(
            entityKind,
            context,
            SHA256.HashData(Encoding.UTF8.GetBytes(ciphertext))));
    }

    private static byte[] ComputeApprovalContextDigest(
        string entityKind,
        MetaCredentialEnvelopeContext context,
        ReadOnlySpan<byte> ciphertextDigest) =>
        SHA256.HashData(BuildCanonicalApprovalContext(
            CreateApprovalKey(entityKind, context, ciphertextDigest)));

    private static string NormalizeContextValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Context value is required.", parameterName);
        return value.Trim().ToLowerInvariant();
    }

    private static bool IsValidLegacyConfiguration(string plaintext)
    {
        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && TryGetRequiredString(root, "appId", out _)
                && TryGetRequiredString(root, "appSecret", out _)
                && TryGetRequiredString(root, "configurationId", out _)
                && TryGetRequiredAbsoluteUri(root, "redirectUri")
                && TryGetRequiredAbsoluteUri(root, "frontendReturnUrl")
                && IsSupportedAuthorizationMode(root);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetRequiredAbsoluteUri(JsonElement root, string propertyName) =>
        TryGetRequiredString(root, propertyName, out var value)
        && Uri.TryCreate(value, UriKind.Absolute, out _);

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return IsValidLegacyToken(value);
    }

    private static bool IsSupportedAuthorizationMode(JsonElement root)
    {
        if (!root.TryGetProperty("authorizationMode", out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && MetaAuthorizationModes.IsSupported(property.GetString());
    }

    private static bool IsValidLegacyToken(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumLegacySecretLength
        && !value.Any(character => char.IsControl(character) || character == (char)0xFFFD);

    private static void Count(
        MigrationState state,
        string entityKind,
        Guid tenantId,
        Guid rowId,
        ref int migrated,
        ref int current,
        List<MetaCredentialEnvelopeMigrationFailure> failures)
    {
        switch (state)
        {
            case MigrationState.Migrated:
                migrated++;
                break;
            case MigrationState.Current:
                current++;
                break;
            default:
                failures.Add(new MetaCredentialEnvelopeMigrationFailure(
                    entityKind,
                    tenantId,
                    rowId,
                    "Unable to decrypt or validate legacy credential."));
                break;
        }
    }

    [LoggerMessage(
        EventId = 5253,
        Level = LogLevel.Information,
        Message = "Meta credential envelope migration completed: migrated {MigratedCount}, current {CurrentCount}, invalid {InvalidCount}")]
    private static partial void LogCompleted(
        ILogger logger,
        int migratedCount,
        int currentCount,
        int invalidCount);

    [LoggerMessage(
        EventId = 5254,
        Level = LogLevel.Warning,
        Message = "Meta credential envelope migration skipped invalid {EntityKind} row {RowId} for tenant {TenantId}: {Reason}")]
    private static partial void LogInvalidRow(
        ILogger logger,
        string entityKind,
        Guid tenantId,
        Guid rowId,
        string reason);

    private sealed class LegacyMigrationApprovalSet(
        Dictionary<MigrationApprovalKey, ApprovedLegacyRow> approvedRows)
    {
        public static LegacyMigrationApprovalSet Create(
            MetaCredentialEnvelopeMigrationApproval[] approvals)
        {
            var approvedRows = new Dictionary<MigrationApprovalKey, ApprovedLegacyRow>(approvals.Length);
            foreach (var approval in approvals)
            {
                var ciphertextDigest = ParseDigest(
                    approval.CiphertextSha256,
                    "Meta credential migration approval ciphertext digest is invalid.");
                var contextDigest = ParseDigest(
                    approval.ContextSha256,
                    "Meta credential migration approval context digest is invalid.");
                var key = CreateApprovalKey(approval, ciphertextDigest);
                var expectedContextDigest = SHA256.HashData(BuildCanonicalApprovalContext(key));
                if (!CryptographicOperations.FixedTimeEquals(contextDigest, expectedContextDigest))
                    throw new InvalidOperationException("Meta credential migration approval context digest does not match.");

                if (!approvedRows.TryAdd(key, new ApprovedLegacyRow(ciphertextDigest, contextDigest)))
                    throw new InvalidOperationException("Meta credential migration approval is duplicated.");
            }

            return new LegacyMigrationApprovalSet(approvedRows);
        }

        public bool IsApproved(
            string entityKind,
            MetaCredentialEnvelopeContext context,
            string ciphertext)
        {
            var actualCiphertextDigest = SHA256.HashData(Encoding.UTF8.GetBytes(ciphertext));
            var key = CreateApprovalKey(entityKind, context, actualCiphertextDigest);
            if (!approvedRows.TryGetValue(key, out var approvedRow))
                return false;

            var actualContextDigest = SHA256.HashData(BuildCanonicalApprovalContext(key));
            return CryptographicOperations.FixedTimeEquals(
                    approvedRow.CiphertextDigest,
                    actualCiphertextDigest)
                && CryptographicOperations.FixedTimeEquals(
                    approvedRow.ContextDigest,
                    actualContextDigest);
        }

        private static byte[] ParseDigest(string value, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(errorMessage);

            byte[] digest;
            try
            {
                digest = Convert.FromHexString(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(errorMessage, ex);
            }

            if (digest.Length != SHA256.HashSizeInBytes)
                throw new InvalidOperationException(errorMessage);
            return digest;
        }
    }

    private static MigrationApprovalKey CreateApprovalKey(
        MetaCredentialEnvelopeMigrationApproval approval,
        ReadOnlySpan<byte> ciphertextDigest)
    {
        try
        {
            return CreateApprovalKey(
                approval.EntityKind,
                new MetaCredentialEnvelopeContext(
                    approval.TenantId,
                    approval.Provider,
                    approval.Purpose,
                    approval.RowId,
                    approval.ParentId),
                ciphertextDigest);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("Meta credential migration approval context is invalid.", ex);
        }
    }

    private static MigrationApprovalKey CreateApprovalKey(
        string entityKind,
        MetaCredentialEnvelopeContext context,
        ReadOnlySpan<byte> ciphertextDigest)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(context));
        if (context.RowId == Guid.Empty)
            throw new ArgumentException("RowId is required.", nameof(context));
        if (context.ParentId == Guid.Empty)
            throw new ArgumentException("ParentId cannot be empty.", nameof(context));
        if (ciphertextDigest.Length != SHA256.HashSizeInBytes)
            throw new ArgumentException("Ciphertext digest is invalid.", nameof(ciphertextDigest));

        var normalizedEntityKind = NormalizeContextValue(entityKind, nameof(entityKind));
        var normalizedProvider = NormalizeContextValue(context.Provider, nameof(context.Provider));
        var normalizedPurpose = NormalizeContextValue(context.Purpose, nameof(context.Purpose));
        if (!IsKnownEntityKind(normalizedEntityKind)
            || normalizedProvider != MetaGraphConfigurationStore.Provider
            || normalizedPurpose != ExpectedPurpose(normalizedEntityKind)
            || (normalizedEntityKind == AssetEntityKind) != context.ParentId.HasValue)
        {
            throw new ArgumentException("Meta credential migration approval context is invalid.", nameof(context));
        }

        return new MigrationApprovalKey(
            ApprovalContextGeneration,
            normalizedEntityKind,
            context.TenantId,
            normalizedProvider,
            normalizedPurpose,
            context.RowId,
            context.ParentId,
            Convert.ToHexString(ciphertextDigest));
    }

    private static byte[] BuildCanonicalApprovalContext(MigrationApprovalKey key)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("generation", key.Generation);
        writer.WriteString("entityKind", key.EntityKind);
        writer.WriteString("tenantId", key.TenantId.ToString("D"));
        writer.WriteString("provider", key.Provider);
        writer.WriteString("secretKind", key.Purpose);
        writer.WriteString("rowId", key.RowId.ToString("D"));
        if (key.ParentId.HasValue)
            writer.WriteString("parentId", key.ParentId.Value.ToString("D"));
        else
            writer.WriteNull("parentId");
        writer.WriteString("ciphertextSha256", key.CiphertextSha256);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string ExpectedPurpose(string entityKind) =>
        entityKind switch
        {
            ConfigurationEntityKind => MetaCredentialPurposes.AppConfiguration,
            ConnectionEntityKind => MetaCredentialPurposes.ConnectionAccessToken,
            AssetEntityKind => MetaCredentialPurposes.PageAccessToken,
            _ => string.Empty,
        };

    private static bool IsKnownEntityKind(string entityKind) =>
        entityKind is ConfigurationEntityKind or ConnectionEntityKind or AssetEntityKind;

    private sealed record ApprovedLegacyRow(
        byte[] CiphertextDigest,
        byte[] ContextDigest);

    private readonly record struct MigrationApprovalKey(
        int Generation,
        string EntityKind,
        Guid TenantId,
        string Provider,
        string Purpose,
        Guid RowId,
        Guid? ParentId,
        string CiphertextSha256);

    private enum MigrationState
    {
        Migrated,
        Current,
        Invalid,
    }
}
