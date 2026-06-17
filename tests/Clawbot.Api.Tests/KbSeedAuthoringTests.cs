using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class KbSeedAuthoringTests
{
    [Fact]
    public void Kb_authoring_harness_defines_required_modules_template_and_validator()
    {
        var manifestPath = FindRepoFile("deploy", "seed", "kb-authoring.required.json");
        var templatePath = FindRepoFile("deploy", "seed", "kb-authoring.template.json");
        var validatorPath = FindRepoFile("deploy", "seed", "validate-kb-authoring.ps1");
        var generatorPath = FindRepoFile("deploy", "seed", "generate-kb-seed.ps1");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        root.GetProperty("minTestCasesPerModule").GetInt32().Should().Be(20);
        root.GetProperty("requiredModules").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString())
            .Should().Equal("KB-01", "KB-02", "KB-03", "KB-04", "KB-05", "KB-06");

        using var template = JsonDocument.Parse(File.ReadAllText(templatePath));
        template.RootElement.GetProperty("modules").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString())
            .Should().Equal("KB-01", "KB-02", "KB-03", "KB-04", "KB-05", "KB-06");

        var validator = File.ReadAllText(validatorPath);
        validator.Should().Contain("ConvertFrom-Json");
        validator.Should().Contain("Read-Property");
        validator.Should().Contain("As-Array");
        validator.Should().Contain("Validate-RequiredText");
        validator.Should().Contain("PSObject.Properties");
        validator.Should().Contain("System.Collections.ArrayList");
        validator.Should().Contain("minTestCasesPerModule");
        validator.Should().Contain("requiredModules");
        validator.Should().Contain("contentMd");
        validator.Should().Contain("expectedAnswer");
        validator.Should().Contain("question");
        validator.Should().Contain("placeholder");
        validator.Should().Contain("exit 1");

        var generator = File.ReadAllText(generatorPath);
        generator.Should().Contain("SmokeTest");
        generator.Should().Contain("New-SmokeAuthoringFile");
        generator.Should().Contain("expectedInserts = 120");
        generator.Should().Contain("validate-kb-authoring.ps1");
        generator.Should().Contain("Escape-Sql");
        generator.Should().Contain("MERGE INTO kb_modules");
        generator.Should().Contain("MERGE INTO kb_versions");
        generator.Should().Contain("DELETE FROM kb_test_cases");
        generator.Should().Contain("INSERT INTO kb_test_cases");
        generator.Should().Contain("@tenant_slug");
        generator.Should().Contain("THROW");
        generator.Should().NotContain("$LASTEXITCODE");
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
