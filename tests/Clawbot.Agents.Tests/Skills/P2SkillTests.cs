using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// ── W2.1 — ClaudeImagePromptGenerator ──
public sealed class ClaudeImagePromptGeneratorTests
{
    [Fact]
    public async Task Generates_prompt_from_claude_response()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(
                """{"prompt":"A modern classroom with warm lighting","negative_prompt":"blurry, low quality","hints":{"composition":"rule of thirds","lighting":"natural","mood":"inspiring"}}""",
                200, 100, 0.003m));

        var sut = new ClaudeImagePromptGenerator(claude);
        var req = new ImagePromptRequest("Chinese language class promotion", "tiktok", "modern", new[] { "ClawBot" });

        var result = await sut.GenerateAsync(req, CancellationToken.None);

        result.Prompt.Should().Contain("classroom");
        result.NegativePrompt.Should().Contain("blurry");
        result.Hints.Should().ContainKey("composition");
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var sut = new ClaudeImagePromptGenerator(claude);

        var act = async () => await sut.GenerateAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void ParseResult_handles_plain_text_fallback()
    {
        var result = ClaudeImagePromptGenerator.ParseResult("just a plain prompt string");

        result.Prompt.Should().Be("just a plain prompt string");
        result.NegativePrompt.Should().BeEmpty();
    }
}

// ── W2.2 — HvcVideoScriptComposer ──
public sealed class HvcVideoScriptComposerTests
{
    [Fact]
    public async Task Composes_hvc_script_from_claude()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(
                """{"hook":"Did you know Chinese is the easiest language to start?","value":"Our 3-month course gets you to HSK3","cta":"Sign up today — link in bio","shot_list":["Close-up of student writing","Teacher explaining tones","Group conversation practice"]}""",
                150, 80, 0.002m));

        var sut = new HvcVideoScriptComposer(claude);
        var req = new VideoScriptRequest("Chinese learning", "tiktok", 30, "young professionals");

        var result = await sut.ComposeAsync(req, CancellationToken.None);

        result.Hook.Should().Contain("Chinese");
        result.Value.Should().Contain("HSK3");
        result.Cta.Should().Contain("Sign up");
        result.ShotList.Should().HaveCount(3);
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var sut = new HvcVideoScriptComposer(claude);

        var act = async () => await sut.ComposeAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

// ── W2.3 — ClaudeViZhTranslator ──
public sealed class ClaudeViZhTranslatorTests
{
    [Fact]
    public async Task Translates_and_extracts_glossary_hits()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(
                """{"translated":"我们提供HSK考试准备课程","glossary_hits":["HSK","考试准备"]}""",
                120, 60, 0.001m));

        var sut = new ClaudeViZhTranslator(claude);

        var result = await sut.TranslateAsync("Chúng tôi cung cấp khóa luyện thi HSK", "vi", "zh", CancellationToken.None);

        result.Translated.Should().Contain("HSK");
        result.SourceLang.Should().Be("vi");
        result.TargetLang.Should().Be("zh");
        result.GlossaryHits.Should().Contain("HSK");
    }

    [Fact]
    public async Task Empty_text_throws()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        var sut = new ClaudeViZhTranslator(claude);

        var act = async () => await sut.TranslateAsync("", "vi", "zh", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

// ── W2.4 — OpenCcZhScriptValidator ──
public sealed class OpenCcZhScriptValidatorTests
{
    private readonly OpenCcZhScriptValidator _sut = new();

    [Fact]
    public async Task Simplified_text_valid_against_simplified_target()
    {
        var result = await _sut.ValidateAsync("简体中文测试", "s", CancellationToken.None);

        result.IsConsistent.Should().BeTrue();
        result.DetectedScript.Should().Be("Simplified");
        result.ConvertedText.Should().BeNull();
    }

    [Fact]
    public async Task Text_with_traditional_chars_detected()
    {
        // Use CJK Extension A range char (U+3400+) which is traditional-specific
        var result = await _sut.ValidateAsync("\u3447\u3469\u4E2D\u6587", "s", CancellationToken.None);

        result.IsConsistent.Should().BeFalse();
        result.DetectedScript.Should().Be("Traditional");
    }

    [Fact]
    public async Task Simplified_text_inconsistent_with_traditional_target()
    {
        var result = await _sut.ValidateAsync("简体中文测试", "t", CancellationToken.None);

        result.IsConsistent.Should().BeFalse();
        result.DetectedScript.Should().Be("Simplified");
    }

    [Fact]
    public async Task Invalid_target_script_throws()
    {
        var act = async () => await _sut.ValidateAsync("test", "x", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Empty_text_throws()
    {
        var act = async () => await _sut.ValidateAsync("", "s", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

// ── W2.5 — RssCompetitorMonitor ──
public sealed class RssCompetitorMonitorTests
{
    [Fact]
    public async Task Parses_valid_rss_feed_with_items()
    {
        var rssXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
                <title>Test Feed</title>
                <item>
                    <title>New Course Launched</title>
                    <link>https://example.com/course</link>
                    <description>A new Chinese course</description>
                    <pubDate>2026-06-09T10:00:00+07:00</pubDate>
                </item>
                <item>
                    <title>Old Article</title>
                    <link>https://example.com/old</link>
                    <pubDate>2025-01-01T00:00:00+07:00</pubDate>
                </item>
            </channel></rss>
            """;

        var handler = new StubHttpMessageHandler(rssXml);
        var http = new HttpClient(handler);
        var sut = new RssCompetitorMonitor(http);

        var since = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var results = await sut.FetchSinceAsync(new[] { "https://example.com/feed.xml" }, since, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("New Course Launched");
        results[0].Source.Should().Be("example.com");
        results[0].Snippet.Should().Be("A new Chinese course");
    }

    [Fact]
    public async Task Deduplicates_by_url_hash()
    {
        var rssXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
                <item><title>A</title><link>https://example.com/a</link><pubDate>2026-06-09T10:00:00+07:00</pubDate></item>
                <item><title>A dup</title><link>https://example.com/a</link><pubDate>2026-06-09T10:00:00+07:00</pubDate></item>
                <item><title>B</title><link>https://example.com/b</link><pubDate>2026-06-09T10:00:00+07:00</pubDate></item>
            </channel></rss>
            """;

        var http = new HttpClient(new StubHttpMessageHandler(rssXml));
        var sut = new RssCompetitorMonitor(http);

        var results = await sut.FetchSinceAsync(new[] { "https://example.com/feed" }, DateTimeOffset.MinValue, CancellationToken.None);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Empty_sources_returns_empty()
    {
        var http = new HttpClient(new StubHttpMessageHandler(""));
        var sut = new RssCompetitorMonitor(http);

        var results = await sut.FetchSinceAsync(Array.Empty<string>(), DateTimeOffset.MinValue, CancellationToken.None);

        results.Should().BeEmpty();
    }

    private sealed class StubHttpMessageHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/xml"),
            });
    }
}

// ── W2.7 — QuestPdfTableRenderer ──
public sealed class QuestPdfTableRendererTests
{
    [Fact]
    public async Task Renders_pdf_with_headers_and_rows()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var sut = new QuestPdfTableRenderer();
        var spec = new PdfTableSpec(
            Headers: new[] { "Name", "Score", "Stage" },
            Rows: new[]
            {
                new[] { "Nguyen Van A", "85", "hot" },
                new[] { "Tran Thi B", "42", "warm" },
            },
            Title: "Lead Report",
            Style: null);

        var result = await sut.RenderAsync(spec, CancellationToken.None);

        result.PdfBytes.Should().NotBeEmpty();
        result.MimeType.Should().Be("application/pdf");
        result.PdfBytes[0..4].Should().Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
    }

    [Fact]
    public async Task Empty_headers_throws()
    {
        var sut = new QuestPdfTableRenderer();
        var spec = new PdfTableSpec(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>(), null, null);

        var act = async () => await sut.RenderAsync(spec, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

// ── W2.8 — QRCoderGenerator ──
public sealed class QRCoderGeneratorTests
{
    [Fact]
    public async Task Generates_png_qr_code()
    {
        var sut = new QRCoderGenerator();
        var spec = new QrSpec("https://clawbot.ai/signup", 200, "M");

        var result = await sut.GenerateAsync(spec, CancellationToken.None);

        result.PngBytes.Should().NotBeEmpty();
        result.PngBytes[0..8].Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG header
        result.Width.Should().Be(200);
        result.Height.Should().Be(200);
    }

    [Fact]
    public async Task Different_ecc_levels_work()
    {
        var sut = new QRCoderGenerator();

        var l = await sut.GenerateAsync(new QrSpec("test", 100, "L"), CancellationToken.None);
        var h = await sut.GenerateAsync(new QrSpec("test", 100, "H"), CancellationToken.None);

        l.PngBytes.Should().NotBeEmpty();
        h.PngBytes.Should().NotBeEmpty();
        h.PngBytes.Length.Should().BeGreaterThan(l.PngBytes.Length);
    }

    [Fact]
    public async Task Empty_payload_throws()
    {
        var sut = new QRCoderGenerator();

        var act = async () => await sut.GenerateAsync(new QrSpec("", 200, "M"), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
