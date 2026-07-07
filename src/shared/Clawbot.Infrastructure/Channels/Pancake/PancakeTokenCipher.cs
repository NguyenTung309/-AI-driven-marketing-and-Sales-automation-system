using Clawbot.SharedKernel.Security;

namespace Clawbot.Infrastructure.Channels.Pancake;

// Stored inbox tokens are AES-encrypted, but rows written before the encrypt-at-write fix
// hold the raw JWT. DecryptOrRaw keeps those readable until InboxTokenEncryptionMigrator
// re-encrypts them at startup.
public static class PancakeTokenCipher
{
    public static string DecryptOrRaw(IEncryptor encryptor, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        try { return encryptor.Decrypt(stored); }
        catch (FormatException) { return stored; }
        catch (System.Security.Cryptography.CryptographicException) { return stored; }
    }

    public static bool IsEncrypted(IEncryptor encryptor, string stored)
    {
        ArgumentNullException.ThrowIfNull(encryptor);
        if (string.IsNullOrEmpty(stored)) return false;
        try { encryptor.Decrypt(stored); return true; }
        catch (FormatException) { return false; }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
}
