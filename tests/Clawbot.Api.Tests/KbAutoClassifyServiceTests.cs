using Clawbot.Agents.Core.Chat;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class KbAutoClassifyServiceTests
{
    [Fact]
    public void ParseVerdict_reads_existing_module_match()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            """{"moduleCode":"hsk-courses","newModule":null,"confidence":0.92,"reason":"Nội dung về học phí HSK"}""");

        verdict.Should().NotBeNull();
        verdict!.ModuleCode.Should().Be("hsk-courses");
        verdict.NewCode.Should().BeNull();
        verdict.Confidence.Should().Be(0.92);
        verdict.Reason.Should().Be("Nội dung về học phí HSK");
    }

    [Fact]
    public void ParseVerdict_reads_new_module_and_strips_code_fences()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            "```json\n{\"moduleCode\":null,\"newModule\":{\"code\":\"chinh-sach-hoan-tien\",\"name\":\"Chính sách hoàn tiền\",\"description\":\"Điều khoản hoàn học phí\"},\"confidence\":0.8,\"reason\":\"Chưa có nhóm phù hợp\"}\n```");

        verdict.Should().NotBeNull();
        verdict!.ModuleCode.Should().BeNull();
        verdict.NewCode.Should().Be("chinh-sach-hoan-tien");
        verdict.NewName.Should().Be("Chính sách hoàn tiền");
        verdict.NewDescription.Should().Be("Điều khoản hoàn học phí");
    }

    [Fact]
    public void ParseVerdict_clamps_confidence_and_rejects_garbage()
    {
        KbAutoClassifyService.ParseVerdict("not json").Should().BeNull();
        KbAutoClassifyService.ParseVerdict("").Should().BeNull();
        KbAutoClassifyService.ParseVerdict("""{"confidence":5}""").Should().BeNull();

        var verdict = KbAutoClassifyService.ParseVerdict("""{"moduleCode":"a","confidence":5}""");
        verdict!.Confidence.Should().Be(1d);
    }

    [Fact]
    public void BuildPrompt_lists_modules_and_flags_empty_catalog()
    {
        var withModules = KbAutoClassifyService.BuildPrompt("bang-gia.pdf", "Học phí HSK3...",
            [new KbModuleChoice("hsk-courses", "Khóa học HSK", "Học phí và lịch khai giảng")]);
        withModules.Should().Contain("- hsk-courses: Khóa học HSK — Học phí và lịch khai giảng");
        withModules.Should().Contain("bang-gia.pdf");

        var empty = KbAutoClassifyService.BuildPrompt("a.txt", "x", []);
        empty.Should().Contain("chưa có nhóm nào");
    }

    [Fact]
    public async Task ClassifyAsync_truncates_content_and_parses_reply()
    {
        var claude = new CapturingClaude(
            """{"moduleCode":"hsk-courses","newModule":null,"confidence":0.9,"reason":"ok"}""");
        var sut = new KbAutoClassifyService(claude, new LlmCallScope());
        var longContent = new string('x', 10_000);

        var verdict = await sut.ClassifyAsync(Guid.NewGuid(), "a.pdf", longContent,
            [new KbModuleChoice("hsk-courses", "Khóa học HSK", null)], CancellationToken.None);

        verdict!.ModuleCode.Should().Be("hsk-courses");
        claude.CapturedPrompt!.Length.Should().BeLessThan(5_000);
    }

    [Theory]
    [InlineData("Chính sách Hoàn Tiền!!", "ch-nh-s-ch-ho-n-ti-n")]
    [InlineData("hsk-courses", "hsk-courses")]
    [InlineData("  HSK  Courses  ", "hsk-courses")]
    public void SlugifyModuleCode_normalizes(string raw, string expected)
    {
        KbEndpoints.SlugifyModuleCode(raw).Should().Be(expected);
    }

    [Fact]
    public void SlugifyModuleCode_falls_back_when_nothing_survives()
    {
        KbEndpoints.SlugifyModuleCode("!!!").Should().StartWith("kb-").And.HaveLength(11);
    }

    private sealed class CapturingClaude(string response) : IClaudeChatClient
    {
        public string? CapturedPrompt { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            _ = systemPrompt;
            _ = history;
            CapturedPrompt = userMessage;
            return Task.FromResult(new ClaudeReply(response, 11, 7, 0.000138m));
        }

        public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            _ = systemPrompt;
            _ = history;
            _ = userMessage;
            _ = ct;
            return EmptyStream();
        }

        private static async IAsyncEnumerable<ClaudeStreamChunk> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
