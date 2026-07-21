using System.Security.Cryptography;
using System.Text;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Docs;
using Clawbot.Domain.Content;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Content;

public sealed class ContentAssetReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidateStorageKey_accepts_canonical_server_key_only()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var ok = $"tenants/{tenantId:N}/content/{itemId:N}/{assetId:N}";

        var act = () => ContentAssetReader.ValidateStorageKey(tenantId, itemId, assetId, ok);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("tenants/x/../y")]
    [InlineData("C:/windows/secret")]
    [InlineData("/absolute")]
    [InlineData("tenants\\bad\\path")]
    [InlineData("tenants/a/content/b/c?x=1")]
    [InlineData("tenants/a/content/b/c#frag")]
    [InlineData("tenants/a/content/b/%2e%2e/c")]
    [InlineData("tenants/a//content/b/c")]
    public void ValidateStorageKey_rejects_path_attacks(string key)
    {
        var act = () => ContentAssetReader.ValidateStorageKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), key);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("content_asset_storage_key_invalid*");
    }

    [Fact]
    public void ValidateStorageKey_rejects_tenant_item_asset_mismatch()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var wrong = $"tenants/{Guid.NewGuid():N}/content/{itemId:N}/{assetId:N}";

        var act = () => ContentAssetReader.ValidateStorageKey(tenantId, itemId, assetId, wrong);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("content_asset_storage_key_mismatch");
    }

    [Fact]
    public async Task ReadAsync_returns_bytes_when_ready_and_integrity_matches()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var png = PngBytes();
        var asset = ReadyAsset(tenantId, itemId, png, "image/png", sortOrder: 0);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(tenantId, itemId, asset.Id, Arg.Any<CancellationToken>())
            .Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>()).Returns(png);
        var sut = new ContentAssetReader(repo, storage);

        var result = await sut.ReadAsync(tenantId, itemId, asset.Id, CancellationToken.None);

        result.Bytes.Should().Equal(png);
        result.Stat.AssetId.Should().Be(asset.Id);
        result.Stat.ContentType.Should().Be("image/png");
        result.Stat.SizeBytes.Should().Be(png.Length);
    }

    [Fact]
    public async Task ReadAsync_rejects_sha256_mismatch()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var png = PngBytes();
        var asset = ReadyAsset(tenantId, itemId, png, "image/png", sortOrder: 0);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(tenantId, itemId, asset.Id, Arg.Any<CancellationToken>())
            .Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>()).Returns(PngBytes(seed: 9));
        var sut = new ContentAssetReader(repo, storage);

        var act = () => sut.ReadAsync(tenantId, itemId, asset.Id, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("content_asset_integrity_mismatch");
    }

    [Fact]
    public async Task ReadAsync_rejects_not_ready_or_missing()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(tenantId, itemId, assetId, Arg.Any<CancellationToken>())
            .Returns((ContentAsset?)null);
        var sut = new ContentAssetReader(repo, Substitute.For<IDocumentStorage>());

        var act = () => sut.ReadAsync(tenantId, itemId, assetId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("content_asset_not_found");
    }

    [Fact]
    public async Task ListReadyAsync_orders_by_sort_and_enforces_cap()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var a = ReadyAsset(tenantId, itemId, PngBytes(), "image/png", sortOrder: 1);
        var b = ReadyAsset(tenantId, itemId, PngBytes(seed: 2), "image/png", sortOrder: 0);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.ListReadyAsync(tenantId, itemId, Arg.Any<CancellationToken>())
            .Returns([a, b]);
        var sut = new ContentAssetReader(repo, Substitute.For<IDocumentStorage>());

        var list = await sut.ListReadyAsync(tenantId, itemId, CancellationToken.None);
        list.Select(x => x.AssetId).Should().Equal(b.Id, a.Id);

        repo.ListReadyAsync(tenantId, itemId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 11).Select(i => ReadyAsset(tenantId, itemId, PngBytes(i), "image/png", i)).ToArray());
        var act = () => sut.ListReadyAsync(tenantId, itemId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("content_asset_count_exceeded");
    }

    [Fact]
    public async Task ReadAsync_rejects_oversized_payload()
    {
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var png = PngBytes();
        var asset = ReadyAsset(tenantId, itemId, png, "image/png", sortOrder: 0);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(tenantId, itemId, asset.Id, Arg.Any<CancellationToken>())
            .Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>()).Returns(png);
        var sut = new ContentAssetReader(
            repo,
            storage,
            new ContentAssetReaderOptions { MaxBytesPerAsset = 4 });

        var act = () => sut.ReadAsync(tenantId, itemId, asset.Id, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("content_asset_too_large");
    }

    private static ContentAsset ReadyAsset(
        Guid tenantId,
        Guid itemId,
        byte[] bytes,
        string contentType,
        int sortOrder)
    {
        var asset = ContentAsset.Reserve(tenantId, itemId, "img.png", sortOrder, Now);
        asset.MarkReady(SHA256.HashData(bytes), bytes.LongLength, contentType, Now.AddSeconds(1));
        return asset;
    }

    private static byte[] PngBytes(int seed = 1)
    {
        // Minimal PNG signature + seed padding so hashes differ.
        var bytes = new byte[32];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        bytes[8] = (byte)seed;
        return bytes;
    }
}
