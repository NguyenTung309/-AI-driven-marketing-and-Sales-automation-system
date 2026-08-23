using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Integrations.Meta;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Integrations.Meta;

// Các helper thuần của bộ migrate envelope Meta: nhận diện payload đã versioned + băm ngữ cảnh phê duyệt.
public sealed class MetaCredentialEnvelopeMigratorHelpersTests
{
    [Fact]
    public void LooksLikeVersionedEnvelope_FullEnvelope_True()
    {
        var json = """{"version":1,"context":{"tenantId":"x"},"plaintext":"secret"}""";

        MetaCredentialEnvelopeMigrator.LooksLikeVersionedEnvelope(json).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"context":{}}""")]        // thiếu plaintext
    [InlineData("""{"context":{},"plaintext":"x"}""")]    // thiếu version
    [InlineData("""{"version":1,"plaintext":"x"}""")]     // thiếu context
    [InlineData("\"just-a-string\"")]                        // không phải object
    [InlineData("[1,2,3]")]                                   // mảng
    [InlineData("not json")]                                  // rác
    public void LooksLikeVersionedEnvelope_Incomplete_False(string json)
    {
        MetaCredentialEnvelopeMigrator.LooksLikeVersionedEnvelope(json).Should().BeFalse();
    }

    [Fact]
    public void ComputeCiphertextSha256_MatchesSha256Hex()
    {
        const string ciphertext = "some-ciphertext";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ciphertext)));

        MetaCredentialEnvelopeMigrator.ComputeCiphertextSha256(ciphertext).Should().Be(expected);
    }

    [Fact]
    public void ComputeCiphertextSha256_Blank_Throws()
    {
        var act = () => MetaCredentialEnvelopeMigrator.ComputeCiphertextSha256("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeApprovalContextSha256_IsStableAndContextBound()
    {
        var context = new MetaCredentialEnvelopeContext(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "meta",
            MetaCredentialPurposes.PageAccessToken,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var a = MetaCredentialEnvelopeMigrator.ComputeApprovalContextSha256("meta_assets", context, "cipher");
        var b = MetaCredentialEnvelopeMigrator.ComputeApprovalContextSha256("meta_assets", context, "cipher");

        a.Should().Be(b);
        a.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void ComputeApprovalContextSha256_DifferentCiphertext_DifferentDigest()
    {
        var context = new MetaCredentialEnvelopeContext(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "meta",
            MetaCredentialPurposes.PageAccessToken,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var a = MetaCredentialEnvelopeMigrator.ComputeApprovalContextSha256("meta_assets", context, "cipher-a");
        var b = MetaCredentialEnvelopeMigrator.ComputeApprovalContextSha256("meta_assets", context, "cipher-b");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ComputeApprovalContextSha256_MismatchedPurposeForEntityKind_Throws()
    {
        // meta_assets yêu cầu purpose page_access_token; đưa sai purpose → context invalid.
        var badContext = new MetaCredentialEnvelopeContext(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "meta",
            MetaCredentialPurposes.AppConfiguration,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var act = () => MetaCredentialEnvelopeMigrator.ComputeApprovalContextSha256("meta_assets", badContext, "cipher");
        act.Should().Throw<ArgumentException>();
    }
}
