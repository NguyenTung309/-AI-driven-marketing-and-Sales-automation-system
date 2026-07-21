using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Integrations;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record MetaConnectResponse(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record UpdateMetaAppConfigurationRequest(
    string AppId,
    string? AppSecret,
    string ConfigurationId,
    string? AuthorizationMode,
    string? WebhookVerifyToken,
    string RedirectUri,
    string FrontendReturnUrl);

public sealed record MetaIntegrationStatusResponse(
    bool Configured,
    bool BusinessWebhookConfigured,
    MetaAppConfigurationSnapshot AppConfiguration,
    bool Connected,
    string Status,
    string ClientBusinessId,
    string SystemUserId,
    string TokenType,
    IReadOnlyList<string> GrantedScopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DataAccessExpiresAt,
    DateTimeOffset? LastValidatedAt,
    string? LastError,
    IReadOnlyList<MetaAssetSnapshot> Assets);

public static partial class AdminMetaIntegrationEndpoints
{
    private static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(10);
    private const int MaxIdentifierChars = 256;
    private const int MaxSecretChars = 2048;
    private const int MaxUrlChars = 2048;

    public static IEndpointRouteBuilder MapAdminMetaIntegration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/meta")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("", StatusAsync).RequirePermission("system:config");
        group.MapPut("/config", UpdateConfigurationAsync).RequirePermission("system:config");
        group.MapPost("/connect", ConnectAsync).RequirePermission("system:config");
        group.MapPost("/sync", SyncAsync).RequirePermission("system:config");
        group.MapPost("/validate", ValidateAsync).RequirePermission("system:config");
        group.MapPut("/assets/{assetId:guid}/default", SetDefaultAsync).RequirePermission("system:config");
        group.MapDelete("", DisconnectAsync).RequirePermission("system:config");

        app.MapGet("/api/admin/meta/callback", CallbackAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        return app;
    }

    private static async Task<IResult> StatusAsync(
        ITenantAccessor tenants,
        IMetaIntegrationService integrations,
        IMetaAppConfigurationService configurations,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var connection = await integrations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false);
        var configuration = await configurations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(configuration, connection));
    }

    private static async Task<IResult> UpdateConfigurationAsync(
        UpdateMetaAppConfigurationRequest body,
        ITenantAccessor tenants,
        IMetaAppConfigurationService configurations,
        IMetaIntegrationService integrations,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var authorizationMode = MetaAuthorizationModes.NormalizeOrDefault(body.AuthorizationMode);
        if (!MetaAuthorizationModes.IsSupported(authorizationMode))
        {
            return Error(http, StatusCodes.Status400BadRequest, "meta.authorization_mode_invalid", "Chế độ kết nối Meta không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(body.AppId)
            || string.IsNullOrWhiteSpace(body.ConfigurationId)
            || string.IsNullOrWhiteSpace(body.RedirectUri)
            || string.IsNullOrWhiteSpace(body.FrontendReturnUrl))
        {
            return Error(http, StatusCodes.Status400BadRequest, "meta.config_required", "App ID, Configuration ID và các URL callback là bắt buộc.");
        }
        if (TooLong(body.AppId, MaxIdentifierChars)
            || TooLong(body.ConfigurationId, MaxIdentifierChars)
            || TooLong(body.AuthorizationMode, MaxIdentifierChars)
            || TooLong(body.AppSecret, MaxSecretChars)
            || TooLong(body.WebhookVerifyToken, MaxSecretChars)
            || TooLong(body.RedirectUri, MaxUrlChars)
            || TooLong(body.FrontendReturnUrl, MaxUrlChars))
        {
            return Error(http, StatusCodes.Status400BadRequest, "meta.config_too_long", "Một hoặc nhiều giá trị cấu hình Meta vượt quá độ dài cho phép.");
        }
        if (!IsAllowedUrl(body.RedirectUri, requireCallbackPath: true)
            || !IsAllowedUrl(body.FrontendReturnUrl, requireCallbackPath: false))
        {
            return Error(http, StatusCodes.Status400BadRequest, "meta.config_url_invalid", "URL phải dùng HTTPS; môi trường local được phép dùng HTTP loopback. OAuth callback phải kết thúc bằng /api/admin/meta/callback.");
        }

        try
        {
            var tenantId = tenants.Require().TenantId;
            var result = await configurations.UpdateAsync(
                tenantId,
                new MetaAppConfigurationUpdate(
                    body.AppId,
                    body.AppSecret,
                    body.ConfigurationId,
                    authorizationMode,
                    body.WebhookVerifyToken,
                    body.RedirectUri,
                    body.FrontendReturnUrl),
                ct).ConfigureAwait(false);
            if (result.AuthorizationChanged)
            {
                await integrations.MarkReconnectRequiredAsync(
                    tenantId,
                    "meta_app_configuration_changed",
                    ct).ConfigureAwait(false);
            }
            return Results.Ok(result.Snapshot);
        }
        catch (InvalidOperationException)
        {
            return Error(http, StatusCodes.Status400BadRequest, "meta.config_incomplete", "Hãy nhập App Secret khi lưu cấu hình Meta lần đầu.");
        }
    }

    private static async Task<IResult> ConnectAsync(
        ClaimsPrincipal user,
        AppDbContext db,
        ITenantAccessor tenants,
        IMetaGraphClient graph,
        IMetaAppConfigurationService configurations,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = CurrentUserId(user);
        if (!userId.HasValue)
            return Error(http, StatusCodes.Status400BadRequest, "meta.user_missing", "Không xác định được người dùng đang kết nối Meta.");

        var tenant = tenants.Require();
        var configuration = await configurations.GetSnapshotAsync(tenant.TenantId, ct).ConfigureAwait(false);
        if (!configuration.Configured)
            return Error(http, StatusCodes.Status409Conflict, "meta.app_not_configured", "Hãy lưu đầy đủ cấu hình Meta App trên giao diện trước khi kết nối.");

        var now = clock.UtcNow;
        var expired = await db.MetaOAuthStates
            .Where(x => x.ExpiresAt <= now || x.ConsumedAt != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (expired.Count > 0)
            db.MetaOAuthStates.RemoveRange(expired);

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = now.Add(OAuthStateLifetime);
        db.MetaOAuthStates.Add(MetaOAuthState.Create(tenant.TenantId, userId.Value, HashState(state), expiresAt, now));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var authorizationUrl = await graph.BuildAuthorizationUrlAsync(tenant.TenantId, state, ct).ConfigureAwait(false);
        return Results.Ok(new MetaConnectResponse(authorizationUrl, expiresAt));
    }

    private static async Task<IResult> CallbackAsync(
        [FromQuery] string? state,
        [FromQuery] string? code,
        [FromQuery(Name = "error")] string? oauthError,
        AppDbContext db,
        IMetaIntegrationService integrations,
        IMetaGraphConfigurationResolver configurations,
        IClock clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state))
            return Results.BadRequest(new { code = "meta.oauth_state_missing", message = "Thiếu mã xác thực OAuth state." });

        var now = clock.UtcNow;
        var row = await db.MetaOAuthStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StateHash == HashState(state), ct)
            .ConfigureAwait(false);
        if (row is null)
            return Results.BadRequest(new { code = "meta.oauth_state_invalid", message = "OAuth state không hợp lệ hoặc đã hết hạn." });

        var consumed = await db.MetaOAuthStates
            .IgnoreQueryFilters()
            .Where(x => x.Id == row.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConsumedAt, now),
                ct)
            .ConfigureAwait(false);
        if (consumed != 1)
            return Results.BadRequest(new { code = "meta.oauth_state_invalid", message = "OAuth state không hợp lệ hoặc đã hết hạn." });

        var configuration = await configurations.ResolveAsync(row.TenantId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(oauthError))
            return Results.Redirect(ReturnUrl(configuration, "error", "authorization_denied"));
        if (string.IsNullOrWhiteSpace(code))
            return Results.Redirect(ReturnUrl(configuration, "error", "code_missing"));

        try
        {
            await integrations.CompleteAuthorizationAsync(row.TenantId, code, ct).ConfigureAwait(false);
            return Results.Redirect(ReturnUrl(configuration, "connected", null));
        }
        catch (MetaGraphException ex)
        {
            return Results.Redirect(ReturnUrl(configuration, "error", SafeReason(ex)));
        }
        catch (InvalidOperationException)
        {
            return Results.Redirect(ReturnUrl(configuration, "error", "app_not_configured"));
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            LogCallbackFailed(loggerFactory.CreateLogger("MetaOAuthCallback"), row.TenantId, ex);
            return Results.Redirect(ReturnUrl(configuration, "error", "connection_failed"));
        }
    }

    private static async Task<IResult> SyncAsync(
        ITenantAccessor tenants,
        IMetaIntegrationService integrations,
        IMetaGraphConfigurationResolver configurations,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if (!(await configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false)).IsConfigured)
            return Error(http, StatusCodes.Status409Conflict, "meta.app_not_configured", "Hãy lưu đầy đủ cấu hình Meta App trên giao diện trước khi đồng bộ.");

        try
        {
            await integrations.SyncPagesAsync(tenantId, ct).ConfigureAwait(false);
            return Results.Ok(await integrations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException)
        {
            return Error(http, StatusCodes.Status409Conflict, "meta.connection_missing", "Chưa có kết nối Meta để đồng bộ.");
        }
        catch (MetaGraphException)
        {
            return Error(http, StatusCodes.Status502BadGateway, "meta.sync_failed", "Không thể đồng bộ Facebook Pages từ Meta lúc này.");
        }
    }

    private static async Task<IResult> ValidateAsync(
        ITenantAccessor tenants,
        IMetaIntegrationService integrations,
        IMetaGraphConfigurationResolver configurations,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if (!(await configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false)).IsConfigured)
            return Error(http, StatusCodes.Status409Conflict, "meta.app_not_configured", "Hãy lưu đầy đủ cấu hình Meta App trên giao diện trước khi kiểm tra.");

        try
        {
            await integrations.ValidateAsync(tenantId, ct).ConfigureAwait(false);
            return Results.Ok(await integrations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException)
        {
            return Error(http, StatusCodes.Status409Conflict, "meta.connection_missing", "Chưa có kết nối Meta để kiểm tra.");
        }
        catch (MetaGraphException ex)
        {
            LogValidateFailed(loggerFactory.CreateLogger("MetaValidate"), tenantId, ex.Code, ex.Subcode, ex.HttpStatus, ex.Message, ex);
            // Vẫn trả snapshot (status reconnect_required + lastError) kèm message Graph thật để FE/admin soi được.
            var snapshot = await integrations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false);
            return Results.Json(
                new
                {
                    code = "meta.validation_failed",
                    errorCode = "meta.validation_failed",
                    message = DescribeValidateFailure(ex),
                    metaCode = ex.Code,
                    metaSubcode = ex.Subcode,
                    metaHttpStatus = ex.HttpStatus,
                    metaType = ex.ErrorType,
                    lastError = snapshot.LastError,
                    connection = snapshot,
                    requestId = http.TraceIdentifier,
                },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> SetDefaultAsync(
        Guid assetId,
        ITenantAccessor tenants,
        IMetaIntegrationService integrations,
        HttpContext http,
        CancellationToken ct)
    {
        try
        {
            var tenantId = tenants.Require().TenantId;
            await integrations.SetDefaultPageAsync(tenantId, assetId, ct).ConfigureAwait(false);
            return Results.Ok(await integrations.GetSnapshotAsync(tenantId, ct).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return Error(http, StatusCodes.Status404NotFound, "meta.asset_not_found", "Không tìm thấy Facebook Page đã chọn.");
        }
        catch (InvalidOperationException)
        {
            return Error(http, StatusCodes.Status409Conflict, "meta.asset_cannot_publish", "Kết nối Meta không còn hoạt động hoặc Facebook Page chưa cấp quyền tạo nội dung.");
        }
    }

    private static async Task<IResult> DisconnectAsync(
        ITenantAccessor tenants,
        IMetaIntegrationService integrations,
        CancellationToken ct)
    {
        await integrations.DisconnectAsync(tenants.Require().TenantId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static MetaIntegrationStatusResponse ToResponse(
        MetaAppConfigurationSnapshot configuration,
        MetaIntegrationSnapshot value) =>
        new(
            configuration.Configured,
            configuration.BusinessWebhookConfigured,
            configuration,
            value.Connected,
            value.Status,
            value.ClientBusinessId,
            value.SystemUserId,
            value.TokenType,
            value.GrantedScopes,
            value.ExpiresAt,
            value.DataAccessExpiresAt,
            value.LastValidatedAt,
            value.LastError,
            value.Assets);

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private static string HashState(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();

    private static string ReturnUrl(MetaGraphOptions options, string result, string? reason)
    {
        var baseUrl = Uri.TryCreate(options.FrontendReturnUrl, UriKind.Absolute, out _)
            ? options.FrontendReturnUrl
            : "http://localhost:15876/system";
        var separator = baseUrl.Contains('?') ? '&' : '?';
        var suffix = $"meta={Uri.EscapeDataString(result)}";
        if (!string.IsNullOrWhiteSpace(reason))
            suffix += $"&meta_reason={Uri.EscapeDataString(reason)}";
        return $"{baseUrl}{separator}{suffix}";
    }

    private static bool TooLong(string? value, int maxLength) => value?.Length > maxLength;

    private static bool IsAllowedUrl(string value, bool requireCallbackPath)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;
        var secure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
        return secure
            && (!requireCallbackPath
                || uri.AbsolutePath.EndsWith("/api/admin/meta/callback", StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeReason(MetaGraphException ex) =>
        ex.IsTokenError ? "token_invalid"
        : ex.Message.StartsWith("meta_required_permissions_missing", StringComparison.Ordinal) ? "permissions_missing"
        : ex.Message switch
          {
              "meta_business_system_user_token_required" => "business_system_user_required",
              "meta_user_access_token_required" => "user_access_token_required",
              "meta_oauth_token_invalid" => "token_invalid",
              _ => "connection_failed",
          };

    private static string DescribeValidateFailure(MetaGraphException ex)
    {
        var graphHint = string.IsNullOrWhiteSpace(ex.Message) ? "Meta Graph trả lỗi không rõ." : ex.Message.Trim();
        if (graphHint.Length > 280)
            graphHint = graphHint[..280];

        // "API access blocked" (code 200) = Meta block app/BM/developer — không phải thiếu pages_* scope.
        if (graphHint.Contains("API access blocked", StringComparison.OrdinalIgnoreCase)
            || (ex.Code == 200 && graphHint.Contains("blocked", StringComparison.OrdinalIgnoreCase)))
        {
            return "Meta đang chặn truy cập Graph API (API access blocked). Không phải thiếu quyền Page trong OAuth — "
                + "kiểm tra developers.facebook.com (checkpoint/app restricted), Business Manager Account Quality, "
                + "Business Verification, rồi thử Access Token Debugger. Reconnect OAuth thường không gỡ block này. "
                + $"Chi tiết: {graphHint}";
        }

        // Code hay gặp khi validate:
        // - 190 + subcodes: token hết hạn/thu hồi (thường đã được service mark reconnect, không tới đây)
        // - 1/2/4/17/32: transient / rate limit
        // - 100: param invalid (AppId/AppSecret/config sai)
        // - 200/10: permission (message cổ điển "Requires pages_…")
        if (ex.Code is 1 or 2 or 4 or 17 or 32 || ex.IsTransient)
            return $"Meta tạm thời lỗi/rate-limit (code {ex.Code}). Thử lại sau. Chi tiết: {graphHint}";
        if (ex.Code is 100)
            return $"Cấu hình Meta App không hợp lệ (App ID/Secret/API). Chi tiết: {graphHint}";
        if (ex.Code is 10 or 200 or 294)
            return $"Thiếu quyền Graph (permission). Chi tiết: {graphHint}";
        if (ex.HttpStatus is 401 or 403)
            return $"Meta từ chối xác thực app token (HTTP {ex.HttpStatus}). Kiểm tra App Secret. Chi tiết: {graphHint}";
        return $"Không kiểm tra được kết nối Meta (code {ex.Code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}/{ex.Subcode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}). {graphHint}";
    }

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);

    [LoggerMessage(EventId = 5251, Level = LogLevel.Error, Message = "Meta OAuth callback failed for tenant {TenantId}")]
    private static partial void LogCallbackFailed(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(
        EventId = 5252,
        Level = LogLevel.Error,
        Message = "Meta validate failed for tenant {TenantId}: code={Code} subcode={Subcode} http={HttpStatus} message={Message}")]
    private static partial void LogValidateFailed(
        ILogger logger,
        Guid tenantId,
        int? code,
        int? subcode,
        int? httpStatus,
        string message,
        Exception exception);
}
