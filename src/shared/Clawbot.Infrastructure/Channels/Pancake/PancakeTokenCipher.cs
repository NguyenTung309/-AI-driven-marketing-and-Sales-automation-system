using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Channels.Pancake;

// Stored inbox tokens are AES-encrypted, but rows written before the encrypt-at-write fix
// hold the raw JWT. DecryptOrRaw keeps those readable until InboxTokenEncryptionMigrator
// re-encrypts them at startup.
public static class PancakeTokenCipher
{
    private const byte AuthenticatedEnvelopeVersion = 1;
    private const int AuthenticatedEnvelopeMinimumLength = 49;

    public static string DecryptOrRaw(IEncryptor encryptor, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try
        {
            return encryptor.Decrypt(stored);
        }
        catch (FormatException)
        {
            return stored;
        }
        catch (System.Security.Cryptography.CryptographicException)
            when (!HasAuthenticatedEnvelope(stored)
                && !HasLegacyCiphertextEnvelope(stored))
        {
            return stored;
        }
    }

    public static bool IsEncrypted(IEncryptor encryptor, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        if (string.IsNullOrEmpty(stored)) return false;
        try { encryptor.Decrypt(stored); return true; }
        catch (FormatException) { return false; }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    public static bool HasAuthenticatedEnvelope(string stored)
    {
        if (!TryDecode(stored, out var bytes)) return false;
        return bytes.Length >= AuthenticatedEnvelopeMinimumLength
            && bytes[0] == AuthenticatedEnvelopeVersion;
    }

    public static bool HasLegacyCiphertextEnvelope(string stored)
    {
        if (!TryDecode(stored, out var bytes)
            || HasAuthenticatedEnvelope(stored))
        {
            return false;
        }

        const int ivLength = 16;
        return bytes.Length > ivLength
            && (bytes.Length - ivLength) % ivLength == 0;
    }

    private static bool TryDecode(string stored, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(stored)) return false;

        try
        {
            bytes = Convert.FromBase64String(stored);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
