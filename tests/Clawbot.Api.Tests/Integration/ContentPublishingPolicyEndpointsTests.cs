using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// GET/PUT /api/content/settings/publishing-policy — nguồn ghi duy nhất cho chính sách duyệt
/// đăng nội dung của tenant (automatic | human_required). Agent review text luôn bắt buộc
/// (AgentReviewRequired luôn true); endpoint chỉ đổi bước duyệt người sau review.
/// </summary>
public sealed class ContentPublishingPolicyEndpointsTests : IClassFixture<ApiTestFactory>
{
    private static readonly Uri PolicyUri = new("/api/content/settings/publishing-policy", UriKind.Relative);

    private readonly ApiTestFactory _factory;

    public ContentPublishingPolicyEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<int> CountPolicyAuditLogsAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Action == "content.publishing_policy.changed")
            .CountAsync();
    }

    // ------------------------------------------------------------------
    // GET
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsCurrentPolicy_WithAgentReviewAlwaysRequired()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(PolicyUri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentPublishingPolicyDto>();
        dto.Should().NotBeNull();
        dto!.AgentReviewRequired.Should().BeTrue("agent review text luôn bắt buộc, không phụ thuộc policy");
        dto.AgentReviewMode.Should().Be("text_required_vision_optional");
        dto.PublishingApprovalPolicy.Should().BeOneOf(
            Tenant.ContentPublishingPolicyAutomatic, Tenant.ContentPublishingPolicyHumanRequired);
        dto.PolicyVersion.Should().BeGreaterThanOrEqualTo(1);
        dto.ReviewerVisionCapability.Should().BeOneOf("available", "unavailable", "unknown");
    }

    [Fact]
    public async Task Get_ReviewerVisionCapability_IsUnknown_WhenNoReviewerAgentBound()
    {
        // Tenant test mặc định chưa bind LlmConfig cho agent reviewer nào -> nhánh "unknown".
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(PolicyUri);

        var dto = await response.Content.ReadFromJsonAsync<ContentPublishingPolicyDto>();
        dto!.ReviewerVisionCapability.Should().Be("unknown");
    }

    [Fact]
    public async Task Get_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(PolicyUri);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // PUT — validation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_BlankPolicy_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            PolicyUri, new UpdateContentPublishingPolicyRequest(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.publishing_policy_required");
    }

    [Fact]
    public async Task Put_InvalidPolicy_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            PolicyUri, new UpdateContentPublishingPolicyRequest("khong_ton_tai"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.publishing_policy_invalid");
    }

    // ------------------------------------------------------------------
    // PUT — thay đổi thực sự
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_NewPolicyValue_UpdatesTenant_BumpsVersion_AndWritesAuditLog()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();

        var before = await client.GetFromJsonAsync<ContentPublishingPolicyDto>(PolicyUri);
        var nextPolicy = string.Equals(
            before!.PublishingApprovalPolicy, Tenant.ContentPublishingPolicyAutomatic, StringComparison.Ordinal)
            ? Tenant.ContentPublishingPolicyHumanRequired
            : Tenant.ContentPublishingPolicyAutomatic;
        var auditCountBefore = await CountPolicyAuditLogsAsync(tenantId);

        var response = await client.PutAsJsonAsync(
            PolicyUri, new UpdateContentPublishingPolicyRequest(nextPolicy));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await response.Content.ReadFromJsonAsync<ContentPublishingPolicyDto>();
        after!.PublishingApprovalPolicy.Should().Be(nextPolicy);
        after.PolicyVersion.Should().BeGreaterThan(before.PolicyVersion, "đổi policy phải tăng version");

        var auditCountAfter = await CountPolicyAuditLogsAsync(tenantId);
        auditCountAfter.Should().Be(auditCountBefore + 1, "đổi policy thực sự phải ghi đúng 1 audit log mới");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var latest = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.Action == "content.publishing_policy.changed")
            .OrderByDescending(a => a.OccurredAt)
            .FirstAsync();
        latest.ResourceType.Should().Be("tenant");
        latest.ResourceId.Should().Be(tenantId);
        latest.DiffJson.Should().NotBeNullOrWhiteSpace();
        latest.DiffJson.Should().Contain(nextPolicy);
    }

    [Fact]
    public async Task Put_SameValueAsCurrent_ReturnsOk_ButDoesNotWriteExtraAuditLog()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();

        // Đảm bảo trạng thái ổn định trước khi so sánh: set 1 giá trị cụ thể trước.
        var setup = await client.PutAsJsonAsync(
            PolicyUri,
            new UpdateContentPublishingPolicyRequest(Tenant.ContentPublishingPolicyAutomatic));
        setup.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditCountBefore = await CountPolicyAuditLogsAsync(tenantId);

        var response = await client.PutAsJsonAsync(
            PolicyUri,
            new UpdateContentPublishingPolicyRequest(Tenant.ContentPublishingPolicyAutomatic));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentPublishingPolicyDto>();
        dto!.PublishingApprovalPolicy.Should().Be(Tenant.ContentPublishingPolicyAutomatic);

        var auditCountAfter = await CountPolicyAuditLogsAsync(tenantId);
        auditCountAfter.Should().Be(auditCountBefore, "giá trị không đổi thì không ghi thêm audit log");
    }

    [Fact]
    public async Task Put_WithoutToken_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            PolicyUri, new UpdateContentPublishingPolicyRequest(Tenant.ContentPublishingPolicyAutomatic));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
