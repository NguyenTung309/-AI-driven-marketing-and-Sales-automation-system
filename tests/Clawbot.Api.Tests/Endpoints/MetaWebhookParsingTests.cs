using System.Security.Cryptography;
using System.Text;
using Clawbot.Api.Endpoints;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;

namespace Clawbot.Api.Tests.Endpoints;

public sealed class MetaPageCommentParsingTests
{
    private static readonly DateTimeOffset Fallback = new(2026, 8, 18, 3, 0, 0, TimeSpan.Zero);

    private static byte[] Payload(string json) => Encoding.UTF8.GetBytes(json);

    private static string FeedComment(
        string pageId = "page-1",
        string commentId = "cmt-1",
        string postId = "post-1",
        string fromId = "user-1",
        string message = "cho em hỏi học phí",
        string verb = "add",
        string item = "comment") =>
        $$"""
        {
          "object": "page",
          "entry": [{
            "id": "{{pageId}}",
            "changes": [{
              "field": "feed",
              "value": {
                "item": "{{item}}",
                "verb": "{{verb}}",
                "comment_id": "{{commentId}}",
                "post_id": "{{postId}}",
                "message": "{{message}}",
                "from": { "id": "{{fromId}}", "name": "Nguyen Van A" }
              }
            }]
          }]
        }
        """;

    [Fact]
    public void ParseComments_ValidFeedComment_IsParsed()
    {
        var comments = MetaPageWebhookEndpoints.ParseComments(Payload(FeedComment()), Fallback);

        comments.Should().ContainSingle();
        var comment = comments[0];
        comment.PageId.Should().Be("page-1");
        comment.CommentId.Should().Be("cmt-1");
        comment.PostId.Should().Be("post-1");
        comment.FromId.Should().Be("user-1");
        comment.FromName.Should().Be("Nguyen Van A");
        comment.Message.Should().Be("cho em hỏi học phí");
        comment.SentAt.Should().Be(Fallback);
    }

    [Theory]
    [InlineData("""{"object":"instagram","entry":[]}""")]
    [InlineData("""{"entry":[]}""")]
    [InlineData("""{"object":"page"}""")]
    [InlineData("""{"object":"page","entry":{}}""")]
    [InlineData("""[1,2,3]""")]
    public void ParseComments_WrongEnvelope_ReturnsEmpty(string json)
    {
        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback).Should().BeEmpty();
    }

    [Theory]
    [InlineData("edited")]
    [InlineData("remove")]
    public void ParseComments_NonAddVerb_IsIgnored(string verb)
    {
        MetaPageWebhookEndpoints.ParseComments(Payload(FeedComment(verb: verb)), Fallback)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseComments_NonCommentItem_IsIgnored()
    {
        MetaPageWebhookEndpoints.ParseComments(Payload(FeedComment(item: "status")), Fallback)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseComments_NonFeedField_IsIgnored()
    {
        var json = FeedComment().Replace("\"field\": \"feed\"", "\"field\": \"mention\"", StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback).Should().BeEmpty();
    }

    [Theory]
    [InlineData("page id có dấu cách")]
    [InlineData("page/../1")]
    [InlineData("")]
    public void ParseComments_InvalidPageIdentifier_IsIgnored(string pageId)
    {
        MetaPageWebhookEndpoints.ParseComments(Payload(FeedComment(pageId: pageId)), Fallback)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("cmt 1")]
    [InlineData("cmt;drop")]
    public void ParseComments_InvalidCommentIdentifier_IsIgnored(string commentId)
    {
        MetaPageWebhookEndpoints.ParseComments(Payload(FeedComment(commentId: commentId)), Fallback)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseComments_MissingFrom_IsIgnored()
    {
        var json = """
            {"object":"page","entry":[{"id":"page-1","changes":[{"field":"feed","value":{
              "item":"comment","verb":"add","comment_id":"c1","post_id":"p1","message":"x"}}]}]}
            """;

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback).Should().BeEmpty();
    }

    [Fact]
    public void ParseComments_FallsBackToIdWhenCommentIdMissing()
    {
        var json = """
            {"object":"page","entry":[{"id":"page-1","changes":[{"field":"feed","value":{
              "item":"comment","verb":"add","id":"c-fallback","post_id":"p1","message":"x",
              "from":{"id":"u1"}}}]}]}
            """;

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .CommentId.Should().Be("c-fallback");
    }

    [Fact]
    public void ParseComments_DeduplicatesSamePageAndCommentId()
    {
        var json = """
            {"object":"page","entry":[
              {"id":"page-1","changes":[{"field":"feed","value":{"item":"comment","verb":"add",
                "comment_id":"c1","post_id":"p1","message":"x","from":{"id":"u1"}}}]},
              {"id":"page-1","changes":[{"field":"feed","value":{"item":"comment","verb":"add",
                "comment_id":"c1","post_id":"p1","message":"x","from":{"id":"u1"}}}]}]}
            """;

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback).Should().ContainSingle();
    }

    [Fact]
    public void ParseComments_NumericCreatedTime_IsUsedAsSentAt()
    {
        var json = FeedComment().Replace(
            "\"message\": \"cho em hỏi học phí\"",
            "\"message\": \"x\", \"created_time\": 1755500000",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .SentAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1755500000));
    }

    [Fact]
    public void ParseComments_StringCreatedTime_IsParsed()
    {
        var json = FeedComment().Replace(
            "\"message\": \"cho em hỏi học phí\"",
            "\"message\": \"x\", \"created_time\": \"2026-08-18T05:00:00+07:00\"",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .SentAt.Should().Be(new DateTimeOffset(2026, 8, 18, 5, 0, 0, TimeSpan.FromHours(7)));
    }

    [Fact]
    public void ParseComments_UnparseableCreatedTime_FallsBackToClock()
    {
        var json = FeedComment().Replace(
            "\"message\": \"cho em hỏi học phí\"",
            "\"message\": \"x\", \"created_time\": \"hom qua\"",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .SentAt.Should().Be(Fallback);
    }

    [Fact]
    public void ParseComments_TruncatesOverlongMessage()
    {
        var longMessage = new string('a', 40_000);
        var json = FeedComment(message: longMessage);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .Message.Length.Should().Be(32_000);
    }

    [Fact]
    public void ParseComments_TruncatesOverlongSenderName()
    {
        var json = FeedComment().Replace(
            "\"name\": \"Nguyen Van A\"",
            $"\"name\": \"{new string('n', 500)}\"",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .FromName!.Length.Should().Be(256);
    }

    [Fact]
    public void ParseComments_DropsOverlongParentId()
    {
        var json = FeedComment().Replace(
            "\"message\": \"cho em hỏi học phí\"",
            $"\"message\": \"x\", \"parent_id\": \"{new string('p', 500)}\"",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .ParentId.Should().BeNull();
    }

    [Fact]
    public void ParseComments_KeepsValidParentId()
    {
        var json = FeedComment().Replace(
            "\"message\": \"cho em hỏi học phí\"",
            "\"message\": \"x\", \"parent_id\": \"parent-9\"",
            StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .ParentId.Should().Be("parent-9");
    }

    [Fact]
    public void ParseComments_MissingMessage_BecomesEmptyString()
    {
        var json = """
            {"object":"page","entry":[{"id":"page-1","changes":[{"field":"feed","value":{
              "item":"comment","verb":"add","comment_id":"c1","post_id":"p1","from":{"id":"u1"}}}]}]}
            """;

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback)[0]
            .Message.Should().BeEmpty();
    }

    [Fact]
    public void ParseComments_CapsAtFiveHundredComments()
    {
        const string template =
            """{"field":"feed","value":{"item":"comment","verb":"add","comment_id":"__ID__","post_id":"p1","message":"x","from":{"id":"u1"}}}""";
        var changes = string.Join(",", Enumerable.Range(0, 600)
            .Select(i => template.Replace("__ID__", $"c{i}", StringComparison.Ordinal)));
        var json = """{"object":"page","entry":[{"id":"page-1","changes":[__CHANGES__]}]}"""
            .Replace("__CHANGES__", changes, StringComparison.Ordinal);

        MetaPageWebhookEndpoints.ParseComments(Payload(json), Fallback).Should().HaveCount(500);
    }

    [Fact]
    public void ParseComments_MalformedJson_Throws()
    {
        // Endpoint bắt JsonException để trả 400; parser cứ ném lên.
        var act = () => MetaPageWebhookEndpoints.ParseComments(Payload("{ hong"), Fallback);

        act.Should().Throw<System.Text.Json.JsonException>();
    }
}

public sealed class MetaWebhookSignatureTests
{
    private const string AppSecret = "app-secret-123";

    private static byte[] Payload => Encoding.UTF8.GetBytes("""{"object":"application"}""");

    private static string Sign(byte[] payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(payload));
    }

    [Fact]
    public void IsValidSignature_CorrectSignature_ReturnsTrue()
    {
        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Payload, Sign(Payload, AppSecret), AppSecret)
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidSignature_IsCaseInsensitiveOnPrefix()
    {
        var signature = Sign(Payload, AppSecret).Replace("sha256=", "SHA256=", StringComparison.Ordinal);

        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Payload, signature, AppSecret).Should().BeTrue();
    }

    [Fact]
    public void IsValidSignature_WrongSecret_ReturnsFalse()
    {
        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Payload, Sign(Payload, "secret-khac"), AppSecret)
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidSignature_TamperedPayload_ReturnsFalse()
    {
        var signature = Sign(Payload, AppSecret);

        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Encoding.UTF8.GetBytes("""{"object":"page"}"""), signature, AppSecret)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-co-prefix")]
    [InlineData("sha256=khong-phai-hex")]
    [InlineData("sha256=abcd")]
    public void IsValidSignature_BadHeader_ReturnsFalse(string? header)
    {
        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Payload, header, AppSecret).Should().BeFalse();
    }

    [Fact]
    public void IsValidSignature_EmptyPayloadOrSecret_ReturnsFalse()
    {
        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature([], Sign(Payload, AppSecret), AppSecret).Should().BeFalse();
        MetaBusinessIntegrationWebhookEndpoints
            .IsValidSignature(Payload, Sign(Payload, AppSecret), "  ").Should().BeFalse();
    }
}

public sealed class MetaBusinessIntegrationParsingTests
{
    private const string AppId = "app-42";

    private static byte[] Payload(string json) => Encoding.UTF8.GetBytes(json);

    private const string ChangeTemplate =
        """{"object":"application","entry":[{"id":"__APP__","changes":[{"field":"__FIELD__","value":{"business_manager_id":"__BIZ__"}}]}]}""";

    private static string ChangePayload(
        string field = MetaBusinessIntegrationWebhookJob.InstallField,
        string appId = AppId,
        string businessId = "biz-1") =>
        ChangeTemplate
            .Replace("__APP__", appId, StringComparison.Ordinal)
            .Replace("__FIELD__", field, StringComparison.Ordinal)
            .Replace("__BIZ__", businessId, StringComparison.Ordinal);

    [Theory]
    [InlineData(MetaBusinessIntegrationWebhookJob.InstallField)]
    [InlineData(MetaBusinessIntegrationWebhookJob.UpdateField)]
    [InlineData(MetaBusinessIntegrationWebhookJob.UninstallField)]
    public void ParseChanges_SupportedField_IsParsed(string field)
    {
        var changes = MetaBusinessIntegrationWebhookEndpoints
            .ParseChanges(Payload(ChangePayload(field)), AppId);

        changes.Should().ContainSingle();
        changes[0].Field.Should().Be(field);
        changes[0].BusinessManagerId.Should().Be("biz-1");
    }

    [Fact]
    public void ParseChanges_UnsupportedField_IsIgnored()
    {
        MetaBusinessIntegrationWebhookEndpoints
            .ParseChanges(Payload(ChangePayload(field: "some_other_field")), AppId)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_DifferentAppId_IsIgnored()
    {
        MetaBusinessIntegrationWebhookEndpoints
            .ParseChanges(Payload(ChangePayload(appId: "app-khac")), AppId)
            .Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_MissingBusinessManagerId_IsIgnored()
    {
        var json = ChangeTemplate
            .Replace("__APP__", AppId, StringComparison.Ordinal)
            .Replace("__FIELD__", MetaBusinessIntegrationWebhookJob.InstallField, StringComparison.Ordinal)
            .Replace("\"business_manager_id\":\"__BIZ__\"", "", StringComparison.Ordinal);

        MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Payload(json), AppId).Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_DeduplicatesIdenticalChanges()
    {
        const string duplicated =
            """{"object":"application","entry":[{"id":"__APP__","changes":[{"field":"__FIELD__","value":{"business_manager_id":"biz-1"}},{"field":"__FIELD__","value":{"business_manager_id":"biz-1"}}]}]}""";
        var json = duplicated
            .Replace("__APP__", AppId, StringComparison.Ordinal)
            .Replace("__FIELD__", MetaBusinessIntegrationWebhookJob.InstallField, StringComparison.Ordinal);

        MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Payload(json), AppId)
            .Should().ContainSingle();
    }

    [Theory]
    [InlineData("""{"object":"page","entry":[]}""")]
    [InlineData("""{"entry":[]}""")]
    [InlineData("""{"object":"application"}""")]
    public void ParseChanges_WrongEnvelope_ReturnsEmpty(string json)
    {
        MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Payload(json), AppId).Should().BeEmpty();
    }

    [Fact]
    public void ParseApplicationIds_CollectsEntryIds()
    {
        var json = """{"object":"application","entry":[{"id":"app-1"},{"id":"app-2"},{"id":"app-1"}]}""";

        MetaBusinessIntegrationWebhookEndpoints.ParseApplicationIds(Payload(json))
            .Should().BeEquivalentTo(["app-1", "app-2"]);
    }

    [Theory]
    [InlineData("""{"object":"page","entry":[{"id":"app-1"}]}""")]
    [InlineData("""{"object":"application","entry":{}}""")]
    public void ParseApplicationIds_WrongEnvelope_ReturnsEmpty(string json)
    {
        MetaBusinessIntegrationWebhookEndpoints.ParseApplicationIds(Payload(json)).Should().BeEmpty();
    }

    [Fact]
    public void MatchConfigurations_ReturnsCandidatesForMatchingAppId()
    {
        var wanted = new MetaGraphConfigurationCandidate(
            Guid.NewGuid(), new MetaGraphOptions { AppId = "app-1" });
        var other = new MetaGraphConfigurationCandidate(
            Guid.NewGuid(), new MetaGraphOptions { AppId = "app-2" });

        var matched = MetaBusinessIntegrationWebhookEndpoints.MatchConfigurations(
            [wanted, other],
            new HashSet<string>(StringComparer.Ordinal) { "app-1" });

        matched.Should().ContainSingle().Which.Should().Be(wanted);
    }

    [Fact]
    public void MatchConfigurations_NoMatchingAppId_ReturnsEmpty()
    {
        var candidate = new MetaGraphConfigurationCandidate(
            null, new MetaGraphOptions { AppId = "app-1" });

        MetaBusinessIntegrationWebhookEndpoints.MatchConfigurations(
                [candidate],
                new HashSet<string>(StringComparer.Ordinal) { "app-999" })
            .Should().BeEmpty();
    }

    [Fact]
    public void MatchConfigurations_EmptyApplicationIds_ReturnsEmpty()
    {
        var candidate = new MetaGraphConfigurationCandidate(
            null, new MetaGraphOptions { AppId = "app-1" });

        MetaBusinessIntegrationWebhookEndpoints
            .MatchConfigurations([candidate], new HashSet<string>(StringComparer.Ordinal))
            .Should().BeEmpty();
    }
}
