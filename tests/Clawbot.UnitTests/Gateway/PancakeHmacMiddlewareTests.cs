using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Clawbot.Gateway.Configuration;
using Clawbot.Gateway.Middleware;
using Serilog;

namespace Clawbot.UnitTests.Gateway;

public class PancakeHmacMiddlewareTests
{
    private const string TestSecret = "test-webhook-secret";
    private const string TestBody = "{\"test\": \"data\"}";

    [Fact]
    public async Task ValidHmacSignature_CallsNext()
    {
        // Arrange
        var signature = ComputeHmac(TestSecret, Encoding.UTF8.GetBytes(TestBody));
        var builder = CreateTestApp(requiresHmac: true);
        
        var client = new TestClient(builder);

        // Act
        var response = await client.PostAsync("/webhook/test",
            new StringContent(TestBody, Encoding.UTF8, "application/json"),
            new Dictionary<string, string>
            {
                { "X-Pancake-Signature", signature }
            });

        // Assert
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidHmacSignature_Returns401()
    {
        // Arrange
        var builder = CreateTestApp(requiresHmac: true);
        var client = new TestClient(builder);

        // Act
        var response = await client.PostAsync("/webhook/test",
            new StringContent(TestBody, Encoding.UTF8, "application/json"),
            new Dictionary<string, string>
            {
                { "X-Pancake-Signature", "invalid-signature" }
            });

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingSignatureHeader_Returns401()
    {
        // Arrange
        var builder = CreateTestApp(requiresHmac: true);
        var client = new TestClient(builder);

        // Act
        var response = await client.PostAsync("/webhook/test",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RouteWithoutRequiresHmac_SkipsValidation()
    {
        // Arrange
        var builder = CreateTestApp(requiresHmac: false);
        var client = new TestClient(builder);

        // Act
        var response = await client.PostAsync("/api/test",
            new StringContent(TestBody, Encoding.UTF8, "application/json"));

        // Assert
        Assert.NotEqual(StatusCodes.Status401Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Helper to compute HMAC-SHA256 signature
    /// </summary>
    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(body);
        return Convert.ToHexString(hashBytes).ToLower();
    }

    /// <summary>
    /// Helper to create a test application with minimal setup
    /// </summary>
    private static WebApplicationBuilder CreateTestApp(bool requiresHmac)
    {
        var builder = WebApplication.CreateBuilder();

        // Configure gateway options
        var gatewayOptions = new GatewayOptions
        {
            Pancake = new PancakeOptions { WebhookSecret = TestSecret }
        };

        builder.Services.Configure<GatewayOptions>(options =>
        {
            options.Pancake = gatewayOptions.Pancake;
            options.RateLimit = gatewayOptions.RateLimit;
        });

        return builder;
    }

    /// <summary>
    /// Simple HTTP client wrapper for testing middleware
    /// </summary>
    private class TestClient
    {
        private readonly TestServer _server;
        private readonly HttpClient _httpClient;

        public TestClient(WebApplicationBuilder builder)
        {
            var app = builder.Build();

            app.UseMiddleware<TraceIdMiddleware>();
            app.UseMiddleware<PancakeHmacMiddleware>();

            app.MapPost("/webhook/{**catch-all}", async context =>
            {
                await context.Response.WriteAsync("OK");
            }).WithMetadata(new object[] { new { RequiresHmac = true } });

            app.MapPost("/api/{**catch-all}", async context =>
            {
                await context.Response.WriteAsync("OK");
            });

            _server = new TestServer(app);
            _httpClient = _server.CreateClient();
        }

        public async Task<HttpResponseMessage> PostAsync(string path, HttpContent content,
            Dictionary<string, string>? headers = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = content
            };

            if (headers != null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.Add(key, value);
                }
            }

            return await _httpClient.SendAsync(request);
        }
    }
}
