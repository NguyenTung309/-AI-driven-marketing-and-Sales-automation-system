using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Docs;
using Clawbot.Domain.Content;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Skills;

// Đọc asset nội dung có giới hạn tenant/item/asset: validate storage key + toàn vẹn sha256 + magic bytes.
public sealed class ContentAssetReaderTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid Item = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    // 1x1 PNG magic + padding tới >=12 byte để qua LooksLikeDeclaredImage.
    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48];

    private static ContentAsset ReadyAsset(byte[] bytes)
    {
        var asset = ContentAsset.Reserve(Tenant, Item, "pic.png", null, 0, Now);
        asset.MarkReady(System.Security.Cryptography.SHA256.HashData(bytes), bytes.LongLength, "image/png", Now);
        return asset;
    }

    private static ContentAssetReader NewReader(
        IContentAssetRepository repo,
        IDocumentStorage storage) => new(repo, storage);

    // ---- ValidateStorageKey (static) ----

    [Fact]
    public void ValidateStorageKey_Canonical_DoesNotThrow()
    {
        var assetId = Guid.NewGuid();
        var key = $"tenants/{Tenant:N}/content/{Item:N}/{assetId:N}";

        var act = () => ContentAssetReader.ValidateStorageKey(Tenant, Item, assetId, key);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/absolute/path")]
    [InlineData("has\\backslash")]
    [InlineData("has?query")]
    [InlineData("has#frag")]
    [InlineData("has%2e")]
    [InlineData("has:colon")]
    [InlineData("double//slash")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    public void ValidateStorageKey_Malformed_Throws(string key)
    {
        var act = () => ContentAssetReader.ValidateStorageKey(Tenant, Item, Guid.NewGuid(), key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("content_asset_storage_key_invalid");
    }

    [Fact]
    public void ValidateStorageKey_WrongTenantPrefix_ThrowsMismatch()
    {
        var assetId = Guid.NewGuid();
        var key = $"tenants/{Guid.NewGuid():N}/content/{Item:N}/{assetId:N}";

        var act = () => ContentAssetReader.ValidateStorageKey(Tenant, Item, assetId, key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("content_asset_storage_key_mismatch");
    }

    // ---- ReadAsync flow ----

    [Fact]
    public async Task ReadAsync_HappyPath_ReturnsBytes()
    {
        var bytes = PngBytes();
        var asset = ReadyAsset(bytes);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(Tenant, Item, asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>()).Returns(bytes);

        var result = await NewReader(repo, storage).ReadAsync(Tenant, Item, asset.Id, CancellationToken.None);

        result.Bytes.Should().Equal(bytes);
        result.Stat.AssetId.Should().Be(asset.Id);
    }

    [Fact]
    public async Task ReadAsync_NotFound_Throws()
    {
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(Tenant, Item, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ContentAsset?)null);
        var storage = Substitute.For<IDocumentStorage>();

        var act = async () => await NewReader(repo, storage).ReadAsync(Tenant, Item, Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("content_asset_not_found");
    }

    [Fact]
    public async Task ReadAsync_IntegrityMismatch_Throws()
    {
        var asset = ReadyAsset(PngBytes());
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(Tenant, Item, asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        // Trả về bytes khác kích thước đã ghi => size mismatch trước cả sha.
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>())
            .Returns(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0xFF });

        var act = async () => await NewReader(repo, storage).ReadAsync(Tenant, Item, asset.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("content_asset_size_mismatch");
    }

    [Fact]
    public async Task ReadAsync_EmptyStorage_Throws()
    {
        var asset = ReadyAsset(PngBytes());
        var repo = Substitute.For<IContentAssetRepository>();
        repo.FindReadyAsync(Tenant, Item, asset.Id, Arg.Any<CancellationToken>()).Returns(asset);
        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>()).Returns(Array.Empty<byte>());

        var act = async () => await NewReader(repo, storage).ReadAsync(Tenant, Item, asset.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("content_asset_empty");
    }

    [Fact]
    public async Task ListReadyAsync_OrdersBySortOrder()
    {
        var a0 = ContentAsset.Reserve(Tenant, Item, "a.png", null, 0, Now);
        a0.MarkReady(System.Security.Cryptography.SHA256.HashData(PngBytes()), 14, "image/png", Now);
        var a1 = ContentAsset.Reserve(Tenant, Item, "b.png", null, 1, Now);
        a1.MarkReady(System.Security.Cryptography.SHA256.HashData(PngBytes()), 14, "image/png", Now);
        var repo = Substitute.For<IContentAssetRepository>();
        repo.ListReadyAsync(Tenant, Item, Arg.Any<CancellationToken>())
            .Returns(new[] { a1, a0 });
        var storage = Substitute.For<IDocumentStorage>();

        var result = await NewReader(repo, storage).ListReadyAsync(Tenant, Item, CancellationToken.None);

        result.Select(s => s.SortOrder).Should().Equal(0, 1);
    }

    [Fact]
    public async Task ListReadyAsync_EmptyTenant_Throws()
    {
        var repo = Substitute.For<IContentAssetRepository>();
        var storage = Substitute.For<IDocumentStorage>();

        var act = async () => await NewReader(repo, storage).ListReadyAsync(Guid.Empty, Item, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
