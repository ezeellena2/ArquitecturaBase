using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// El lock global de administradores (users:admins) pone en fila el conteo del último administrador activo. Se toma solo
/// cuando el chequeo aplica (la cuenta es un administrador activo que se va), después de los locks de contactos y de
/// cuenta que ya toma el caso de uso (el orden global de backend.md) y antes de contar.
/// </summary>
public sealed class AdminsLockTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivating_an_admin_takes_the_admins_lock_after_the_account_lock_and_before_counting()
    {
        var host = new UserServiceTestHost();
        var (target, _) = TwoAdmins(host);
        Guid[] accountLocksAtThatMoment = [];
        host.Accounts.OnLockAdmins = () => accountLocksAtThatMoment = [.. host.Links.LockedAccounts];

        var result = await host.Access.SetUserActiveAsync(target.Id, isActive: false, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([target.Id], accountLocksAtThatMoment);
        Assert.Equal(["lock-admins", "count-admins"], host.Accounts.AdminEvents);
    }

    [Fact]
    public async Task Deleting_an_admin_takes_the_admins_lock_after_the_account_lock_and_before_counting()
    {
        var host = new UserServiceTestHost();
        var (target, _) = TwoAdmins(host);
        Guid[] accountLocksAtThatMoment = [];
        host.Accounts.OnLockAdmins = () => accountLocksAtThatMoment = [.. host.Links.LockedAccounts];

        var result = await host.Access.DeleteUserAsync(target.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([target.Id], accountLocksAtThatMoment);
        Assert.Equal(["lock-admins", "count-admins"], host.Accounts.AdminEvents);
    }

    [Fact]
    public async Task Taking_the_admin_role_takes_the_admins_lock_after_the_contact_and_account_locks()
    {
        var host = new UserServiceTestHost();
        var target = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(target.Id, SystemRoles.Admin);
        host.Accounts.SetRoles(host.Accounts.AddUser("beto@example.com").Id, SystemRoles.Admin);
        string[] destinationLocks = [];
        Guid[] accountLocks = [];
        host.Accounts.OnLockAdmins = () =>
        {
            destinationLocks = [.. host.Destinations.LockedDestinations];
            accountLocks = [.. host.Links.LockedAccounts];
        };

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            target.Id, "Ana", [SystemRoles.User], Email: target.Email,
            Phone: new PhoneNumberInput("AR", target.PhoneNumber)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["ana@example.com", "+5493515550101"], destinationLocks);
        Assert.Equal([target.Id], accountLocks);
        Assert.Equal(["lock-admins", "count-admins"], host.Accounts.AdminEvents);
    }

    [Fact]
    public async Task The_last_admin_is_still_rejected_under_the_lock()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Access.SetUserActiveAsync(admin.Id, isActive: false, Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.Equal(["lock-admins", "count-admins"], host.Accounts.AdminEvents);
    }

    [Fact]
    public async Task Removing_an_account_that_is_not_an_admin_does_not_take_the_admins_lock()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("user@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        var other = host.Accounts.AddUser("other@example.com");
        host.Accounts.SetRoles(other.Id, SystemRoles.User);

        Assert.True((await host.Access.SetUserActiveAsync(user.Id, isActive: false, Ct)).IsSuccess);
        Assert.True((await host.Access.DeleteUserAsync(other.Id, Ct)).IsSuccess);

        Assert.Empty(host.Accounts.AdminEvents);
    }

    [Fact]
    public async Task Removing_an_inactive_admin_does_not_take_the_admins_lock()
    {
        var host = new UserServiceTestHost();
        var inactive = host.Accounts.AddUser("inactive@example.com", isActive: false);
        host.Accounts.SetRoles(inactive.Id, SystemRoles.Admin);

        Assert.True((await host.Access.DeleteUserAsync(inactive.Id, Ct)).IsSuccess);

        Assert.Empty(host.Accounts.AdminEvents);
    }

    [Theory]
    [InlineData(SystemRoles.Admin, new[] { SystemRoles.Admin, SystemRoles.User })]
    [InlineData(SystemRoles.User, new[] { SystemRoles.Admin })]
    [InlineData(SystemRoles.User, new[] { SystemRoles.User })]
    public async Task An_edit_that_does_not_take_the_admin_role_away_does_not_take_the_admins_lock(
        string currentRole, string[] requestedRoles)
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com");
        host.Accounts.SetRoles(user.Id, currentRole);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", requestedRoles), Ct);

        Assert.True(result.IsSuccess);
        Assert.Empty(host.Accounts.AdminEvents);
    }

    private static (UserAccount Target, UserAccount Other) TwoAdmins(UserServiceTestHost host)
    {
        var target = host.Accounts.AddUser("ana@example.com");
        var other = host.Accounts.AddUser("beto@example.com");
        host.Accounts.SetRoles(target.Id, SystemRoles.Admin);
        host.Accounts.SetRoles(other.Id, SystemRoles.Admin);

        return (target, other);
    }
}
