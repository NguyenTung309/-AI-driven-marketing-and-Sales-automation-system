using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakeTokenCipherTests
{
    [Fact]
    public void HasAuthenticatedEnvelope_ReturnsTrue_WhenCiphertextUsesAnotherKey()
    {
        // Arrange
        var writer = CreateEncryptor(0x11);
        var reader = CreateEncryptor(0x22);
        var stored = writer.Encrypt("page-token");

        // Act
        var isDecryptable = PancakeTokenCipher.IsEncrypted(reader, stored);
        var hasEnvelope = PancakeTokenCipher.HasAuthenticatedEnvelope(stored);

        // Assert
        isDecryptable.Should().BeFalse();
        hasEnvelope.Should().BeTrue();
    }

    [Fact]
    public void DecryptOrRaw_Throws_WhenCiphertextUsesAnotherKey()
    {
        // Arrange
        var writer = CreateEncryptor(0x11);
        var reader = CreateEncryptor(0x22);
        var stored = writer.Encrypt("page-token");

        // Act
        var act = () => PancakeTokenCipher.DecryptOrRaw(reader, stored);

        // Assert
        act.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void HasAuthenticatedEnvelope_ReturnsFalse_WhenValueIsRawToken()
    {
        // Arrange
        const string stored = "header.payload.signature";

        // Act
        var hasEnvelope = PancakeTokenCipher.HasAuthenticatedEnvelope(stored);

        // Assert
        hasEnvelope.Should().BeFalse();
    }

    private static AesEncryptor CreateEncryptor(byte fill) =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()),
        }));
}
