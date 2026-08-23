using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/experiments. Group chỉ RequireAuthorization (không RequirePermission) nên admin client
/// chạy được mọi operation. Service ném InvalidOperationException khi experiment/variant không
/// tồn tại nhưng endpoint không catch — nhánh đó thành 500 phụ thuộc exception middleware nên
/// không assert ở đây.
/// </summary>
public sealed class ExperimentsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ExperimentsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static Guid ReadGuidId(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? Guid.Parse(prop.GetString()!)
            : Guid.Empty;

    /// <summary>Tạo experiment hợp lệ qua POST; code unique để không đụng conflict giữa các test.</summary>
    private static async Task<JsonElement> CreateExperimentAsync(HttpClient client, string targetType = "chat_scenario")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{suffix}",
            name = $"Thu nghiem {suffix}",
            targetType,
            targetId = Guid.NewGuid(),
            variants = new object[]
            {
                new { code = "a", name = "Bien A", weight = 50 },
                new { code = "b", name = "Bien B", weight = 50 },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    // ------------------------------------------------------------------
    // POST create + GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_RoundTrips_AndAppearsInList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");
        id.Should().NotBe(Guid.Empty);
        created.GetProperty("status").GetString().Should().Be("active");
        created.GetProperty("variants").GetArrayLength().Should().Be(2);

        var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/experiments/", UriKind.Relative));
        list.ValueKind.Should().Be(JsonValueKind.Array);
        list.EnumerateArray().Any(e => ReadGuidId(e, "id") == id).Should().BeTrue();
    }

    [Fact]
    public async Task List_FiltersByTargetType()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var kbExperiment = await CreateExperimentAsync(client, "kb_version");
        var kbCode = kbExperiment.GetProperty("code").GetString();

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/experiments/?targetType=kb_version", UriKind.Relative));

        filtered.EnumerateArray().Should().OnlyContain(e => e.GetProperty("targetType").GetString() == "kb_version");
        filtered.EnumerateArray().Any(e => e.GetProperty("code").GetString() == kbCode).Should().BeTrue();
    }

    [Fact]
    public async Task Create_EmptyCode_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = "",
            name = "Thieu code",
            targetType = "chat_scenario",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 1 }, new { code = "b", name = "B", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("code_required");
    }

    [Fact]
    public async Task Create_EmptyName_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{Guid.NewGuid():N}",
            name = "",
            targetType = "chat_scenario",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 1 }, new { code = "b", name = "B", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("name_required");
    }

    [Fact]
    public async Task Create_InvalidTargetType_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{Guid.NewGuid():N}",
            name = "Sai target type",
            targetType = "khong_hop_le",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 1 }, new { code = "b", name = "B", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("target_type_invalid");
    }

    [Fact]
    public async Task Create_EmptyTargetId_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{Guid.NewGuid():N}",
            name = "Thieu target id",
            targetType = "chat_scenario",
            targetId = Guid.Empty,
            variants = new object[] { new { code = "a", name = "A", weight = 1 }, new { code = "b", name = "B", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("target_id_required");
    }

    [Fact]
    public async Task Create_SingleVariant_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{Guid.NewGuid():N}",
            name = "Mot bien",
            targetType = "chat_scenario",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("variants_min_2");
    }

    [Fact]
    public async Task Create_ZeroWeightVariant_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code = $"exp-{Guid.NewGuid():N}",
            name = "Bien weight 0",
            targetType = "chat_scenario",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 0 }, new { code = "b", name = "B", weight = 1 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("variant_invalid");
    }

    [Fact]
    public async Task Create_DuplicateCode_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var code = created.GetProperty("code").GetString();

        var duplicate = await client.PostAsJsonAsync(new Uri("/api/experiments/", UriKind.Relative), new
        {
            code,
            name = "Trung code",
            targetType = "chat_scenario",
            targetId = Guid.NewGuid(),
            variants = new object[] { new { code = "a", name = "A", weight = 1 }, new { code = "b", name = "B", weight = 1 } },
        });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("experiment_exists");
    }

    // ------------------------------------------------------------------
    // POST /{id}/assign
    // ------------------------------------------------------------------

    [Fact]
    public async Task Assign_EmptySubjectKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/experiments/{id}/assign", UriKind.Relative),
            new { subjectKey = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("subject_key_required");
    }

    [Fact]
    public async Task Assign_ReturnsVariant_AndRepeatAssignIsStable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");
        var url = new Uri($"/api/experiments/{id}/assign", UriKind.Relative);
        var subjectKey = $"khach-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync(url, new { subjectKey });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var variantCode = firstBody.GetProperty("variantCode").GetString();
        variantCode.Should().BeOneOf("a", "b");

        // Cung subject -> cung variant (sticky assignment).
        var second = await client.PostAsJsonAsync(url, new { subjectKey });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("variantCode").GetString().Should().Be(variantCode);
    }

    // ------------------------------------------------------------------
    // POST /{id}/events + GET /{id}/summary
    // ------------------------------------------------------------------

    [Fact]
    public async Task RecordEvent_MissingFields_AreRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");
        var variantId = ReadGuidId(created.GetProperty("variants")[0], "id");
        var url = new Uri($"/api/experiments/{id}/events", UriKind.Relative);

        var missingVariant = await client.PostAsJsonAsync(url, new { variantId = Guid.Empty, subjectKey = "k", eventType = "conversion" });
        missingVariant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingVariant.Content.ReadAsStringAsync()).Should().Contain("variant_id_required");

        var missingSubject = await client.PostAsJsonAsync(url, new { variantId, subjectKey = "", eventType = "conversion" });
        missingSubject.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingSubject.Content.ReadAsStringAsync()).Should().Contain("subject_key_required");

        var missingType = await client.PostAsJsonAsync(url, new { variantId, subjectKey = "k", eventType = "" });
        missingType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingType.Content.ReadAsStringAsync()).Should().Contain("event_type_required");
    }

    [Fact]
    public async Task AssignThenConvert_IsCountedInSummary()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");
        var subjectKey = $"khach-{Guid.NewGuid():N}";

        var assign = await client.PostAsJsonAsync(
            new Uri($"/api/experiments/{id}/assign", UriKind.Relative),
            new { subjectKey });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignment = await assign.Content.ReadFromJsonAsync<JsonElement>();
        var variantId = ReadGuidId(assignment, "variantId");
        var variantCode = assignment.GetProperty("variantCode").GetString();

        // Assign da ghi exposure; ghi them conversion cho cung variant.
        var record = await client.PostAsJsonAsync(
            new Uri($"/api/experiments/{id}/events", UriKind.Relative),
            new { variantId, subjectKey, eventType = "conversion", value = (decimal?)1 });
        record.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/experiments/{id}/summary", UriKind.Relative));
        summary.GetProperty("winnerVariantCode").GetString().Should().Be(variantCode);

        var winner = summary.GetProperty("variants").EnumerateArray()
            .First(v => ReadGuidId(v, "variantId") == variantId);
        winner.GetProperty("exposures").GetInt32().Should().Be(1);
        winner.GetProperty("conversions").GetInt32().Should().Be(1);
        winner.GetProperty("conversionRate").GetDecimal().Should().Be(1m);
    }

    [Fact]
    public async Task RecordEvent_DuplicateExposureOrConversion_IsIgnored()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");
        var variantId = ReadGuidId(created.GetProperty("variants")[0], "id");
        var subjectKey = $"khach-{Guid.NewGuid():N}";
        var url = new Uri($"/api/experiments/{id}/events", UriKind.Relative);

        // Conversion 2 lan cho cung subject+variant: lan 2 bi dedup (khong loi, khong tang count).
        (await client.PostAsJsonAsync(url, new { variantId, subjectKey, eventType = "conversion" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(url, new { variantId, subjectKey, eventType = "conversion" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var summary = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/experiments/{id}/summary", UriKind.Relative));
        var row = summary.GetProperty("variants").EnumerateArray()
            .First(v => ReadGuidId(v, "variantId") == variantId);
        row.GetProperty("conversions").GetInt32().Should().Be(1);
    }

    // ------------------------------------------------------------------
    // POST /{id}/stop
    // ------------------------------------------------------------------

    [Fact]
    public async Task Stop_UnknownExperiment_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/experiments/{Guid.NewGuid()}/stop", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stop_ActiveExperiment_ReturnsStoppedStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateExperimentAsync(client);
        var id = ReadGuidId(created, "id");

        var response = await client.PostAsync(
            new Uri($"/api/experiments/{id}/stop", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("stopped");
    }
}
