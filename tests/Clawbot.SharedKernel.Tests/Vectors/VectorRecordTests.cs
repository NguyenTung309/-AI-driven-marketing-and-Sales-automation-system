using Clawbot.SharedKernel.Vectors;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Vectors;

public sealed class VectorRecordTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var metadata = new Dictionary<string, string> { ["tenant"] = "abc" };

        var record = new VectorRecord("vec-1", embedding, metadata);

        record.Id.Should().Be("vec-1");
        record.Embedding.ToArray().Should().Equal(0.1f, 0.2f, 0.3f);
        record.Metadata.Should().ContainKey("tenant");
    }
}

public sealed class VectorMatchTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var match = new VectorMatch("vec-1", 0.95f, new Dictionary<string, string> { ["k"] = "v" });

        match.Id.Should().Be("vec-1");
        match.Score.Should().Be(0.95f);
        match.Metadata.Should().ContainKey("k");
    }
}

public sealed class VectorMetadataFilterTests
{
    [Fact]
    public void Constructor_SetsFieldAndValues()
    {
        var filter = new VectorMetadataFilter("category", ["tech", "science"]);

        filter.Field.Should().Be("category");
        filter.Values.Should().Equal("tech", "science");
    }
}
