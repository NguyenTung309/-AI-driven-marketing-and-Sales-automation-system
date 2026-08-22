using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/inbox/conversations/{id}/labels + /notes. Các handler ghi (attach/detach/create/update/delete)
/// có guard "role có admin:inboxes thì Forbid" — admin bootstrap có perm đó nên mọi write op trả 403;
/// happy path phải chạy bằng user role Sale (có conversations:write, không có admin:inboxes).
/// </summary>
public sealed class InboxLabelsAndNotesEndpointTests : IClassFixture<ApiTestFactory>
{
    private const string SaleEmail = "sale-label-note-test@test.local";
    private const string SalePassword = "Test-Sale-Password-1!";

    private readonly ApiTestFactory _factory;

    public InboxLabelsAndNotesEndpointTests(ApiTestFactory factory) => _factory = factory;

    private static async Task<Guid> DefaultTenantIdAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private static async Task<Guid> SeedConversationAsync(ApiTestFactory factory)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var conversation = Conversation.Open(tenantId, "facebook", "igsc_" + Guid.NewGuid().ToString("N"), clock.UtcNow);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    private static async Task<Guid> SeedLabelAsync(ApiTestFactory factory, string name = "vip")
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var label = Label.Create(tenantId, name, "#ff0000");
        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return label.Id;
    }

    private static async Task<Guid> SeedNoteAsync(ApiTestFactory factory, Guid conversationId, string content = "ghi chú gốc")
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == ApiTestFactory.AdminEmail);
        var note = ConversationNote.Create(tenantId, conversationId, admin.Id, content, admin.DisplayName, "private");
        db.ConversationNotes.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }

    /// <summary>
    /// Seed idempotent user role Sale rồi đăng nhập thật qua /auth/login để lấy JWT mang role_id
    /// của Sale — JWT là nguồn role_id duy nhất (SPEC-11) nên không giả lập claim được.
    /// </summary>
    private static async Task<HttpClient> CreateSaleClientAsync(ApiTestFactory factory)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await userManager.FindByEmailAsync(SaleEmail) is null)
            {
                var tenantId = await DefaultTenantIdAsync(factory);
                var user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserName = SaleEmail,
                    Email = SaleEmail,
                    DisplayName = "Sale Test",
                    IsActive = true,
                };
                var created = await userManager.CreateAsync(user, SalePassword);
                if (!created.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
                db.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = user.Id,
                    RoleId = RbacSeeder.RoleIds[RbacSeeder.Sale],
                });
                await db.SaveChangesAsync();
            }
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(SaleEmail, SalePassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static Guid ReadGuidId(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? Guid.Parse(prop.GetString()!)
            : Guid.Empty;

    // ------------------------------------------------------------------
    // Labels
    // ------------------------------------------------------------------

    [Fact]
    public async Task AttachAndListLabel_RoundTrips_AndDuplicateIsConflict()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var labelId = await SeedLabelAsync(_factory);
        var client = await CreateSaleClientAsync(_factory);
        var baseUrl = $"/api/inbox/conversations/{conversationId}/labels/";

        var attach = await client.PostAsJsonAsync(new Uri(baseUrl, UriKind.Relative), new { labelId });
        attach.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri(baseUrl, UriKind.Relative));
        list.ValueKind.Should().Be(JsonValueKind.Array);
        ReadGuidId(list[0], "id").Should().Be(labelId);

        var duplicate = await client.PostAsJsonAsync(new Uri(baseUrl, UriKind.Relative), new { labelId });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AttachLabel_UnknownLabelOrConversation_ReturnsNotFound()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var client = await CreateSaleClientAsync(_factory);

        var unknownLabel = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{conversationId}/labels/", UriKind.Relative),
            new { labelId = Guid.NewGuid() });
        unknownLabel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknownConversation = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/labels/", UriKind.Relative),
            new { labelId = await SeedLabelAsync(_factory, "khac") });
        unknownConversation.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DetachLabel_RemovesAttachment_AndSecondDetachIsNotFound()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var labelId = await SeedLabelAsync(_factory);
        var client = await CreateSaleClientAsync(_factory);
        var baseUrl = $"/api/inbox/conversations/{conversationId}/labels/";

        (await client.PostAsJsonAsync(new Uri(baseUrl, UriKind.Relative), new { labelId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.DeleteAsync(new Uri($"{baseUrl}{labelId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri(baseUrl, UriKind.Relative));
        list.GetArrayLength().Should().Be(0);

        (await client.DeleteAsync(new Uri($"{baseUrl}{labelId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AttachLabel_AsAdminWithInboxPermission_IsForbidden()
    {
        // Guard trong handler: role có "admin:inboxes" (admin bootstrap) bị cấm attach label.
        var conversationId = await SeedConversationAsync(_factory);
        var labelId = await SeedLabelAsync(_factory);
        var adminClient = await _factory.CreateAuthenticatedClientAsync();

        var response = await adminClient.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{conversationId}/labels/", UriKind.Relative),
            new { labelId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListLabels_UnknownConversation_ReturnsNotFound()
    {
        var client = await CreateSaleClientAsync(_factory);

        var response = await client.GetAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/labels/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Notes
    // ------------------------------------------------------------------

    [Fact]
    public async Task NotesCrud_RoundTrips()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var client = await CreateSaleClientAsync(_factory);
        var baseUrl = $"/api/inbox/conversations/{conversationId}/notes/";

        var created = await client.PostAsJsonAsync(new Uri(baseUrl, UriKind.Relative), new { content = "ghi chú mới", type = "private" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var noteId = ReadGuidId(createdBody, "id");
        noteId.Should().NotBe(Guid.Empty);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri(baseUrl, UriKind.Relative));
        list.GetArrayLength().Should().Be(1);

        (await client.PutAsJsonAsync(new Uri($"{baseUrl}{noteId}", UriKind.Relative), new { content = "đã sửa" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.DeleteAsync(new Uri($"{baseUrl}{noteId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<JsonElement>(new Uri(baseUrl, UriKind.Relative)))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Notes_MissingConversationOrNote_ReturnsNotFound()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var client = await CreateSaleClientAsync(_factory);

        var missingConversation = await client.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{Guid.NewGuid()}/notes/", UriKind.Relative),
            new { content = "không có hội thoại" });
        missingConversation.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var missingNote = await client.PutAsJsonAsync(
            new Uri($"/api/inbox/conversations/{conversationId}/notes/{Guid.NewGuid()}", UriKind.Relative),
            new { content = "không có ghi chú" });
        missingNote.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteMissing = await client.DeleteAsync(
            new Uri($"/api/inbox/conversations/{conversationId}/notes/{Guid.NewGuid()}", UriKind.Relative));
        deleteMissing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateAndDeleteNote_SeededNote_RoundTrips()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var noteId = await SeedNoteAsync(_factory, conversationId);
        var client = await CreateSaleClientAsync(_factory);
        var baseUrl = $"/api/inbox/conversations/{conversationId}/notes/";

        (await client.PutAsJsonAsync(new Uri($"{baseUrl}{noteId}", UriKind.Relative), new { content = "nội dung đã sửa" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.DeleteAsync(new Uri($"{baseUrl}{noteId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateNote_AsAdminWithInboxPermission_IsForbidden()
    {
        var conversationId = await SeedConversationAsync(_factory);
        var adminClient = await _factory.CreateAuthenticatedClientAsync();

        var response = await adminClient.PostAsJsonAsync(
            new Uri($"/api/inbox/conversations/{conversationId}/notes/", UriKind.Relative),
            new { content = "admin không được tạo ghi chú" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
