using System.Text.Json;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

// Planner LLM sinh JSON "lỏng lẻo" (version là số/chuỗi, input là object lồng, id/dependsOn là số...).
// Các Tolerant*Converter phải nuốt được mọi biến thể thay vì làm hỏng cả plan parse.
public sealed class OrchestrationPlanDocumentTests
{
    private static readonly JsonSerializerOptions Options = AgentJson.Options;

    private static OrchestrationPlanDocument Parse(string json) =>
        JsonSerializer.Deserialize<OrchestrationPlanDocument>(json, Options)!;

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("\"1.0\"", 1)]
    [InlineData("1.9", 1)]
    [InlineData("\"abc\"", 1)]   // không parse được -> fallback 1
    [InlineData("true", 1)]      // token lạ -> fallback 1
    public void Version_AcceptsIntFloatStringAndFallsBack(string versionToken, int expected)
    {
        var doc = Parse($"{{\"version\":{versionToken},\"tasks\":[]}}");

        doc.Version.Should().Be(expected);
    }

    [Fact]
    public void TaskId_CoercesNumberToString()
    {
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":42,"agent":"a","description":"d","input":{},"dependsOn":[],"status":"pending"}
            ]}
            """);

        doc.Tasks.Should().ContainSingle();
        doc.Tasks[0].Id.Should().Be("42");
    }

    [Fact]
    public void Input_CoercesMixedScalarsToStrings()
    {
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":"t1","agent":"a","description":"d",
               "input":{"count":30,"flag":true,"off":false,"nothing":null,"name":"hi","nested":{"x":1}},
               "dependsOn":[],"status":"pending"}
            ]}
            """);

        var input = doc.Tasks[0].Input;
        input["count"].Should().Be("30");
        input["flag"].Should().Be("true");
        input["off"].Should().Be("false");
        input["nothing"].Should().Be(string.Empty);
        input["name"].Should().Be("hi");
        // Object lồng -> giữ nguyên raw JSON thay vì fail.
        input["nested"].Should().Contain("\"x\"");
    }

    [Fact]
    public void Input_NonObjectToken_YieldsEmptyDictionary()
    {
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":"t1","agent":"a","description":"d","input":"not-an-object","dependsOn":[],"status":"pending"}
            ]}
            """);

        doc.Tasks[0].Input.Should().BeEmpty();
    }

    [Fact]
    public void DependsOn_CoercesNumbersAndAcceptsScalarAsSingleElement()
    {
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":"t1","agent":"a","description":"d","input":{},"dependsOn":[1,"t0",true],"status":"pending"},
              {"id":"t2","agent":"a","description":"d","input":{},"dependsOn":"t1","status":"pending"}
            ]}
            """);

        doc.Tasks[0].DependsOn.Should().BeEquivalentTo("1", "t0", "true");
        // Scalar đơn thay vì mảng -> mảng 1 phần tử.
        doc.Tasks[1].DependsOn.Should().BeEquivalentTo("t1");
    }

    [Fact]
    public void Normalize_NullCollectionsBecomeEmpty()
    {
        // Positional record: property vắng mặt -> System.Text.Json gán null dù non-nullable.
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":"t1","agent":"a","description":"d"}
            ]}
            """);

        var normalized = doc.Normalize();

        var task = normalized.Tasks[0];
        task.Input.Should().NotBeNull().And.BeEmpty();
        task.DependsOn.Should().NotBeNull().And.BeEmpty();
        task.Status.Should().Be(string.Empty);
    }

    [Fact]
    public void WithTaskStatus_UpdatesOnlyMatchingTask()
    {
        var doc = Parse("""
            {"version":1,"tasks":[
              {"id":"t1","agent":"a","description":"d","input":{},"dependsOn":[],"status":"pending"},
              {"id":"t2","agent":"a","description":"d","input":{},"dependsOn":[],"status":"pending"}
            ]}
            """);

        var updated = doc.WithTaskStatus("t2", "completed", "result-out", null);

        updated.Tasks.Single(t => t.Id == "t1").Status.Should().Be("pending");
        var t2 = updated.Tasks.Single(t => t.Id == "t2");
        t2.Status.Should().Be("completed");
        t2.Output.Should().Be("result-out");
    }

    [Fact]
    public void RoundTrip_SerializeThenDeserialize_PreservesTask()
    {
        var doc = Parse("""
            {"version":3,"tasks":[
              {"id":"t1","agent":"content-agent","description":"write","input":{"topic":"hsk"},
               "dependsOn":["t0"],"status":"pending","output":null,"error":null}
            ]}
            """);

        var json = JsonSerializer.Serialize(doc, Options);
        var again = JsonSerializer.Deserialize<OrchestrationPlanDocument>(json, Options)!;

        again.Version.Should().Be(3);
        again.Tasks[0].Id.Should().Be("t1");
        again.Tasks[0].Input["topic"].Should().Be("hsk");
        again.Tasks[0].DependsOn.Should().BeEquivalentTo("t0");
    }

    [Fact]
    public void ValidationResult_ValidAndInvalidFactories()
    {
        OrchestrationPlanValidationResult.Valid.IsValid.Should().BeTrue();
        OrchestrationPlanValidationResult.Valid.Error.Should().BeNull();

        var invalid = OrchestrationPlanValidationResult.Invalid("boom");
        invalid.IsValid.Should().BeFalse();
        invalid.Error.Should().Be("boom");
    }
}
