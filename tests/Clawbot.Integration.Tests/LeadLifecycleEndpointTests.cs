using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Integration.Tests;

/// <summary>
/// Runtime WAF tests for lead assign authz + stage/revenue (pre-push §7).
/// Một factory / class — hai factory cùng SQL làm RbacSeeder tracking conflict.
/// </summary>
public sealed class LeadLifecycleAdminEndpointTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private static readonly Guid TenantId = Guid.Parse(TestAuthHandler.TenantId);
    private static readonly Guid AdminUserId = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid SaleUserId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid OtherSaleUserId = Guid.Parse("00000000-0000-0000-0000-0000000000A2");

    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _factory;
    private readonly HttpClient _admin;

    public LeadLifecycleAdminEndpointTests(SqlServerFixture sql)
    {
        _sql = sql;
        _factory = new ClawbotWebApplicationFactory(sql);
        _admin = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await LeadLifecycleTestData.EnsureUsersAndRolesAsync(_sql, SaleUserId, OtherSaleUserId, AdminUserId, TenantId);
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    public void Dispose()
    {
        _admin.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Admin_can_reassign_owned_lead()
    {
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: SaleUserId);

        var resp = await _admin.PostAsJsonAsync($"/api/leads/{leadId}/assign", new { userId = OtherSaleUserId });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var conn = await _sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT owner_user_id FROM leads WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", leadId);
        var owner = (Guid)(await cmd.ExecuteScalarAsync())!;
        owner.Should().Be(OtherSaleUserId);
    }

    [Fact]
    public async Task Assign_rejects_assignee_from_other_tenant()
    {
        var foreignUserId = Guid.Parse("00000000-0000-0000-0000-0000000000FF");
        await LeadLifecycleTestData.EnsureForeignTenantUserAsync(_sql, foreignUserId);
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: null);

        var resp = await _admin.PostAsJsonAsync($"/api/leads/{leadId}/assign", new { userId = foreignUserId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("assignee_not_eligible");
    }

    [Fact]
    public async Task Payment_stage_with_amount_creates_approved_revenue_and_blocks_duplicate()
    {
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: AdminUserId);

        var first = await _admin.PutAsJsonAsync($"/api/leads/{leadId}/stage", new
        {
            stage = "customer",
            reason = "Đã thanh toán",
            amount = 5_000_000m,
            currency = "VND",
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await LeadLifecycleTestData.ReadJsonAsync(first);
        firstJson.RootElement.GetProperty("stage").GetString().Should().Be("customer");

        var revenues = await _admin.GetAsync($"/api/leads/{leadId}/revenues");
        revenues.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await LeadLifecycleTestData.ReadJsonAsync(revenues);
        list.RootElement.GetArrayLength().Should().Be(1);
        list.RootElement[0].GetProperty("status").GetString().Should().Be("approved");
        list.RootElement[0].GetProperty("amount").GetDecimal().Should().Be(5_000_000m);

        var second = await _admin.PutAsJsonAsync($"/api/leads/{leadId}/stage", new
        {
            stage = "customer",
            reason = "retry",
            amount = 6_000_000m,
            currency = "VND",
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("revenue_already_recorded");
    }

    [Fact]
    public async Task Manual_payment_replaces_pending_ai_proposal()
    {
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: AdminUserId);
        await using (var conn = await _sql.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE leads SET stage = N'customer' WHERE id = @leadId;
                INSERT INTO lead_revenues
                    (id, tenant_id, lead_id, amount, currency, source, status, evidence, proposed_by, decided_by, created_at, decided_at)
                VALUES
                    (@id, @tenantId, @leadId, 1000000, N'VND', N'ai', N'pending', N'AI guess', NULL, NULL, SYSDATETIMEOFFSET(), NULL);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("@tenantId", TenantId);
            cmd.Parameters.AddWithValue("@leadId", leadId);
            await cmd.ExecuteNonQueryAsync();
        }

        var pay = await _admin.PostAsJsonAsync($"/api/leads/{leadId}/revenues", new
        {
            amount = 4_200_000m,
            currency = "VND",
        });
        pay.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await LeadLifecycleTestData.ReadJsonAsync(pay);
        created.RootElement.GetProperty("status").GetString().Should().Be("approved");
        created.RootElement.GetProperty("amount").GetDecimal().Should().Be(4_200_000m);

        await using var verify = await _sql.OpenConnectionAsync();
        await using var countCmd = verify.CreateCommand();
        countCmd.CommandText = """
            SELECT
                SUM(CASE WHEN status = N'approved' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status = N'pending' THEN 1 ELSE 0 END),
                SUM(CASE WHEN status = N'rejected' THEN 1 ELSE 0 END)
            FROM lead_revenues WHERE lead_id = @leadId
            """;
        countCmd.Parameters.AddWithValue("@leadId", leadId);
        await using var reader = await countCmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetInt32(1).Should().Be(0);
        reader.GetInt32(2).Should().Be(1);
    }
}

public sealed class LeadLifecycleSaleEndpointTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private static readonly Guid TenantId = Guid.Parse(TestAuthHandler.TenantId);
    private static readonly Guid AdminUserId = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid SaleUserId = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid OtherSaleUserId = Guid.Parse("00000000-0000-0000-0000-0000000000A2");

    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _factory;
    private readonly HttpClient _sale;

    public LeadLifecycleSaleEndpointTests(SqlServerFixture sql)
    {
        _sql = sql;
        _factory = new ClawbotWebApplicationFactory(sql, services =>
        {
            services.AddAuthentication("TestSale")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandlerSale>("TestSale", _ => { });
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("TestSale")
                    .RequireAuthenticatedUser()
                    .Build());
        });
        _sale = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await LeadLifecycleTestData.EnsureUsersAndRolesAsync(_sql, SaleUserId, OtherSaleUserId, AdminUserId, TenantId);
    }

    public Task DisposeAsync()
    {
        _sale.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    public void Dispose()
    {
        _sale.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Sale_can_claim_unowned_lead_for_self_but_not_other_user()
    {
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: null);

        var claimSelf = await _sale.PostAsJsonAsync($"/api/leads/{leadId}/assign", new { userId = SaleUserId });
        claimSelf.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var claimOther = await _sale.PostAsJsonAsync($"/api/leads/{leadId}/assign", new { userId = OtherSaleUserId });
        claimOther.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await claimOther.Content.ReadAsStringAsync()).Should().Contain("can_only_claim_self");
    }

    [Fact]
    public async Task Sale_cannot_reassign_owned_lead_of_another_sale()
    {
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, TenantId, ownerUserId: OtherSaleUserId);

        var resp = await _sale.PostAsJsonAsync($"/api/leads/{leadId}/assign", new { userId = SaleUserId });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("lead_not_owned");
    }
}

/// <summary>Pure SQL — không cần WAF host.</summary>
public sealed class LeadRevenueInvariantSqlTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _sql;

    public LeadRevenueInvariantSqlTests(SqlServerFixture sql) => _sql = sql;

    [Fact]
    public async Task Unique_active_revenue_index_blocks_second_pending()
    {
        var tenantId = Guid.Parse(TestAuthHandler.TenantId);
        await LeadLifecycleTestData.EnsureTenantAsync(_sql, tenantId);
        var leadId = await LeadLifecycleTestData.SeedLeadAsync(_sql, tenantId, ownerUserId: null);

        await using var conn = await _sql.OpenConnectionAsync();
        await using (var prep = conn.CreateCommand())
        {
            prep.CommandText = "UPDATE leads SET stage = N'customer' WHERE id = @id";
            prep.Parameters.AddWithValue("@id", leadId);
            await prep.ExecuteNonQueryAsync();
        }

        await using (var first = conn.CreateCommand())
        {
            first.CommandText = """
                INSERT INTO lead_revenues
                    (id, tenant_id, lead_id, amount, currency, source, status, evidence, proposed_by, decided_by, created_at, decided_at)
                VALUES
                    (@id, @tenantId, @leadId, 1000, N'VND', N'ai', N'pending', NULL, NULL, NULL, SYSDATETIMEOFFSET(), NULL);
                """;
            first.Parameters.AddWithValue("@id", Guid.NewGuid());
            first.Parameters.AddWithValue("@tenantId", tenantId);
            first.Parameters.AddWithValue("@leadId", leadId);
            await first.ExecuteNonQueryAsync();
        }

        Func<Task> secondInsert = async () =>
        {
            await using var second = conn.CreateCommand();
            second.CommandText = """
                INSERT INTO lead_revenues
                    (id, tenant_id, lead_id, amount, currency, source, status, evidence, proposed_by, decided_by, created_at, decided_at)
                VALUES
                    (@id, @tenantId, @leadId, 2000, N'VND', N'ai', N'pending', NULL, NULL, NULL, SYSDATETIMEOFFSET(), NULL);
                """;
            second.Parameters.AddWithValue("@id", Guid.NewGuid());
            second.Parameters.AddWithValue("@tenantId", tenantId);
            second.Parameters.AddWithValue("@leadId", leadId);
            await second.ExecuteNonQueryAsync();
        };

        await secondInsert.Should().ThrowAsync<Exception>();
    }
}

internal static class LeadLifecycleTestData
{
    public static async Task EnsureTenantAsync(SqlServerFixture sql, Guid tenantId)
    {
        await using var conn = await sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM tenants WHERE id = @id)
            INSERT INTO tenants (id, slug, display_name, plan_name, is_active, settings_json, created_at, updated_at)
            VALUES (@id, N'test', N'Test Tenant', N'free', 1, N'{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """;
        cmd.Parameters.AddWithValue("@id", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureUsersAndRolesAsync(
        SqlServerFixture sql,
        Guid saleUserId,
        Guid otherSaleUserId,
        Guid adminUserId,
        Guid tenantId)
    {
        await using var conn = await sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM users WHERE id = @sale)
            INSERT INTO users (id, tenant_id, display_name, email, password_hash, security_stamp, access_failed_count, is_active, created_at, updated_at, user_name, normalized_user_name, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled)
            VALUES (@sale, @tenant, N'Sale One', N'sale1@clawbot.local', N'x', N'stamp', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'sale1@clawbot.local', N'SALE1@CLAWBOT.LOCAL', N'SALE1@CLAWBOT.LOCAL', 1, 0, 0, 1);

            IF NOT EXISTS (SELECT 1 FROM users WHERE id = @other)
            INSERT INTO users (id, tenant_id, display_name, email, password_hash, security_stamp, access_failed_count, is_active, created_at, updated_at, user_name, normalized_user_name, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled)
            VALUES (@other, @tenant, N'Sale Two', N'sale2@clawbot.local', N'x', N'stamp', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'sale2@clawbot.local', N'SALE2@CLAWBOT.LOCAL', N'SALE2@CLAWBOT.LOCAL', 1, 0, 0, 1);

            IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE id = '22222222-2222-2222-2222-222222222222')
                INSERT INTO AspNetRoles (id, name, normalized_name, concurrency_stamp)
                VALUES ('22222222-2222-2222-2222-222222222222', N'Sale', N'SALE', N'stamp');

            IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE id = '11111111-1111-1111-1111-111111111111')
                INSERT INTO AspNetRoles (id, name, normalized_name, concurrency_stamp)
                VALUES ('11111111-1111-1111-1111-111111111111', N'Admin', N'ADMIN', N'stamp');

            IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE user_id = @sale AND role_id = '22222222-2222-2222-2222-222222222222')
                    INSERT INTO AspNetUserRoles (user_id, role_id) VALUES (@sale, '22222222-2222-2222-2222-222222222222');
                IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE user_id = @other AND role_id = '22222222-2222-2222-2222-222222222222')
                    INSERT INTO AspNetUserRoles (user_id, role_id) VALUES (@other, '22222222-2222-2222-2222-222222222222');
                IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE user_id = @admin AND role_id = '11111111-1111-1111-1111-111111111111')
                    INSERT INTO AspNetUserRoles (user_id, role_id) VALUES (@admin, '11111111-1111-1111-1111-111111111111');
            END
            """;
        cmd.Parameters.AddWithValue("@sale", saleUserId);
        cmd.Parameters.AddWithValue("@other", otherSaleUserId);
        cmd.Parameters.AddWithValue("@admin", adminUserId);
        cmd.Parameters.AddWithValue("@tenant", tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureForeignTenantUserAsync(SqlServerFixture sql, Guid foreignUserId)
    {
        var foreignTenant = Guid.Parse("00000000-0000-0000-0000-0000000000FE");
        await using var conn = await sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM tenants WHERE id = @tid)
            INSERT INTO tenants (id, slug, display_name, plan_name, is_active, settings_json, created_at, updated_at)
            VALUES (@tid, N'foreign', N'Foreign', N'free', 1, N'{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

            IF NOT EXISTS (SELECT 1 FROM users WHERE id = @uid)
            INSERT INTO users (id, tenant_id, display_name, email, password_hash, security_stamp, access_failed_count, is_active, created_at, updated_at, user_name, normalized_user_name, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled)
            VALUES (@uid, @tid, N'Foreign Sale', N'foreign@clawbot.local', N'x', N'stamp', 0, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'foreign@clawbot.local', N'FOREIGN@CLAWBOT.LOCAL', N'FOREIGN@CLAWBOT.LOCAL', 1, 0, 0, 1);
            """;
        cmd.Parameters.AddWithValue("@tid", foreignTenant);
        cmd.Parameters.AddWithValue("@uid", foreignUserId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<Guid> SeedLeadAsync(SqlServerFixture sql, Guid tenantId, Guid? ownerUserId)
    {
        var leadId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        await using var conn = await sql.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO contacts (id, tenant_id, display_name, phone, email)
            VALUES (@contactId, @tenantId, N'Test Contact', N'0900000000', N'c@test.com');

            INSERT INTO leads (id, tenant_id, contact_id, owner_user_id, score, stage, source_platform)
            VALUES (@leadId, @tenantId, @contactId, @owner, 40, N'warm', N'facebook');
            """;
        cmd.Parameters.AddWithValue("@contactId", contactId);
        cmd.Parameters.AddWithValue("@leadId", leadId);
        cmd.Parameters.AddWithValue("@tenantId", tenantId);
        cmd.Parameters.AddWithValue("@owner", (object?)ownerUserId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return leadId;
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage resp)
    {
        var stream = await resp.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

/// <summary>Sale role principal — leads:write + Sale role_id (not Admin/SalesLead).</summary>
public sealed class TestAuthHandlerSale(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string UserId = "00000000-0000-0000-0000-0000000000A1";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId),
            new Claim("tenant_id", TestAuthHandler.TenantId),
            new Claim("tenant_slug", "test"),
            new Claim("role_id", "22222222-2222-2222-2222-222222222222"), // Sale
            new Claim("perm", "leads:read"),
            new Claim("perm", "leads:write"),
        };
        var identity = new ClaimsIdentity(claims, "TestSale");
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "TestSale")));
    }
}
