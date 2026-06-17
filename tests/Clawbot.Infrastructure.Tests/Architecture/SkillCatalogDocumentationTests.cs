using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Architecture;

public sealed class SkillCatalogDocumentationTests
{
    [Fact]
    public void Skill_catalog_docs_do_not_describe_current_skill_wiring_as_notimplemented_stubs()
    {
        var root = FindRepositoryRoot();
        var architecture = File.ReadAllText(Path.Combine(root, "docs", "arch.md"));
        var docGenerator = File.ReadAllText(Path.Combine(root, "docs", "generate-doc.js"));
        var skillsModule = File.ReadAllText(Path.Combine(
            root,
            "src",
            "agents",
            "Clawbot.Agents.Core",
            "Skills",
            "SkillsModule.cs"));

        architecture.Should().NotContain("NotImplementedException");
        docGenerator.Should().NotContain("NotImplementedException");
        skillsModule.Should().NotContain("NotImplementedException");

        skillsModule.Should().Contain("KeywordIntentClassifier");
        skillsModule.Should().Contain("ClaudeConversationSummarizer");
        skillsModule.Should().Contain("QdrantLeadDeduplicator");
        skillsModule.Should().Contain("ClaudeImagePromptGenerator");
        skillsModule.Should().Contain("MlNetForecaster");
    }

    [Fact]
    public void Module_checklist_does_not_contradict_completed_agent_flow_items()
    {
        var root = FindRepositoryRoot();
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "module-checklist.md"));

        checklist.Should().Contain("**0 MISSING (code thật còn thiếu):**");
        checklist.Should().Contain("Chat-2 comment auto-reply <30s + DM | ⚠️ partial | Code path wired");
        checklist.Should().Contain("Score-change reason logging");
        checklist.Should().Contain("M25 — Agent control & observability");
        checklist.Should().Contain("ChatAgent flag-honor covered");
        checklist.Should().Contain("Lead scoring + dedup + least-busy assign");

        checklist.Should().NotContain("comment auto-reply <30s + DM mời riêng** (Luồng 2) chưa wire");
        checklist.Should().NotContain("Pancake ingest comment nhưng chưa có flow tự trả lời");
        checklist.Should().NotContain("Lead score-change reason** (Lead-L1) — chưa lưu lý do");
        checklist.Should().NotContain("ChatAgent flag-honor deferred");
        checklist.Should().NotContain("Lead scoring + dedup + round-robin assign");
    }

    [Fact]
    public void Planning_docs_do_not_report_resolved_admin_or_agent_flow_work_as_open()
    {
        var root = FindRepositoryRoot();
        var backendAdminOps = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "ai",
            "planning",
            "2026-06-13-feature-backend-admin-ops.md"));
        var agentFlowGaps = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "ai",
            "planning",
            "2026-06-13-feature-agent-flow-gaps.md"));

        backendAdminOps.Should().Contain("RESOLVED");
        backendAdminOps.Should().NotContain("BLOCKER (");
        backendAdminOps.Should().NotContain("Do M23 blocked");

        agentFlowGaps.Should().Contain("status: implemented");
        agentFlowGaps.Should().Contain("- [x] **T1.1**");
        agentFlowGaps.Should().Contain("- [x] **T1.6**");
        agentFlowGaps.Should().NotContain("- [ ] **T1.1**");
        agentFlowGaps.Should().NotContain("- [ ] **T1.6**");
    }

    [Fact]
    public void Architecture_docs_keep_pancake_and_mobile_scope_current()
    {
        var root = FindRepositoryRoot();
        var architecture = File.ReadAllText(Path.Combine(root, "docs", "arch.md"));
        var specAudit = File.ReadAllText(Path.Combine(root, "docs", "spec-audit.md"));
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "module-checklist.md"));

        architecture.Should().Contain("Pancake unified channel");
        architecture.Should().Contain("T6 | Pancake omnichannel hardening");
        architecture.Should().NotContain("T6 | TikTok+IG+YT");

        specAudit.Should().Contain("Pancake unified broker");
        specAudit.Should().NotContain("Zalo only — 🔲 4 channel còn lại");
        specAudit.Should().NotContain("Chỉ có 1/5 channel adapter");
        specAudit.Should().NotContain("Backlog T6");

        checklist.Should().Contain("Mobile app (React Native): OUT OF SCOPE");
        checklist.Should().Contain("does not count toward web/backend completion percentage");
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
