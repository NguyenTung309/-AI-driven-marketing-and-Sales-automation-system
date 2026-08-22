using System.Text.Json;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests;

// Edge "likes" của Facebook CHỈ đếm reaction loại LIKE. Bài ăn nhiều LOVE/HAHA sẽ bị báo hụt
// nếu chỉ đọc edge đó, nên tổng reaction phải lấy từ edge "reactions".
public sealed class MetaReactionBreakdownTests
{
    private const string GraphResponse = """
    {
      "likes":     { "summary": { "total_count": 10 } },
      "comments":  { "summary": { "total_count": 4  } },
      "reactions": { "summary": { "total_count": 27 } },
      "love":      { "summary": { "total_count": 9  } },
      "haha":      { "summary": { "total_count": 5  } },
      "wow":       { "summary": { "total_count": 2  } },
      "sad":       { "summary": { "total_count": 1  } },
      "angry":     { "summary": { "total_count": 0  } },
      "care":      { "summary": { "total_count": 0  } }
    }
    """;

    [Fact]
    public void ReadFacebookReactions_ReadsTotalAndEveryReactionType()
    {
        // Arrange
        using var document = JsonDocument.Parse(GraphResponse);

        // Act
        var reactions = MetaEngagementSyncJob.ReadFacebookReactions(document.RootElement);

        // Assert
        reactions.Total.Should().Be(27);
        reactions.Love.Should().Be(9);
        reactions.Haha.Should().Be(5);
        reactions.Wow.Should().Be(2);
        reactions.Sad.Should().Be(1);
        reactions.Angry.Should().Be(0);
        reactions.Care.Should().Be(0);
    }

    [Fact]
    public void ReadFacebookReactions_TotalIsLargerThanLikeCount_SoLikeAloneUndercounts()
    {
        // Arrange
        using var document = JsonDocument.Parse(GraphResponse);

        // Act
        var (likes, _) = MetaEngagementSyncJob.ReadFacebookCounts(document.RootElement);
        var reactions = MetaEngagementSyncJob.ReadFacebookReactions(document.RootElement);

        // Assert
        likes.Should().Be(10);
        reactions.Total.Should().BeGreaterThan(likes!.Value);
    }

    [Fact]
    public void ReadFacebookReactions_ReturnsNulls_WhenGraphOmitsReactionEdges()
    {
        // Bài cũ đồng bộ trước khi thêm reaction, hoặc page không trả edge: không được coi là 0.
        using var document = JsonDocument.Parse("""{"likes":{"summary":{"total_count":3}}}""");

        var reactions = MetaEngagementSyncJob.ReadFacebookReactions(document.RootElement);

        reactions.Total.Should().BeNull();
        reactions.Love.Should().BeNull();
        reactions.Care.Should().BeNull();
    }

    [Fact]
    public void FacebookEngagementFields_RequestReactionBreakdown()
    {
        // Act
        var fields = MetaEngagementSyncJob.FacebookEngagementFields;

        // Assert
        fields.Should().Contain("reactions.summary(true)");
        foreach (var type in new[] { "LOVE", "HAHA", "WOW", "SAD", "ANGRY", "CARE" })
            fields.Should().Contain($"reactions.type({type})");
        fields.Should().Contain("comments.summary(true)");
    }
}
