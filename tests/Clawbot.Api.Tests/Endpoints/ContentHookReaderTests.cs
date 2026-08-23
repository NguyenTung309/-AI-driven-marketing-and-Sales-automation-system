using Clawbot.Api.Endpoints;
using FluentAssertions;

namespace Clawbot.Api.Tests.Endpoints;

public sealed class ContentHookReaderTests
{
    [Fact]
    public void Read_ValidOutline_ReturnsHooksAndSelectedIndex()
    {
        var (hooks, selected) = ContentHookReader.Read(
            """{"hooks":["Mở bài A","Mở bài B"],"selectedHookIndex":1}""");

        hooks.Should().Equal("Mở bài A", "Mở bài B");
        selected.Should().Be(1);
    }

    [Fact]
    public void Read_SkipsNonStringAndBlankHooks()
    {
        var (hooks, _) = ContentHookReader.Read(
            """{"hooks":["Giữ lại", "  ", "", 42, null, {"a":1}]}""");

        hooks.Should().Equal("Giữ lại");
    }

    [Fact]
    public void Read_MissingSelectedIndex_ReturnsMinusOne()
    {
        var (hooks, selected) = ContentHookReader.Read("""{"hooks":["A"]}""");

        hooks.Should().ContainSingle();
        selected.Should().Be(-1);
    }

    [Fact]
    public void Read_NonNumericSelectedIndex_ReturnsMinusOne()
    {
        var (_, selected) = ContentHookReader.Read(
            """{"hooks":["A"],"selectedHookIndex":"mot"}""");

        selected.Should().Be(-1);
    }

    [Fact]
    public void Read_MissingHooksProperty_ReturnsEmpty()
    {
        var (hooks, selected) = ContentHookReader.Read("""{"selectedHookIndex":0}""");

        hooks.Should().BeEmpty();
        selected.Should().Be(0);
    }

    [Fact]
    public void Read_HooksNotAnArray_ReturnsEmpty()
    {
        var (hooks, _) = ContentHookReader.Read("""{"hooks":"A"}""");

        hooks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_BlankJson_ReturnsEmptyAndMinusOne(string? json)
    {
        var (hooks, selected) = ContentHookReader.Read(json);

        hooks.Should().BeEmpty();
        selected.Should().Be(-1);
    }

    [Fact]
    public void Read_MalformedJson_IsToleratedNotThrown()
    {
        // Outline hỏng phải cho ra canRegenerate=false, tuyệt đối không ném lỗi ra endpoint.
        var (hooks, selected) = ContentHookReader.Read("{ khong-phai-json");

        hooks.Should().BeEmpty();
        selected.Should().Be(-1);
    }

    [Fact]
    public void Read_JsonArrayRoot_ReturnsEmpty()
    {
        var (hooks, selected) = ContentHookReader.Read("""["A","B"]""");

        hooks.Should().BeEmpty();
        selected.Should().Be(-1);
    }
}

public sealed class UnifiedDiffTests
{
    [Fact]
    public void Compute_IdenticalText_ReportsNoChanges()
    {
        var (added, removed, text) = UnifiedDiff.Compute("dòng 1\ndòng 2", "dòng 1\ndòng 2");

        added.Should().Be(0);
        removed.Should().Be(0);
        text.Should().Contain(" dòng 1");
    }

    [Fact]
    public void Compute_AddedLine_IsMarkedWithPlus()
    {
        var (added, removed, text) = UnifiedDiff.Compute("A", "A\nB");

        added.Should().Be(1);
        removed.Should().Be(0);
        text.Should().Contain("+B");
    }

    [Fact]
    public void Compute_RemovedLine_IsMarkedWithMinus()
    {
        var (added, removed, text) = UnifiedDiff.Compute("A\nB", "A");

        added.Should().Be(0);
        removed.Should().Be(1);
        text.Should().Contain("-B");
    }

    [Fact]
    public void Compute_ReplacedLine_CountsBothSides()
    {
        var (added, removed, _) = UnifiedDiff.Compute("A\nB", "A\nC");

        added.Should().Be(1);
        removed.Should().Be(1);
    }

    [Fact]
    public void Compute_NormalizesCrLfSoLineEndingAloneIsNotADiff()
    {
        var (added, removed, _) = UnifiedDiff.Compute("A\r\nB", "A\nB");

        added.Should().Be(0);
        removed.Should().Be(0);
    }

    [Fact]
    public void Compute_NullInputs_AreTreatedAsEmpty()
    {
        var (added, removed, _) = UnifiedDiff.Compute(null!, null!);

        added.Should().Be(0);
        removed.Should().Be(0);
    }

    [Fact]
    public void Compute_FromNull_CountsEverythingAsAdded()
    {
        var (added, _, text) = UnifiedDiff.Compute(null!, "A");

        added.Should().Be(1);
        text.Should().Contain("+A");
    }
}
