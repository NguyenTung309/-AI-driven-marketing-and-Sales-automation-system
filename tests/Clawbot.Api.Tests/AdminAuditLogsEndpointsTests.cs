using System.Net;
using System.Reflection;
using System.Text.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests;

public sealed class AdminAuditLogsEndpointsTests
{
    [Fact]
    public async Task ListAuditLogsAsync_ReturnsLogsWithUserEmailAndIpAddress()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options, new FixedTenantAccessor(tenantId));

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = "admin@example.com",
            Email = "admin@example.com",
            DisplayName = "Admin User",
            IsActive = true,
        };
        db.Users.Add(user);

        var auditLog = AuditLog.Create(
            tenantId,
            user.Id,
            "auth.login",
            "user",
            user.Id,
            DateTimeOffset.UtcNow,
            diffJson: null,
            ip: IPAddress.Loopback,
            userAgent: "Mozilla/5.0");
        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync();

        var handler = typeof(AdminEndpoints).GetMethod(
            "ListAuditLogsAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ListAuditLogsAsync handler was not found.");

        // Act
        var resultTask = (Task<IResult>)handler.Invoke(
            null,
            [db, new FixedTenantAccessor(tenantId), (string?)null, (string?)null, (Guid?)null, 1, 50, CancellationToken.None])!;
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
        document.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        var item = document.RootElement.GetProperty("items")[0];
        item.GetProperty("action").GetString().Should().Be("auth.login");
        item.GetProperty("userEmail").GetString().Should().Be("admin@example.com");
        item.GetProperty("ipAddress").GetString().Should().Be("127.0.0.1");
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
