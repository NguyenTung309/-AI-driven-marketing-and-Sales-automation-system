using Clawbot.Infrastructure.Content;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class ContentWorkflowRuntimeGateTests
{
    [Fact]
    public async Task GetAsync_is_publication_permissive_when_gate_table_is_missing_on_sqlite()
    {
        using var fx = new TestAppDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gate = new ContentWorkflowRuntimeGate(fx.Db, cache, NullLogger<ContentWorkflowRuntimeGate>.Instance);

        var snapshot = await gate.GetAsync();

        // SQLite TestAppDb has no content_workflow_runtime_gate table → expand/bridge permissive path.
        snapshot.PublicationPaused.Should().BeFalse();
        snapshot.MinimumWriterVersion.Should().Be(0);
        snapshot.Notes.Should().NotBeNull();
        snapshot.Notes!.Should().Contain("permissive");
        (await gate.IsPublicationPausedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_caches_snapshot_for_subsequent_calls()
    {
        using var fx = new TestAppDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gate = new ContentWorkflowRuntimeGate(fx.Db, cache, NullLogger<ContentWorkflowRuntimeGate>.Instance);

        var first = await gate.GetAsync();
        var second = await gate.GetAsync();

        second.Should().BeSameAs(first);
        cache.TryGetValue(ContentWorkflowRuntimeGate.CacheKey, out ContentWorkflowRuntimeGateSnapshot? cached)
            .Should().BeTrue();
        cached.Should().BeSameAs(first);
    }
}
