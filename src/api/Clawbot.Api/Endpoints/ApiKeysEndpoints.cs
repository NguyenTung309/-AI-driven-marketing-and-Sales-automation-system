using System.Security.Cryptography;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Security;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ApiKeysEndpoints
{
    public static IEndpointRouteBuilder MapApiKeys(this IEndpointRouteBuilder app)
    {
        // SPEC-11 §6a: API key management requires api-keys:manage (Admin).
        var group = app.MapGroup("/api/api-keys").RequirePermission("api-keys:manage");

        group.MapGet("/", ListAsync);
        group.MapPost("/", IssueAsync);
        group.MapDelete("/{id:guid}", RevokeAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var keys = await db.ApiKeys
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.CreatedAt, k.ExpiresAt, k.RevokedAt))
            .ToListAsync(ct);
        return Results.Ok(keys);
    }

    private static async Task<IResult> IssueAsync(
        CreateApiKeyRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("name_required");
        var tenantId = tenants.Require().TenantId;

        var plaintext = GenerateKey();
        var hash = Hash(plaintext);
        var key = ApiKey.Issue(tenantId, req.Name, hash, clock.UtcNow, req.ExpiresAt);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/api-keys/{key.Id}",
            new CreateApiKeyResponse(key.Id, key.Name, plaintext, key.ExpiresAt));
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.TenantId == tenantId, ct);
        if (key is null) return Results.NotFound();

        key.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string GenerateKey()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return "cbk_" + Convert.ToBase64String(buffer)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string Hash(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
