using Clawbot.Domain.Content;
using Clawbot.Domain.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class ContentWorkflowPersistenceTests
{
    [Fact]
    public void Model_maps_durable_content_workflow_entities_and_indexes()
    {
        using var fx = new TestAppDb();
        var model = fx.Db.Model;

        var schedule = model.FindEntityType(typeof(ContentSchedule))!;
        schedule.FindProperty(nameof(ContentSchedule.ActiveRevisionSlot))!
            .GetColumnName().Should().Be("active_revision_slot");
        var activeScheduleIndex = FindIndex(
            schedule,
            nameof(ContentSchedule.TenantId),
            nameof(ContentSchedule.ContentItemId),
            nameof(ContentSchedule.ActiveRevisionSlot));
        activeScheduleIndex.IsUnique.Should().BeTrue();
        activeScheduleIndex.GetFilter().Should().Be("[active_revision_slot] IS NOT NULL");
        schedule.GetForeignKeys().Should().ContainSingle(foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(ContentSchedule.TenantId),
                nameof(ContentSchedule.ContentItemId),
            })
            && foreignKey.PrincipalEntityType.ClrType == typeof(ContentItem));

        var reviewTask = model.FindEntityType(typeof(ContentReviewTask))!;
        reviewTask.Should().NotBeNull();
        reviewTask.GetTableName().Should().Be("content_review_tasks");
        reviewTask.FindProperty(nameof(ContentReviewTask.Status))!.GetMaxLength().Should().Be(24);
        reviewTask.FindProperty(nameof(ContentReviewTask.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        FindIndex(reviewTask, nameof(ContentReviewTask.TenantId), nameof(ContentReviewTask.ContentItemId), nameof(ContentReviewTask.ContentRevision))
            .IsUnique.Should().BeTrue();
        reviewTask.GetQueryFilter().Should().NotBeNull();

        var asset = model.FindEntityType(typeof(ContentAsset))!;
        asset.Should().NotBeNull();
        asset.GetTableName().Should().Be("content_assets");
        asset.FindProperty(nameof(ContentAsset.StorageKey))!.GetMaxLength().Should().Be(256);
        asset.FindProperty(nameof(ContentAsset.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        FindIndex(asset, nameof(ContentAsset.StorageKey)).IsUnique.Should().BeTrue();
        FindIndex(
            asset,
            nameof(ContentAsset.TenantId),
            nameof(ContentAsset.ContentItemId),
            nameof(ContentAsset.Status),
            nameof(ContentAsset.SortOrder));
        var readyOrderIndex = FindIndex(
            asset,
            nameof(ContentAsset.TenantId),
            nameof(ContentAsset.ContentItemId),
            nameof(ContentAsset.SortOrder));
        readyOrderIndex.IsUnique.Should().BeTrue();
        readyOrderIndex.GetFilter().Should().Be("[status] = 'ready'");
        asset.GetQueryFilter().Should().NotBeNull();

        var attempt = model.FindEntityType(typeof(ContentPublishAttempt))!;
        attempt.Should().NotBeNull();
        attempt.GetTableName().Should().Be("content_publish_attempts");
        attempt.FindProperty(nameof(ContentPublishAttempt.IdempotencyKey))!.GetMaxLength().Should().Be(160);
        attempt.FindProperty(nameof(ContentPublishAttempt.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        FindIndex(attempt, nameof(ContentPublishAttempt.AttemptToken)).IsUnique.Should().BeTrue();
        FindIndex(attempt, nameof(ContentPublishAttempt.TenantId), nameof(ContentPublishAttempt.IdempotencyKey))
            .IsUnique.Should().BeTrue();
        var activeClaimIndex = FindIndex(
            attempt,
            nameof(ContentPublishAttempt.TenantId),
            nameof(ContentPublishAttempt.ScheduleId),
            nameof(ContentPublishAttempt.ContentItemId),
            nameof(ContentPublishAttempt.ContentRevision),
            nameof(ContentPublishAttempt.PublishTargetId));
        activeClaimIndex.IsUnique.Should().BeTrue();
        activeClaimIndex.GetFilter().Should().Be("[status] IN ('claimed', 'transmitted')");
        attempt.GetQueryFilter().Should().NotBeNull();

        var metrics = model.FindEntityType(typeof(ContentWorkflowMetricsHourly))!;
        metrics.Should().NotBeNull();
        metrics.GetTableName().Should().Be("content_workflow_metrics_hourly");
        FindIndex(metrics, nameof(ContentWorkflowMetricsHourly.TenantId), nameof(ContentWorkflowMetricsHourly.HourUtc))
            .IsUnique.Should().BeTrue();
        var cost = metrics.FindProperty(nameof(ContentWorkflowMetricsHourly.LlmCostUsd))!;
        cost.GetPrecision().Should().Be(18);
        cost.GetScale().Should().Be(6);
        metrics.GetQueryFilter().Should().NotBeNull();
    }

    [Fact]
    public void Model_maps_audit_business_event_identity()
    {
        using var fx = new TestAppDb();
        var audit = fx.Db.Model.FindEntityType(typeof(AuditLog))!;

        audit.FindProperty(nameof(AuditLog.EventKey))!.GetMaxLength().Should().Be(256);
        var eventIndex = FindIndex(audit, nameof(AuditLog.TenantId), nameof(AuditLog.EventKey));
        eventIndex.IsUnique.Should().BeTrue();
        eventIndex.GetFilter().Should().Be("[event_key] IS NOT NULL");
        FindIndex(
            audit,
            nameof(AuditLog.TenantId),
            nameof(AuditLog.ResourceId),
            nameof(AuditLog.StateSequence));
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_schedule_item_reference()
    {
        using var fx = new TestAppDb();
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            fx.TenantId,
            "facebook",
            "body",
            createdBy: null,
            now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var schedule = ContentSchedule.Schedule(
            Guid.NewGuid(),
            item.Id,
            item.ContentRevision,
            item.Platform,
            now.AddHours(1),
            now);
        fx.Db.ContentSchedules.Add(schedule);

        var act = () => fx.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_rejects_second_active_schedule_for_same_revision()
    {
        using var fx = new TestAppDb();
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            fx.TenantId,
            "facebook",
            "body",
            createdBy: null,
            now);
        var first = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            item.Platform,
            now.AddHours(1),
            now);
        var second = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            item.Platform,
            now.AddHours(2),
            now);
        fx.Db.AddRange(item, first, second);

        var act = () => fx.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_rejects_duplicate_ready_asset_sort_order()
    {
        using var fx = new TestAppDb();
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(
            fx.TenantId,
            "facebook",
            "body",
            createdBy: null,
            now);
        var first = ContentAsset.Reserve(
            fx.TenantId,
            item.Id,
            "first.png",
            sortOrder: 0,
            createdAt: now);
        var second = ContentAsset.Reserve(
            fx.TenantId,
            item.Id,
            "second.png",
            sortOrder: 0,
            createdAt: now);
        first.MarkReady(new byte[32], 128, "image/png", now);
        second.MarkReady(new byte[32], 128, "image/png", now);
        fx.Db.AddRange(item, first, second);

        var act = () => fx.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_review_task_item_reference()
    {
        using var fx = new TestAppDb();
        var item = ContentItem.Create(
            fx.TenantId,
            "facebook",
            "body",
            createdBy: null,
            DateTimeOffset.UtcNow);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var task = ContentReviewTask.CreatePending(
            Guid.NewGuid(),
            item.Id,
            item.ContentRevision,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        fx.Db.ContentReviewTasks.Add(task);

        var act = () => fx.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task Database_persists_same_tenant_asset_and_publish_attempt_snapshots()
    {
        using var fx = new TestAppDb();
        var now = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, now);
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            item.Platform,
            now.AddHours(1),
            now);
        var publishTargetId = Guid.NewGuid();
        schedule.SetApprovalContext(
            ContentItem.ApprovalModeAutomatic,
            publishingPolicyVersionApplied: 1,
            publishTargetId);
        var asset = ContentAsset.Reserve(
            fx.TenantId,
            item.Id,
            "banner.png",
            sortOrder: 0,
            createdAt: now);
        asset.MarkReady(new byte[32], 128, "image/png", now.AddMinutes(1));
        var attempt = ContentPublishAttempt.Claim(
            fx.TenantId,
            schedule.Id,
            item.Id,
            item.ContentRevision,
            item.Platform,
            publishTargetId,
            bodySnapshot: item.Body,
            assetSnapshots: Array.Empty<ContentPublishAssetSnapshot>(),
            leaseExpiresAt: now.AddMinutes(5),
            claimedAt: now);
        fx.Db.AddRange(item, schedule, asset, attempt);

        await fx.Db.SaveChangesAsync();

        (await fx.Db.ContentAssets.IgnoreQueryFilters().SingleAsync()).Sha256.Should().Equal(new byte[32]);
        (await fx.Db.ContentPublishAttempts.IgnoreQueryFilters().SingleAsync())
            .SnapshotSha256.Should().HaveCount(32);
    }

    private static IReadOnlyIndex FindIndex(IReadOnlyEntityType entity, params string[] properties) =>
        entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(properties));
}
