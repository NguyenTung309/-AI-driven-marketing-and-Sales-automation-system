using System.Security.Claims;
using Clawbot.Api.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace Clawbot.Api.Tests;

// Regression guard for the perm-gating lockout class: an endpoint gated with
// RequireAuthorization("perm:X") must DENY a principal that lacks the "perm" claim
// (empty role_permissions / code mismatch). The integration WebApplicationFactory
// bypasses authz (Test scheme), so this asserts the handler directly.
public sealed class PermissionAuthorizationHandlerTests
{
    private static async Task<bool> EvaluateAsync(string requiredPerm, params string[] userPerms)
    {
        var handler = new PermissionAuthorizationHandler();
        var requirement = new PermissionRequirement(requiredPerm);
        var identity = new ClaimsIdentity(userPerms.Select(p => new Claim("perm", p)), "Test");
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_user_has_matching_perm()
    {
        var granted = await EvaluateAsync("lead.read", "inbox.read", "lead.read");
        granted.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_when_user_missing_the_required_perm()
    {
        var granted = await EvaluateAsync("lead.write", "lead.read");
        granted.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_when_user_has_no_perm_claims_at_all()
    {
        // The exact lockout case: role_permissions unseeded → empty perm claims → 403.
        var granted = await EvaluateAsync("lead.read");
        granted.Should().BeFalse();
    }
}
