using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestrationPlanValidatorTests
{
    // Danh muc gia: 1 agent kha dung de plan hop le; nhieu test dung danh muc nay
    private static AgentCatalogEntry[] Catalog(params string[] codes)
    {
        if (codes.Length == 0) codes = ["research-agent"];
        return codes.Select(code => new AgentCatalogEntry(
            Code: code,
            ShortName: code.Split('-')[0],
            DisplayName: code,
            AgentType: code + "-type",
            Description: "test",
            InputSchemaJson: "{}",
            Orchestratable: true)).ToArray();
    }

    private static AgentCatalogEntry[] CatalogWithNonOrchestratable()
        => new[]
        {
            new AgentCatalogEntry("research-agent", "research", "Research", "research-type", "test", "{}", Orchestratable: false),
            new AgentCatalogEntry("writer-agent", "writer", "Writer", "writer-type", "test", "{}", Orchestratable: true),
        };

    private static OrchestrationPlanDocument Plan(params OrchestrationPlanTask[] tasks)
        => new(Version: 1, Tasks: tasks);

    private static OrchestrationPlanTask Task(
        string id,
        string agent = "research-agent",
        IReadOnlyDictionary<string, string>? input = null,
        IReadOnlyList<string>? dependsOn = null)
        => new(
            Id: id,
            Agent: agent,
            Description: "desc " + id,
            Input: input ?? new Dictionary<string, string>(),
            DependsOn: dependsOn ?? Array.Empty<string>(),
            Status: "pending",
            Output: null,
            Error: null);

    // --- null guard ---
    [Fact]
    public void Validate_NullPlan_Throws()
    {
        FluentActions.Invoking(() => OrchestrationPlanValidator.Validate(null!, Catalog()))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_NullCatalog_Throws()
    {
        FluentActions.Invoking(() => OrchestrationPlanValidator.Validate(Plan(Task("t1")), null!))
            .Should().Throw<ArgumentNullException>();
    }

    // --- empty ---
    [Fact]
    public void Validate_EmptyTasks_ReturnsInvalid_EmptyPlan()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("empty_plan");
    }

    [Fact]
    public void Validate_NullTasks_ReturnsInvalid_EmptyPlan()
    {
        var doc = new OrchestrationPlanDocument(1, null!);
        var result = OrchestrationPlanValidator.Validate(doc, Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("empty_plan");
    }

    // --- qua so luong ---
    [Fact]
    public void Validate_TooManyTasks_ReturnsInvalid()
    {
        var tasks = Enumerable.Range(0, 51).Select(i => Task($"t{i}")).ToArray();
        var result = OrchestrationPlanValidator.Validate(Plan(tasks), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().StartWith("too_many_tasks:51:50");
    }

    [Fact]
    public void Validate_Exactly50Tasks_IsValid()
    {
        var tasks = Enumerable.Range(0, 50).Select(i => Task($"t{i}")).ToArray();
        var result = OrchestrationPlanValidator.Validate(Plan(tasks), Catalog());
        result.IsValid.Should().BeTrue();
    }

    // --- trung id ---
    [Fact]
    public void Validate_DuplicateTaskId_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1"), Task("t1")), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("duplicate_task:t1");
    }

    [Fact]
    public void Validate_DuplicateTaskId_CaseInsensitive()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("T1"), Task("t1")), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("duplicate_task");
    }

    [Fact]
    public void Validate_NullTaskEntry_ReturnsInvalid()
    {
        var doc = new OrchestrationPlanDocument(1, new OrchestrationPlanTask?[] { null! }!);
        var result = OrchestrationPlanValidator.Validate(doc, Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("task_required");
    }

    [Fact]
    public void Validate_MissingTaskId_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("")), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("task_id_required");
    }

    // --- input qua lon ---
    [Fact]
    public void Validate_InputTooLarge_ReturnsInvalid()
    {
        var big = new Dictionary<string, string> { ["k"] = new string('x', 8193) };
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", input: big)), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("input_too_large:t1");
    }

    [Fact]
    public void Validate_InputExactly8192_IsValid()
    {
        var exact = new Dictionary<string, string> { ["k"] = new string('x', 8191) }; // key 1 + value 8191 = 8192
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", input: exact)), Catalog());
        result.IsValid.Should().BeTrue();
    }

    // --- unknown agent ---
    [Fact]
    public void Validate_UnknownAgent_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", agent: "ghost-agent")), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().StartWith("unknown_agent:t1:ghost-agent");
    }

    [Fact]
    public void Validate_NonOrchestratableAgent_IsUnknown()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", agent: "research-agent")), CatalogWithNonOrchestratable());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("unknown_agent");
    }

    [Fact]
    public void Validate_ShortName_Matches()
    {
        // Catalog shortName = "research", agent = "research" phai hop le
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", agent: "research")), Catalog());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AgentType_Matches()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", agent: "research-agent-type")), Catalog());
        result.IsValid.Should().BeTrue();
    }

    // --- dangling dependency ---
    [Fact]
    public void Validate_DanglingDependency_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1", dependsOn: new[] { "missing" })), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("dangling_dependency:t1:missing");
    }

    [Fact]
    public void Validate_ValidDependency_IsValid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("t1"), Task("t2", dependsOn: new[] { "t1" })), Catalog());
        result.IsValid.Should().BeTrue();
    }

    // --- cycle ---
    [Fact]
    public void Validate_Cycle_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(
            Plan(Task("a", dependsOn: new[] { "b" }), Task("b", dependsOn: new[] { "a" })),
            Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("cycle_detected");
    }

    [Fact]
    public void Validate_SelfCycle_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(Plan(Task("a", dependsOn: new[] { "a" })), Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("cycle_detected");
    }

    [Fact]
    public void Validate_ThreeNodeCycle_ReturnsInvalid()
    {
        var result = OrchestrationPlanValidator.Validate(
            Plan(
                Task("a", dependsOn: new[] { "c" }),
                Task("b", dependsOn: new[] { "a" }),
                Task("c", dependsOn: new[] { "b" })),
            Catalog());
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("cycle_detected");
    }

    [Fact]
    public void Validate_Diamond_NoCycle_IsValid()
    {
        var result = OrchestrationPlanValidator.Validate(
            Plan(
                Task("a"),
                Task("b", dependsOn: new[] { "a" }),
                Task("c", dependsOn: new[] { "a" }),
                Task("d", dependsOn: new[] { "b", "c" })),
            Catalog());
        result.IsValid.Should().BeTrue();
    }

    // --- valid plan tong hop ---
    [Fact]
    public void Validate_ValidPlan_ReturnsValid()
    {
        var result = OrchestrationPlanValidator.Validate(
            Plan(Task("t1"), Task("t2", dependsOn: new[] { "t1" })),
            Catalog());
        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
    }
}
