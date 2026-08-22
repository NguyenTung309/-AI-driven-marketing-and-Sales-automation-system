using Clawbot.Api.Endpoints;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Unit test thuần cho các hàm nội bộ (internal, InternalsVisibleTo Clawbot.Api.Tests) của
/// OrchestrationV2Endpoints không đi qua HTTP: parser JSON đề xuất kế hoạch từ LLM (bọc fence,
/// object lẫn mảng, JSON hỏng) + validator allowedTools/inputSchema JSON. Không cần LLM/DB.
/// </summary>
public sealed class OrchestrationV2SuggestionsTests
{
    [Fact]
    public void ParseSuggestions_PlainJsonObject_ReturnsItems()
    {
        var text = """{"suggestions":[{"name":"Cham diem khach","goal":"Cham diem toan bo lead","cadence":"daily","reason":"vi du"}]}""";

        var result = OrchestrationV2Endpoints.ParseSuggestions(text);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Cham diem khach");
        result[0].Cadence.Should().Be("daily");
    }

    [Fact]
    public void ParseSuggestions_FencedJson_StripsCodeFenceBeforeParsing()
    {
        var text = "Day la de xuat:\n```json\n"
            + """{"suggestions":[{"name":"Bao cao KPI","goal":"Tong hop KPI tuan","cadence":"weekly","reason":"x"}]}"""
            + "\n```\nCam on.";

        var result = OrchestrationV2Endpoints.ParseSuggestions(text);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Bao cao KPI");
    }

    [Fact]
    public void ParseSuggestions_RootIsArray_ParsesDirectly()
    {
        var text = """[{"name":"A","goal":"Goal A","cadence":"monthly","reason":"r"}]""";

        var result = OrchestrationV2Endpoints.ParseSuggestions(text);

        result.Should().ContainSingle();
        result[0].Cadence.Should().Be("monthly");
    }

    [Fact]
    public void ParseSuggestions_UnknownCadence_FallsBackToWeekly()
    {
        var text = """{"suggestions":[{"name":"A","goal":"Goal A","cadence":"hourly","reason":"r"}]}""";

        var result = OrchestrationV2Endpoints.ParseSuggestions(text);

        result[0].Cadence.Should().Be("weekly", "cadence lạ phải rơi về mặc định weekly");
    }

    [Fact]
    public void ParseSuggestions_MissingNameOrGoal_SkipsEntry()
    {
        var text = """{"suggestions":[{"name":"","goal":"Goal A","cadence":"daily","reason":"r"},{"name":"B","goal":"","cadence":"daily","reason":"r"}]}""";

        var result = OrchestrationV2Endpoints.ParseSuggestions(text);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseSuggestions_MalformedJson_ReturnsEmpty()
    {
        var result = OrchestrationV2Endpoints.ParseSuggestions("khong phai json chut nao");

        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeAllowedTools_NullOrBlank_ReturnsEmptyArrayJson()
    {
        OrchestrationV2Endpoints.NormalizeAllowedTools(null).Should().Be("[]");
        OrchestrationV2Endpoints.NormalizeAllowedTools("   ").Should().Be("[]");
    }

    [Fact]
    public void NormalizeAllowedTools_ValidArray_ReturnsTrimmed()
    {
        OrchestrationV2Endpoints.NormalizeAllowedTools(" [\"a\",\"b\"] ").Should().Be("[\"a\",\"b\"]");
    }

    [Fact]
    public void NormalizeAllowedTools_NotAnArray_ReturnsNull()
    {
        OrchestrationV2Endpoints.NormalizeAllowedTools("{\"a\":1}").Should().BeNull();
    }

    [Fact]
    public void NormalizeAllowedTools_InvalidJson_ReturnsNull()
    {
        OrchestrationV2Endpoints.NormalizeAllowedTools("not-json").Should().BeNull();
    }

    [Fact]
    public void NormalizeJsonObject_NullOrBlank_ReturnsEmptyObjectJson()
    {
        OrchestrationV2Endpoints.NormalizeJsonObject(null).Should().Be("{}");
    }

    [Fact]
    public void NormalizeJsonObject_ArrayInput_ReturnsNull()
    {
        OrchestrationV2Endpoints.NormalizeJsonObject("[1,2,3]").Should().BeNull();
    }

    [Fact]
    public void NormalizeJsonObject_ValidObject_ReturnsTrimmed()
    {
        OrchestrationV2Endpoints.NormalizeJsonObject(" {\"a\":1} ").Should().Be("{\"a\":1}");
    }
}
