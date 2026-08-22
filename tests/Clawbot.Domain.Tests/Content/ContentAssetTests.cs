using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentAssetTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ContentItemId = Guid.NewGuid();
    private static readonly byte[] ValidSha256 = new byte[32];

    // ── Reserve ───────────────────────────────────────────────────────

    [Fact]
    public void Reserve_SetsInitialDefaults()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "photo.png", ".png", 0, Now);

        asset.TenantId.Should().Be(TenantId);
        asset.ContentItemId.Should().Be(ContentItemId);
        asset.Status.Should().Be(ContentAsset.StatusUploading);
        asset.SortOrder.Should().Be(0);
        asset.CreatedAt.Should().Be(Now);
        asset.SizeBytes.Should().BeNull();
        asset.ContentType.Should().BeNull();
        asset.ReadyAt.Should().BeNull();
        asset.DeletedAt.Should().BeNull();
        asset.LastErrorCode.Should().BeNull();
        asset.Sha256.Should().BeNull();
    }

    [Fact]
    public void Reserve_BuildsStorageKeyWithNormalizedExtension()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "file.PNG", ".PNG", 0, Now);

        asset.StorageKey.Should().Contain(".png");
        asset.StorageKey.Should().StartWith($"tenants/{TenantId:N}/content/{ContentItemId:N}/");
    }

    [Fact]
    public void Reserve_NormalizesFileName()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, @"path\to\image.jpg", ".jpg", 0, Now);

        asset.OriginalFileName.Should().Be("image.jpg");
    }

    [Fact]
    public void Reserve_NullFileNameIsAllowed()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, null, null, 0, Now);

        asset.OriginalFileName.Should().BeNull();
        asset.StorageKey.Should().NotEndWith(".");
    }

    [Fact]
    public void Reserve_ThrowsOnFileNameWithControlChars()
    {
        var act = () => ContentAsset.Reserve(TenantId, ContentItemId, "file\x01name.png", ".png", 0, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reserve_ThrowsOnEmptyTenantId()
    {
        var act = () => ContentAsset.Reserve(Guid.Empty, ContentItemId, "f.png", ".png", 0, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reserve_ThrowsOnNegativeSortOrder()
    {
        var act = () => ContentAsset.Reserve(TenantId, ContentItemId, "f.png", ".png", -1, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── MarkReady ─────────────────────────────────────────────────────

    [Fact]
    public void MarkReady_TransitionsToReady()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        asset.MarkReady(ValidSha256, 1024, "image/png", Now.AddMinutes(1));

        asset.Status.Should().Be(ContentAsset.StatusReady);
        asset.SizeBytes.Should().Be(1024);
        asset.ContentType.Should().Be("image/png");
        asset.ReadyAt.Should().Be(Now.AddMinutes(1));
        asset.Sha256.Should().Equal(ValidSha256);
        asset.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void MarkReady_ReturnsDefensiveCopyOfSha256()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);

        var first = asset.Sha256!;
        var second = asset.Sha256!;

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void MarkReady_ThrowsWhenNotUploading()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);

        var act = () => asset.MarkReady(ValidSha256, 100, "image/png", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkReady_ThrowsOnNullSha256()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        var act = () => asset.MarkReady(null!, 100, "image/png", Now);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MarkReady_ThrowsOnWrongSha256Length()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        var act = () => asset.MarkReady(new byte[16], 100, "image/png", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkReady_ThrowsOnZeroSize()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        var act = () => asset.MarkReady(ValidSha256, 0, "image/png", Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkReady_ThrowsOnEmptyContentType()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        var act = () => asset.MarkReady(ValidSha256, 100, "", Now);

        act.Should().Throw<ArgumentException>();
    }

    // ── MarkFailed ────────────────────────────────────────────────────

    [Fact]
    public void MarkFailed_TransitionsToFailed()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);

        asset.MarkFailed("upload_timeout", Now);

        asset.Status.Should().Be(ContentAsset.StatusFailed);
        asset.LastErrorCode.Should().Be("upload_timeout");
    }

    [Fact]
    public void MarkFailed_ThrowsWhenNotUploading()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkFailed("err", Now);

        var act = () => asset.MarkFailed("err2", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── MarkDeletePending ─────────────────────────────────────────────

    [Fact]
    public void MarkDeletePending_TransitionsFromReady()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);

        asset.MarkDeletePending("user_requested", Now);

        asset.Status.Should().Be(ContentAsset.StatusDeletePending);
        asset.LastErrorCode.Should().Be("user_requested");
    }

    [Fact]
    public void MarkDeletePending_ClearsErrorCodeWhenBlank()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);

        asset.MarkDeletePending(null, Now);

        asset.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void MarkDeletePending_ThrowsWhenAlreadyDeletePending()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);
        asset.MarkDeletePending(null, Now);

        var act = () => asset.MarkDeletePending(null, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── MarkDeleted ───────────────────────────────────────────────────

    [Fact]
    public void MarkDeleted_TransitionsFromDeletePending()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);
        asset.MarkDeletePending(null, Now);

        asset.MarkDeleted(Now.AddMinutes(5));

        asset.Status.Should().Be(ContentAsset.StatusDeleted);
        asset.DeletedAt.Should().Be(Now.AddMinutes(5));
        asset.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void MarkDeleted_ThrowsWhenNotDeletePending()
    {
        var asset = ContentAsset.Reserve(TenantId, ContentItemId, "a.png", ".png", 0, Now);
        asset.MarkReady(ValidSha256, 100, "image/png", Now);

        var act = () => asset.MarkDeleted(Now);

        act.Should().Throw<InvalidOperationException>();
    }
}
