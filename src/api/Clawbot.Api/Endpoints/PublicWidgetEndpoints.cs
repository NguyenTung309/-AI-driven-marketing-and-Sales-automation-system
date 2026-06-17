using Clawbot.Agents.Core.Lead;
using Clawbot.Api.Contracts.Tenants;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record WidgetLeadRequest(string Phone, string? DisplayName, string? Email, string? Message);
public sealed record WidgetLeadResponse(Guid ContactId, Guid LeadId, Guid ConversationId, string Reply);
public sealed record WidgetMessageRequest(Guid ConversationId, string Content);
public sealed record WidgetMessageResponse(Guid MessageId, string Reply, DateTimeOffset SentAt);
public sealed record PublicFaqItem(Guid Id, string ModuleCode, string ModuleName, string Question, string Answer);
public sealed record PublicFaqResponse(string TenantSlug, string TenantName, IReadOnlyList<PublicFaqItem> Items, TenantBrandingDto Branding);
public sealed record WidgetBootstrapResponse(
    string TenantSlug,
    string TenantName,
    string SupportName,
    bool Online,
    string Greeting,
    IReadOnlyList<string> SuggestedQuestions,
    TenantBrandingDto Branding);

public static class PublicWidgetEndpoints
{
    private const string Platform = "web";
    private const string SourcePlatform = "web-widget";
    private static readonly string[] SuggestedQuestions =
    [
        "Tư vấn khóa HSK phù hợp",
        "Đặt lịch kiểm tra đầu vào",
        "Nhận học phí và ưu đãi mới nhất",
    ];

    public static IEndpointRouteBuilder MapPublicWidget(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/widget/{tenantSlug}")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/bootstrap", BootstrapAsync);
        group.MapGet("/faq", FaqAsync);
        group.MapPost("/lead", CaptureLeadAsync);
        group.MapPost("/messages", PostMessageAsync);

        return app;
    }

    private static async Task<IResult> BootstrapAsync(string tenantSlug, AppDbContext db, CancellationToken ct)
    {
        var tenant = await ResolveTenantAsync(db, tenantSlug, ct);
        if (tenant is null) return Results.NotFound(new { error = "tenant_not_found" });
        var branding = TenantBrandingService.ToPublicBranding(tenant);

        return Results.Ok(new WidgetBootstrapResponse(
            tenant.Slug,
            branding.BrandName,
            branding.SupportName,
            true,
            branding.WidgetGreeting,
            SuggestedQuestions,
            branding));
    }

    private static async Task<IResult> FaqAsync(string tenantSlug, AppDbContext db, CancellationToken ct)
    {
        var tenant = await ResolveTenantAsync(db, tenantSlug, ct);
        if (tenant is null) return Results.NotFound(new { error = "tenant_not_found" });

        var items = await (
                from testCase in db.KbTestCases.IgnoreQueryFilters()
                join module in db.KbModules.IgnoreQueryFilters() on testCase.KbModuleId equals module.Id
                where module.TenantId == tenant.Id &&
                      module.DeletedAt == null &&
                      module.Status != "archived" &&
                      testCase.IsActive
                orderby module.Code, testCase.CreatedAt
                select new PublicFaqItem(
                    testCase.Id,
                    module.Code,
                    module.Name,
                    testCase.Question,
                    testCase.ExpectedAnswer))
            .Take(24)
            .ToListAsync(ct);

        var branding = TenantBrandingService.ToPublicBranding(tenant);
        return Results.Ok(new PublicFaqResponse(tenant.Slug, branding.BrandName, items, branding));
    }

    private static async Task<IResult> CaptureLeadAsync(
        string tenantSlug,
        WidgetLeadRequest req,
        AppDbContext db,
        ILeadAssignmentService assignment,
        IInboxNotifier notifier,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Phone))
            return Results.BadRequest(new { error = "phone_required" });

        var tenant = await ResolveTenantAsync(db, tenantSlug, ct);
        if (tenant is null) return Results.NotFound(new { error = "tenant_not_found" });

        var now = clock.UtcNow;
        var contact = await FindOrCreateContactAsync(db, tenant.Id, req, now, ct);
        var lead = await FindOrCreateLeadAsync(db, tenant.Id, contact.Id, assignment, now, ct);
        var conversation = await FindOrCreateConversationAsync(db, tenant.Id, contact.Id, now, ct);

        var visitorText = string.IsNullOrWhiteSpace(req.Message)
            ? $"Tôi muốn được tư vấn. Số điện thoại: {req.Phone.Trim()}"
            : req.Message.Trim();
        var inbound = conversation.AppendMessage("in", "visitor", visitorText, "text", now);

        const string reply = "Cảm ơn bạn. Học Bá đã ghi nhận thông tin và đội tư vấn sẽ liên hệ trong thời gian sớm nhất.";
        var outbound = conversation.AppendMessage("out", "bot", reply, "text", now.AddMilliseconds(1));

        await db.SaveChangesAsync(ct);
        await NotifyAsync(notifier, tenant.Id, conversation, inbound, ct);
        await NotifyAsync(notifier, tenant.Id, conversation, outbound, ct);

        return Results.Ok(new WidgetLeadResponse(contact.Id, lead.Id, conversation.Id, reply));
    }

    private static async Task<IResult> PostMessageAsync(
        string tenantSlug,
        WidgetMessageRequest req,
        AppDbContext db,
        IInboxNotifier notifier,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return Results.BadRequest(new { error = "content_required" });

        var tenant = await ResolveTenantAsync(db, tenantSlug, ct);
        if (tenant is null) return Results.NotFound(new { error = "tenant_not_found" });

        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == req.ConversationId && c.TenantId == tenant.Id, ct);
        if (conversation is null) return Results.NotFound(new { error = "conversation_not_found" });

        var now = clock.UtcNow;
        var inbound = conversation.AppendMessage("in", "visitor", req.Content.Trim(), "text", now);
        const string reply = "Mình đã nhận được tin nhắn. Nếu cần tư vấn gấp, bạn vui lòng để lại số điện thoại trong khung chat.";
        var outbound = conversation.AppendMessage("out", "bot", reply, "text", now.AddMilliseconds(1));

        await db.SaveChangesAsync(ct);
        await NotifyAsync(notifier, tenant.Id, conversation, inbound, ct);
        await NotifyAsync(notifier, tenant.Id, conversation, outbound, ct);

        return Results.Ok(new WidgetMessageResponse(outbound.Id, reply, outbound.SentAt));
    }

    private static Task<Tenant?> ResolveTenantAsync(AppDbContext db, string tenantSlug, CancellationToken ct) =>
        db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, ct);

    private static async Task<Contact> FindOrCreateContactAsync(
        AppDbContext db,
        Guid tenantId,
        WidgetLeadRequest req,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var phone = req.Phone.Trim();
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        var displayName = string.IsNullOrWhiteSpace(req.DisplayName) ? $"Khách {phone}" : req.DisplayName.Trim();

        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                (c.Phone == phone || (email != null && c.Email == email)), ct);

        if (contact is null)
        {
            contact = Contact.Create(tenantId, displayName, now);
            db.Contacts.Add(contact);
        }

        SetContactValue(db, contact, nameof(Contact.DisplayName), displayName);
        SetContactValue(db, contact, nameof(Contact.Phone), phone);
        if (email is not null) SetContactValue(db, contact, nameof(Contact.Email), email);
        return contact;
    }

    private static async Task<Lead> FindOrCreateLeadAsync(
        AppDbContext db,
        Guid tenantId,
        Guid contactId,
        ILeadAssignmentService assignment,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var lead = await db.Leads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l =>
                l.TenantId == tenantId &&
                l.ContactId == contactId &&
                l.SourcePlatform == SourcePlatform &&
                l.DeletedAt == null, ct);

        if (lead is not null) return lead;

        lead = Lead.Create(tenantId, contactId, SourcePlatform, now);
        lead.AdjustScore(30, "Web widget lead capture", now);
        var owner = await assignment.PickOwnerAsync(tenantId, ct);
        if (owner.HasValue) lead.Assign(owner.Value);
        db.Leads.Add(lead);
        return lead;
    }

    private static async Task<Conversation> FindOrCreateConversationAsync(
        AppDbContext db,
        Guid tenantId,
        Guid contactId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var conversation = await db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.ContactId == contactId &&
                c.Platform == Platform &&
                c.Status == "open", ct);

        if (conversation is not null) return conversation;

        conversation = Conversation.Open(tenantId, Platform, $"widget:{contactId:N}", now, contactId);
        db.Conversations.Add(conversation);
        return conversation;
    }

    private static void SetContactValue(AppDbContext db, Contact contact, string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        db.Entry(contact).Property(propertyName).CurrentValue = value;
    }

    private static async Task NotifyAsync(
        IInboxNotifier notifier,
        Guid tenantId,
        Conversation conversation,
        Message message,
        CancellationToken ct)
    {
        await notifier.NotifyMessageAsync(tenantId, new InboxMessageEvent(
            conversation.Id,
            message.Id,
            message.Direction,
            message.SenderType,
            message.Content,
            message.ContentType,
            message.SentAt), ct);

        await notifier.NotifyConversationUpdatedAsync(tenantId, new InboxConversationEvent(
            conversation.Id,
            conversation.Status,
            conversation.AssignedTo,
            conversation.LastMessageAt), ct);
    }
}
