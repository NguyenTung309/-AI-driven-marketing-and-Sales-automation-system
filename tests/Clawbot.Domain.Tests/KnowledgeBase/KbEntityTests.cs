using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;

namespace Clawbot.Domain.Tests.KnowledgeBase;

public sealed class KbModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var module = KbModule.Create(TenantId, "ielts-speaking", "IELTS Speaking", Now);

        module.TenantId.Should().Be(TenantId);
        module.Code.Should().Be("ielts-speaking");
        module.Name.Should().Be("IELTS Speaking");
        module.Status.Should().Be("active");
        module.CreatedAt.Should().Be(Now);
        module.DeletedAt.Should().BeNull();
        module.Versions.Should().BeEmpty();
        module.TestCases.Should().BeEmpty();
    }
}

public sealed class KbTestCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsAllFields()
    {
        var moduleId = Guid.NewGuid();
        var tc = KbTestCase.Create(moduleId, "What is IELTS?", "IELTS is an English test.", Now);

        tc.KbModuleId.Should().Be(moduleId);
        tc.Question.Should().Be("What is IELTS?");
        tc.ExpectedAnswer.Should().Be("IELTS is an English test.");
        tc.IsActive.Should().BeTrue();
        tc.CreatedAt.Should().Be(Now);
    }
}

public sealed class KbVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsDraftStatus()
    {
        var moduleId = Guid.NewGuid();
        var version = KbVersion.Create(moduleId, 1, "# Content", Now);

        version.KbModuleId.Should().Be(moduleId);
        version.Version.Should().Be(1);
        version.ContentMd.Should().Be("# Content");
        version.Status.Should().Be("draft");
        version.DeployedAt.Should().BeNull();
        version.AccuracyScore.Should().BeNull();
        version.Embedding.Should().BeNull();
    }

    [Fact]
    public void Deploy_SetsStatusAndTimestamp()
    {
        var version = KbVersion.Create(Guid.NewGuid(), 1, "# C", Now);

        version.Deploy(Now.AddHours(1));

        version.Status.Should().Be("deployed");
        version.DeployedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void RecordAccuracy_SetsScore()
    {
        var version = KbVersion.Create(Guid.NewGuid(), 1, "# C", Now);

        version.RecordAccuracy(0.85m);

        version.AccuracyScore.Should().Be(0.85m);
    }

    [Fact]
    public void SetEmbeddingJson_SetsEmbedding()
    {
        var version = KbVersion.Create(Guid.NewGuid(), 1, "# C", Now);

        version.SetEmbeddingJson("[0.1,0.2,0.3]");

        version.Embedding.Should().Be("[0.1,0.2,0.3]");
    }
}
