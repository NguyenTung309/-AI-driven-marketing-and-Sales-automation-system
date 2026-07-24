namespace Clawbot.Infrastructure.Security;

/// <summary>
/// Migration-only access to the pre-authentication AES-CBC format.
/// Normal runtime services must never depend on this interface.
/// </summary>
internal interface ILegacyCiphertextDecryptor
{
    string DecryptLegacyForMigration(string ciphertext);
}
