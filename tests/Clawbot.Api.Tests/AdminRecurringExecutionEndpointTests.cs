using System.Reflection;
using System.Text.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests;

public sealed class AdminRecurringExecutionEndpointTests
{
    [Fact]
    public async Task GetRecurringExecutionAsync_UsesZeroWhenProgressHasNotBeenReported()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, new FixedTenantAccessor(tenantId));
        var execution = RecurringJobExecution.CreateManual(
            "health-check",
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow);
        db.RecurringJobExecutions.Add(execution);
        await db.SaveChangesAsync();
        var handler = typeof(AdminJobsEndpoints).GetMethod(
            "GetRecurringExecutionAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Recurring execution handler was not found.");

        // Act
        var resultTask = (Task<IResult>)handler.Invoke(
            null,
            [execution.Id, db, new FixedTenantAccessor(tenantId), CancellationToken.None])!;
        var result = await resultTask;
        using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(http.Response.Body);

        // Assert
        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        document.RootElement.GetProperty("progressPercent").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("progressNote").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
