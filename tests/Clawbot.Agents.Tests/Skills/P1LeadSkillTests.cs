using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Rag;
using Clawbot.SharedKernel.Vectors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// M11 P1 — AkismetSpamDetector (heuristic URL/emoji/scam).
public sealed class AkismetSpamDetectorTests
{
    private readonly AkismetSpamDetector _sut = new();

    [Fact]
    public async Task Clean_text_not_spam()
    {
        var result = await _sut.EvaluateAsync("Hi, I want to register for a trial class", null, null, CancellationToken.None);

        result.IsSpam.Should().BeFalse();
        result.Confidence.Should().BeLessThan(0.5f);
    }

    [Fact]
    public async Task Url_flood_detected()
    {
        var text = "Check this https://a.com https://b.com https://c.com https://d.com";
        var result = await _sut.EvaluateAsync(text, null, null, CancellationToken.None);

        result.Confidence.Should().BeGreaterThan(0.3f);
        result.Reason.Should().Contain("url_flood");
    }

    [Fact]
    public async Task Scam_keywords_detected()
    {
        var text = "Kiếm tiền nhanh! Đầu tư ngay! Nhân đôi thu nhập thụ động!";
        var result = await _sut.EvaluateAsync(text, null, null, CancellationToken.None);

        result.Confidence.Should().BeGreaterThan(0.3f);
        result.Reason.Should().Contain("scam_keyword");
    }

    [Fact]
    public async Task Repeated_chars_detected()
    {
        var result = await _sut.EvaluateAsync("aaaaaaa bbbbbbb", null, null, CancellationToken.None);

        result.Reason.Should().Contain("repeated_chars");
    }

    [Fact]
    public async Task Empty_text_not_spam()
    {
        var result = await _sut.EvaluateAsync("", null, null, CancellationToken.None);

        result.IsSpam.Should().BeFalse();
    }
}

// M11 P1 — NodaTimezoneDetector (heuristic E.164).
public sealed class NodaTimezoneDetectorTests
{
    private readonly NodaTimezoneDetector _sut = new();

    [Fact]
    public void VN_phone_returns_ho_chi_minh()
    {
        var result = _sut.Detect("+84912345678", null, null);

        result.IanaTimezone.Should().Be("Asia/Ho_Chi_Minh");
        result.Confidence.Should().Be(0.85f);
        result.Source.Should().Contain("phone_prefix");
    }

    [Fact]
    public void CN_phone_returns_shanghai()
    {
        var result = _sut.Detect("+8613800138000", null, null);

        result.IanaTimezone.Should().Be("Asia/Shanghai");
        result.Confidence.Should().Be(0.85f);
    }

    [Fact]
    public void Country_code_fallback()
    {
        var result = _sut.Detect(null, null, "KR");

        result.IanaTimezone.Should().Be("Asia/Seoul");
        result.Confidence.Should().Be(0.80f);
    }

    [Fact]
    public void Locale_fallback()
    {
        var result = _sut.Detect(null, "ja", null);

        result.IanaTimezone.Should().Be("Asia/Tokyo");
        result.Confidence.Should().Be(0.65f);
    }

    [Fact]
    public void No_input_defaults_to_vn()
    {
        var result = _sut.Detect(null, null, null);

        result.IanaTimezone.Should().Be("Asia/Ho_Chi_Minh");
        result.Confidence.Should().Be(0.30f);
        result.Source.Should().Be("default");
    }
}

// M11 P1 — QdrantLeadDeduplicator (embed → Qdrant search).
public sealed class QdrantLeadDeduplicatorTests
{
    [Fact]
    public async Task Empty_key_returns_empty()
    {
        var embedding = Substitute.For<IEmbeddingProvider>();
        var store = Substitute.For<IVectorStore>();
        var sut = new QdrantLeadDeduplicator(embedding, store);

        var result = await sut.FindCandidatesAsync(
            Guid.NewGuid(),
            new DedupQuery("", null, null, new Dictionary<string, string>()),
            5, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Filters_by_tenant_and_threshold()
    {
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var embedding = Substitute.For<IEmbeddingProvider>();
        embedding.Dimension.Returns(384);
        embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[384]));

        var store = Substitute.For<IVectorStore>();
        // Collection is versioned by embedder dimension (contacts_v{dim}).
        store.SearchAsync(
                "contacts_v384",
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<VectorMetadataFilter>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<VectorMatch>
            {
                new(contactId.ToString("D"), 0.92f,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tenant_id"] = tenantId.ToString("D"),
                        ["contact_id"] = contactId.ToString("D")
                    }),
                new(Guid.NewGuid().ToString("D"), 0.91f,  // below threshold
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tenant_id"] = tenantId.ToString("D"),
                        ["contact_id"] = Guid.NewGuid().ToString("D")
                    }),
                new(Guid.NewGuid().ToString("D"), 0.90f,  // different tenant
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tenant_id"] = Guid.NewGuid().ToString("D"),
                        ["contact_id"] = Guid.NewGuid().ToString("D")
                    })
            });

        var sut = new QdrantLeadDeduplicator(embedding, store);
        var result = await sut.FindCandidatesAsync(
            tenantId,
            new DedupQuery("John Doe", "1234567", "john@test.com", new Dictionary<string, string>()),
            5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].CandidateContactId.Should().Be(contactId);
        result[0].Similarity.Should().Be(0.92f);
    }
}

// M11 P1 — HunterContactEnricher (config-gated HTTP + heuristic).
public sealed class HunterContactEnricherTests
{
    [Fact]
    public async Task Disabled_returns_heuristic_for_business_email()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var options = Options.Create(new ContactEnricherOptions { Enabled = false });
        var sut = new HunterContactEnricher(httpFactory, options, NullLogger<HunterContactEnricher>.Instance);

        var result = await sut.EnrichByEmailAsync("user@acmecorp.com", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Company.Should().Be("Acmecorp");
    }

    [Fact]
    public async Task Disabled_returns_null_for_gmail()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var options = Options.Create(new ContactEnricherOptions { Enabled = false });
        var sut = new HunterContactEnricher(httpFactory, options, NullLogger<HunterContactEnricher>.Instance);

        var result = await sut.EnrichByEmailAsync("user@gmail.com", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_phone_returns_null()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var options = Options.Create(new ContactEnricherOptions { Enabled = false });
        var sut = new HunterContactEnricher(httpFactory, options, NullLogger<HunterContactEnricher>.Instance);

        var result = await sut.EnrichByPhoneAsync("+84912345678", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Empty_email_returns_null()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var options = Options.Create(new ContactEnricherOptions());
        var sut = new HunterContactEnricher(httpFactory, options, NullLogger<HunterContactEnricher>.Instance);

        var result = await sut.EnrichByEmailAsync("", CancellationToken.None);

        result.Should().BeNull();
    }
}
