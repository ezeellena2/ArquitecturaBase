using System.Security.Claims;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Authorization;

public sealed class HasAnyPermissionAttributeTests
{
    [Fact]
    public async Task The_attribute_requires_a_session_and_preserves_every_alternative()
    {
        var attribute = new HasAnyPermissionAttribute(Permissions.Users.Read, Permissions.Roles.Manage);
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(attribute.Policy!);

        Assert.Null(attribute.Roles);
        Assert.Equal([Permissions.Users.Read, Permissions.Roles.Manage], attribute.Permissions);
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Equal(attribute.Permissions, Assert.Single(policy.Requirements.OfType<PermissionRequirement>()).Permissions);
    }

    [Theory]
    [InlineData(Permissions.Users.Read, true)]
    [InlineData(Permissions.Roles.Manage, true)]
    [InlineData(Permissions.Settings.Manage, false)]
    public async Task Either_permission_satisfies_one_requirement(string granted, bool expected)
    {
        var fixture = new PermissionAuthorizationFixture(granted);
        var requirement = new PermissionRequirement([Permissions.Users.Read, Permissions.Roles.Manage]);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);

        await new PermissionAuthorizationHandler(fixture, fixture).HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task Separate_attributes_still_require_every_requirement()
    {
        var fixture = new PermissionAuthorizationFixture(Permissions.Users.Read);
        var first = new PermissionRequirement([Permissions.Users.Read, Permissions.Roles.Manage]);
        var second = new PermissionRequirement([Permissions.Settings.Manage]);
        var context = new AuthorizationHandlerContext([first, second], new ClaimsPrincipal(), null);

        await new PermissionAuthorizationHandler(fixture, fixture).HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.Equal(second, Assert.Single(context.PendingRequirements));
    }

    [Fact]
    public async Task An_empty_alternative_or_an_anonymous_user_does_not_authorize()
    {
        var fixture = new PermissionAuthorizationFixture(Permissions.Users.Read) { UserId = null };
        var requirement = new PermissionRequirement([Permissions.Users.Read]);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);
        await new PermissionAuthorizationHandler(fixture, fixture).HandleAsync(context);
        Assert.False(context.HasSucceeded);

        var empty = new PermissionRequirement([]);
        var emptyContext = new AuthorizationHandlerContext([empty], new ClaimsPrincipal(), null);
        fixture.UserId = Guid.CreateVersion7();
        await new PermissionAuthorizationHandler(fixture, fixture).HandleAsync(emptyContext);
        Assert.False(emptyContext.HasSucceeded);
    }

    [Fact]
    public async Task The_handler_preserves_request_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new PermissionAuthorizationFixture(Permissions.Users.Read);
        var requirement = new PermissionRequirement([Permissions.Users.Read]);
        var request = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), request);
        await new PermissionAuthorizationHandler(fixture, fixture).HandleAsync(context);
        Assert.Equal(cancellation.Token, fixture.ObservedToken);
    }
}
