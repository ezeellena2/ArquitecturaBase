using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Authorization;

public sealed class HasPermissionAttributeTests
{
    [Fact]
    public void The_attribute_asks_for_the_policy_of_its_permission()
    {
        var attribute = new HasPermissionAttribute(Permissions.Users.Read);

        Assert.IsAssignableFrom<AuthorizeAttribute>(attribute);
        Assert.Equal(Permissions.Users.Read, attribute.Permission);
        Assert.Equal(PermissionPolicyProvider.PolicyPrefix + Permissions.Users.Read, attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Fact]
    public async Task The_policy_of_the_attribute_requires_an_authenticated_user_with_the_permission()
    {
        var attribute = new HasPermissionAttribute(Permissions.Roles.Manage);
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(attribute.Policy!);

        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        var permission = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(Permissions.Roles.Manage, permission.Permission);
    }
}
