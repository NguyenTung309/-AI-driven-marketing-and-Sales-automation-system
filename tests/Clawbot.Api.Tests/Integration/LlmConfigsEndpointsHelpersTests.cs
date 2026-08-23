using Clawbot.Api.Endpoints;
using FluentAssertions;
using NSubstitute;
using System.ClientModel;
using System.Net;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần cho các helper internal static của LlmConfigsEndpoints (không cần HTTP host / AppDbContext).
public sealed class LlmConfigsEndpointsHelpersTests
{
    // ── NormalizeBaseUrl ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("openai", "https://api.openai.com", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/v1", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/v1/", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/chat/completions", "https://api.openai.com/v1")]
    [InlineData("openai-compatible", "https://custom.host", "https://custom.host")]
    [InlineData("openai-compatible", "https://custom.host/v1", "https://custom.host/v1")]
    [InlineData("openai-compatible", "https://custom.host/chat/completions", "https://custom.host")]
    [InlineData("openai-compatible", "https://custom.host/v1/chat/completions", "https://custom.host/v1")]
    [InlineData("openai-responses", "https://api.openai.com/v1", "https://api.openai.com/v1")]
    [InlineData("openai-responses", "https://api.openai.com/v1/responses", "https://api.openai.com/v1")]
    [InlineData("openai-responses", "https://api.openai.com/responses", "https://api.openai.com")]
    [InlineData("anthropic", "https://api.anthropic.com", "https://api.anthropic.com")]
    [InlineData("anthropic", "https://api.anthropic.com/v1", "https://api.anthropic.com")]
    [InlineData("anthropic", "https://api.anthropic.com/v1/", "https://api.anthropic.com")]
    [InlineData("other-provider", "https://api.example.com/v1", "https://api.example.com/v1")]
    public void NormalizeBaseUrl_VariousProviders_ReturnsExpected(string provider, string input, string expected)
    {
        LlmConfigsEndpoints.NormalizeBaseUrl(provider, input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeBaseUrl_NullOrEmpty_ReturnsNull(string? input)
    {
        LlmConfigsEndpoints.NormalizeBaseUrl("openai", input).Should().BeNull();
    }

    [Fact]
    public void NormalizeBaseUrl_OpenAiCompatible_BareHostDoesNotForceV1()
    {
        // openai-compatible không ép /v1, khác với "openai"
        LlmConfigsEndpoints.NormalizeBaseUrl("openai-compatible", "https://custom.host")
            .Should().Be("https://custom.host");
        LlmConfigsEndpoints.NormalizeBaseUrl("openai", "https://custom.host")
            .Should().Be("https://custom.host/v1");
    }

    // ── MaskSecret ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "<empty>")]
    [InlineData("", "<empty>")]
    [InlineData("   ", "<empty>")]
    public void MaskSecret_NullOrEmpty_ReturnsEmptyMarker(string? input, string expected)
    {
        LlmConfigsEndpoints.MaskSecret(input).Should().Be(expected);
    }

    [Fact]
    public void MaskSecret_Short_ReturnsMaskedWithLastTwo()
    {
        LlmConfigsEndpoints.MaskSecret("abcdef").Should().Be("***ef");
        LlmConfigsEndpoints.MaskSecret("1234567890").Should().Be("***90"); // đúng 10 ký tự
    }

    [Fact]
    public void MaskSecret_Long_ReturnsHeadEllipsisTail()
    {
        // >10 ký tự: 6 đầu + "..." + 4 cuối
        var secret = "sk-proj-abcdefghijklmnop12345";
        var result = LlmConfigsEndpoints.MaskSecret(secret);
        result.Should().Be("sk-pro...2345");
    }

    [Fact]
    public void MaskSecret_TrimsBeforeMasking()
    {
        LlmConfigsEndpoints.MaskSecret("  abcdef  ").Should().Be("***ef");
    }

    // ── SecretHash ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "<empty>")]
    [InlineData("", "<empty>")]
    [InlineData("   ", "<empty>")]
    public void SecretHash_NullOrEmpty_ReturnsEmptyMarker(string? input, string expected)
    {
        LlmConfigsEndpoints.SecretHash(input).Should().Be(expected);
    }

    [Fact]
    public void SecretHash_SameInput_ReturnsSameTwelveHexChars()
    {
        var a = LlmConfigsEndpoints.SecretHash("sk-abc-123");
        var b = LlmConfigsEndpoints.SecretHash("sk-abc-123");
        a.Should().Be(b);
        a.Should().HaveLength(12);
        a.Should().MatchRegex("^[0-9A-F]{12}$");
    }

    [Fact]
    public void SecretHash_DifferentInputs_ReturnDifferentHashes()
    {
        var a = LlmConfigsEndpoints.SecretHash("sk-aaa");
        var b = LlmConfigsEndpoints.SecretHash("sk-bbb");
        a.Should().NotBe(b);
    }

    [Fact]
    public void SecretHash_TrimsBeforeHashing()
    {
        LlmConfigsEndpoints.SecretHash("  sk-abc  ")
            .Should().Be(LlmConfigsEndpoints.SecretHash("sk-abc"));
    }

    // ── TestConnectionStatus ────────────────────────────────────────────────

    [Fact]
    public void TestConnectionStatus_HttpRequestExceptionWithCode_ReturnsCode()
    {
        var ex = new HttpRequestException("fail", null, HttpStatusCode.Unauthorized);
        LlmConfigsEndpoints.TestConnectionStatus(ex).Should().Be(401);
    }

    [Fact]
    public void TestConnectionStatus_ClientResultException_ReturnsStatus()
    {
        // ClientResultException(string, ClientResult) — status lấy từ response
        var ex = CreateClientResultException(429);
        LlmConfigsEndpoints.TestConnectionStatus(ex).Should().Be(429);
    }

    [Fact]
    public void TestConnectionStatus_OtherException_ReturnsZero()
    {
        LlmConfigsEndpoints.TestConnectionStatus(new InvalidOperationException("x")).Should().Be(0);
        LlmConfigsEndpoints.TestConnectionStatus(new TimeoutException()).Should().Be(0);
    }

    // ── SafeTestConnectionError ─────────────────────────────────────────────

    [Fact]
    public void SafeTestConnectionError_Timeout_AndTaskCanceled_ReturnTimeout()
    {
        LlmConfigsEndpoints.SafeTestConnectionError(new TimeoutException()).Should().Be("llm_connection_timeout");
        LlmConfigsEndpoints.SafeTestConnectionError(new TaskCanceledException()).Should().Be("llm_connection_timeout");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "llm_connection_auth_failed")]
    [InlineData(HttpStatusCode.Forbidden, "llm_connection_auth_failed")]
    [InlineData(HttpStatusCode.TooManyRequests, "llm_connection_rate_limited")]
    [InlineData(HttpStatusCode.BadRequest, "llm_connection_invalid_request")]
    [InlineData(HttpStatusCode.NotFound, "llm_connection_invalid_request")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "llm_connection_invalid_request")]
    public void SafeTestConnectionError_HttpRequestException_MapsCorrectly(HttpStatusCode status, string expected)
    {
        var ex = new HttpRequestException("fail", null, status);
        LlmConfigsEndpoints.SafeTestConnectionError(ex).Should().Be(expected);
    }

    [Fact]
    public void SafeTestConnectionError_HttpRequestExceptionNullStatus_ReturnsUnreachable()
    {
        var ex = new HttpRequestException("fail");
        ex.Data["StatusCode"] = null; // đảm bảo StatusCode == null (mặc định đã null)
        LlmConfigsEndpoints.SafeTestConnectionError(ex).Should().Be("llm_connection_unreachable");
    }

    [Fact]
    public void SafeTestConnectionError_HttpRequestExceptionOtherStatus_ReturnsUpstreamError()
    {
        var ex = new HttpRequestException("fail", null, HttpStatusCode.InternalServerError);
        LlmConfigsEndpoints.SafeTestConnectionError(ex).Should().Be("llm_connection_upstream_error");
    }

    [Theory]
    [InlineData(401, "llm_connection_auth_failed")]
    [InlineData(403, "llm_connection_auth_failed")]
    [InlineData(429, "llm_connection_rate_limited")]
    [InlineData(400, "llm_connection_invalid_request")]
    [InlineData(404, "llm_connection_invalid_request")]
    [InlineData(422, "llm_connection_invalid_request")]
    [InlineData(500, "llm_connection_upstream_error")]
    public void SafeTestConnectionError_ClientResultException_MapsCorrectly(int status, string expected)
    {
        var ex = CreateClientResultException(status);
        LlmConfigsEndpoints.SafeTestConnectionError(ex).Should().Be(expected);
    }

    [Fact]
    public void SafeTestConnectionError_NotSupportedException_ReturnsProviderUnsupported()
    {
        LlmConfigsEndpoints.SafeTestConnectionError(new NotSupportedException("nope"))
            .Should().Be("llm_connection_provider_unsupported");
    }

    [Fact]
    public void SafeTestConnectionError_OtherException_ReturnsTestFailed()
    {
        LlmConfigsEndpoints.SafeTestConnectionError(new InvalidOperationException("x"))
            .Should().Be("llm_connection_test_failed");
        LlmConfigsEndpoints.SafeTestConnectionError(new ArgumentException("x"))
            .Should().Be("llm_connection_test_failed");
    }

    // ── AreBoundAgentModelsCompatible ───────────────────────────────────────

    [Fact]
    public void AreBoundAgentModelsCompatible_AnthropicClaudeModels_ReturnsTrue()
    {
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("anthropic", "claude-3-5-sonnet-20241022", ["claude-3-haiku-20240307"])
            .Should().BeTrue();
    }

    [Fact]
    public void AreBoundAgentModelsCompatible_OpenAiWithClaudeModel_ReturnsFalse()
    {
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("openai", "claude-3-5-sonnet-20241022", [])
            .Should().BeFalse();
    }

    [Fact]
    public void AreBoundAgentModelsCompatible_OpenAiBoundAgentHasClaudeModel_ReturnsFalse()
    {
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("openai", "gpt-4o", ["claude-3-haiku-20240307"])
            .Should().BeFalse();
    }

    [Fact]
    public void AreBoundAgentModelsCompatible_UnknownProvider_AlwaysTrue()
    {
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("custom-llm", "any-model-name", ["another-model"])
            .Should().BeTrue();
    }

    [Fact]
    public void AreBoundAgentModelsCompatible_EmptyBoundModel_FallsBackToConfigModel()
    {
        // model rỗng/whitespace -> dùng configModel để kiểm
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("anthropic", "claude-3-haiku", ["  "])
            .Should().BeTrue();
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible("openai", "claude-3-haiku", ["  "])
            .Should().BeFalse();
    }

    private static ClientResultException CreateClientResultException(int status)
    {
        var response = CreateFakePipelineResponse(status);
        // Ctor thực tế (System.ClientModel 1.10.0): (PipelineResponse response, Exception innerException)
        // hoặc (string message, PipelineResponse response, Exception innerException)
        return new ClientResultException(response, new InvalidOperationException("test"));
    }

    private static System.ClientModel.Primitives.PipelineResponse CreateFakePipelineResponse(int status)
    {
        var sub = NSubstitute.Substitute.For<System.ClientModel.Primitives.PipelineResponse>();
        sub.Status.Returns(status);
        return sub;
    }
}
