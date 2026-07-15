using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Integration.Tests;

public sealed class UserPancakeChannelManagementTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private static readonly Guid TenantId = Guid.Parse(TestAuthHandler.TenantId);
    private static readonly Guid AdminId = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _factory;
    private readonly ClawbotWebApplicationFactory _tokenManagerFactory;
    private readonly ClawbotWebApplicationFactory _inboxAdminFactory;
    private readonly ClawbotWebApplicationFactory _noPermsFactory;
    private readonly HttpClient _client;
    private readonly HttpClient _tokenManagerClient;
    private readonly HttpClient _inboxAdminClient;
    private readonly HttpClient _noPermsClient;

    public UserPancakeChannelManagementTests(SqlServerFixture sql)
    {
        _sql = sql;
        _factory = new ClawbotWebApplicationFactory(sql);
        _tokenManagerFactory = CreateFactory<TestAuthHandlerTokenManager>(sql, "TestTokenManager");
        _inboxAdminFactory = CreateFactory<TestAuthHandlerInboxAdmin>(sql, "TestInboxAdmin");
        _noPermsFactory = CreateFactory<TestAuthHandlerNoPerms>(sql, "TestNoPerms");
        _client = _factory.CreateClient();
        _tokenManagerClient = _tokenManagerFactory.CreateClient();
        _inboxAdminClient = _inboxAdminFactory.CreateClient();
        _noPermsClient = _noPermsFactory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await _tokenManagerFactory.InitializeAsync();
        await _inboxAdminFactory.InitializeAsync();
        await _noPermsFactory.InitializeAsync();
    }

    public Task DisposeAsync() => Task.WhenAll(
        _factory.DisposeAsync().AsTask(),
        _tokenManagerFactory.DisposeAsync().AsTask(),
        _inboxAdminFactory.DisposeAsync().AsTask(),
        _noPermsFactory.DisposeAsync().AsTask());

    public void Dispose()
    {
        _client.Dispose();
        _tokenManagerClient.Dispose();
        _inboxAdminClient.Dispose();
        _noPermsClient.Dispose();
        _factory.Dispose();
        _tokenManagerFactory.Dispose();
        _inboxAdminFactory.Dispose();
        _noPermsFactory.Dispose();
    }

    [Fact]
    public async Task Admin_users_projection_returns_stable_inbox_ids_without_token_material()
    {
        var projectionUserId = Guid.NewGuid();
        var activeWithToken = Guid.NewGuid();
        var activeWithoutToken = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        const string tokenMaterial = "ciphertext-token-that-must-not-leak";

        await InsertTenantAsync(OtherTenantId);
        await InsertUserAsync(projectionUserId, TenantId, "Projection User");
        await InsertInboxAsync(activeWithToken, TenantId, "Primary Pancake", "facebook", "page-primary", tokenMaterial);
        await InsertInboxAsync(activeWithoutToken, TenantId, "Secondary Pancake", "zalo", "page-secondary", null);
        await InsertInboxAsync(deleted, TenantId, "Deleted Pancake", "facebook", "page-deleted", tokenMaterial, deleted: true);
        await InsertInboxAsync(otherTenant, OtherTenantId, "Other Tenant Pancake", "facebook", "page-other", tokenMaterial);
        await InsertMemberAsync(activeWithToken, TenantId, projectionUserId);
        await InsertMemberAsync(activeWithoutToken, TenantId, projectionUserId);
        await InsertMemberAsync(deleted, TenantId, projectionUserId);
        await InsertMemberAsync(otherTenant, OtherTenantId, projectionUserId);

        var response = await _client.GetAsync("/api/admin/users?page=1&pageSize=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        var user = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == projectionUserId);
        var channels = user.GetProperty("pancakeChannels").EnumerateArray().ToArray();

        channels.Should().HaveCount(2);
        channels.Select(channel => channel.GetProperty("inboxId").GetGuid())
            .Should()
            .BeEquivalentTo([activeWithToken, activeWithoutToken]);
        channels.Should().Contain(channel =>
            channel.GetProperty("inboxId").GetGuid() == activeWithToken
            && channel.GetProperty("pageId").GetString() == "page-primary"
            && channel.GetProperty("name").GetString() == "Primary Pancake"
            && channel.GetProperty("platform").GetString() == "facebook"
            && channel.GetProperty("hasToken").GetBoolean());
        channels.Should().Contain(channel =>
            channel.GetProperty("inboxId").GetGuid() == activeWithoutToken
            && !channel.GetProperty("hasToken").GetBoolean());
        json.RootElement.ToString().Should().NotContain(tokenMaterial);
        json.RootElement.ToString().Should().NotContain("encryptedAccessToken");
    }

    [Fact]
    public async Task Token_manager_can_update_channel_name_without_replacing_token()
    {
        var inboxId = Guid.NewGuid();
        const string existingCiphertext = "existing-ciphertext";
        await InsertInboxAsync(inboxId, TenantId, "Before name", "facebook", "page-metadata", existingCiphertext);

        var response = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{inboxId}", new
        {
            name = "After name",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var state = await ReadInboxStateAsync(inboxId);
        state.Name.Should().Be("After name");
        state.EncryptedToken.Should().Be(existingCiphertext);
    }

    [Fact]
    public async Task Token_manager_can_replace_channel_token_without_returning_secret()
    {
        var inboxId = Guid.NewGuid();
        const string existingCiphertext = "existing-ciphertext";
        const string replacementToken = "replacement-token-value";
        await InsertInboxAsync(inboxId, TenantId, "Token channel", "facebook", "page-token", existingCiphertext);

        var response = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{inboxId}", new
        {
            pageAccessToken = replacementToken,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(replacementToken);
        var state = await ReadInboxStateAsync(inboxId);
        state.EncryptedToken.Should().NotBe(existingCiphertext);
        state.EncryptedToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Channel_metadata_requires_token_management_permission_and_tenant_scope()
    {
        var inboxId = Guid.NewGuid();
        var otherTenantInboxId = Guid.NewGuid();
        await InsertTenantAsync(OtherTenantId);
        await InsertInboxAsync(inboxId, TenantId, "Protected channel", "facebook", "page-protected", "ciphertext");
        await InsertInboxAsync(otherTenantInboxId, OtherTenantId, "Other tenant channel", "facebook", "page-other", "ciphertext");

        var forbidden = await _noPermsClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{inboxId}", new { name = "Nope" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var crossTenant = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{otherTenantInboxId}", new { name = "Nope" });
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Channel_metadata_rejects_empty_or_invalid_updates()
    {
        var inboxId = Guid.NewGuid();
        await InsertInboxAsync(inboxId, TenantId, "Validation channel", "facebook", "page-validation", "ciphertext");

        var empty = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{inboxId}", new { name = (string?)null, pageAccessToken = (string?)null });
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(empty)).RootElement.GetProperty("error").GetString().Should().Be("channel_update_required");

        var blankName = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{inboxId}", new { name = "   " });
        blankName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(blankName)).RootElement.GetProperty("error").GetString().Should().Be("channel_name_required");
    }

    [Fact]
    public async Task Channel_metadata_rejects_deleted_overlong_and_oversized_values()
    {
        var activeInboxId = Guid.NewGuid();
        var deletedInboxId = Guid.NewGuid();
        await InsertInboxAsync(activeInboxId, TenantId, "Limits channel", "facebook", $"page-{activeInboxId:N}", "ciphertext");
        await InsertInboxAsync(deletedInboxId, TenantId, "Deleted channel", "facebook", $"page-{deletedInboxId:N}", "ciphertext", deleted: true);

        var deleted = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{deletedInboxId}", new { name = "Nope" });
        deleted.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var overlongName = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{activeInboxId}", new { name = new string('n', 257) });
        overlongName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(overlongName)).RootElement.GetProperty("error").GetString().Should().Be("channel_name_too_long");

        var oversizedToken = await _tokenManagerClient.PatchAsJsonAsync($"/api/admin/pancake-channels/{activeInboxId}", new { pageAccessToken = new string('t', 2000) });
        oversizedToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(oversizedToken)).RootElement.GetProperty("error").GetString().Should().Be("page_access_token_invalid");
    }

    [Fact]
    public async Task Owner_update_assigns_unowned_inbox_and_rejects_cross_tenant_user()
    {
        var newOwnerId = Guid.NewGuid();
        var otherTenantUserId = Guid.NewGuid();
        var unownedInboxId = Guid.NewGuid();
        var ownedInboxId = Guid.NewGuid();
        await InsertTenantAsync(OtherTenantId);
        await InsertUserAsync(newOwnerId, TenantId, "New Owner");
        await InsertUserAsync(otherTenantUserId, OtherTenantId, "Other Tenant Owner");
        await InsertInboxAsync(unownedInboxId, TenantId, "Unowned", "facebook", $"page-{unownedInboxId:N}", null);
        await InsertInboxAsync(ownedInboxId, TenantId, "Owned", "facebook", $"page-{ownedInboxId:N}", null);
        await InsertMemberAsync(ownedInboxId, TenantId, AdminId);

        var assigned = await _inboxAdminClient.PutAsJsonAsync($"/api/admin/inboxes/{unownedInboxId}/members", new { agentId = newOwnerId });
        assigned.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadInboxOwnerAsync(unownedInboxId)).Should().Be(newOwnerId);

        var crossTenant = await _inboxAdminClient.PutAsJsonAsync($"/api/admin/inboxes/{ownedInboxId}/members", new { agentId = otherTenantUserId });
        crossTenant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadInboxOwnerAsync(ownedInboxId)).Should().Be(AdminId);
    }

    [Fact]
    public async Task Owner_update_is_noop_when_selected_user_is_already_responsible()
    {
        var inboxId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await InsertInboxAsync(inboxId, TenantId, "No-op owner", "facebook", $"page-{inboxId:N}", null);
        await InsertMemberAsync(inboxId, TenantId, AdminId);
        await InsertConversationAsync(conversationId, inboxId, AdminId);

        var response = await _inboxAdminClient.PutAsJsonAsync($"/api/admin/inboxes/{inboxId}/members", new { agentId = AdminId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadInboxOwnerAsync(inboxId)).Should().Be(AdminId);
        (await ReadConversationAssigneeAsync(conversationId)).Should().Be(AdminId);
    }

    [Fact]
    public async Task Owner_update_changes_one_inbox_and_unassigns_only_former_owner_conversations()
    {
        var replacementId = Guid.NewGuid();
        var inboxA = Guid.NewGuid();
        var inboxB = Guid.NewGuid();
        var conversationAFormerOwner = Guid.NewGuid();
        var conversationAOtherUser = Guid.NewGuid();
        var conversationBFormerOwner = Guid.NewGuid();
        await InsertUserAsync(replacementId, TenantId, "Replacement Owner");
        await InsertInboxAsync(inboxA, TenantId, "Inbox A", "facebook", $"page-{inboxA:N}", null);
        await InsertInboxAsync(inboxB, TenantId, "Inbox B", "facebook", $"page-{inboxB:N}", null);
        await InsertMemberAsync(inboxA, TenantId, AdminId);
        await InsertMemberAsync(inboxB, TenantId, AdminId);
        await InsertConversationAsync(conversationAFormerOwner, inboxA, AdminId);
        await InsertConversationAsync(conversationAOtherUser, inboxA, replacementId);
        await InsertConversationAsync(conversationBFormerOwner, inboxB, AdminId);

        var response = await _inboxAdminClient.PutAsJsonAsync($"/api/admin/inboxes/{inboxA}/members", new { agentId = replacementId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadInboxOwnerAsync(inboxA)).Should().Be(replacementId);
        (await ReadInboxOwnerAsync(inboxB)).Should().Be(AdminId);
        (await ReadConversationAssigneeAsync(conversationAFormerOwner)).Should().BeNull();
        (await ReadConversationAssigneeAsync(conversationAOtherUser)).Should().Be(replacementId);
        (await ReadConversationAssigneeAsync(conversationBFormerOwner)).Should().Be(AdminId);
    }

    [Fact]
    public async Task Exact_unlink_removes_only_requested_owner_and_keeps_inbox_active()
    {
        var otherUserId = Guid.NewGuid();
        var inboxA = Guid.NewGuid();
        var inboxB = Guid.NewGuid();
        var conversationAFormerOwner = Guid.NewGuid();
        var conversationAOtherUser = Guid.NewGuid();
        var conversationBFormerOwner = Guid.NewGuid();
        await InsertUserAsync(otherUserId, TenantId, "Other User");
        await InsertInboxAsync(inboxA, TenantId, "Unlink A", "facebook", $"page-{inboxA:N}", null);
        await InsertInboxAsync(inboxB, TenantId, "Unlink B", "facebook", $"page-{inboxB:N}", null);
        await InsertMemberAsync(inboxA, TenantId, AdminId);
        await InsertMemberAsync(inboxB, TenantId, AdminId);
        await InsertConversationAsync(conversationAFormerOwner, inboxA, AdminId);
        await InsertConversationAsync(conversationAOtherUser, inboxA, otherUserId);
        await InsertConversationAsync(conversationBFormerOwner, inboxB, AdminId);

        var response = await _inboxAdminClient.DeleteAsync($"/api/admin/inboxes/{inboxA}/members/{AdminId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ReadInboxOwnerAsync(inboxA)).Should().BeNull();
        (await ReadInboxOwnerAsync(inboxB)).Should().Be(AdminId);
        (await InboxExistsAsync(inboxA)).Should().BeTrue();
        (await ReadConversationAssigneeAsync(conversationAFormerOwner)).Should().BeNull();
        (await ReadConversationAssigneeAsync(conversationAOtherUser)).Should().Be(otherUserId);
        (await ReadConversationAssigneeAsync(conversationBFormerOwner)).Should().Be(AdminId);
    }

    [Fact]
    public async Task Exact_unlink_rejects_stale_owner_and_token_manager_permission()
    {
        var ownerId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        await InsertUserAsync(ownerId, TenantId, "Current Owner");
        await InsertInboxAsync(inboxId, TenantId, "Protected unlink", "facebook", $"page-{inboxId:N}", null);
        await InsertMemberAsync(inboxId, TenantId, ownerId);

        var forbidden = await _tokenManagerClient.DeleteAsync($"/api/admin/inboxes/{inboxId}/members/{ownerId}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var stale = await _inboxAdminClient.DeleteAsync($"/api/admin/inboxes/{inboxId}/members/{AdminId}");
        stale.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadInboxOwnerAsync(inboxId)).Should().Be(ownerId);
    }

    [Fact]
    public async Task Exact_unlink_repeated_and_cross_tenant_requests_do_not_mutate_other_data()
    {
        var sameTenantInboxId = Guid.NewGuid();
        var otherTenantInboxId = Guid.NewGuid();
        await InsertTenantAsync(OtherTenantId);
        await InsertInboxAsync(sameTenantInboxId, TenantId, "Repeat unlink", "facebook", $"page-{sameTenantInboxId:N}", null);
        await InsertInboxAsync(otherTenantInboxId, OtherTenantId, "Cross tenant unlink", "facebook", $"page-{otherTenantInboxId:N}", null);
        await InsertMemberAsync(sameTenantInboxId, TenantId, AdminId);
        await InsertMemberAsync(otherTenantInboxId, OtherTenantId, AdminId);

        var first = await _inboxAdminClient.DeleteAsync($"/api/admin/inboxes/{sameTenantInboxId}/members/{AdminId}");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var repeated = await _inboxAdminClient.DeleteAsync($"/api/admin/inboxes/{sameTenantInboxId}/members/{AdminId}");
        repeated.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossTenant = await _inboxAdminClient.DeleteAsync($"/api/admin/inboxes/{otherTenantInboxId}/members/{AdminId}");
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadInboxOwnerAsync(otherTenantInboxId)).Should().Be(AdminId);
    }

    [Fact]
    public async Task Create_user_with_initial_channel_remains_supported()
    {
        var pageId = $"page-create-{Guid.NewGuid():N}";
        var email = $"created-{Guid.NewGuid():N}@integration.clawbot.local";

        var response = await _inboxAdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            displayName = "Created With Channel",
            email,
            password = "TempPass1!",
            roles = Array.Empty<string>(),
            pancakePageId = pageId,
            pancakeChannelName = "Created Channel",
            pancakePlatform = "facebook",
            pancakeAccessToken = "created-channel-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await ReadJsonAsync(response);
        var createdUserId = json.RootElement.GetProperty("id").GetGuid();
        var inboxId = await ReadInboxIdByPageAsync(pageId);
        inboxId.Should().NotBeNull();
        (await ReadInboxOwnerAsync(inboxId!.Value)).Should().Be(createdUserId);
    }

    [Fact]
    public async Task Existing_user_can_add_second_channel_without_removing_first()
    {
        var firstInboxId = Guid.NewGuid();
        var secondPageId = $"page-second-{Guid.NewGuid():N}";
        await InsertInboxAsync(firstInboxId, TenantId, "First Channel", "facebook", $"page-first-{Guid.NewGuid():N}", null);
        await InsertMemberAsync(firstInboxId, TenantId, AdminId);

        var response = await _inboxAdminClient.PutAsJsonAsync($"/api/admin/users/{AdminId}", new
        {
            pancakePageId = secondPageId,
            pancakeChannelName = "Second Channel",
            pancakePlatform = "facebook",
            pancakeAccessToken = "second-channel-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var secondInboxId = await ReadInboxIdByPageAsync(secondPageId);
        secondInboxId.Should().NotBeNull();
        (await ReadInboxOwnerAsync(firstInboxId)).Should().Be(AdminId);
        (await ReadInboxOwnerAsync(secondInboxId!.Value)).Should().Be(AdminId);
    }

    private static ClawbotWebApplicationFactory CreateFactory<THandler>(SqlServerFixture sql, string scheme)
        where THandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        return new ClawbotWebApplicationFactory(sql, services =>
        {
            services.AddAuthentication(scheme)
                .AddScheme<AuthenticationSchemeOptions, THandler>(scheme, _ => { });
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new AuthorizationPolicyBuilder(scheme)
                    .RequireAuthenticatedUser()
                    .Build());
        });
    }

    private async Task<(string Name, string? EncryptedToken)> ReadInboxStateAsync(Guid inboxId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, encrypted_access_token FROM inboxes WHERE id = @id";
        command.Parameters.Add(new SqlParameter("@id", inboxId));
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task InsertUserAsync(Guid userId, Guid tenantId, string displayName)
    {
        var email = $"{userId:N}@integration.clawbot.local";
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users
                (id, tenant_id, display_name, email, password_hash, security_stamp, access_failed_count,
                 is_active, created_at, updated_at, user_name, normalized_user_name, normalized_email,
                 email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled)
            VALUES
                (@id, @tenantId, @displayName, @email, 'test-hash', @securityStamp, 0,
                 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), @email, @normalizedEmail, @normalizedEmail,
                 1, 0, 0, 1);
            """;
        command.Parameters.Add(new SqlParameter("@id", userId));
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@displayName", displayName));
        command.Parameters.Add(new SqlParameter("@email", email));
        command.Parameters.Add(new SqlParameter("@normalizedEmail", email.ToUpperInvariant()));
        command.Parameters.Add(new SqlParameter("@securityStamp", Guid.NewGuid().ToString("N")));
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertConversationAsync(Guid id, Guid inboxId, Guid assignedTo)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations
                (id, tenant_id, platform, external_thread_id, status, assigned_to, inbox_id, created_at, updated_at)
            VALUES
                (@id, @tenantId, 'facebook', @threadId, 'open', @assignedTo, @inboxId, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """;
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        command.Parameters.Add(new SqlParameter("@threadId", $"thread-{id:N}"));
        command.Parameters.Add(new SqlParameter("@assignedTo", assignedTo));
        command.Parameters.Add(new SqlParameter("@inboxId", inboxId));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid?> ReadInboxIdByPageAsync(string pageId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM inboxes WHERE tenant_id = @tenantId AND external_page_id = @pageId AND deleted_at IS NULL";
        command.Parameters.Add(new SqlParameter("@tenantId", TenantId));
        command.Parameters.Add(new SqlParameter("@pageId", pageId));
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task<Guid?> ReadInboxOwnerAsync(Guid inboxId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT agent_id FROM inbox_members WHERE inbox_id = @inboxId";
        command.Parameters.Add(new SqlParameter("@inboxId", inboxId));
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task<Guid?> ReadConversationAssigneeAsync(Guid conversationId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT assigned_to FROM conversations WHERE id = @id";
        command.Parameters.Add(new SqlParameter("@id", conversationId));
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task<bool> InboxExistsAsync(Guid inboxId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM inboxes WHERE id = @id AND deleted_at IS NULL";
        command.Parameters.Add(new SqlParameter("@id", inboxId));
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }

    private async Task InsertTenantAsync(Guid tenantId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM tenants WHERE id = @tenantId)
            BEGIN
                INSERT INTO tenants (id, slug, display_name, plan_name, is_active, settings_json, created_at, updated_at)
                VALUES (@tenantId, @slug, @displayName, 'free', 1, '{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            END;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@slug", $"test-{tenantId:N}"));
        command.Parameters.Add(new SqlParameter("@displayName", "Other Test Tenant"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertInboxAsync(
        Guid id,
        Guid tenantId,
        string name,
        string platform,
        string pageId,
        string? encryptedToken,
        bool deleted = false)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inboxes
                (id, tenant_id, name, platform, external_page_id, is_active, created_at, updated_at, deleted_at, encrypted_access_token)
            VALUES
                (@id, @tenantId, @name, @platform, @pageId, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), @deletedAt, @encryptedToken);
            """;
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        command.Parameters.Add(new SqlParameter("@name", name));
        command.Parameters.Add(new SqlParameter("@platform", platform));
        command.Parameters.Add(new SqlParameter("@pageId", pageId));
        command.Parameters.Add(new SqlParameter("@deletedAt", deleted ? DateTimeOffset.UtcNow : DBNull.Value));
        command.Parameters.Add(new SqlParameter("@encryptedToken", encryptedToken ?? (object)DBNull.Value));
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertMemberAsync(Guid inboxId, Guid tenantId, Guid agentId)
    {
        await using var connection = await _sql.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inbox_members (inbox_id, agent_id, tenant_id)
            VALUES (@inboxId, @agentId, @tenantId);
            """;
        command.Parameters.Add(new SqlParameter("@inboxId", inboxId));
        command.Parameters.Add(new SqlParameter("@agentId", agentId));
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
