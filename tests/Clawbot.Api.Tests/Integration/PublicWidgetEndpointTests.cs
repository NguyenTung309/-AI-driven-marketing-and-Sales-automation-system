using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/public/widget/{tenantSlug}: 4 route AllowAnonymous cho web widget khách. Không cần
/// bearer token — dùng CreateClient() trần, không phải ClientAsync() có auth.
/// </summary>
public sealed class PublicWidgetEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> DefaultTenantSlugAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Slug;
    }

    [Fact]
    public async Task Bootstrap_KnownTenant_ReturnsGreetingAndBranding()
    {
        var slug = await DefaultTenantSlugAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/public/widget/{slug}/bootstrap", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<WidgetBootstrapResponse>();
        dto!.TenantSlug.Should().Be(slug);
        dto.Online.Should().BeTrue();
        dto.SuggestedQuestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Bootstrap_UnknownTenant_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/api/public/widget/khong-ton-tai-{Guid.NewGuid():N}/bootstrap", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Faq_ReturnsOnlyActiveTestCasesFromNonArchivedModules()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var now = DateTimeOffset.UtcNow;

        var activeModule = KbModule.Create(tenant.Id, $"MOD-{Guid.NewGuid():N}", "Học phí", now);
        db.KbModules.Add(activeModule);
        var visibleCase = KbTestCase.Create(activeModule.Id, "Học phí bao nhiêu?", "Liên hệ tư vấn viên.", now);
        db.KbTestCases.Add(visibleCase);

        // Câu hỏi inactive không được lộ ra widget công khai.
        var inactiveCaseEntity = KbTestCase.Create(activeModule.Id, "Câu hỏi ẩn", "Không hiển thị.", now);
        db.KbTestCases.Add(inactiveCaseEntity);
        db.Entry(inactiveCaseEntity).Property(nameof(KbTestCase.IsActive)).CurrentValue = false;

        db.SaveChanges();

        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/public/widget/default/faq", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PublicFaqResponse>();
        dto!.Items.Should().Contain(i => i.Id == visibleCase.Id);
        dto.Items.Should().NotContain(i => i.Id == inactiveCaseEntity.Id);
    }

    [Fact]
    public async Task CaptureLead_BlankPhone_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest("   ", "Khách", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CaptureLead_UnknownTenant_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/public/widget/khong-ton-tai-{Guid.NewGuid():N}/lead", UriKind.Relative),
            new WidgetLeadRequest("0900000000", "Khách", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CaptureLead_NewPhone_CreatesContactLeadAndConversation()
    {
        var client = _factory.CreateClient();
        var phone = $"09{Random.Shared.Next(10000000, 99999999)}";

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest(phone, "Khách vãng lai", null, "Tôi cần tư vấn khóa HSK"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<WidgetLeadResponse>();
        dto!.ContactId.Should().NotBe(Guid.Empty);
        dto.LeadId.Should().NotBe(Guid.Empty);
        dto.ConversationId.Should().NotBe(Guid.Empty);
        dto.Reply.Should().NotBeNullOrWhiteSpace();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Contacts.IgnoreQueryFilters().CountAsync(c => c.Id == dto.ContactId)).Should().Be(1);
        (await verifyDb.Leads.IgnoreQueryFilters().CountAsync(l => l.Id == dto.LeadId)).Should().Be(1);
    }

    [Fact]
    public async Task CaptureLead_SamePhoneTwice_ReusesContactAndLead()
    {
        var client = _factory.CreateClient();
        var phone = $"09{Random.Shared.Next(10000000, 99999999)}";

        var first = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest(phone, "Khách vãng lai", null, null));
        var firstDto = await first.Content.ReadFromJsonAsync<WidgetLeadResponse>();

        var second = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest(phone, "Khách vãng lai", null, "Câu hỏi thêm"));
        var secondDto = await second.Content.ReadFromJsonAsync<WidgetLeadResponse>();

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        secondDto!.ContactId.Should().Be(firstDto!.ContactId);
        secondDto.LeadId.Should().Be(firstDto.LeadId);
    }

    [Fact]
    public async Task PostMessage_BlankContent_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/messages", UriKind.Relative),
            new WidgetMessageRequest(Guid.NewGuid(), "   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMessage_UnknownConversation_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/messages", UriKind.Relative),
            new WidgetMessageRequest(Guid.NewGuid(), "Xin chào"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostMessage_ExistingConversation_AppendsMessageAndReplies()
    {
        var client = _factory.CreateClient();
        var phone = $"09{Random.Shared.Next(10000000, 99999999)}";
        var lead = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest(phone, "Khách vãng lai", null, null));
        var leadDto = await lead.Content.ReadFromJsonAsync<WidgetLeadResponse>();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/messages", UriKind.Relative),
            new WidgetMessageRequest(leadDto!.ConversationId, "Cho mình hỏi thêm về học phí"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<WidgetMessageResponse>();
        dto!.MessageId.Should().NotBe(Guid.Empty);
        dto.Reply.Should().NotBeNullOrWhiteSpace();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conversation = await verifyDb.Conversations
            .IgnoreQueryFilters()
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == leadDto.ConversationId);
        conversation.Messages.Should().HaveCountGreaterThan(2, "lead gốc + tin nhắn mới, cả visitor lẫn bot reply");
    }

    [Fact]
    public async Task PostMessage_SnoozedConversation_AppendsMessageWithoutReopening()
    {
        // HÀNH VI HIỆN TẠI: chỉ CaptureLeadAsync gọi ReopenIfNeeded(); PostMessageAsync chỉ
        // append tin nhắn, không đụng tới Status — conversation snoozed vẫn giữ nguyên snoozed.
        var client = _factory.CreateClient();
        var phone = $"09{Random.Shared.Next(10000000, 99999999)}";
        var lead = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/lead", UriKind.Relative),
            new WidgetLeadRequest(phone, "Khách vãng lai", null, null));
        var leadDto = await lead.Content.ReadFromJsonAsync<WidgetLeadResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conversation = await db.Conversations.IgnoreQueryFilters()
                .FirstAsync(c => c.Id == leadDto!.ConversationId);
            conversation.Snooze(DateTimeOffset.UtcNow.AddHours(1));
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            new Uri("/api/public/widget/default/messages", UriKind.Relative),
            new WidgetMessageRequest(leadDto!.ConversationId, "Mình quay lại đây"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stillSnoozed = await verifyDb.Conversations.IgnoreQueryFilters()
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == leadDto.ConversationId);
        stillSnoozed.Status.Should().Be("snoozed");
        stillSnoozed.Messages.Should().Contain(m => m.Content.Contains("quay lại"));
    }
}
