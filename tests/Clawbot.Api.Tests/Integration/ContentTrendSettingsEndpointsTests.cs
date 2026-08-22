using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// GET/PUT /api/content/trends/settings. Settings được lưu dưới 1 row social_credentials
/// (provider "trends", mã hoá qua IEncryptor) và lịch quét dưới 1 row AgentSchedules với
/// GoalTemplate marker "[trend-scan]". Test bao phủ mặc định khi chưa có settings, các nhánh
/// validate trả 400 trước khi chạm DB, và vòng PUT hợp lệ rồi đọc lại qua GET.
/// </summary>
public sealed class ContentTrendSettingsEndpointsTests
{
    private const string Endpoint = "/api/content/trends/settings";

    private static async Task<JsonElement> GetSettingsAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri(Endpoint, UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ------------------------------------------------------------------
    // GET mặc định (chưa có settings)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_NoSettingsSeeded_ReturnsDefaults()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var body = await GetSettingsAsync(client);

        body.GetProperty("geo").GetString().Should().Be("VN");
        body.GetProperty("google").GetProperty("enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("youTube").GetProperty("enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("youTube").GetProperty("hasApiKey").GetBoolean().Should().BeFalse();
        body.GetProperty("tikTok").GetProperty("enabled").GetBoolean().Should().BeFalse();
        body.GetProperty("schedule").GetProperty("cadence").GetString().Should().Be("off");
    }

    // ------------------------------------------------------------------
    // PUT validate trước DB
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_GeoNotTwoLetters_ReturnsBadRequest()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { geo = "VNM" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.trend_settings_geo_invalid");
    }

    [Fact]
    public async Task Put_YouTubeApiKeyTooLong_ReturnsBadRequest()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { youTube = new { apiKey = new string('k', 257) } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.trend_settings_key_invalid");
    }

    [Fact]
    public async Task Put_TikTokUrlNotHttps_ReturnsBadRequest()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { tikTok = new { url = "http://www.tiktok.com/@someone" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.trend_settings_url_invalid");
    }

    [Fact]
    public async Task Put_ScheduleCadenceHourly_ReturnsBadRequest()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { scheduleCadence = "hourly" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.trend_settings_cadence_invalid");
    }

    // ------------------------------------------------------------------
    // PUT hợp lệ + đọc lại qua GET
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_ValidUpdate_PersistsAndGetReflectsNewValues()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var putResponse = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new
            {
                geo = "us",
                youTube = new { enabled = true, apiKey = "test-key-123" },
                scheduleCadence = "daily",
            });

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var putBody = await putResponse.Content.ReadFromJsonAsync<JsonElement>();
        putBody.GetProperty("geo").GetString().Should().Be("US");
        putBody.GetProperty("youTube").GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        putBody.GetProperty("schedule").GetProperty("cadence").GetString().Should().Be("daily");
        putBody.GetProperty("schedule").GetProperty("nextRunAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        var getBody = await GetSettingsAsync(client);
        getBody.GetProperty("geo").GetString().Should().Be("US");
        getBody.GetProperty("youTube").GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
        getBody.GetProperty("schedule").GetProperty("cadence").GetString().Should().Be("daily");
    }

    [Fact]
    public async Task Put_CadenceOffAfterEnabled_TurnsScheduleOff()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        // Bật lịch trước để có row AgentSchedules đang active.
        var enableResponse = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { scheduleCadence = "weekly" });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var enabledBody = await enableResponse.Content.ReadFromJsonAsync<JsonElement>();
        enabledBody.GetProperty("schedule").GetProperty("cadence").GetString().Should().Be("weekly");

        var offResponse = await client.PutAsJsonAsync(
            new Uri(Endpoint, UriKind.Relative),
            new { scheduleCadence = "off" });

        offResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var offBody = await offResponse.Content.ReadFromJsonAsync<JsonElement>();
        offBody.GetProperty("schedule").GetProperty("cadence").GetString().Should().Be("off");
        offBody.GetProperty("schedule").GetProperty("nextRunAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
