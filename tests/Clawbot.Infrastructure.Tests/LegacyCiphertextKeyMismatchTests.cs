using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

// Rows written before the authenticated-envelope change hold AES-CBC ciphertext: [IV(16)][cipher].
// CBC carries no tag, so a wrong key usually fails PKCS7 padding instead of failing authentication.
// These tests pin the behaviour that matters: a legacy blob we cannot decrypt must surface as an
// error, never be mistaken for a raw token and re-encrypted on top of itself.
public sealed class LegacyCiphertextKeyMismatchTests
{
    private const string Plaintext = "legacy-page-token";

    [Fact]
    public void DecryptOrRaw_Throws_WhenLegacyCiphertextUsesAnotherKey()
    {
        // Arrange
        var stored = CreateLegacyCiphertext(KeyBytes(0x11), Plaintext);
        var reader = CreateEncryptor(0x22);

        // Act
        var act = () => PancakeTokenCipher.DecryptOrRaw(reader, stored);

        // Assert
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void DecryptOrRaw_ReturnsPlaintext_WhenLegacyCiphertextUsesTheSameKey()
    {
        // Arrange
        var stored = CreateLegacyCiphertext(KeyBytes(0x11), Plaintext);
        var reader = CreateEncryptor(0x11);

        // Act
        var decrypted = PancakeTokenCipher.DecryptOrRaw(reader, stored);

        // Assert
        decrypted.Should().Be(Plaintext);
    }

    [Fact]
    public void IsEncrypted_ReturnsFalse_WhenLegacyCiphertextUsesAnotherKey()
    {
        // Arrange
        var stored = CreateLegacyCiphertext(KeyBytes(0x11), Plaintext);
        var reader = CreateEncryptor(0x22);

        // Act
        var isDecryptable = PancakeTokenCipher.IsEncrypted(reader, stored);

        // Assert
        isDecryptable.Should().BeFalse();
    }

    [Fact]
    public void HasLegacyCiphertextEnvelope_ReturnsTrue_ForAnUnreadableLegacyBlob()
    {
        // Arrange
        var stored = CreateLegacyCiphertext(KeyBytes(0x11), Plaintext);

        // Act
        var hasLegacyEnvelope = PancakeTokenCipher.HasLegacyCiphertextEnvelope(stored);
        var hasAuthenticatedEnvelope = PancakeTokenCipher.HasAuthenticatedEnvelope(stored);

        // Assert
        // Chinh cap nay chan duong "coi nhu raw token roi ma hoa de len" trong DecryptOrRaw.
        hasLegacyEnvelope.Should().BeTrue();
        hasAuthenticatedEnvelope.Should().BeFalse();
    }

    [Fact]
    public void HasLegacyCiphertextEnvelope_ReturnsFalse_ForARawJwtStyleToken()
    {
        // Arrange
        const string stored = "header.payload.signature";

        // Act
        var hasLegacyEnvelope = PancakeTokenCipher.HasLegacyCiphertextEnvelope(stored);

        // Assert
        hasLegacyEnvelope.Should().BeFalse();
    }

    // IV co dinh: khoa sai lam PKCS7 padding hong ~255/256 lan, IV ngau nhien se khien test thinh thoang
    // do (1/256 truong hop padding tinh co hop le va tra ve rac). Hang so nay da duoc kiem chung la hong.
    private static string CreateLegacyCiphertext(byte[] key, string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);

        var blob = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, blob, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    private static byte[] KeyBytes(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static AesEncryptor CreateEncryptor(byte fill) =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(KeyBytes(fill)),
        }));
}
