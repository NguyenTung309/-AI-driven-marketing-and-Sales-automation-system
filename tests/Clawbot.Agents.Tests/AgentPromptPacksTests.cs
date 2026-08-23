using Clawbot.Agents.Core;
using FluentAssertions;

namespace Clawbot.Agents.Tests;

public sealed class AgentPromptPacksTests
{
    [Theory]
    [InlineData("chat-agent", "TƯ VẤN VIÊN HỌC BÁ")]
    [InlineData("sale-assist", "TRỢ LÝ ẢO CHO SALE")]
    [InlineData("sale-assist-agent", "TRỢ LÝ ẢO CHO SALE")]
    [InlineData("lead-agent", "CHUYÊN VIÊN PHÂN LOẠI LEAD")]
    [InlineData("content-agent", "CHUYÊN VIÊN SÁNG TẠO NỘI DUNG")]
    [InlineData("research-agent", "CHUYÊN VIÊN NGHIÊN CỨU THỊ TRƯỜNG")]
    [InlineData("docs-agent", "CHUYÊN VIÊN XỬ LÝ TÀI LIỆU")]
    [InlineData("report-agent", "CHUYÊN GIA PHÂN TÍCH & BÁO CÁO")]
    [InlineData("reviewer-agent", "NGƯỜI KIỂM DUYỆT NỘI DUNG HỌC BÁ")]
    [InlineData("orchestrator", "ĐIỀU PHỐI VIÊN")]
    [InlineData("publisher-agent", "Hoàn thành đúng nhiệm vụ")]
    [InlineData("reporter-agent", "Hoàn thành đúng nhiệm vụ")]
    public void For_ReturnsHocBaBrandContextAndAgentSpecificInstructions(string code, string expectedInstruction)
    {
        // Act
        var prompt = AgentPromptPacks.For(code);

        // Assert
        prompt.Should().Contain("BỐI CẢNH THƯƠNG HIỆU");
        prompt.Should().Contain("HSK từ 1 đến 6");
        prompt.Should().Contain("Tiếng Trung Công Xưởng");
        prompt.Should().Contain(expectedInstruction);
    }

    [Fact]
    public void For_UsesBrandAwareFallbackForUnknownCodes()
    {
        // Act
        var prompt = AgentPromptPacks.For("custom-agent");

        // Assert
        prompt.Should().Contain(AgentPromptPacks.BrandContext);
        prompt.Should().Contain("Hoàn thành đúng nhiệm vụ");
    }

    [Fact]
    public void ReviewerPack_DoesNotEscalateClaimsAlreadyConfirmedByKb()
    {
        // Act
        var prompt = AgentPromptDefaults.DefaultFor("reviewer-agent");

        // Assert
        prompt.Should().Contain("đã khớp KB");
        prompt.Should().Contain("không được trả needs_human");
        prompt.Should().Contain("mâu thuẫn KB là reject");
    }

    [Fact]
    public void DefaultFor_NormalizesSaleAssistAliases()
    {
        // Act
        var appAgentPrompt = AgentPromptDefaults.DefaultFor("sale-assist");
        var orchestrationPrompt = AgentPromptDefaults.DefaultFor("sale-assist-agent");

        // Assert
        orchestrationPrompt.Should().Be(appAgentPrompt);
    }
}
