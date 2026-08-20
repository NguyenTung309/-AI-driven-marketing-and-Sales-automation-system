using Clawbot.Agents.Core.Skills.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Kiểm tra chữ Hán khớp target script (s=giản thể, t=phồn thể) + gợi ý bản chuyển đổi.
public sealed class ZhScriptValidatorTests
{
    private static OpenCcZhScriptValidator NewValidator() => new();

    private static async Task<ZhScriptCheck> ValidateAsync(string text, string target)
        => await NewValidator().ValidateAsync(text, target, CancellationToken.None);

    [Fact]
    public void Name_IsZhScriptValidation()
    {
        NewValidator().Name.Should().Be("zh-script-validation");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_BlankText_Throws(string text)
    {
        var act = async () => await ValidateAsync(text, "s");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_BlankTarget_Throws(string target)
    {
        var act = async () => await ValidateAsync("你好", target);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("x")]
    [InlineData("simplified")]
    [InlineData("zh")]
    public async Task Validate_InvalidTarget_Throws(string target)
    {
        var act = async () => await ValidateAsync("你好", target);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Validate_SimplifiedText_WantSimplified_IsConsistent()
    {
        // 你好世界: chữ giản thể/chung, không có ký tự phồn thể riêng.
        var result = await ValidateAsync("你好世界", "s");

        result.IsConsistent.Should().BeTrue();
        result.DetectedScript.Should().Be("Simplified");
        result.ConvertedText.Should().BeNull();
    }

    [Fact]
    public async Task Validate_TraditionalText_WantSimplified_IsInconsistent()
    {
        // 電話 chứa ký tự phồn thể riêng (0x96FB, 0x8A71-> dùng 0x96FB trong bảng).
        var result = await ValidateAsync("電", "s");

        result.IsConsistent.Should().BeFalse();
        result.DetectedScript.Should().Be("Traditional");
    }

    [Fact]
    public async Task Validate_TraditionalText_WantTraditional_IsConsistent()
    {
        var result = await ValidateAsync("電", "t");

        result.IsConsistent.Should().BeTrue();
        result.DetectedScript.Should().Be("Traditional");
    }

    [Fact]
    public async Task Validate_NonCjkText_DetectedUnknown()
    {
        var result = await ValidateAsync("hello", "s");

        result.DetectedScript.Should().Be("Unknown");
    }

    [Fact]
    public async Task Validate_TargetIsCaseInsensitive()
    {
        var result = await ValidateAsync("你好", "S");

        result.IsConsistent.Should().BeTrue();
    }
}
