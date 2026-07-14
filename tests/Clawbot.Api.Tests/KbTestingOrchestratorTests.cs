using Clawbot.Api.Services;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class KbTestingOrchestratorTests
{
    // Số case kiểm thử bám theo độ dài tài liệu (~1 case / 1k ký tự), sàn 8, kẹp trần theo ngữ cảnh
    // (nạp-file 20, bấm tay 40) để tài liệu ngắn vẫn đủ case mà tài liệu dài phủ được nhiều hơn.
    [Theory]
    [InlineData(0, 40, 8)]         // rỗng -> sàn 8
    [InlineData(3000, 40, 8)]      // ngắn -> sàn 8
    [InlineData(8000, 40, 8)]      // ceil(8) = 8
    [InlineData(12000, 40, 12)]    // ceil(12) = 12
    [InlineData(40000, 40, 40)]    // chạm trần bấm tay
    [InlineData(100000, 40, 40)]   // rất dài -> trần 40
    [InlineData(30000, 20, 20)]    // trần nạp-file thấp hơn
    [InlineData(12000, 20, 12)]    // dưới trần nạp-file
    public void ScaleCaseCount_scales_with_length_and_clamps(int contentLength, int maxCases, int expected)
    {
        KbTestingOrchestrator.ScaleCaseCount(contentLength, maxCases).Should().Be(expected);
    }
}
