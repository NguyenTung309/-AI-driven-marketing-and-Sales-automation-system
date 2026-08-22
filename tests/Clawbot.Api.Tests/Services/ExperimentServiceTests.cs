using Clawbot.Api.Services;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Experiments;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests.Services;

public sealed class ExperimentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 3, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenant(tenantId));
    }

    private static async Task<Experiment> SeedExperimentAsync(
        AppDbContext db,
        Guid tenantId,
        params (string Code, int Weight)[] variants)
    {
        var experiment = Experiment.Create(tenantId, "exp-1", "chat_scenario", Guid.NewGuid(), "Thử nghiệm", Now);
        foreach (var (code, weight) in variants)
            experiment.AddVariant(code, $"Nhánh {code}", weight, null, null, Now);

        db.Experiments.Add(experiment);
        await db.SaveChangesAsync();
        return experiment;
    }

    [Fact]
    public async Task AssignAsync_BlankSubject_Throws()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.AssignAsync(tenantId, Guid.NewGuid(), "  ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("subjectKey");
    }

    [Fact]
    public async Task AssignAsync_UnknownExperiment_Throws()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.AssignAsync(tenantId, Guid.NewGuid(), "user-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("experiment_not_found");
    }

    [Fact]
    public async Task AssignAsync_FirstCall_PersistsAssignmentAndExposureEvent()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 50), ("b", 50));
        var service = new ExperimentService(db, new FixedClock(Now));

        var result = await service.AssignAsync(tenantId, experiment.Id, "user-1");

        result.ExperimentId.Should().Be(experiment.Id);
        result.VariantCode.Should().BeOneOf("a", "b");
        db.ExperimentAssignments.Count().Should().Be(1);
        db.ExperimentEvents.Count(e => e.EventType == "exposure").Should().Be(1);
    }

    [Fact]
    public async Task AssignAsync_IsStableForTheSameSubject()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 50), ("b", 50));
        var service = new ExperimentService(db, new FixedClock(Now));

        var first = await service.AssignAsync(tenantId, experiment.Id, "user-1");
        var second = await service.AssignAsync(tenantId, experiment.Id, "user-1");

        second.VariantId.Should().Be(first.VariantId);
        // Lần 2 phải đọc lại bản ghi cũ, không đẻ thêm assignment hay exposure.
        db.ExperimentAssignments.Count().Should().Be(1);
        db.ExperimentEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task AssignAsync_TrimsSubjectKeyBeforeMatching()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 100));
        var service = new ExperimentService(db, new FixedClock(Now));

        await service.AssignAsync(tenantId, experiment.Id, "user-1");
        await service.AssignAsync(tenantId, experiment.Id, "  user-1  ");

        db.ExperimentAssignments.Count().Should().Be(1);
    }

    [Fact]
    public async Task AssignAsync_SingleVariant_AlwaysPicksIt()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("only", 100));
        var service = new ExperimentService(db, new FixedClock(Now));

        var result = await service.AssignAsync(tenantId, experiment.Id, "user-x");

        result.VariantCode.Should().Be("only");
    }

    [Fact]
    public async Task AssignAsync_DistributesAcrossVariants()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 50), ("b", 50));
        var service = new ExperimentService(db, new FixedClock(Now));

        var codes = new List<string>();
        for (var i = 0; i < 40; i++)
            codes.Add((await service.AssignAsync(tenantId, experiment.Id, $"user-{i}")).VariantCode);

        codes.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task AssignAsync_StoppedExperiment_IsNotFound()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 100));
        experiment.Stop(Now);
        await db.SaveChangesAsync();
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.AssignAsync(tenantId, experiment.Id, "user-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("experiment_not_found");
    }

    [Fact]
    public async Task AssignAsync_OtherTenantExperiment_IsNotFound()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, Guid.NewGuid(), ("a", 100));
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.AssignAsync(tenantId, experiment.Id, "user-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RecordEventAsync_BlankSubjectOrType_Throws()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new ExperimentService(db, new FixedClock(Now));

        var blankSubject = async () => await service.RecordEventAsync(
            tenantId, Guid.NewGuid(), Guid.NewGuid(), " ", "conversion", null);
        var blankType = async () => await service.RecordEventAsync(
            tenantId, Guid.NewGuid(), Guid.NewGuid(), "user-1", " ", null);

        await blankSubject.Should().ThrowAsync<ArgumentException>().WithParameterName("subjectKey");
        await blankType.Should().ThrowAsync<ArgumentException>().WithParameterName("eventType");
    }

    [Fact]
    public async Task RecordEventAsync_UnknownVariant_Throws()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.RecordEventAsync(
            tenantId, Guid.NewGuid(), Guid.NewGuid(), "user-1", "conversion", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("experiment_variant_not_found");
    }

    [Fact]
    public async Task RecordEventAsync_Conversion_IsRecordedOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 100));
        var variantId = experiment.Variants.Single().Id;
        var service = new ExperimentService(db, new FixedClock(Now));

        await service.RecordEventAsync(tenantId, experiment.Id, variantId, "user-1", "conversion", 100m);
        await service.RecordEventAsync(tenantId, experiment.Id, variantId, "user-1", "CONVERSION", 200m);

        db.ExperimentEvents.Count(e => e.EventType == "conversion").Should().Be(1);
    }

    [Fact]
    public async Task RecordEventAsync_CustomEventType_IsNotDeduplicated()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 100));
        var variantId = experiment.Variants.Single().Id;
        var service = new ExperimentService(db, new FixedClock(Now));

        await service.RecordEventAsync(tenantId, experiment.Id, variantId, "user-1", "click", null);
        await service.RecordEventAsync(tenantId, experiment.Id, variantId, "user-1", "click", null);

        db.ExperimentEvents.Count(e => e.EventType == "click").Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_UnknownExperiment_Throws()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new ExperimentService(db, new FixedClock(Now));

        var act = async () => await service.GetSummaryAsync(tenantId, Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("experiment_not_found");
    }

    [Fact]
    public async Task GetSummaryAsync_NoEvents_ReportsZeroRatesAndNoWinner()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 50), ("b", 50));
        var service = new ExperimentService(db, new FixedClock(Now));

        var summary = await service.GetSummaryAsync(tenantId, experiment.Id);

        summary.Variants.Should().HaveCount(2);
        summary.Variants.Should().OnlyContain(v => v.Exposures == 0 && v.ConversionRate == 0m);
        summary.WinnerVariantCode.Should().BeNull();
    }

    [Fact]
    public async Task GetSummaryAsync_CountsDistinctSubjectsAndPicksWinner()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("a", 50), ("b", 50));
        var variantA = experiment.Variants.Single(v => v.Code == "a").Id;
        var variantB = experiment.Variants.Single(v => v.Code == "b").Id;
        var service = new ExperimentService(db, new FixedClock(Now));

        // a: 2 người tiếp xúc, 2 chuyển đổi (100%); b: 2 tiếp xúc, 1 chuyển đổi (50%).
        db.ExperimentEvents.AddRange(
            ExperimentEvent.Create(tenantId, experiment.Id, variantA, "u1", "exposure", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantA, "u1", "exposure", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantA, "u2", "exposure", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantA, "u1", "conversion", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantA, "u2", "conversion", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantB, "u3", "exposure", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantB, "u4", "exposure", null, Now),
            ExperimentEvent.Create(tenantId, experiment.Id, variantB, "u3", "conversion", null, Now));
        await db.SaveChangesAsync();

        var summary = await service.GetSummaryAsync(tenantId, experiment.Id);

        var a = summary.Variants.Single(v => v.Code == "a");
        a.Exposures.Should().Be(2);
        a.Conversions.Should().Be(2);
        a.ConversionRate.Should().Be(1m);
        summary.Variants.Single(v => v.Code == "b").ConversionRate.Should().Be(0.5m);
        summary.WinnerVariantCode.Should().Be("a");
    }

    [Fact]
    public async Task GetSummaryAsync_OrdersVariantsByCode()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var experiment = await SeedExperimentAsync(db, tenantId, ("z", 50), ("a", 50));
        var service = new ExperimentService(db, new FixedClock(Now));

        var summary = await service.GetSummaryAsync(tenantId, experiment.Id);

        summary.Variants.Select(v => v.Code).Should().Equal("a", "z");
    }
}

public sealed class ConversationExportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 3, 0, 0, TimeSpan.Zero);

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenant(tenantId));
    }

    [Fact]
    public async Task ExportCsvAsync_UnknownConversation_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);

        var result = await new ConversationExportService(db).ExportCsvAsync(tenantId, Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExportCsvAsync_OtherTenantConversation_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var conversation = Conversation.Open(Guid.NewGuid(), "facebook", "t-1", Now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var result = await new ConversationExportService(db).ExportCsvAsync(tenantId, conversation.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExportCsvAsync_NoMessages_ReturnsHeaderOnly()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var conversation = Conversation.Open(tenantId, "facebook", "t-1", Now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var result = await new ConversationExportService(db).ExportCsvAsync(tenantId, conversation.Id);

        result.Should().NotBeNull();
        result!.FileName.Should().Be($"conversation-{conversation.Id:N}.csv");
        result.Content.Trim().Should().Be(
            "sent_at,direction,sender_type,content_type,message_type,parent_post_id,external_message_id,content");
    }

    [Fact]
    public async Task ExportCsvAsync_WritesMessagesInChronologicalOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var conversation = Conversation.Open(tenantId, "facebook", "t-1", Now);
        conversation.AppendMessage("in", "contact", "Tin nhắn sau", "text", Now.AddMinutes(5));
        conversation.AppendMessage("out", "agent", "Tin nhắn trước", "text", Now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var content = (await new ConversationExportService(db)
            .ExportCsvAsync(tenantId, conversation.Id))!.Content;

        content.IndexOf("Tin nhắn trước", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("Tin nhắn sau", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportCsvAsync_EscapesCommasAndQuotes()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var conversation = Conversation.Open(tenantId, "facebook", "t-1", Now);
        conversation.AppendMessage("in", "contact", "xin chào, bạn nói \"gì\"?", "text", Now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var content = (await new ConversationExportService(db)
            .ExportCsvAsync(tenantId, conversation.Id))!.Content;

        content.Should().Contain("\"xin chào, bạn nói \"\"gì\"\"?\"");
    }
}

internal sealed class FixedTenant(Guid tenantId) : ITenantAccessor
{
    public TenantContext? Current { get; } = new(tenantId, "test-tenant");

    public TenantContext Require() => Current!;
}
