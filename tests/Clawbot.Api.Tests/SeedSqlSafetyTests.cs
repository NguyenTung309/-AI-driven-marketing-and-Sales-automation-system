using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class SeedSqlSafetyTests
{
    [Theory]
    [InlineData("ads-rules.sql")]
    [InlineData("chat-scenarios.sql")]
    [InlineData("content-briefs.sql")]
    [InlineData("document-templates.sql")]
    [InlineData("lead-scoring-rules.sql")]
    public void Tenant_seed_sql_is_transactional_and_fails_when_tenant_is_missing(string fileName)
    {
        var sql = File.ReadAllText(FindRepoFile("deploy", "seed", fileName));

        sql.Should().Contain("SET XACT_ABORT ON;");
        sql.Should().Contain("DECLARE @tenant_slug");
        sql.Should().Contain("DECLARE @tenant_id");
        sql.Should().Contain("IF @tenant_id IS NULL");
        sql.Should().Contain("RAISERROR");
        sql.Should().Contain("BEGIN TRANSACTION;");
        sql.Should().Contain("COMMIT TRANSACTION;");
        sql.Should().Contain("MERGE");
        sql.Should().Contain("@tenant_id");
        sql.Should().Contain("DECLARE @expected_rows INT");
        sql.Should().Contain("DECLARE @actual_rows INT");
        sql.Should().Contain("ROLLBACK TRANSACTION;");
        sql.Should().NotContain("PRINT 'WARNING");
        sql.Should().NotContain("GO");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
