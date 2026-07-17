using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Kb;
using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.SharedKernel.Vectors;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
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

public sealed class ConfiguredEmbeddingProviderTests
{
    [Fact]
    public async Task ResolveConfigAsync_uses_tenant_resolver_when_available()
    {
        var tenantId = Guid.NewGuid();
        var resolver = Substitute.For<IEmbeddingConfigResolver>();
        resolver.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new ResolvedEmbeddingConfig("openai", "text-embedding-3-small", "key", null, 1536, "tenant-db"));
        var sut = new ConfiguredEmbeddingProvider(
            [resolver],
            Options.Create(new EmbeddingOptions()),
            Options.Create(new Clawbot.Agents.Core.Chat.LlmBaseUrlOptions()),
            new TestHostEnvironment(),
            NullLogger<ConfiguredEmbeddingProvider>.Instance);

        var config = await sut.ResolveConfigAsync(tenantId, CancellationToken.None);

        config.Source.Should().Be("tenant-db");
        config.Dimension.Should().Be(1536);
    }

    [Fact]
    public void CollectionName_includes_provider_model_and_dimension()
    {
        var config = new ResolvedEmbeddingConfig("openai", "text-embedding-3-small", "key", null, 1536, "tenant-db");

        ConfiguredEmbeddingProvider.CollectionName(config).Should().Be("kb_openai_text_embedding_3_small_v1536");
    }

    [Fact]
    public async Task ResolveConfigAsync_falls_back_to_hash_without_config()
    {
        var sut = new ConfiguredEmbeddingProvider(
            [],
            Options.Create(new EmbeddingOptions()),
            Options.Create(new Clawbot.Agents.Core.Chat.LlmBaseUrlOptions()),
            new TestHostEnvironment(),
            NullLogger<ConfiguredEmbeddingProvider>.Instance);

        var config = await sut.ResolveConfigAsync(Guid.NewGuid(), CancellationToken.None);

        config.Provider.Should().Be("hash");
        config.Dimension.Should().Be(384);
        config.IsFallback.Should().BeTrue();
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
        store.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<VectorMetadataFilter>?>(),
                Arg.Any<CancellationToken>())
             .Returns(hits);
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ReadOnlyMemory<float>(new float[8]));
        return new QdrantRagRetriever(store, embedder, [], Microsoft.Extensions.Logging.Abstractions.NullLogger<QdrantRagRetriever>.Instance);
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
    public async Task Pushes_metadata_filters_to_vector_store_before_limit()
    {
        var tenant = Guid.NewGuid();
        var store = Substitute.For<IVectorStore>();
        store.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<int>(),
                Arg.Any<IReadOnlyList<VectorMetadataFilter>?>(),
                Arg.Any<CancellationToken>())
             .Returns([Match("1", tenant.ToString(), "KB-002", "a", 0.9f)]);
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.Dimension.Returns(8);
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ReadOnlyMemory<float>(new float[8]));
        var activeResolver = Substitute.For<IActiveKbVersionResolver>();
        activeResolver.ResolveActiveVersionIdsAsync(tenant, "KB-002", Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["v1"], StringComparer.Ordinal));
        var sut = new QdrantRagRetriever(store, embedder, [activeResolver], Microsoft.Extensions.Logging.Abstractions.NullLogger<QdrantRagRetriever>.Instance);

        await sut.RetrieveAsync(new RagRequest(tenant, "KB-002", "q", TopK: 3), CancellationToken.None);

        await store.Received(1).SearchAsync(
            "kb_runtime_dim_8_v8",
            Arg.Any<ReadOnlyMemory<float>>(),
            3,
            Arg.Is<IReadOnlyList<VectorMetadataFilter>?>(filters =>
                filters != null
                && filters.Any(f => f.Field == "tenant_id" && f.Values.Contains(tenant.ToString()))
                && filters.Any(f => f.Field == "module_code" && f.Values.Contains("KB-002"))
                && filters.Any(f => f.Field == "kb_version_id" && f.Values.Contains("v1"))),
            Arg.Any<CancellationToken>());
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

public sealed class CachedRagRetrieverTests
{
    [Fact]
    public async Task BuildCacheKeyAsync_includes_topK_and_embedding_collection()
    {
        var tenant = Guid.NewGuid();
        var inner = Substitute.For<IRagRetriever>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.Dimension.Returns(8);
        var sut = new CachedRagRetriever(inner, embedder, redis, [], NullLogger<CachedRagRetriever>.Instance);

        var top3 = await sut.BuildCacheKeyAsync(new RagRequest(tenant, null, "q", TopK: 3), CancellationToken.None);
        var top4 = await sut.BuildCacheKeyAsync(new RagRequest(tenant, null, "q", TopK: 4), CancellationToken.None);
        embedder.Dimension.Returns(16);
        var dim16 = await sut.BuildCacheKeyAsync(new RagRequest(tenant, null, "q", TopK: 3), CancellationToken.None);

        top3.Should().Contain(":top3:kb_runtime_dim_8_v8:");
        top4.Should().NotBe(top3);
        dim16.Should().Contain(":top3:kb_runtime_dim_16_v16:");
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Clawbot.Agents.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

public sealed class KbDeployServiceTests
{
    [Fact]
    public async Task EmbedAndUpsertAsync_stores_sql_embedding_json_and_upserts_qdrant_chunks()
    {
        var tenantId = Guid.NewGuid();
        var version = KbVersion.Create(Guid.NewGuid(), 1, "HSK course content", DateTimeOffset.UtcNow);
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.Dimension.Returns(2);
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new[] { 0.25f, 0.75f }));
        var store = Substitute.For<IVectorStore>();
        store.UpsertAsync(Arg.Any<string>(), Arg.Any<IEnumerable<VectorRecord>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sut = new KbDeployService(embedder, store, NullLogger<KbDeployService>.Instance);

        var count = await sut.EmbedAndUpsertAsync(version, "HSK", tenantId, CancellationToken.None);

        count.Should().Be(1);
        version.Embedding.Should().NotBeNullOrWhiteSpace();
        JsonSerializer.Deserialize<float[]>(version.Embedding!).Should().Equal(0.25f, 0.75f);
        await store.Received(1).UpsertAsync(
            "kb_runtime_dim_2_v2",
            Arg.Is<IEnumerable<VectorRecord>>(records => records.Single().Metadata["module_code"] == "HSK"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAndUpsertAsync_gives_each_chunk_a_distinct_id()
    {
        var tenantId = Guid.NewGuid();
        // Three paragraphs each over maxChunkChars/2 force multiple chunks.
        var big = string.Join("\n\n", Enumerable.Range(0, 3).Select(i => new string((char)('a' + i), 900)));
        var version = KbVersion.Create(Guid.NewGuid(), 1, big, DateTimeOffset.UtcNow);
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.Dimension.Returns(2);
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new[] { 0.1f, 0.2f }));
        var store = Substitute.For<IVectorStore>();
        List<VectorRecord>? captured = null;
        store.UpsertAsync(Arg.Any<string>(), Arg.Do<IEnumerable<VectorRecord>>(r => captured = r.ToList()), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sut = new KbDeployService(embedder, store, NullLogger<KbDeployService>.Instance);

        var count = await sut.EmbedAndUpsertAsync(version, "HSK", tenantId, CancellationToken.None);

        count.Should().BeGreaterThan(1);
        captured.Should().NotBeNull();
        // Regression: every chunk used to share version.Id → Qdrant overwrote all but the last.
        captured!.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ChunkContent_splits_a_single_oversized_paragraph_under_the_char_cap()
    {
        // Hồi quy: một đoạn liền mạch không xuống dòng kép, dài hơn maxChunkChars, trước đây được giữ
        // nguyên → chunk vượt hạn mức token model embedding → deploy fail (đoạn 15955 ký tự > 8192 token).
        var oneLongParagraph = new string('a', 3500);

        var chunks = KbDeployService.ChunkContent(oneLongParagraph, maxChunkChars: 1000);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Length <= 1000);
        // Không mất ký tự: nối lại phải đủ độ dài gốc (bỏ qua khoảng trắng cắt ở ranh giới — ở đây không có).
        string.Concat(chunks).Length.Should().Be(oneLongParagraph.Length);
    }
}
