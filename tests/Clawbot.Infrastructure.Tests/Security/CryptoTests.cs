using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Security;

// M13 — HmacSignatureVerifier (webhook signature verification).
public sealed class HmacSignatureVerifierTests
{
    private const string Secret = "webhook-secret";
    private const string Body = "{\"event\":\"message\"}";

    private static string HexSig(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string Base64Sig(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void Hex_valid_signature_passes()
    {
        HmacSignatureVerifier.VerifyHexSha256(Body, HexSig(Body, Secret), Secret).Should().BeTrue();
    }

    [Fact]
    public void Hex_wrong_secret_fails()
    {
        HmacSignatureVerifier.VerifyHexSha256(Body, HexSig(Body, Secret), "other-secret").Should().BeFalse();
    }

    [Fact]
    public void Hex_strips_sha256_prefix()
    {
        HmacSignatureVerifier.VerifyHexSha256(Body, "sha256=" + HexSig(Body, Secret), Secret).Should().BeTrue();
    }

    [Fact]
    public void Base64_valid_signature_passes()
    {
        HmacSignatureVerifier.VerifyBase64Sha256(Body, Base64Sig(Body, Secret), Secret).Should().BeTrue();
    }

    [Fact]
    public void Base64_invalid_encoding_fails()
    {
        HmacSignatureVerifier.VerifyBase64Sha256(Body, "!!!not-base64!!!", Secret).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "sig", "secret")]
    [InlineData("body", "", "secret")]
    [InlineData("body", "sig", "")]
    public void Missing_inputs_return_false(string body, string sig, string secret)
    {
        HmacSignatureVerifier.VerifyHexSha256(body, sig, secret).Should().BeFalse();
    }
}

// M06/M13 — AesEncryptor round-trip + tamper rejection.
public sealed class AesEncryptorTests
{
    private static AesEncryptor Build()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new AesEncryptor(Options.Create(new EncryptionOptions { Base64Key = key }));
    }

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var sut = Build();

        var cipher = sut.Encrypt("super-secret-token");

        sut.Decrypt(cipher).Should().Be("super-secret-token");
    }

    [Fact]
    public void Same_plaintext_produces_different_ciphertext()
    {
        var sut = Build();

        var a = sut.Encrypt("same");
        var b = sut.Encrypt("same");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var sut = Build();
        var bytes = Convert.FromBase64String(sut.Encrypt("payload"));
        bytes[^1] ^= 0xFF;

        var act = () => sut.Decrypt(Convert.ToBase64String(bytes));

        act.Should().Throw<CryptographicException>();
    }
}
