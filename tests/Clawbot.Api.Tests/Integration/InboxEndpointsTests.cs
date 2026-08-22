using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/inbox — hội thoại, tin nhắn, draft review-gate, thống kê.
/// Admin có admin:inboxes nên UserInboxResolver trả danh sách rỗng = không lọc inbox.
/// Giới hạn InMemory: EF.Functions.Like (search q), ExecuteUpdateAsync (approve draft claim),
/// GetDbConnection (ListChannels kiểm schema) không chạy được — bỏ qua các nhánh đó.
/// </summary>
public sealed class InboxEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public InboxEndpointsTests(ApiTestFactory factory) => _factory = factory;

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
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == ApiTestFactory.AdminEmail);
        return admin.Id;
    }

    /// <summary>Seed hội thoại (InboxId null để admin thấy được mà không cần InboxMembers).</summary>
    private async Task<Guid> SeedConversationAsync(Guid tenantId, string status = "open",
        Guid? contactId = null, Guid? assignedTo = null, Action<Conversation>? seedMessages = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = Conversation.Open(tenantId, "facebook", $"thread-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow, contactId);
        if (assignedTo.HasValue) conv.Assign(assignedTo.Value);
        seedMessages?.Invoke(conv);
        if (status == "resolved") conv.Resolve();
        if (status == "escalated") conv.Escalate();
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();
        return conv.Id;
    }

    // ------------------------------------------------------------------
    // List + counts
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsConversations_WithTotalAndCursorShape()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedConversationAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/inbox/conversations", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("items").EnumerateArray()
            .Should().Contain(i => Guid.Parse(i.GetProperty("id").GetString()!) == id);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedConversationAsync(tenantId, status: "open");
        await SeedConversationAsync(tenantId, status: "resolved");

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/inbox/conversations?status=resolved", UriKind.Relative));

        body.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("status").GetString() == "resolved");
    }

    [Fact]
    public async Task List_SortByLeadScore_ReturnsOffsetPage()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedConversationAsync(tenantId);

        // sort=lead_score đi đường offset pagination thay vì keyset.
        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/inbox/conversations?sort=lead_score&page=1&pageSize=10", UriKind.Relative));

        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(10);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Counts_ReturnsBreakdownByStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedConversationAsync(tenantId, status: "open");
        await SeedConversationAsync(tenantId, status: "resolved");
        await SeedConversationAsync(tenantId, status: "escalated");

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/inbox/conversations/counts", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        body.GetProperty("open").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("resolved").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("escalated").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ------------------------------------------------------------------
    // Detail + export
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsDetailWithMessagesAndContactName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();

        Guid contactId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contact = Contact.Create(tenantId, "Khach Test Inbox", DateTimeOffset.UtcNow);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();
            contactId = contact.Id;
        }

        var convId = await SeedConversationAsync(tenantId, contactId: contactId, seedMessages: conv =>
        {
            conv.AppendMessage("in", "customer", "Xin chao shop", "text", DateTimeOffset.UtcNow.AddMinutes(-2));
            conv.AppendMessage("out", "user", "Chao ban, shop co the giup gi?", "text", DateTimeOffset.UtcNow.AddMinutes(-1));
        });

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/inbox/conversations/{convId}", UriKind.Relative));

        body.GetProperty("contactDisplayName").GetString().Should().Be("Khach Test Inbox");
        body.GetProperty("messages").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_WithoutQuery_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/inbox/search", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("query_required");
    }

    [Fact]
    public async Task ExportCsv_ReturnsCsvWithMessages()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
            conv.AppendMessage("in", "customer", "Noi dung xuat csv", "text", DateTimeOffset.UtcNow));

        var response = await client.GetAsync(
            new Uri($"/api/inbox/conversations/{convId}/export.csv", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("sent_at,direction,sender_type").And.Contain("Noi dung xuat csv");
    }

    [Fact]
    public async Task ExportCsv_UnknownConversation_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/export.csv", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Assign / resolve / escalate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Assign_ConversationWithoutInbox_AssignsUser()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var adminId = await GetAdminUserIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{convId}/assign", UriKind.Relative),
            new { userId = adminId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.AssignedTo.Should().Be(adminId);
    }

    [Fact]
    public async Task Assign_UnknownConversation_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var adminId = await GetAdminUserIdAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/assign", UriKind.Relative),
            new { userId = adminId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_MarksConversationResolved()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/resolve", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.Status.Should().Be("resolved");
    }

    [Fact]
    public async Task Escalate_MarksEscalated_AndDisablesAiAutoReply()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/escalate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.Status.Should().Be("escalated");
        conv.AiAutoReplyEnabled.Should().BeFalse("escalate = chuyển hẳn cho người, AI không tự bật lại");
    }

    // ------------------------------------------------------------------
    // AI auto-reply toggle + regenerate
    // ------------------------------------------------------------------

    [Fact]
    public async Task SetAiAutoReply_Disable_PersistsFlag()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{convId}/ai", UriKind.Relative),
            new { enabled = false });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.AiAutoReplyEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetAiAutoReply_Enable_WithoutHangingMessage_DoesNotReply()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        // Tin cuối là "out" -> resumer không thấy tin khách treo -> không gọi LLM.
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
        {
            conv.AppendMessage("in", "customer", "Cho minh hoi", "text", DateTimeOffset.UtcNow.AddMinutes(-2));
            conv.AppendMessage("out", "user", "Da co shop day", "text", DateTimeOffset.UtcNow.AddMinutes(-1));
        });

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{convId}/ai", UriKind.Relative),
            new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conv = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == convId);
        conv.AiAutoReplyEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task RegenerateAiReply_AiDisabled_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
            conv.SetAiAutoReply(false));

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/ai/regenerate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ai_disabled");
    }

    [Fact]
    public async Task RegenerateAiReply_NoHangingMessage_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        // AI bật sẵn + tin cuối là out -> resumer trả false -> 409 no_hanging_message.
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
            conv.AppendMessage("out", "user", "Shop da tra loi", "text", DateTimeOffset.UtcNow));

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/ai/regenerate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no_hanging_message");
    }

    // ------------------------------------------------------------------
    // Send outbound
    // ------------------------------------------------------------------

    [Fact]
    public async Task SendOutbound_EmptyContent_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{convId}/messages", UriKind.Relative),
            new { content = "  ", contentType = "text" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendOutbound_ChannelUnavailable_ReturnsChannelSendFailed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        // Test host không có Pancake token -> adapter.SendAsync ném lỗi -> 400 channel_send_failed.
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{convId}/messages", UriKind.Relative),
            new { content = "Tin gui thu qua kenh", contentType = "text" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("channel_send_failed");
    }

    // ------------------------------------------------------------------
    // Retry failed message
    // ------------------------------------------------------------------

    [Fact]
    public async Task RetryFailedMessage_UnknownConversation_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/retry", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryFailedMessage_UnknownMessage_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/messages/{Guid.NewGuid()}/retry", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryFailedMessage_MessageNotFailed_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        Guid messageId = Guid.Empty;
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
        {
            var msg = conv.AppendMessage("out", "user", "Tin da gui thanh cong", "text", DateTimeOffset.UtcNow);
            messageId = msg.Id;
        });

        // Tin status "sent" không phải ứng viên gửi lại -> NotAvailable -> 409.
        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/messages/{messageId}/retry", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("message_retry_not_available");
    }

    // ------------------------------------------------------------------
    // Review-gate drafts (approve/reject)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ApproveDraft_MessageNotPending_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        Guid messageId = Guid.Empty;
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
        {
            var msg = conv.AppendMessage("out", "user", "Tin binh thuong", "text", DateTimeOffset.UtcNow);
            messageId = msg.Id;
        });

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/drafts/{messageId}/approve", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("draft_not_pending");
    }

    [Fact]
    public async Task ApproveDraft_UnknownMessage_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var convId = await SeedConversationAsync(tenantId);

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/drafts/{Guid.NewGuid()}/approve", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectDraft_PendingApproval_MarksBlocked()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        Guid messageId = Guid.Empty;
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
        {
            var msg = conv.AppendMessage("out", "ai", "Ban nhap AI cho duyet", "text", DateTimeOffset.UtcNow,
                status: "pending_approval");
            messageId = msg.Id;
        });

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/drafts/{messageId}/reject", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("blocked");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var msg = await db.Messages.IgnoreQueryFilters().FirstAsync(m => m.Id == messageId);
        msg.Status.Should().Be("blocked");
    }

    [Fact]
    public async Task RejectDraft_MessageNotPending_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        Guid messageId = Guid.Empty;
        var convId = await SeedConversationAsync(tenantId, seedMessages: conv =>
        {
            var msg = conv.AppendMessage("out", "user", "Tin da gui", "text", DateTimeOffset.UtcNow);
            messageId = msg.Id;
        });

        var response = await client.PostAsync(
            new Uri($"/api/inbox/conversations/{convId}/drafts/{messageId}/reject", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("draft_not_pending");
    }

    // ------------------------------------------------------------------
    // Daily summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task DailySummary_ReturnsStatsForCurrentUser()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var adminId = await GetAdminUserIdAsync();
        // Hội thoại gán cho admin có tin hôm nay -> conversationsHandled >= 1.
        await SeedConversationAsync(tenantId, assignedTo: adminId, seedMessages: conv =>
            conv.AppendMessage("in", "customer", "Tin hom nay", "text", DateTimeOffset.UtcNow));

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/inbox/daily-summary", UriKind.Relative));

        body.GetProperty("conversationsHandled").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("date").GetString().Should().NotBeNullOrEmpty();
    }
}
