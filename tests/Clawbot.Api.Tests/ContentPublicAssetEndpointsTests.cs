using System.Reflection;
using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Clawbot.Api.Tests;

public sealed class ContentPublicAssetEndpointsTests
{
    [Fact]
    public async Task GetPublicAssetAsync_ReadyAsset_ReturnsFile()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, new FixedTenantAccessor(tenantId));

        var asset = ContentAsset.Reserve(
            tenantId,
            itemId,
            "test.jpg",
            ".jpg",
            0,
            DateTimeOffset.UtcNow);
        var dummyBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        asset.MarkReady(new byte[32], dummyBytes.Length, "image/jpeg", DateTimeOffset.UtcNow);
        db.ContentAssets.Add(asset);
        await db.SaveChangesAsync();

        var storage = Substitute.For<IDocumentStorage>();
        storage.ReadAsync(asset.StorageKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dummyBytes));

        var handler = typeof(ContentEndpoints).GetMethod(
            "GetPublicAssetAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetPublicAssetAsync handler was not found.");

        var http = new DefaultHttpContext();

        // Act
        var resultTask = (Task<IResult>)handler.Invoke(
            null,
            [asset.Id, db, storage, http, CancellationToken.None])!;
        var result = await resultTask;

        // Assert
        result.Should().NotBeNull();
        http.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=86400");
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
