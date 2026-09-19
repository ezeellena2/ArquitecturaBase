using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Domain.UnitTests.Authorization;

public sealed partial class PermissionsTests
{
    [Fact]
    public void All_lists_every_permission_once()
    {
        Assert.Equal(
            [Permissions.Users.Read, Permissions.Users.Manage, Permissions.Roles.Read, Permissions.Roles.Manage],
            Permissions.All);
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Permissions_follow_the_area_action_format()
    {
        Assert.All(Permissions.All, permission => Assert.Matches(PermissionFormat(), permission));
    }

    [Fact]
    public void System_roles_are_admin_and_user()
    {
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], SystemRoles.All);
    }

    [GeneratedRegex("^[a-z]+\\.[a-z]+$")]
    private static partial Regex PermissionFormat();
}
