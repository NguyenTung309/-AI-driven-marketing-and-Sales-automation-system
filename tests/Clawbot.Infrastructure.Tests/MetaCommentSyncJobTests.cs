using System.Globalization;
using System.Text.Json;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests;

public sealed class MetaCommentSyncJobTests
{
    [Fact]
    public void ParseComments_MapsFacebookCommentAndOwnerMetadata()
    {
        const string json = """
            {
              "data": [
                {
                  "id": "comment-1",
                  "message": "Cho mình xin giá",
                  "from": { "id": "customer-1", "name": "Nguyen" },
                  "created_time": "2026-07-26T08:00:00Z",
                  "parent_id": "post-1"
                }
              ]
            }
            """;
        var source = new MetaCommentSyncJob.MetaCommentSource(
            IsInstagram: false,
            Platform: "facebook",
            ExternalPageId: "page-1",
            Name: "Page",
            AccessToken: "token");

        using var document = JsonDocument.Parse(json);
        var result = MetaCommentSyncJob.ParseComments(
            document.RootElement,
            source,
            "post-1",
            DateTimeOffset.Parse("2026-07-26T09:00:00Z", CultureInfo.InvariantCulture));

        result.Should().ContainSingle();
        var message = result[0];
        message.Channel.Should().Be("facebook");
        message.ExternalThreadId.Should().Be("page-1:customer-1");
        message.MessageType.Should().Be("comment");
        message.ParentPostId.Should().Be("post-1");
        message.Text.Should().Be("Cho mình xin giá");
        message.Metadata["external_message_id"].Should().Be("comment-1");
        message.Metadata["comment_parent_id"].Should().Be("post-1");
        message.Metadata["sender_name"].Should().Be("Nguyen");
    }

    [Fact]
    public void ParseComments_MapsInstagramTextAndUsername()
    {
        const string json = """
            {
              "data": [
                {
                  "id": "ig-comment-1",
                  "text": "Mình muốn đăng ký",
                  "from": { "id": "ig-customer-1", "username": "customer" },
                  "timestamp": "2026-07-26T08:00:00+00:00",
                  "replies": {
                    "data": [
                      {
                        "id": "ig-reply-1",
                        "text": "Mình cũng quan tâm",
                        "from": { "id": "ig-customer-2", "username": "second" },
                        "timestamp": "2026-07-26T08:01:00+00:00",
                        "parent_id": "ig-comment-1"
                      }
                    ]
                  }
                }
              ]
            }
            """;
        var source = new MetaCommentSyncJob.MetaCommentSource(
            IsInstagram: true,
            Platform: "instagram",
            ExternalPageId: "ig-page-1",
            Name: "Instagram",
            AccessToken: "token");

        using var document = JsonDocument.Parse(json);
        var result = MetaCommentSyncJob.ParseComments(
            document.RootElement,
            source,
            "ig-media-1",
            DateTimeOffset.UtcNow);

        result.Should().HaveCount(2);
        result[0].Channel.Should().Be("instagram");
        result[0].Text.Should().Be("Mình muốn đăng ký");
        result[0].ParentPostId.Should().Be("ig-media-1");
        result[0].Metadata["sender_name"].Should().Be("customer");
        result[1].Metadata["external_message_id"].Should().Be("ig-reply-1");
        result[1].Metadata["comment_parent_id"].Should().Be("ig-comment-1");
    }

    [Theory]
    [InlineData("123_456", true)]
    [InlineData("17841400000000000", true)]
    [InlineData("123/456", false)]
    [InlineData("123?fields=id", false)]
    [InlineData("..", false)]
    public void IsSafeGraphObjectId_RejectsPathManipulation(string value, bool expected)
    {
        MetaEngagementSyncJob.IsSafeGraphObjectId(value).Should().Be(expected);
    }

    [Fact]
    public void ReadInstagramCounts_RejectsMissingAndNegativeMetrics()
    {
        using var document = JsonDocument.Parse("{\"like_count\": -1}");

        var counts = MetaEngagementSyncJob.ReadInstagramCounts(document.RootElement);

        counts.Likes.Should().BeNull();
        counts.Comments.Should().BeNull();
    }
}
