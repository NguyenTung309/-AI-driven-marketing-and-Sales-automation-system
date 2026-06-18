using FluentAssertions;
using System.Diagnostics;

namespace Clawbot.Infrastructure.Tests.Ci;

public sealed class CoverageGateWorkflowTests
{
    [Fact]
    public void Test_workflow_enforces_current_line_coverage_baseline_gate()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var script = File.ReadAllText(Path.Combine(root, "deploy", "ci", "enforce-coverage.ps1"));

        workflow.Should().Contain("name: Coverage Gate");
        workflow.Should().Contain("deploy/ci/enforce-coverage.ps1");
        workflow.Should().Contain("-MinimumLineCoverage 30");
        script.Should().Contain("[double]$MinimumLineCoverage = 30");
        script.Should().Contain("Set-StrictMode -Version Latest");
        script.Should().Contain("InvariantCulture");
        script.Should().Contain("class[@filename]");
        script.Should().Contain("lines-covered");
        script.Should().Contain("lines-valid");
        script.Should().Contain("exit 1");
    }

    [Fact]
    public void Coverage_gate_merges_same_source_lines_across_test_project_reports()
    {
        var root = FindRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawbot-coverage-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ApiTests"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "InfrastructureTests"));

        try
        {
            File.WriteAllText(
                Path.Combine(tempRoot, "ApiTests", "coverage.cobertura.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <coverage line-rate="0.5" lines-covered="1" lines-valid="2">
                  <packages>
                    <package name="Clawbot.Api">
                      <classes>
                        <class name="Clawbot.Api.Sample" filename="src/api/Sample.cs">
                          <lines>
                            <line number="10" hits="1" />
                            <line number="11" hits="0" />
                          </lines>
                        </class>
                      </classes>
                    </package>
                  </packages>
                </coverage>
                """);

            File.WriteAllText(
                Path.Combine(tempRoot, "InfrastructureTests", "coverage.cobertura.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <coverage line-rate="0.5" lines-covered="1" lines-valid="2">
                  <packages>
                    <package name="Clawbot.Api">
                      <classes>
                        <class name="Clawbot.Api.Sample" filename="src/api/Sample.cs">
                          <lines>
                            <line number="10" hits="0" />
                            <line number="11" hits="1" />
                          </lines>
                        </class>
                      </classes>
                    </package>
                  </packages>
                </coverage>
                """);

            var result = RunPowerShell(
                Path.Combine(root, "deploy", "ci", "enforce-coverage.ps1"),
                "-CoverageRoot",
                tempRoot,
                "-MinimumLineCoverage",
                "75");

            result.ExitCode.Should().Be(
                0,
                "the coverage gate must merge line hits across test project reports instead of summing duplicated report totals. Stdout: {0}; Stderr: {1}",
                result.Stdout,
                result.Stderr);
            result.Stdout.Should().Contain("Line coverage: 100");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Testcontainers_preflight_script_checks_docker_before_integration_suite()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var script = File.ReadAllText(Path.Combine(root, "deploy", "ci", "verify-testcontainers.ps1"));

        workflow.Should().Contain("deploy/ci/verify-testcontainers.ps1");
        script.Should().Contain("Docker CLI not found");
        script.Should().Contain("docker version");
        script.Should().Contain("docker info");
        script.Should().Contain("Testcontainers");
        script.Should().Contain("Clawbot.Integration.Tests.csproj");
        script.Should().Contain("RunIntegrationTests");
        script.Should().Contain("dotnet test");
        script.Should().Contain("--configuration");
        script.Should().Contain("integration-results.trx");
        script.Should().Contain("XPlat Code Coverage");
        script.Should().Contain("--results-directory");
        script.Should().Contain("exit 1");
    }

    [Fact]
    public void Go_live_readiness_script_reports_remaining_external_blockers()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var script = File.ReadAllText(Path.Combine(root, "deploy", "ci", "verify-go-live-readiness.ps1"));
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "module-checklist.md"));

        workflow.Should().Contain("name: Go-live Readiness Report");
        workflow.Should().Contain("./deploy/ci/verify-go-live-readiness.ps1 -ReportOnly -SkipDockerProbe");
        workflow.IndexOf("name: Go-live Readiness Report", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Integration Tests (Testcontainers)", StringComparison.Ordinal));

        script.Should().Contain("Set-StrictMode -Version Latest");
        script.Should().Contain("ReportOnly");
        script.Should().Contain("Strict");
        script.Should().Contain("GO-LIVE READINESS FAILED");
        script.Should().Contain("GO-LIVE READINESS PASSED");
        script.Should().Contain("verify-testcontainers.ps1");
        script.Should().Contain("validate-kb-authoring.ps1");
        script.Should().Contain("PANCAKE_ACCESS_TOKEN");
        script.Should().Contain("PANCAKE_WEBHOOK_SECRET");
        script.Should().Contain("PANCAKE_WEBHOOK_PAYLOAD");
        script.Should().Contain("ANTHROPIC_API_KEY");
        script.Should().Contain("EMBEDDING_API_KEY");
        script.Should().Contain("CONTENT_LLM_API_KEY");
        script.Should().Contain("META_ACCESS_TOKEN");
        script.Should().Contain("TIKTOK_ACCESS_TOKEN");
        script.Should().Contain("CONTENT_PUBLISHER_BASE_URL");

        checklist.Should().Contain("verify-go-live-readiness.ps1");
        checklist.Should().Contain("go-live readiness");
    }

    [Fact]
    public void Go_live_readiness_accepts_dotnet_config_env_aliases_documented_in_env_example()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "deploy", "ci", "verify-go-live-readiness.ps1");
        var envExample = File.ReadAllText(Path.Combine(root, "deploy", ".env.example"));

        foreach (var key in new[]
        {
            "PANCAKE_BASE_URL",
            "PANCAKE_ACCESS_TOKEN",
            "PANCAKE_PAGE_ID",
            "PANCAKE_TENANT_SLUG",
            "CLAWBOT_PUBLIC_BASE_URL",
            "PANCAKE_WEBHOOK_SECRET",
            "PANCAKE_WEBHOOK_PAYLOAD",
            "ANTHROPIC_API_KEY",
            "Anthropic__ApiKey",
            "EMBEDDING_API_KEY",
            "Embedding__ApiKey",
            "CONTENT_LLM_API_KEY",
            "Content__Llm__ApiKey",
            "META_ACCESS_TOKEN",
            "Ads__Meta__AccessToken",
            "META_PAGE_ID",
            "TIKTOK_ACCESS_TOKEN",
            "Ads__TikTok__AccessToken",
            "TIKTOK_ADVERTISER_ID",
            "Ads__TikTok__AdvertiserId",
            "CONTENT_PUBLISHER_BASE_URL",
            "Content__Publisher__Endpoint",
            "CONTENT_PUBLISHER_API_KEY",
            "Content__Publisher__Token"
        })
        {
            envExample.Should().Contain(key);
        }

        var result = RunPowerShell(
            scriptPath,
            new Dictionary<string, string>
            {
                ["Channels__Pancake__BaseUrl"] = "https://pancake.vn/api/v1",
                ["Channels__Pancake__AccessToken"] = "pancake-token",
                ["PANCAKE_PAGE_ID"] = "page-123",
                ["PANCAKE_TENANT_SLUG"] = "hoc-ba",
                ["CLAWBOT_PUBLIC_BASE_URL"] = "https://clawbot.example",
                ["Channels__Pancake__WebhookSecret"] = "webhook-secret",
                ["PANCAKE_WEBHOOK_PAYLOAD"] = """{"events":[]}""",
                ["Anthropic__ApiKey"] = "anthropic-key",
                ["Embedding__ApiKey"] = "embedding-key",
                ["Content__Llm__ApiKey"] = "content-llm-key",
                ["Ads__Meta__AccessToken"] = "meta-token",
                ["META_PAGE_ID"] = "meta-page-123",
                ["Ads__TikTok__AccessToken"] = "tiktok-token",
                ["Ads__TikTok__AdvertiserId"] = "advertiser-123",
                ["Content__Publisher__Endpoint"] = "https://publisher.example/api/posts",
                ["Content__Publisher__Token"] = "publisher-token"
            },
            "-ReportOnly",
            "-SkipDockerProbe");

        result.ExitCode.Should().Be(0, "ReportOnly mode should not fail CI while reporting remaining external blockers. Stdout: {0}; Stderr: {1}", result.Stdout, result.Stderr);
        result.Stdout.Should().Contain("GO-LIVE READINESS FAILED");
        result.Stdout.Should().NotMatchRegex(@"PANCAKE_BASE_URL\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"PANCAKE_ACCESS_TOKEN\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"PANCAKE_WEBHOOK_SECRET\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"ANTHROPIC_API_KEY\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"EMBEDDING_API_KEY\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"CONTENT_LLM_API_KEY\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"META_ACCESS_TOKEN\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"TIKTOK_ACCESS_TOKEN\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"TIKTOK_ADVERTISER_ID\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"CONTENT_PUBLISHER_BASE_URL\s+MISSING");
        result.Stdout.Should().NotMatchRegex(@"CONTENT_PUBLISHER_API_KEY\s+MISSING");
    }

    [Fact]
    public void Identity_ddl_preflight_checks_reconcile_migrations_before_docker_tests()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var script = File.ReadAllText(Path.Combine(root, "deploy", "ci", "verify-identity-ddl.ps1"));

        workflow.Should().Contain("deploy/ci/verify-identity-ddl.ps1");
        script.Should().Contain("Set-StrictMode -Version Latest");
        script.Should().Contain("0001_init.sql");
        script.Should().Contain("0013_identity_reconcile.sql");
        script.Should().Contain("0014_identity_user_indexes.sql");
        script.Should().Contain("CREATE TABLE users");
        script.Should().Contain("ALTER TABLE users ADD");
        script.Should().Contain("normalized_email");
        script.Should().Contain("concurrency_stamp");
        script.Should().Contain("phone_number");
        script.Should().Contain("two_factor_enabled");
        script.Should().Contain("lockout_enabled");
        script.Should().Contain("CREATE TABLE AspNetUserTokens");
        script.Should().Contain("CREATE TABLE AspNetUserRoles");
        script.Should().Contain("ix_users_normalized_email");
        script.Should().Contain("GO");
        script.Should().Contain("exit 1");
    }

    [Fact]
    public void Test_workflow_runs_static_migration_guard_before_identity_and_docker_tests()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var script = File.ReadAllText(Path.Combine(root, "deploy", "ci", "verify-migrations.ps1"));

        workflow.Should().Contain("name: Migration Static Guard");
        workflow.Should().Contain("deploy/ci/verify-migrations.ps1");

        workflow.IndexOf("name: Migration Static Guard", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Identity DDL Preflight", StringComparison.Ordinal));
        workflow.IndexOf("name: Migration Static Guard", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Integration Tests (Testcontainers)", StringComparison.Ordinal));

        script.Should().Contain("Set-StrictMode -Version Latest");
        script.Should().Contain("deploy/migrations");
        script.Should().Contain("*.sql");
        script.Should().Contain("GO batch separators");
        script.Should().Contain("SqlServerFixture");
        script.Should().Contain("ALTER-added column");
        script.Should().Contain("CREATE INDEX");
        script.Should().Contain("Extract-AlterAddedColumns");
        script.Should().Contain("exit 1");
    }

    [Fact]
    public void Test_workflow_smoke_tests_kb_seed_authoring_before_docker_tests()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));
        var generator = File.ReadAllText(Path.Combine(root, "deploy", "seed", "generate-kb-seed.ps1"));

        workflow.Should().Contain("name: KB Seed Authoring Smoke Test");
        workflow.Should().Contain("shell: pwsh");
        workflow.Should().Contain("./deploy/seed/generate-kb-seed.ps1 -SmokeTest");

        workflow.IndexOf("name: KB Seed Authoring Smoke Test", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Integration Tests (Testcontainers)", StringComparison.Ordinal));

        generator.Should().Contain("New-SmokeAuthoringFile");
        generator.Should().Contain("expectedInserts = 120");
        generator.Should().Contain("KB seed generator SmokeTest passed");
    }

    [Fact]
    public void Test_workflow_dry_runs_pancake_ops_scripts_without_live_credentials()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));

        workflow.Should().Contain("name: Pancake Ops Script Dry Run");
        workflow.Should().Contain("PANCAKE_BASE_URL");
        workflow.Should().Contain("PANCAKE_ACCESS_TOKEN");
        workflow.Should().Contain("PANCAKE_WEBHOOK_SECRET");
        workflow.Should().Contain("PANCAKE_WEBHOOK_PAYLOAD");
        workflow.Should().Contain("./deploy/pancake-webhook-subscribe.ps1 -DryRun");
        workflow.Should().Contain("./deploy/pancake-webhook-replay.ps1 -DryRun");

        workflow.IndexOf("name: Pancake Ops Script Dry Run", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Integration Tests (Testcontainers)", StringComparison.Ordinal));
    }

    [Fact]
    public void Test_workflow_builds_frontend_assets_before_running_backend_tests()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "test.yml"));

        workflow.Should().Contain("NODE_VERSION");
        workflow.Should().Contain("actions/setup-node@v4");
        workflow.Should().Contain("cache: npm");
        workflow.Should().Contain("cache-dependency-path: src/frontend/clawbot-web/package-lock.json");
        workflow.Should().Contain("working-directory: src/frontend/clawbot-web");
        workflow.Should().Contain("npm ci");
        workflow.Should().Contain("npm run lint");
        workflow.Should().Contain("npm run build");

        workflow.IndexOf("name: Frontend Install", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Frontend Lint", StringComparison.Ordinal));
        workflow.IndexOf("name: Frontend Lint", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Frontend Build", StringComparison.Ordinal));
        workflow.IndexOf("name: Frontend Build", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("name: Unit Tests (no Docker)", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunPowerShell(string scriptPath, params string[] arguments)
        => RunPowerShell(scriptPath, environment: null, arguments);

    private static (int ExitCode, string Stdout, string Stderr) RunPowerShell(
        string scriptPath,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var executable = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        if (environment is not null)
        {
            foreach (var item in environment)
            {
                process.StartInfo.Environment[item.Key] = item.Value;
            }
        }

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }
}
