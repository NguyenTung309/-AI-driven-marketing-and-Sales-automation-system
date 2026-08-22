using System.Text.Json;
using Clawbot.Agents.Contracts.Lead;
using Clawbot.Api.Jobs;
using Clawbot.SharedKernel.Jobs;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần (không qua ApiTestFactory/HTTP host) cho LeadCreateWithSkillsJobHandler
// (leads.create-with-skills). Job này KHÔNG dùng JobResultJson.Web nên Summary được serialize bằng
// JsonSerializer mặc định (PascalCase, không camelCase) — assertion đọc đúng field PascalCase.
public sealed class LeadCreateWithSkillsJobHandlerTests
{
    [Fact]
    public async Task LeadCreateWithSkillsJobHandler_ValidPayload_ReturnsLeadsLinkAndSummaryWithLeadId()
    {
        var leadClient = Substitute.For<LeadAgent.LeadAgentClient>();
        var leadId = Guid.NewGuid();
        var response = new LeadCreateWithSkillsResponse
        {
            LeadId = leadId.ToString(),
            SpamFlagged = false,
            SpamReason = string.Empty,
            Timezone = "Asia/Ho_Chi_Minh",
            EnrichmentCompany = string.Empty,
            PossibleDup = false,
        };
        leadClient
            .CreateWithSkillsAsync(Arg.Any<LeadCreateWithSkillsRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new LeadCreateWithSkillsJobHandler(leadClient);
        var contactId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new LeadCreateWithSkillsJobPayload(
            contactId, "Nguyễn Văn A", "0900000000", "a@example.com", "facebook", "vi-VN");
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/leads?lead={leadId}");
        using var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.GetProperty("LeadId").GetGuid().Should().Be(leadId);
        doc.RootElement.GetProperty("Timezone").GetString().Should().Be("Asia/Ho_Chi_Minh");
        doc.RootElement.GetProperty("PossibleDup").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("DedupCandidates").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task LeadCreateWithSkillsJobHandler_ResponseWithDedupCandidate_SummaryContainsDedupCandidateFields()
    {
        var leadClient = Substitute.For<LeadAgent.LeadAgentClient>();
        var leadId = Guid.NewGuid();
        var dupContactId = Guid.NewGuid();
        var response = new LeadCreateWithSkillsResponse
        {
            LeadId = leadId.ToString(),
            PossibleDup = true,
        };
        response.DedupCandidates.Add(new DedupCandidateDto
        {
            ContactId = dupContactId.ToString(),
            Similarity = 0.87f,
        });
        leadClient
            .CreateWithSkillsAsync(Arg.Any<LeadCreateWithSkillsRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new LeadCreateWithSkillsJobHandler(leadClient);
        var contactId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new LeadCreateWithSkillsJobPayload(
            contactId, "Trần Thị B", null, null, "zalo", "vi-VN");
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.GetProperty("PossibleDup").GetBoolean().Should().BeTrue();
        var candidates = doc.RootElement.GetProperty("DedupCandidates");
        candidates.GetArrayLength().Should().Be(1);
        candidates[0].GetProperty("ContactId").GetGuid().Should().Be(dupContactId);
        candidates[0].GetProperty("Similarity").GetDouble().Should().BeApproximately(0.87, 0.001);
    }

    [Fact]
    public async Task LeadCreateWithSkillsJobHandler_ValidPayload_SendsRequestWithTenantContactAndDefaults()
    {
        var leadClient = Substitute.For<LeadAgent.LeadAgentClient>();
        leadClient
            .CreateWithSkillsAsync(Arg.Any<LeadCreateWithSkillsRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new LeadCreateWithSkillsResponse { LeadId = Guid.NewGuid().ToString() }));

        var handler = new LeadCreateWithSkillsJobHandler(leadClient);
        var contactId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new LeadCreateWithSkillsJobPayload(
            contactId, "Lê Văn C", null, null, "tiktok", "vi-VN");
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        _ = leadClient.Received(1).CreateWithSkillsAsync(
            Arg.Is<LeadCreateWithSkillsRequest>(req =>
                req.TenantId == tenantId.ToString("D") &&
                req.ContactId == contactId.ToString("D") &&
                req.DisplayName == "Lê Văn C" &&
                req.Phone == string.Empty &&
                req.Email == string.Empty &&
                req.SourcePlatform == "tiktok" &&
                req.Locale == "vi-VN" &&
                req.Country == string.Empty),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeadCreateWithSkillsJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var leadClient = Substitute.For<LeadAgent.LeadAgentClient>();
        var handler = new LeadCreateWithSkillsJobHandler(leadClient);
        var ctx = new JobContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "null",
            new NoopJobProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AsyncUnaryCall<T> CompletedUnaryCall<T>(T response) where T : class =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private sealed class NoopJobProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
