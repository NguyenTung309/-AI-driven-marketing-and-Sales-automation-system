using System.Security.Cryptography;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.SharedKernel.Security;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Content.Publishing;

// Envelope tín chỉ social gắn ngữ cảnh tenant/provider/page; ciphertext của tenant/page khác không replay được.
public sealed class SocialCredentialEnvelopeCodecTests
{
    // Encryptor giả: "mã hoá" = bọc tiền tố để phân biệt, DecryptAuthenticated = gỡ tiền tố.
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

    // Encryptor không xác thực → codec phải từ chối.
    private sealed class PlainEncryptor : IEncryptor
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }

    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static GraphChannelOptions SampleOptions() => new()
    {
        Enabled = true,
        Endpoint = "  https://graph.facebook.com  ",
        PageAccessToken = " token ",
        PageId = " 12345 ",
    };

    [Fact]
    public void EncodeThenDecode_SameContext_RoundTripsAndTrims()
    {
        var enc = new FakeAuthenticatedEncryptor();

        var ciphertext = SocialCredentialEnvelopeCodec.Encode(enc, Tenant, "Facebook", "PAGE1", SampleOptions());
        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", "PAGE1", ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Resolved);
        result.Options!.Endpoint.Should().Be("https://graph.facebook.com");
        result.Options.PageAccessToken.Should().Be("token");
        result.Options.PageId.Should().Be("12345");
        result.Options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Encode_NonAuthenticatedEncryptor_Throws()
    {
        var act = () => SocialCredentialEnvelopeCodec.Encode(
            new PlainEncryptor(), Tenant, "facebook", null, SampleOptions());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Encode_EmptyTenant_Throws()
    {
        var act = () => SocialCredentialEnvelopeCodec.Encode(
            new FakeAuthenticatedEncryptor(), Guid.Empty, "facebook", null, SampleOptions());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encode_BlankProvider_Throws()
    {
        var act = () => SocialCredentialEnvelopeCodec.Encode(
            new FakeAuthenticatedEncryptor(), Tenant, "   ", null, SampleOptions());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WrongTenant_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = SocialCredentialEnvelopeCodec.Encode(enc, Tenant, "facebook", "PAGE1", SampleOptions());

        var result = SocialCredentialEnvelopeCodec.Decode(enc, OtherTenant, "facebook", "PAGE1", ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
        result.Options.Should().BeNull();
    }

    [Fact]
    public void Decode_WrongProvider_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = SocialCredentialEnvelopeCodec.Encode(enc, Tenant, "facebook", "PAGE1", SampleOptions());

        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "zalo", "PAGE1", ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Decode_WrongPageId_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = SocialCredentialEnvelopeCodec.Encode(enc, Tenant, "facebook", "PAGE1", SampleOptions());

        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", "PAGE2", ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Decode_NullVsBlankPageId_TreatedEqual()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var ciphertext = SocialCredentialEnvelopeCodec.Encode(enc, Tenant, "facebook", null, SampleOptions());

        // Encode với null, decode với chuỗi trắng → cùng chuẩn hoá về null.
        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", "   ", ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Resolved);
    }

    [Fact]
    public void Decode_TamperedCiphertext_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();

        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", null, "not-a-valid-envelope");

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Decode_LegacyUnversionedPayload_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();
        // Payload hợp lệ JSON nhưng thiếu "version" → legacy, phải từ chối.
        var legacy = enc.Encrypt("""{"tenantId":"11111111-1111-1111-1111-111111111111","provider":"facebook"}""");

        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", null, legacy);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Decode_NonObjectPayload_ReturnsInvalid()
    {
        var enc = new FakeAuthenticatedEncryptor();
        var arrayPayload = enc.Encrypt("[1,2,3]");

        var result = SocialCredentialEnvelopeCodec.Decode(enc, Tenant, "facebook", null, arrayPayload);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Decode_NonAuthenticatedEncryptor_ReturnsInvalid()
    {
        var result = SocialCredentialEnvelopeCodec.Decode(
            new PlainEncryptor(), Tenant, "facebook", null, "anything");

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Theory]
    [InlineData("")]
    public void Decode_EmptyCiphertext_ReturnsInvalid(string ciphertext)
    {
        var result = SocialCredentialEnvelopeCodec.Decode(
            new FakeAuthenticatedEncryptor(), Tenant, "facebook", null, ciphertext);

        result.Status.Should().Be(SocialCredentialEnvelopeStatus.Invalid);
    }

    [Fact]
    public void Normalize_TrimsAllFields()
    {
        var normalized = SocialCredentialEnvelopeCodec.Normalize(new GraphChannelOptions
        {
            Endpoint = " e ",
            PageAccessToken = " p ",
            PageId = " id ",
            OaAccessToken = " oat ",
            OaId = " oaid ",
        });

        normalized.Endpoint.Should().Be("e");
        normalized.PageAccessToken.Should().Be("p");
        normalized.PageId.Should().Be("id");
        normalized.OaAccessToken.Should().Be("oat");
        normalized.OaId.Should().Be("oaid");
    }
}
