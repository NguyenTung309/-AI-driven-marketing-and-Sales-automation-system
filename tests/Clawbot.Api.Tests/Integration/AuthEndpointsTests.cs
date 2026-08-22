using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Api.Contracts.Auth;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /auth — login/refresh/reset/2fa/me. SMTP không cấu hình trong test host nên reset-request
/// no-op an toàn (SmtpEmailSender log rồi return). OTP không lộ ra HTTP response nên các luồng
/// reset/2FA end-to-end (verify OTP thật, xác nhận mã 2FA thật) không test được qua HTTP —
/// chỉ phủ nhánh input sai / chưa đăng nhập.
/// </summary>
public sealed class AuthEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AuthEndpointsTests(ApiTestFactory factory) => _factory = factory;

    // ------------------------------------------------------------------
    // POST /auth/login
    // ------------------------------------------------------------------

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAccessTokenAndRefreshCookie()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(ApiTestFactory.AdminEmail, ApiTestFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("refresh_token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(ApiTestFactory.AdminEmail, "mat-khau-sai-hoan-toan"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new LoginRequest($"khong-ton-tai-{Guid.NewGuid():N}@test.local", "bat-ky"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /auth/login/2fa
    // ------------------------------------------------------------------

    [Fact]
    public async Task LoginWithTwoFactor_WrongPassword_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login/2fa", UriKind.Relative),
            new TwoFactorLoginRequest(ApiTestFactory.AdminEmail, "mat-khau-sai", "000000"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginWithTwoFactor_CorrectPasswordWrongCode_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        // Mật khẩu đúng nhưng tài khoản admin gốc chưa bật 2FA -> VerifyTwoFactorTokenAsync fail -> 401.
        var response = await client.PostAsJsonAsync(new Uri("/auth/login/2fa", UriKind.Relative),
            new TwoFactorLoginRequest(ApiTestFactory.AdminEmail, ApiTestFactory.AdminPassword, "000000"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /auth/refresh + /auth/logout
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_ValidCookie_RotatesAndIssuesNewAccessToken()
    {
        using var handler = _factory.Server.CreateHandler();
        using var cookieClient = new HttpClient(new CookieCapturingHandler(_factory))
        {
            BaseAddress = _factory.Server.BaseAddress,
        };

        var login = await cookieClient.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(ApiTestFactory.AdminEmail, ApiTestFactory.AdminPassword));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await cookieClient.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content: null);

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await refresh.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Logout_WithoutCookie_ReturnsNoContent()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------
    // POST /auth/reset/request + /auth/reset/confirm
    // ------------------------------------------------------------------

    [Fact]
    public async Task RequestReset_UnknownEmail_StillReturnsOk_ToAvoidEnumeration()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/reset/request", UriKind.Relative),
            new PasswordResetRequest($"khong-ton-tai-{Guid.NewGuid():N}@test.local"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequestReset_KnownEmail_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/reset/request", UriKind.Relative),
            new PasswordResetRequest(ApiTestFactory.AdminEmail));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfirmReset_UnknownEmail_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/reset/confirm", UriKind.Relative),
            new PasswordResetConfirm($"khong-ton-tai-{Guid.NewGuid():N}@test.local", "token-bat-ky", "MatKhauMoi-1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_token");
    }

    [Fact]
    public async Task ConfirmReset_ExpiredOrUnknownOtp_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/reset/confirm", UriKind.Relative),
            new PasswordResetConfirm(ApiTestFactory.AdminEmail, "999999", "MatKhauMoi-1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_or_expired_otp");
    }

    // ------------------------------------------------------------------
    // GET /auth/me
    // ------------------------------------------------------------------

    [Fact]
    public async Task Me_Authenticated_ReturnsClaimsAndPermissions()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/auth/me", UriKind.Relative));

        body.GetProperty("tenant_slug").GetString().Should().Be("default");
        body.GetProperty("role").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("permissions").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Me_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // POST /auth/change-password
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/auth/change-password", UriKind.Relative),
            new ChangePasswordRequest("mat-khau-hien-tai-sai", "MatKhauMoi-1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/change-password", UriKind.Relative),
            new ChangePasswordRequest("bat-ky", "MatKhauMoi-1!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // 2FA enable/verify/disable — chỉ nhánh input sai vì OTP thật không lộ qua HTTP
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnableTwoFactor_Authenticated_ReturnsSharedKeyAndUri()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/auth/2fa/enable", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TwoFactorEnableResponse>();
        body!.SharedKey.Should().NotBeNullOrWhiteSpace();
        body.AuthenticatorUri.Should().StartWith("otpauth://totp/");
    }

    [Fact]
    public async Task EnableTwoFactor_Unauthenticated_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri("/auth/2fa/enable", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyTwoFactor_WrongCode_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsync(new Uri("/auth/2fa/enable", UriKind.Relative), content: null);

        var response = await client.PostAsJsonAsync(new Uri("/auth/2fa/verify", UriKind.Relative),
            new TwoFactorVerifyRequest("000000"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DisableTwoFactor_Authenticated_ReturnsOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/auth/2fa/disable", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Handler giữ cookie qua nhiều request để test rotation refresh_token cần cookie login.</summary>
    private sealed class CookieCapturingHandler(ApiTestFactory factory) : DelegatingHandler(factory.Server.CreateHandler())
    {
        private readonly List<string> _cookies = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookies.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies));

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var raw in setCookies)
                {
                    var pair = raw.Split(';', 2)[0];
                    _cookies.RemoveAll(c => c.StartsWith(pair.Split('=')[0] + "=", StringComparison.Ordinal));
                    _cookies.Add(pair);
                }
            }

            return response;
        }
    }
}
