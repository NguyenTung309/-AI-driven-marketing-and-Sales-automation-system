using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Frontend;

public sealed class StitchDesignSystemFrontendTests
{
    [Fact]
    public void Frontend_design_tokens_keep_the_google_stitch_source_and_shell_invariants()
    {
        var root = FindRepositoryRoot();
        var designDoc = File.ReadAllText(Path.Combine(root, "docs", "Design.md"));
        var tokens = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "index.css"));
        var appShell = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "layout", "AppShell.tsx"));
        var sidebar = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "layout", "Sidebar.tsx"));
        var topbar = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "layout", "Topbar.tsx"));

        const string stitchProjectId = "12301695846158842476";

        designDoc.Should().Contain(stitchProjectId);
        tokens.Should().Contain(stitchProjectId);
        tokens.Should().NotContain("15408388482133270285");

        tokens.Should().Contain("--color-primary: #d32f2f");
        tokens.Should().Contain("--color-primary-hover: #b71c1c");
        tokens.Should().Contain("--color-surface: #f8fafc");
        tokens.Should().Contain("--font-sans: \"Inter\"");
        tokens.Should().Contain("--font-mono: \"JetBrains Mono\"");
        tokens.Should().Contain("--spacing-sidebar-width: 260px");
        tokens.Should().Contain("--spacing-topbar-height: 64px");

        appShell.Should().Contain("md:ml-[260px]");
        appShell.Should().Contain("pt-[80px]");
        sidebar.Should().Contain("w-[260px]");
        sidebar.Should().Contain("bg-primary");
        topbar.Should().Contain("h-[64px]");
        topbar.Should().Contain("md:w-[calc(100%-260px)]");
    }

    [Fact]
    public void Admin_branding_form_defaults_to_the_stitch_primary_red()
    {
        var root = FindRepositoryRoot();
        // Branding-form defaults moved into adminHelpers.tsx when AdminConsolePage was split into tab components (b931614).
        var adminHelpers = File.ReadAllText(Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "admin", "adminHelpers.tsx"));

        adminHelpers.Should().Contain("primaryColor: \"#d32f2f\"");
        adminHelpers.Should().NotContain("primaryColor: \"#b91c1c\"");
    }

    [Fact]
    public void Frontend_scope_docs_mark_stitch_surfaces_done_without_counting_mobile()
    {
        var root = FindRepositoryRoot();
        var designDoc = File.ReadAllText(Path.Combine(root, "docs", "Design.md"));
        var moduleChecklist = File.ReadAllText(Path.Combine(root, "docs", "module-checklist.md"));

        designDoc.Should().Contain("M16 surfaces + S17 public (DONE)");
        designDoc.Should().NotContain("Pending (M16");

        moduleChecklist.Should().Contain("M16 — Frontend UI (12 surface + S17 public)");
        moduleChecklist.Should().Contain("**DONE");
        moduleChecklist.Should().NotContain("**IN PROGRESS** — base + Login&Profile");

        moduleChecklist.Should().Contain("Mobile app (React Native): OUT OF SCOPE");
        moduleChecklist.Should().Contain("does not count toward web/backend completion percentage");
        moduleChecklist.Should().NotContain("- [x] Mobile app (React Native)");
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
