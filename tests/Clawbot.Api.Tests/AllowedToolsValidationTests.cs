using System.Security.Claims;
using Clawbot.Api.Endpoints;
using Clawbot.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class AllowedToolsValidationTests
{
    private static DefaultHttpContext UserWithPerms(params string[] perms)
    {
        var ctx = new DefaultHttpContext();
        var claims = perms.Select(p => new Claim("perm", p)).ToList();
        // no role_id → HasPermissionAsync falls back to the perm-claim check only.
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return ctx;
    }

    private static IPermissionResolver Resolver() => Substitute.For<IPermissionResolver>();

    [Fact]
    public async Task Validate_RejectsUnknownToolName()
    {
        // EARS[WHEN an allowed tool name is not in the known catalog THE SYSTEM SHALL reject it]
        var error = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["nonexistent-tool"]""", UserWithPerms("content:write"), Resolver(), CancellationToken.None);
        error.Should().StartWith("unknown_tool:");
    }

    [Fact]
    public async Task Validate_DeniesToolWhenAdminLacksRequiredPermission()
    {
        // EARS[WHEN the admin lacks a tool's required permission THE SYSTEM SHALL deny granting it to an agent]
        var error = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["content.publish"]""", UserWithPerms(), Resolver(), CancellationToken.None);
        error.Should().StartWith("tool_permission_denied:content.publish:content:publish");
    }

    [Fact]
    public async Task Validate_AllowsToolWhenAdminHasRequiredPermission()
    {
        var error = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["content.publish","content.review"]""", UserWithPerms("content:write", "content:publish"), Resolver(), CancellationToken.None);
        error.Should().BeNull();
    }

    [Fact]
    public async Task Validate_AllowsNoPermissionToolWithoutAnyPerms()
    {
        // research-agent carries no required permission → any admin can grant it.
        var error = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            """["research-agent"]""", UserWithPerms(), Resolver(), CancellationToken.None);
        error.Should().BeNull();
    }

    [Fact]
    public async Task Validate_RejectsMalformedJson()
    {
        var error = await OrchestrationV2Endpoints.ValidateAllowedToolsAsync(
            "[not-json", UserWithPerms(), Resolver(), CancellationToken.None);
        error.Should().Be("invalid_allowed_tools_json");
    }
}
