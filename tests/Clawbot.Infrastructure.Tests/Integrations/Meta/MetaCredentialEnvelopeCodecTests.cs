using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.SharedKernel.Security;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Integrations.Meta;

// Envelope tín chỉ Meta xác thực: gắn secret vào tenant/provider/purpose/row/parent; sai ngữ cảnh → không decode được.
public sealed class MetaCredentialEnvelopeCodecTests
{
    private sealed class FakeAuthenticatedEncryptor : IAuthenticatedEncryptor
    {
        private const string Prefix = "enc::";
        public string Encrypt(string plaintext) => Prefix + plaintext;
        public string Decrypt(string ciphertext) => DecryptAuthenticated(ciphertext);
        public string DecryptAuthenticated(string ciphertext)
        {
            if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
                throw new CryptographicException("tampered");
            return ciphertext[Prefix.Length..];
        }
    }

    private sealed class PlainEncryptor : IEncryptor
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }

    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Row = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Parent = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static MetaCredentialEnvelopeContext Context(Guid? parent = null) =>
        new(Tenant, "Facebook", MetaCredentialPurposes.PageAccessToken, Row, parent);

    [Fact]
    public void EncodeThenTryDecode_SameContext_ReturnsPlaintext()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = MetaCredentialEnvelopeCodec.Encode(enc, Context(Parent), "secret-token");

        var ok = MetaCredentialEnvelopeCodec.TryDecode(enc, Context(Parent), ciphertext, out var plaintext);

        ok.Should().BeTrue();
        plaintext.Should().Be("secret-token");
    }

    [Fact]
    public void TryDecode_ProviderCaseInsensitive_StillMatches()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = MetaCredentialEnvelopeCodec.Encode(enc, Context(Parent), "tok");

        var upperContext = new MetaCredentialEnvelopeContext(
            Tenant, "FACEBOOK", MetaCredentialPurposes.PageAccessToken, Row, Parent);
        var ok = MetaCredentialEnvelopeCodec.TryDecode(enc, upperContext, ciphertext, out var plaintext);

        ok.Should().BeTrue();
        plaintext.Should().Be("tok");
    }

    [Fact]
    public void TryDecode_DifferentRow_Fails()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = MetaCredentialEnvelopeCodec.Encode(enc, Context(Parent), "tok");

        var otherRow = new MetaCredentialEnvelopeContext(
            Tenant, "facebook", MetaCredentialPurposes.PageAccessToken, Guid.NewGuid(), Parent);
        MetaCredentialEnvelopeCodec.TryDecode(enc, otherRow, ciphertext, out var plaintext).Should().BeFalse();
        plaintext.Should().BeNull();
    }

    [Fact]
    public void TryDecode_DifferentParent_Fails()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = MetaCredentialEnvelopeCodec.Encode(enc, Context(Parent), "tok");

        MetaCredentialEnvelopeCodec.TryDecode(enc, Context(Guid.NewGuid()), ciphertext, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_DifferentPurpose_Fails()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = MetaCredentialEnvelopeCodec.Encode(enc, Context(Parent), "tok");

        var otherPurpose = new MetaCredentialEnvelopeContext(
            Tenant, "facebook", MetaCredentialPurposes.ConnectionAccessToken, Row, Parent);
        MetaCredentialEnvelopeCodec.TryDecode(enc, otherPurpose, ciphertext, out _).Should().BeFalse();
    }

    [Fact]
    public void Encode_NonAuthenticatedEncryptor_Throws()
    {
        var act = () => MetaCredentialEnvelopeCodec.Encode(new PlainEncryptor(), Context(), "tok");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Encode_BlankPlaintext_Throws()
    {
        var act = () => MetaCredentialEnvelopeCodec.Encode(new FakeAuthenticatedEncryptor(), Context(), "  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_EmptyTenant_Throws()
    {
        var ctx = new MetaCredentialEnvelopeContext(Guid.Empty, "facebook", MetaCredentialPurposes.PageAccessToken, Row);
        var act = () => MetaCredentialEnvelopeCodec.Encode(new FakeAuthenticatedEncryptor(), ctx, "tok");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_EmptyRow_Throws()
    {
        var ctx = new MetaCredentialEnvelopeContext(Tenant, "facebook", MetaCredentialPurposes.PageAccessToken, Guid.Empty);
        var act = () => MetaCredentialEnvelopeCodec.Encode(new FakeAuthenticatedEncryptor(), ctx, "tok");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryDecode_NonAuthenticatedEncryptor_Fails()
    {
        MetaCredentialEnvelopeCodec.TryDecode(new PlainEncryptor(), Context(), "x", out var plaintext).Should().BeFalse();
        plaintext.Should().BeNull();
    }

    [Fact]
    public void TryDecode_EmptyCiphertext_Fails()
    {
        MetaCredentialEnvelopeCodec.TryDecode(new FakeAuthenticatedEncryptor(), Context(), "", out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_TamperedCiphertext_Fails()
    {
        MetaCredentialEnvelopeCodec.TryDecode(new FakeAuthenticatedEncryptor(), Context(), "garbage", out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_WrongVersion_Fails()
    {
        var enc = new FakeAuthenticatedEncryptor();
        // Version 9 không khớp CurrentVersion=1.
        var payload = enc.Encrypt("""{"version":9,"context":{"tenantId":"11111111-1111-1111-1111-111111111111","provider":"facebook","purpose":"page_access_token","rowId":"33333333-3333-3333-3333-333333333333","parentId":null},"plaintext":"tok"}""");

        MetaCredentialEnvelopeCodec.TryDecode(enc, Context(), payload, out _).Should().BeFalse();
    }
}
