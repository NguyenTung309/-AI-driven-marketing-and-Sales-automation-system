using System.Security.Claims;
using Clawbot.Api.Endpoints;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Unit test thuần cho OrchestrationV2Endpoints.ValidateAllowedToolsAsync (internal, InternalsVisibleTo
/// Clawbot.Api.Tests) — không đi qua HTTP host. Dùng IPermissionResolver mock bằng NSubstitute và
/// DefaultHttpContext gắn claim "role_id" giả lập admin. Tool "content-agent" (permission "content:write")
/// lấy từ catalog thật ToolRegistryFactory.KnownTools (Clawbot.Agents.Core.Orchestrator.ToolRegistry.cs)
/// để không bịa tên tool/permission.
/// </summary>
public sealed class OrchestrationV2ValidateToolsTests
{
    private static readonly Guid AdminRoleId = RbacSeeder.RoleIds[RbacSeeder.Admin];

    [Fact]
    public async Task ValidateAllowedToolsAsync_InvalidJson_ReturnsInvalidAllowedToolsJson()
    {
        var http = CreateHttpContext(AdminRoleId);
        var permissions = CreatePermissions(AdminRoleId, []);

        var result = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync("not-json", http, permissions, CancellationToken.None);

        result.Should().Be("invalid_allowed_tools_json");
    }

    [Fact]
    public async Task ValidateAllowedToolsAsync_UnknownToolName_ReturnsUnknownTool()
    {
        var http = CreateHttpContext(AdminRoleId);
        var permissions = CreatePermissions(AdminRoleId, []);

        var result = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["cong_cu_khong_ton_tai"]""", http, permissions, CancellationToken.None);

        result.Should().Be("unknown_tool:cong_cu_khong_ton_tai");
    }

    [Fact]
    public async Task ValidateAllowedToolsAsync_KnownToolWithoutPermission_ReturnsToolPermissionDenied()
    {
        // permissions resolver không cấp "content:write" và http.User cũng không có claim "perm" tương ứng
        var http = CreateHttpContext(AdminRoleId);
        var permissions = CreatePermissions(AdminRoleId, []);

        var result = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["content-agent"]""", http, permissions, CancellationToken.None);

        result.Should().Be("tool_permission_denied:content-agent:content:write");
    }

    [Fact]
    public async Task ValidateAllowedToolsAsync_KnownToolWithPermissionGranted_ReturnsNull()
    {
        var http = CreateHttpContext(AdminRoleId);
        var permissions = CreatePermissions(AdminRoleId, ["content:write"]);

        var result = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["content-agent"]""", http, permissions, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAllowedToolsAsync_EmptyArray_ReturnsNull()
    {
        var http = CreateHttpContext(AdminRoleId);
        var permissions = CreatePermissions(AdminRoleId, []);

        var result = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync("[]", http, permissions, CancellationToken.None);

        result.Should().BeNull();
    }

    private static DefaultHttpContext CreateHttpContext(Guid roleId)
    {
        var identity = new ClaimsIdentity([new Claim("role_id", roleId.ToString())], "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static IPermissionResolver CreatePermissions(Guid roleId, IEnumerable<string> granted)
    {
        var resolver = Substitute.For<IPermissionResolver>();
        IReadOnlySet<string> grantedSet = new HashSet<string>(granted, StringComparer.Ordinal);
        resolver.GetPermissionsAsync(roleId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(grantedSet));
        return resolver;
    }
}
