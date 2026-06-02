using System.Globalization;
using Clawbot.Agents.Core.Rag;
using Clawbot.SharedKernel.Vectors;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Rag;

// M09 — HashEmbeddingProvider deterministic 384-dim stub.
public sealed class HashEmbeddingProviderTests
{
    private readonly HashEmbeddingProvider _sut = new();

    [Fact]
    public void Dimension_is_384()
    {
        _sut.Dimension.Should().Be(384);
    }

    [Fact]
    public async Task Embeds_to_384_dimensions()
    {
        var vector = await _sut.EmbedAsync("hello", CancellationToken.None);

        vector.Length.Should().Be(384);
    }

    [Fact]
    public async Task Deterministic_for_same_input()
    {
        var a = await _sut.EmbedAsync("học tiếng Trung", CancellationToken.None);
        var b = await _sut.EmbedAsync("học tiếng Trung", CancellationToken.None);

        a.ToArray().Should().Equal(b.ToArray());
    }

    [Fact]
    public async Task Different_inputs_differ()
    {
        var a = await _sut.EmbedAsync("alpha", CancellationToken.None);
        var b = await _sut.EmbedAsync("beta", CancellationToken.None);

        a.ToArray().Should().NotEqual(b.ToArray());
    }

    [Fact]
    public async Task Normalized_to_unit_length()
    {
        var vector = (await _sut.EmbedAsync("anything", CancellationToken.None)).ToArray();

        var magnitude = Math.Sqrt(vector.Sum(x => (double)x * x));
        magnitude.Should().BeApproximately(1.0, 1e-5);
    }

    [Fact]
    public async Task Empty_input_returns_zero_vector()
    {
        var vector = (await _sut.EmbedAsync("", CancellationToken.None)).ToArray();

        vector.Length.Should().Be(384);
        vector.Should().OnlyContain(x => x == 0f);
    }
}

// M09 — QdrantRagRetriever client-side tenant + module filtering.
public sealed class QdrantRagRetrieverTests
{
    private static VectorMatch Match(string id, string tenant, string? module, string? snippet, float score)
    {
        var meta = new Dictionary<string, string> { ["tenant_id"] = tenant };
        if (module is not null)
        {
            meta["module_code"] = module;
        }

        if (snippet is not null)
        {
            meta["snippet"] = snippet;
        }

        return new VectorMatch(id, score, meta);
    }

    private static QdrantRagRetriever Build(params VectorMatch[] hits)
    {
        var store = Substitute.For<IVectorStore>();
        store.SearchAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(hits);
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ReadOnlyMemory<float>(new float[8]));
        return new QdrantRagRetriever(store, embedder);
    }

    [Fact]
    public async Task Empty_query_returns_empty()
    {
        var sut = Build();

        var chunks = await sut.RetrieveAsync(new RagRequest(Guid.NewGuid(), null, "  "), CancellationToken.None);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task Filters_by_tenant()
    {
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var sut = Build(
            Match("1", tenant.ToString(), "KB-001", "keep", 0.9f),
            Match("2", other.ToString(), "KB-001", "drop", 0.8f));

        var chunks = await sut.RetrieveAsync(new RagRequest(tenant, null, "q"), CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].KbVersionId.Should().Be("1");
        chunks[0].Snippet.Should().Be("keep");
        chunks[0].Score.Should().BeApproximately(0.9f, 0.0001f);
    }

    [Fact]
    public async Task Filters_by_module_code_when_specified()
    {
        var tenant = Guid.NewGuid();
        var sut = Build(
            Match("1", tenant.ToString(), "KB-001", "a", 0.9f),
            Match("2", tenant.ToString(), "KB-002", "b", 0.8f));

        var chunks = await sut.RetrieveAsync(new RagRequest(tenant, "KB-002", "q"), CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].KbModuleCode.Should().Be("KB-002");
    }

    [Fact]
    public async Task Respects_topK()
    {
        var tenant = Guid.NewGuid();
        var hits = Enumerable.Range(0, 10)
            .Select(i => Match(i.ToString(CultureInfo.InvariantCulture), tenant.ToString(), "KB-001", "s", 0.5f))
            .ToArray();
        var sut = Build(hits);

        var chunks = await sut.RetrieveAsync(new RagRequest(tenant, null, "q", TopK: 3), CancellationToken.None);

        chunks.Should().HaveCount(3);
    }

    [Fact]
    public async Task Missing_metadata_defaults_to_empty()
    {
        var tenant = Guid.NewGuid();
        var sut = Build(Match("1", tenant.ToString(), module: null, snippet: null, 0.7f));

        var chunks = await sut.RetrieveAsync(new RagRequest(tenant, null, "q"), CancellationToken.None);

        chunks.Should().ContainSingle();
        chunks[0].KbModuleCode.Should().BeEmpty();
        chunks[0].Snippet.Should().BeEmpty();
    }
}
