using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Audit;

/// <summary>
/// Documents the audit DiffJson shape the FE parseDiff depends on.
/// </summary>
public sealed class AuditDiffShapeTests
{
    [Fact]
    public void Dictionary_shape_matches_frontend_object_parser()
    {
        // Mirror AuditSaveChangesInterceptor: Dictionary prop -> scalar | {from,to}
        var createDiff = new Dictionary<string, object?>
        {
            ["DisplayName"] = "Ada",
            ["Phone"] = "0912345678",
        };
        var updateDiff = new Dictionary<string, object?>
        {
            ["DisplayName"] = new { from = "Ada", to = "Bob" },
        };

        var createJson = JsonSerializer.Serialize(createDiff);
        var updateJson = JsonSerializer.Serialize(updateDiff);

        using var createDoc = JsonDocument.Parse(createJson);
        createDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        createDoc.RootElement.GetProperty("DisplayName").GetString().Should().Be("Ada");

        using var updateDoc = JsonDocument.Parse(updateJson);
        updateDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        updateDoc.RootElement.GetProperty("DisplayName").GetProperty("from").GetString().Should().Be("Ada");
        updateDoc.RootElement.GetProperty("DisplayName").GetProperty("to").GetString().Should().Be("Bob");
    }
}
