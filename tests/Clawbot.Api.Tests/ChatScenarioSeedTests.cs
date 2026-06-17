using System.Text.RegularExpressions;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ChatScenarioSeedTests
{
    [Fact]
    public void Chat_scenario_seed_is_tenant_scoped_idempotent_and_asserts_expected_rows()
    {
        var sql = File.ReadAllText(FindRepoFile("deploy", "seed", "chat-scenarios.sql"));

        sql.Should().Contain("DECLARE @tenant_slug");
        sql.Should().Contain("SET XACT_ABORT ON;");
        sql.Should().Contain("BEGIN TRANSACTION;");
        sql.Should().Contain("MERGE INTO chat_scenarios AS target");
        sql.Should().Contain("ON target.tenant_id = @tenant_id AND target.code = source.code");
        sql.Should().Contain("DECLARE @expected_rows INT = 50;");
        sql.Should().Contain("COMMIT TRANSACTION;");

        var codes = Regex.Matches(sql, @"\(N'KB-\d{3}'")
            .Select(match => match.Value.TrimStart('(', 'N', '\'').TrimEnd('\''))
            .ToArray();

        codes.Should().HaveCount(50);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().Contain("KB-001");
        codes.Should().Contain("KB-050");
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
