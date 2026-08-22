using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Channels;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/inboxes + /api/admin/users/simple + /api/admin/pancake-channels.
/// Group yêu cầu quyền admin:inboxes / users:pancake-token:manage (admin bootstrap có đủ)
/// nên happy path chạy bằng admin client. CreateInbox KHÔNG truyền PageAccessToken để
/// FetchPageNameAsync trả null sớm — tránh gọi HTTP thật ra pages.fm trong test.
/// </summary>
public sealed class AdminInboxEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AdminInboxEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static async Task<Guid> DefaultTenantIdAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private static async Task<Guid> SeedUserAsync(ApiTestFactory factory, string displayName)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = $"agent-{suffix}@test.local",
            Email = $"agent-{suffix}@test.local",
            DisplayName = displayName,
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedInboxAsync(ApiTestFactory factory, string platform = "facebook")
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = Inbox.Create(tenantId, "Kênh Test", platform, "page_" + Guid.NewGuid().ToString("N"));
        db.Inboxes.Add(inbox);
        await db.SaveChangesAsync();
        return inbox.Id;
    }

    private static async Task SeedMemberAsync(ApiTestFactory factory, Guid inboxId, Guid agentId)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.InboxMembers.Add(InboxMember.Create(tenantId, inboxId, agentId));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedAssignedConversationAsync(ApiTestFactory factory, Guid inboxId, Guid assignedTo)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var conversation = Conversation.Open(tenantId, "facebook", "thread_" + Guid.NewGuid().ToString("N"), clock.UtcNow, inboxId: inboxId);
        conversation.Assign(assignedTo);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    private async Task<Guid> ReadAssignedToAsync(Guid conversationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conversation = await db.Conversations.IgnoreQueryFilters().FirstAsync(c => c.Id == conversationId);
        return conversation.AssignedTo ?? Guid.Empty;
    }

    private async Task<Inbox> ReadInboxAsync(Guid inboxId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Inboxes.IgnoreQueryFilters().FirstAsync(i => i.Id == inboxId);
    }

    // ------------------------------------------------------------------
    // GET users/simple + GET inboxes
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListSimpleUsers_ReturnsSeededTenantUsers()
    {
        var agentId = await SeedUserAsync(_factory, "Agent Simple");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var users = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/users/simple", UriKind.Relative));

        users.ValueKind.Should().Be(JsonValueKind.Array);
        users.EnumerateArray().Any(u => u.GetProperty("id").GetString() == agentId.ToString()).Should().BeTrue();
    }

    [Fact]
    public async Task ListInboxes_ReturnsActiveInboxes_WithMemberCountAndHasToken()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent Member Count");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var inboxes = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/inboxes", UriKind.Relative));

        var match = inboxes.EnumerateArray().First(i => i.GetProperty("id").GetString() == inboxId.ToString());
        match.GetProperty("memberCount").GetInt32().Should().Be(1);
        match.GetProperty("hasToken").GetBoolean().Should().BeFalse();
        match.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Members: list + assignable agents
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListMembers_ReturnsAgentIds()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent List");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));

        members.GetArrayLength().Should().Be(1);
        members[0].GetString().Should().Be(agentId.ToString());
    }

    [Fact]
    public async Task ListMembers_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}/members", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListAssignableAgents_ReturnsMemberDetails()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent Assignable");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var agents = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/assignable-agents", UriKind.Relative));

        agents.GetArrayLength().Should().Be(1);
        agents[0].GetProperty("id").GetString().Should().Be(agentId.ToString());
        agents[0].GetProperty("displayName").GetString().Should().Be("Agent Assignable");
    }

    [Fact]
    public async Task ListAssignableAgents_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}/assignable-agents", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // PUT inboxes/{id}/members (UpdateMember)
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateMember_SetsNewAgent_AndUnassignsOldConversations()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var oldAgent = await SeedUserAsync(_factory, "Agent Cũ");
        var newAgent = await SeedUserAsync(_factory, "Agent Mới");
        await SeedMemberAsync(_factory, inboxId, oldAgent);
        var conversationId = await SeedAssignedConversationAsync(_factory, inboxId, oldAgent);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative),
            new { agentId = newAgent });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));
        members.GetArrayLength().Should().Be(1);
        members[0].GetString().Should().Be(newAgent.ToString());

        // Hội thoại của agent cũ bị unassign khi đổi người phụ trách.
        (await ReadAssignedToAsync(conversationId)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task UpdateMember_UnknownInbox_ReturnsNotFound()
    {
        var agentId = await SeedUserAsync(_factory, "Agent No Inbox");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}/members", UriKind.Relative),
            new { agentId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateMember_UnknownAgent_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative),
            new { agentId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("agent_not_found");
    }

    [Fact]
    public async Task UpdateMember_SameSingleAgent_IsIdempotentNoContent()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent Idempotent");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative),
            new { agentId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateMember_NullAgentId_ClearsAllMembers_AndUnassignsConversations()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent Clear");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var conversationId = await SeedAssignedConversationAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative),
            new { agentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));
        members.GetArrayLength().Should().Be(0);
        (await ReadAssignedToAsync(conversationId)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task UpdateMember_NullAgentId_WhenNoMembers_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative),
            new { agentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("inbox_must_have_member");
    }

    // ------------------------------------------------------------------
    // DELETE inboxes/{id}/members/{agentId} (UnlinkMember)
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnlinkMember_RemovesMember_AndUnassignsConversations()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var agentId = await SeedUserAsync(_factory, "Agent Unlink");
        await SeedMemberAsync(_factory, inboxId, agentId);
        var conversationId = await SeedAssignedConversationAsync(_factory, inboxId, agentId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members/{agentId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));
        members.GetArrayLength().Should().Be(0);
        (await ReadAssignedToAsync(conversationId)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task UnlinkMember_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}/members/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnlinkMember_UnknownMember_ReturnsNotFound()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/members/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST inboxes/{id}/reassign
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reassign_ReplacesMembers_UnassignsConversations_ReturnsOk()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var oldAgent = await SeedUserAsync(_factory, "Agent Reassign Cũ");
        var newAgent = await SeedUserAsync(_factory, "Agent Reassign Mới");
        await SeedMemberAsync(_factory, inboxId, oldAgent);
        var conversationId = await SeedAssignedConversationAsync(_factory, inboxId, oldAgent);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/reassign", UriKind.Relative),
            new { newAgentId = newAgent });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("inboxId").GetString().Should().Be(inboxId.ToString());
        body.GetProperty("newAgentId").GetString().Should().Be(newAgent.ToString());
        body.GetProperty("unassignedConversationCount").GetInt32().Should().Be(1);
        body.GetProperty("oldAgentIds").EnumerateArray()
            .Any(e => e.GetString() == oldAgent.ToString()).Should().BeTrue();

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));
        members.GetArrayLength().Should().Be(1);
        members[0].GetString().Should().Be(newAgent.ToString());
        (await ReadAssignedToAsync(conversationId)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Reassign_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}/reassign", UriKind.Relative),
            new { newAgentId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reassign_UnknownAgent_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}/reassign", UriKind.Relative),
            new { newAgentId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("agent_not_found");
    }

    // ------------------------------------------------------------------
    // POST / PUT inboxes (Create + UpdateInbox)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateInbox_WithoutToken_UsesFallbackName_AndAddsMember()
    {
        var agentId = await SeedUserAsync(_factory, "Agent Create");
        var client = await _factory.CreateAuthenticatedClientAsync();
        var externalPageId = "oa_" + Guid.NewGuid().ToString("N");

        // Không truyền PageAccessToken -> FetchPageNameAsync trả null sớm, tên fallback "{platform} OA - {pageId}".
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/inboxes", UriKind.Relative),
            new { platform = "zalo", externalPageId, pageAccessToken = (string?)null, agentId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be($"zalo OA - {externalPageId}");
        var inboxId = Guid.Parse(body.GetProperty("id").GetString()!);

        var members = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/inboxes/{inboxId}/members", UriKind.Relative));
        members.GetArrayLength().Should().Be(1);
        members[0].GetString().Should().Be(agentId.ToString());
    }

    [Fact]
    public async Task UpdateInbox_WithToken_PersistsEncryptedToken()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}", UriKind.Relative),
            new { pageAccessToken = "token-moi" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var inbox = await ReadInboxAsync(inboxId);
        inbox.EncryptedAccessToken.Should().NotBeNullOrEmpty();
        inbox.PageTokenMintedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateInbox_BlankToken_KeepsExistingTokenNull()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{inboxId}", UriKind.Relative),
            new { pageAccessToken = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var inbox = await ReadInboxAsync(inboxId);
        inbox.EncryptedAccessToken.Should().BeNull();
    }

    [Fact]
    public async Task UpdateInbox_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/admin/inboxes/{Guid.NewGuid()}", UriKind.Relative),
            new { pageAccessToken = "token" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // PATCH pancake-channels/{id}
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdatePancakeChannel_NameAndToken_PersistBoth()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{inboxId}", UriKind.Relative),
            new { name = "Kênh Đổi Tên", pageAccessToken = "pancake-token" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var inbox = await ReadInboxAsync(inboxId);
        inbox.Name.Should().Be("Kênh Đổi Tên");
        inbox.EncryptedAccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdatePancakeChannel_EmptyBody_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{inboxId}", UriKind.Relative),
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("channel_update_required");
    }

    [Fact]
    public async Task UpdatePancakeChannel_BlankName_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{inboxId}", UriKind.Relative),
            new { name = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("channel_name_required");
    }

    [Fact]
    public async Task UpdatePancakeChannel_NameTooLong_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{inboxId}", UriKind.Relative),
            new { name = new string('x', 257) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("channel_name_too_long");
    }

    [Fact]
    public async Task UpdatePancakeChannel_BlankToken_ReturnsBadRequest()
    {
        var inboxId = await SeedInboxAsync(_factory);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{inboxId}", UriKind.Relative),
            new { pageAccessToken = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("page_access_token_invalid");
    }

    [Fact]
    public async Task UpdatePancakeChannel_UnknownInbox_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PatchAsJsonAsync(
            new Uri($"/api/admin/pancake-channels/{Guid.NewGuid()}", UriKind.Relative),
            new { name = "Kênh Ma" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
