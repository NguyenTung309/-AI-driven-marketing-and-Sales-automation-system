using Clawbot.Agents.Core.Skills.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Parser JSON kịch bản video Hook-Value-CTA + shot list (tolerant, không dùng JsonSerializer).
public sealed class HvcVideoScriptComposerTests
{
    [Fact]
    public void ParseScript_WellFormed_ExtractsAllFields()
    {
        var json = """{"hook":"3 giây đầu gây sốc","value":"Bí quyết học HSK","cta":"Đăng ký ngay","shot_list":["cận mặt","toàn cảnh lớp"]}""";

        var script = HvcVideoScriptComposer.ParseScript(json);

        script.Hook.Should().Be("3 giây đầu gây sốc");
        script.Value.Should().Be("Bí quyết học HSK");
        script.Cta.Should().Be("Đăng ký ngay");
        script.ShotList.Should().Equal("cận mặt", "toàn cảnh lớp");
    }

    [Fact]
    public void ParseScript_MissingValueAndCta_DefaultsEmpty()
    {
        var json = """{"hook":"chỉ có hook"}""";

        var script = HvcVideoScriptComposer.ParseScript(json);

        script.Hook.Should().Be("chỉ có hook");
        script.Value.Should().BeEmpty();
        script.Cta.Should().BeEmpty();
        script.ShotList.Should().BeEmpty();
    }

    [Fact]
    public void ParseScript_NoHookField_FallsBackToWholeText()
    {
        var script = HvcVideoScriptComposer.ParseScript("  raw text no json  ");

        script.Hook.Should().Be("raw text no json");
    }

    [Fact]
    public void ParseScript_EmptyShotList_YieldsEmpty()
    {
        var json = """{"hook":"h","value":"v","cta":"c","shot_list":[]}""";

        var script = HvcVideoScriptComposer.ParseScript(json);

        script.ShotList.Should().BeEmpty();
    }
}
