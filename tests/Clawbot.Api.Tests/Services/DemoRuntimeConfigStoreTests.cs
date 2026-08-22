using Clawbot.Api.Services;
using Clawbot.SharedKernel.Demo;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests.Services;

/// <summary>
/// Toàn bộ test nằm trong MỘT class vì store đọc biến môi trường process-wide lúc khởi tạo;
/// tách class ra sẽ chạy song song và đè biến môi trường của nhau.
/// </summary>
public sealed class DemoRuntimeConfigStoreTests
{
    private static DemoRuntimeConfigStore CreateStore() =>
        new(Options.Create(new DemoOptions()));

    private static DemoRuntimeConfigStore CreateStoreWithEnv(
        string? token,
        string? secret,
        string? pageToken,
        string? pageId)
    {
        var previous = ReadEnv();
        try
        {
            WriteEnv(token, secret, pageToken, pageId);
            return CreateStore();
        }
        finally
        {
            WriteEnv(previous.Token, previous.Secret, previous.PageToken, previous.PageId);
        }
    }

    private static (string? Token, string? Secret, string? PageToken, string? PageId) ReadEnv() => (
        Environment.GetEnvironmentVariable("PANCAKE_ACCESS_TOKEN"),
        Environment.GetEnvironmentVariable("PANCAKE_WEBHOOK_SECRET"),
        Environment.GetEnvironmentVariable("PANCAKE_PAGE_ACCESS_TOKEN"),
        Environment.GetEnvironmentVariable("PANCAKE_PAGE_ID"));

    private static void WriteEnv(string? token, string? secret, string? pageToken, string? pageId)
    {
        Environment.SetEnvironmentVariable("PANCAKE_ACCESS_TOKEN", token);
        Environment.SetEnvironmentVariable("PANCAKE_WEBHOOK_SECRET", secret);
        Environment.SetEnvironmentVariable("PANCAKE_PAGE_ACCESS_TOKEN", pageToken);
        Environment.SetEnvironmentVariable("PANCAKE_PAGE_ID", pageId);
    }

    [Fact]
    public void Constructor_SeedsFromEnvironmentVariables()
    {
        var store = CreateStoreWithEnv("env-token", "env-secret", "env-page-token", "env-page-id");

        var config = store.Get();

        config.PancakeAccessToken.Should().Be("env-token");
        config.PancakeWebhookSecret.Should().Be("env-secret");
        config.PancakePageAccessToken.Should().Be("env-page-token");
        config.PancakePageId.Should().Be("env-page-id");
    }

    [Fact]
    public void Constructor_NoEnvironment_LeavesConfigUnset()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        var config = store.Get();

        config.IsTokenConfigured.Should().BeFalse();
        config.IsSecretConfigured.Should().BeFalse();
        config.IsPageTokenConfigured.Should().BeFalse();
    }

    [Fact]
    public void Get_ReturnsSnapshotNotLiveReference()
    {
        // Sửa bản trả về không được ảnh hưởng state trong store.
        var store = CreateStoreWithEnv(null, null, null, null);
        store.UpdateToken("original");

        var snapshot = store.Get();
        snapshot.PancakeAccessToken = "mutated";

        store.Get().PancakeAccessToken.Should().Be("original");
    }

    [Fact]
    public void UpdateToken_ReplacesAccessToken()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdateToken("new-token");

        store.Get().PancakeAccessToken.Should().Be("new-token");
    }

    [Fact]
    public void UpdatePageAccessToken_ReplacesPageToken()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdatePageAccessToken("page-token");

        store.Get().PancakePageAccessToken.Should().Be("page-token");
    }

    [Fact]
    public void UpdateAutoReplyText_ReplacesText()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdateAutoReplyText("Chào bạn nhé");

        store.Get().EffectiveAutoReplyText.Should().Be("Chào bạn nhé");
    }

    [Fact]
    public void UpdateAutoReplyText_Null_FallsBackToDefaultText()
    {
        var store = CreateStoreWithEnv(null, null, null, null);
        store.UpdateAutoReplyText("tạm");

        store.UpdateAutoReplyText(null);

        store.Get().EffectiveAutoReplyText.Should().Contain("Cảm ơn");
    }

    [Fact]
    public void UpdatePageId_ReplacesPageId()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdatePageId("page-99");

        store.Get().PancakePageId.Should().Be("page-99");
    }

    [Fact]
    public void UpdateBaseUrl_ReplacesBaseUrl()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdateBaseUrl("https://pages.fm/api/v1");

        store.Get().PancakeBaseUrl.Should().Be("https://pages.fm/api/v1");
    }

    [Fact]
    public void UpdateSecret_ReplacesWebhookSecret()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.UpdateSecret("s3cr3t");

        store.Get().PancakeWebhookSecret.Should().Be("s3cr3t");
    }

    [Fact]
    public void Override_ReplacesEveryField()
    {
        var store = CreateStoreWithEnv(null, null, null, null);

        store.Override(new DemoRuntimeConfig
        {
            PancakeAccessToken = "t",
            PancakeWebhookSecret = "s",
            PancakePageId = "p",
            PancakeBaseUrl = "https://base",
            PancakePageAccessToken = "pt",
            AutoReplyText = "reply",
        });

        var config = store.Get();
        config.PancakeAccessToken.Should().Be("t");
        config.PancakeWebhookSecret.Should().Be("s");
        config.PancakePageId.Should().Be("p");
        config.PancakeBaseUrl.Should().Be("https://base");
        config.PancakePageAccessToken.Should().Be("pt");
        config.AutoReplyText.Should().Be("reply");
    }

    [Fact]
    public void Override_WithEmptyConfig_ClearsPreviousValues()
    {
        var store = CreateStoreWithEnv(null, null, null, null);
        store.UpdateToken("t");
        store.UpdateSecret("s");

        store.Override(new DemoRuntimeConfig());

        store.Get().IsTokenConfigured.Should().BeFalse();
        store.Get().IsSecretConfigured.Should().BeFalse();
    }
}
