using System.Security.Cryptography;
using System.Text;
using Clawbot.SharedKernel.Security;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Security;

public sealed class HmacSignatureVerifierTests
{
    private const string Secret = "super-secret-key";
    private const string Body = "{\"event\":\"message\",\"id\":42}";

    private static string HexSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
    }

    private static string Base64Signature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void VerifyHexSha256_ValidSignature_ReturnsTrue()
    {
        HmacSignatureVerifier.VerifyHexSha256(Body, HexSignature(Body, Secret), Secret)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifyHexSha256_PrefixedSignature_StripsPrefixAndVerifies()
    {
        var signature = "sha256=" + HexSignature(Body, Secret);

        HmacSignatureVerifier.VerifyHexSha256(Body, signature, Secret).Should().BeTrue();
    }

    [Fact]
    public void VerifyHexSha256_WrongSecret_ReturnsFalse()
    {
        HmacSignatureVerifier.VerifyHexSha256(Body, HexSignature(Body, Secret), "other-secret")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifyHexSha256_TamperedBody_ReturnsFalse()
    {
        HmacSignatureVerifier.VerifyHexSha256("{\"event\":\"tampered\"}", HexSignature(Body, Secret), Secret)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("", "sig", Secret)]
    [InlineData(Body, "", Secret)]
    [InlineData(Body, "sig", "")]
    public void VerifyHexSha256_MissingInput_ReturnsFalse(string body, string signature, string secret)
    {
        HmacSignatureVerifier.VerifyHexSha256(body, signature, secret).Should().BeFalse();
    }

    [Fact]
    public void VerifyBase64Sha256_ValidSignature_ReturnsTrue()
    {
        HmacSignatureVerifier.VerifyBase64Sha256(Body, Base64Signature(Body, Secret), Secret)
            .Should().BeTrue();
    }

    [Fact]
    public void VerifyBase64Sha256_WrongSecret_ReturnsFalse()
    {
        HmacSignatureVerifier.VerifyBase64Sha256(Body, Base64Signature(Body, Secret), "other")
            .Should().BeFalse();
    }

    [Fact]
    public void VerifyBase64Sha256_MalformedBase64_ReturnsFalse()
    {
        HmacSignatureVerifier.VerifyBase64Sha256(Body, "not-valid-base64!!!", Secret)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("", "sig", Secret)]
    [InlineData(Body, "", Secret)]
    [InlineData(Body, "sig", "")]
    public void VerifyBase64Sha256_MissingInput_ReturnsFalse(string body, string signature, string secret)
    {
        HmacSignatureVerifier.VerifyBase64Sha256(body, signature, secret).Should().BeFalse();
    }
}
