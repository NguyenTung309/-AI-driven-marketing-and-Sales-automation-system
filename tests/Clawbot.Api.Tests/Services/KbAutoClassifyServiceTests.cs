using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class KbAutoClassifyPromptTests
{
    private static readonly KbModuleChoice[] Modules =
    [
        new("hoc-phi", "Học phí", "Bảng giá và chính sách"),
        new("lich-hoc", "Lịch học", null),
    ];

    [Fact]
    public void BuildPrompt_ListsExistingModulesWithDescriptions()
    {
        var prompt = KbAutoClassifyService.BuildPrompt("bang-gia.pdf", "nội dung", Modules);

        prompt.Should().Contain("- hoc-phi: Học phí — Bảng giá và chính sách");
    }

    [Fact]
    public void BuildPrompt_ModuleWithoutDescription_OmitsDash()
    {
        var prompt = KbAutoClassifyService.BuildPrompt("f.pdf", "x", Modules);

        prompt.Should().Contain("- lich-hoc: Lịch học\n");
    }

    [Fact]
    public void BuildPrompt_NoModules_TellsModelToProposeNewOne()
    {
        var prompt = KbAutoClassifyService.BuildPrompt("f.pdf", "x", []);

        prompt.Should().Contain("chưa có nhóm nào");
    }

    [Fact]
    public void BuildPrompt_IncludesFileNameAndExcerpt()
    {
        var prompt = KbAutoClassifyService.BuildPrompt("bang-gia.pdf", "học phí 2026", Modules);

        prompt.Should().Contain("Tên tệp: bang-gia.pdf");
        prompt.Should().Contain("học phí 2026");
    }

    [Fact]
    public void BuildPrompt_StatesJsonContract()
    {
        var prompt = KbAutoClassifyService.BuildPrompt("f.pdf", "x", Modules);

        prompt.Should().Contain("moduleCode");
        prompt.Should().Contain("newModule");
        prompt.Should().Contain("confidence");
    }
}

public sealed class KbAutoClassifyVerdictTests
{
    [Fact]
    public void ParseVerdict_ExistingModule_IsParsed()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            """{"moduleCode":"hoc-phi","newModule":null,"confidence":0.87,"reason":"Tài liệu giá"}""");

        verdict.Should().NotBeNull();
        verdict!.ModuleCode.Should().Be("hoc-phi");
        verdict.NewCode.Should().BeNull();
        verdict.Confidence.Should().Be(0.87);
        verdict.Reason.Should().Be("Tài liệu giá");
    }

    [Fact]
    public void ParseVerdict_NewModuleProposal_IsParsed()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            """
            {"moduleCode":null,
             "newModule":{"code":"tuyen-sinh","name":"Tuyển sinh","description":"Hồ sơ nhập học"},
             "confidence":0.4,"reason":"Không nhóm nào khớp"}
            """);

        verdict.Should().NotBeNull();
        verdict!.ModuleCode.Should().BeNull();
        verdict.NewCode.Should().Be("tuyen-sinh");
        verdict.NewName.Should().Be("Tuyển sinh");
        verdict.NewDescription.Should().Be("Hồ sơ nhập học");
    }

    [Fact]
    public void ParseVerdict_ExtractsJsonFromSurroundingProse()
    {
        // LLM hay bọc JSON trong lời dẫn hoặc code fence — phải bóc được phần object.
        var verdict = KbAutoClassifyService.ParseVerdict(
            "Đây là kết quả:\n```json\n{\"moduleCode\":\"hoc-phi\",\"confidence\":0.9}\n```\nHết.");

        verdict.Should().NotBeNull();
        verdict!.ModuleCode.Should().Be("hoc-phi");
    }

    [Fact]
    public void ParseVerdict_TrimsStringValues()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            """{"moduleCode":"  hoc-phi  ","confidence":0.5}""");

        verdict!.ModuleCode.Should().Be("hoc-phi");
    }

    [Theory]
    [InlineData(1.7, 1d)]
    [InlineData(-0.5, 0d)]
    [InlineData(0.5, 0.5)]
    public void ParseVerdict_ClampsConfidenceToUnitRange(double raw, double expected)
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            $$"""{"moduleCode":"hoc-phi","confidence":{{raw.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");

        verdict!.Confidence.Should().Be(expected);
    }

    [Fact]
    public void ParseVerdict_MissingConfidence_DefaultsToZero()
    {
        KbAutoClassifyService.ParseVerdict("""{"moduleCode":"hoc-phi"}""")!
            .Confidence.Should().Be(0d);
    }

    [Fact]
    public void ParseVerdict_NonNumericConfidence_DefaultsToZero()
    {
        KbAutoClassifyService.ParseVerdict("""{"moduleCode":"hoc-phi","confidence":"cao"}""")!
            .Confidence.Should().Be(0d);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseVerdict_BlankResponse_ReturnsNull(string? text)
    {
        KbAutoClassifyService.ParseVerdict(text!).Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_MalformedJson_ReturnsNull()
    {
        KbAutoClassifyService.ParseVerdict("{ khong phai json").Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_NonObjectJson_ReturnsNull()
    {
        KbAutoClassifyService.ParseVerdict("[1,2,3]").Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_NeitherModuleCodeNorNewModule_ReturnsNull()
    {
        // Không quyết định được gì thì coi như thất bại, không tạo nhóm rác.
        KbAutoClassifyService.ParseVerdict("""{"confidence":0.9,"reason":"không rõ"}""")
            .Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_BlankModuleCodeIsTreatedAsMissing()
    {
        KbAutoClassifyService.ParseVerdict("""{"moduleCode":"   ","confidence":0.9}""")
            .Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_NewModuleWithOnlyName_IsAccepted()
    {
        var verdict = KbAutoClassifyService.ParseVerdict(
            """{"moduleCode":null,"newModule":{"name":"Tuyển sinh"},"confidence":0.3}""");

        verdict.Should().NotBeNull();
        verdict!.NewName.Should().Be("Tuyển sinh");
        verdict.NewCode.Should().BeNull();
    }

    [Fact]
    public void ParseVerdict_NewModuleNotAnObject_IsIgnored()
    {
        KbAutoClassifyService.ParseVerdict(
            """{"moduleCode":null,"newModule":"tuyen-sinh","confidence":0.3}""")
            .Should().BeNull();
    }
}

public sealed class KbModuleChoiceTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var choice = new KbModuleChoice("hoc-phi", "Học phí", "Mô tả");

        choice.Code.Should().Be("hoc-phi");
        choice.Name.Should().Be("Học phí");
        choice.Description.Should().Be("Mô tả");
    }
}
