using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/leads + /api/lead-scoring-rules. Admin có leads:read:all nên scope không giới hạn.
/// Rescore dùng KeywordLeadSignalClassifier (không LLM) nên chạy offline an toàn.
/// </summary>
public sealed class LeadsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public LeadsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> GetAdminUserIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == ApiTestFactory.AdminEmail)).Id;
    }

    /// <summary>Seed contact + lead; mutate là hành động domain (Assign/AdjustScore...) trước khi lưu.</summary>
    private async Task<(Guid ContactId, Guid LeadId)> SeedLeadAsync(
        Guid tenantId, string source = "facebook", Action<Lead>? mutate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = Contact.Create(tenantId, $"Khach {Guid.NewGuid():N}"[..12], DateTimeOffset.UtcNow);
        var lead = Lead.Create(tenantId, contact.Id, source, DateTimeOffset.UtcNow);
        mutate?.Invoke(lead);
        db.Contacts.Add(contact);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return (contact.Id, lead.Id);
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsLeads_WithContactName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/leads", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var item = body.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == leadId);
        item.GetProperty("stage").GetString().Should().Be("cold");
        item.GetProperty("contactName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task List_FiltersByStageSourceAndOwner()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var adminId = await GetAdminUserIdAsync();
        var (_, lostId) = await SeedLeadAsync(tenantId, source: "zalo", l => l.MarkLost("test", DateTimeOffset.UtcNow));
        var (_, assignedId) = await SeedLeadAsync(tenantId, source: "facebook", l => l.Assign(adminId));

        var byStage = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/leads?stage=lost", UriKind.Relative));
        byStage.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("stage").GetString() == "lost");

        var bySource = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/leads?source=zalo", UriKind.Relative));
        bySource.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("sourcePlatform").GetString() == "zalo");

        var assigned = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/leads?owner=assigned", UriKind.Relative));
        var assignedIds = assigned.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        assignedIds.Should().Contain(assignedId).And.NotContain(lostId);

        var unassigned = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/leads?owner=unassigned", UriKind.Relative));
        unassigned.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("ownerUserId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task List_ExcludesLeads_FromGroupConversations()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, personalLeadId) = await SeedLeadAsync(tenantId, source: "zalo");
        var (groupContactId, groupLeadId) = await SeedLeadAsync(tenantId, source: "zalo");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Hoi thoai nhom (nhieu thanh vien) gan voi contact cua groupLeadId — khong phai 1 khach ca nhan.
            var groupConversation = Conversation.Open(
                tenantId, "zalo", $"group-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, groupContactId, isGroup: true);
            db.Conversations.Add(groupConversation);
            await db.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/leads?pageSize=200", UriKind.Relative));

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        ids.Should().Contain(personalLeadId, "hội thoại cá nhân vẫn tính Lead");
        ids.Should().NotContain(groupLeadId, "hội thoại nhóm không được tính Lead");
    }

    // ------------------------------------------------------------------
    // GET detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsLeadDetail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (contactId, leadId) = await SeedLeadAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/leads/{leadId}", UriKind.Relative));

        body.GetProperty("id").GetString().Should().Be(leadId.ToString());
        body.GetProperty("contactId").GetString().Should().Be(contactId.ToString());
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/leads/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST create
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsCreated_WithLeadId()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();

        Guid contactId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contact = Contact.Create(tenantId, "Khach tao moi", DateTimeOffset.UtcNow);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            contactId = contact.Id;
        }

        var response = await client.PostAsJsonAsync(new Uri("/api/leads", UriKind.Relative), new
        {
            contactId,
            sourcePlatform = "facebook",
            phone = (string?)null,
            email = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("leadId").GetGuid().Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------
    // POST create-with-skills (job)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateWithSkills_EmptyContactId_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/leads/create-with-skills", UriKind.Relative), new
        {
            contactId = Guid.Empty,
            sourcePlatform = "facebook",
            phone = (string?)null,
            email = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWithSkills_UnknownContact_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/leads/create-with-skills", UriKind.Relative), new
        {
            contactId = Guid.NewGuid(),
            sourcePlatform = "facebook",
            phone = (string?)null,
            email = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateWithSkills_ValidContact_EnqueuesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (contactId, _) = await SeedLeadAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri("/api/leads/create-with-skills", UriKind.Relative), new
        {
            contactId,
            sourcePlatform = "facebook",
            phone = (string?)null,
            email = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------
    // Activities + scoring
    // ------------------------------------------------------------------

    [Fact]
    public async Task RecordActivity_MatchingRule_AdjustsScore()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        // Tạo rule chấm điểm cho event asked_price (weight 9).
        var ruleResponse = await client.PostAsJsonAsync(new Uri("/api/lead-scoring-rules", UriKind.Relative), new
        {
            eventCode = $"asked_price_{Guid.NewGuid():N}"[..16],
            weight = 9,
            platform = (string?)null,
            description = (string?)null,
        });
        ruleResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Dùng event code trùng rule vừa tạo — chắc chắn không đụng rule tenant khác seed sẵn.
        var ruleBody = await ruleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventCode = ruleBody.GetProperty("eventCode").GetString()!;

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{leadId}/activities", UriKind.Relative), new
        {
            eventCode,
            platform = (string?)null,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("newScore").GetInt32().Should().Be(9);
        body.GetProperty("matchedRules").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task RecordActivity_PaymentConfirmed_MarksCustomer()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{leadId}/activities", UriKind.Relative), new
        {
            eventCode = "payment_confirmed",
            platform = (string?)null,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("stage").GetString().Should().Be("customer");
    }

    [Fact]
    public async Task RecordActivity_UnknownLead_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{Guid.NewGuid()}/activities", UriKind.Relative), new
        {
            eventCode = "asked_price",
            platform = (string?)null,
            notes = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Stage transitions
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateStage_CustomerLostReopen_FollowLifecycle()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var customer = await client.PutAsJsonAsync(new Uri($"/api/leads/{leadId}/stage", UriKind.Relative),
            new { stage = "customer", reason = "da thanh toan" });
        customer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await customer.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("stage").GetString().Should().Be("customer");

        var reopen = await client.PutAsJsonAsync(new Uri($"/api/leads/{leadId}/stage", UriKind.Relative),
            new { stage = "reopen", reason = "mo lai" });
        reopen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reopen.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("stage").GetString().Should().Be("cold", "reopen đưa về stage theo điểm (score 0)");

        var lost = await client.PutAsJsonAsync(new Uri($"/api/leads/{leadId}/stage", UriKind.Relative),
            new { stage = "lost", reason = "khong nghe may" });
        lost.StatusCode.Should().Be(HttpStatusCode.OK);
        (await lost.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("stage").GetString().Should().Be("lost");
    }

    [Fact]
    public async Task UpdateStage_InvalidStage_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/leads/{leadId}/stage", UriKind.Relative),
            new { stage = "stage-la", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_stage_action");
    }

    [Fact]
    public async Task UpdateStage_UnknownLead_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/leads/{Guid.NewGuid()}/stage", UriKind.Relative),
            new { stage = "lost", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Assign
    // ------------------------------------------------------------------

    [Fact]
    public async Task Assign_ToAdminUser_SetsOwner()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var adminId = await GetAdminUserIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{leadId}/assign", UriKind.Relative),
            new { userId = (Guid?)adminId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lead = await db.Leads.IgnoreQueryFilters().FirstAsync(l => l.Id == leadId);
        lead.OwnerUserId.Should().Be(adminId);
    }

    [Fact]
    public async Task Assign_UnknownUser_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{leadId}/assign", UriKind.Relative),
            new { userId = (Guid?)Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("assignee_not_eligible");
    }

    [Fact]
    public async Task Assign_UnknownLead_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri($"/api/leads/{Guid.NewGuid()}/assign", UriKind.Relative),
            new { userId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Export / import CSV
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExportCsv_ReturnsCsvWithHeader()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedLeadAsync(tenantId);

        var response = await client.GetAsync(new Uri("/api/leads/export.csv", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("lead_id,contact_id,display_name");
    }

    [Fact]
    public async Task ImportCsv_ImportsValidRows_AndReportsErrors()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var csv = "display_name,source_platform,phone,score\n"
            + "Khach Mot,facebook,0901111111,10\n"
            + ",facebook,0902222222,0\n"
            + "Khach Ba,zalo,0903333333,999\n";
        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");

        var response = await client.PostAsync(new Uri("/api/leads/import.csv", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("imported").GetInt32().Should().Be(1, "chỉ dòng 1 hợp lệ; dòng 2 thiếu tên, dòng 3 score ngoài khoảng");
        body.GetProperty("errors").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ImportCsv_MissingRequiredColumn_ReportsError()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        using var content = new StringContent("display_name,phone\nKhach,090\n", Encoding.UTF8, "text/csv");

        var response = await client.PostAsync(new Uri("/api/leads/import.csv", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("imported").GetInt32().Should().Be(0);
        body.GetProperty("errors")[0].GetString().Should().Contain("source_platform");
    }

    // ------------------------------------------------------------------
    // Forecast + context panel
    // ------------------------------------------------------------------

    [Fact]
    public async Task Forecast_ReturnsShape()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedLeadAsync(tenantId);

        var response = await client.GetAsync(new Uri("/api/leads/forecast", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Ít hơn 7 ngày dữ liệu -> note; đủ dữ liệu -> mảng forecast. Cả hai đều là 200.
        body.Should().ContainAny("need_at_least_7_days_of_data", "forecast");
    }

    [Fact]
    public async Task ContextPanel_ReturnsLead_WithNextStep()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var (_, leadId) = await SeedLeadAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/leads/{leadId}/context", UriKind.Relative));

        body.GetProperty("id").GetString().Should().Be(leadId.ToString());
        body.GetProperty("stage").GetString().Should().Be("cold");
        body.GetProperty("nextStep").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("contact").GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ContextPanel_UnknownLead_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/leads/{Guid.NewGuid()}/context", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Scoring rules + rescore
    // ------------------------------------------------------------------

    [Fact]
    public async Task Rules_ListCreateDeactivate_Lifecycle()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync(new Uri("/api/lead-scoring-rules", UriKind.Relative), new
        {
            eventCode = $"evt_{Guid.NewGuid():N}"[..12],
            weight = 7,
            platform = "facebook",
            description = "rule test",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var ruleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/lead-scoring-rules", UriKind.Relative));
        list.EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == ruleId);

        var delete = await client.DeleteAsync(new Uri($"/api/lead-scoring-rules/{ruleId}", UriKind.Relative));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/lead-scoring-rules", UriKind.Relative));
        after.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == ruleId)
            .GetProperty("isActive").GetBoolean().Should().BeFalse("DELETE chỉ vô hiệu hoá rule");
    }

    [Fact]
    public async Task CreateRule_EmptyEventCode_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/lead-scoring-rules", UriKind.Relative), new
        {
            eventCode = "",
            weight = 5,
            platform = (string?)null,
            description = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeactivateRule_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/lead-scoring-rules/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SeedDefaults_FirstRunCreates_SecondRunSkips()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await client.PostAsync(new Uri("/api/lead-scoring-rules/seed-defaults", UriKind.Relative), content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstBody.GetProperty("created").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        firstBody.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(8);

        var second = await client.PostAsync(new Uri("/api/lead-scoring-rules/seed-defaults", UriKind.Relative), content: null);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("created").GetInt32().Should().Be(0, "seed lại phải bỏ qua code đã tồn tại");
    }

    [Fact]
    public async Task Rescore_ReturnsSummary()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedLeadAsync(tenantId);

        var response = await client.PostAsync(new Uri("/api/leads/rescore", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("leadsScanned").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.TryGetProperty("topPriority", out var top).Should().BeTrue();
        top.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
