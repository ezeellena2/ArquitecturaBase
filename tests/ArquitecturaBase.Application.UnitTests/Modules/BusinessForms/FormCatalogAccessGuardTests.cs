using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// F4: the person of a channel proposal or attachment must still be an active account, as for the drafts.
public sealed class FormCatalogAccessGuardTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_named_account_needs_to_be_active_to_work_on_catalogs_for_a_channel(bool isActive, bool allowed)
    {
        var accounts = new InMemoryUserAccounts();
        var user = accounts.AddUser("persona@example.com", isActive);
        var permissions = new FakePermissionService();
        permissions.Permissions[user.Id] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var guard = new FormCatalogAccessGuard(new FakeCurrentUser { UserId = user.Id }, permissions, accounts);

        var result = await guard.CheckActorAsync(user.Id, writing: true, TestContext.Current.CancellationToken);

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed) Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
        Assert.False((await guard.CheckActorAsync(Guid.NewGuid(), writing: false, TestContext.Current.CancellationToken)).IsSuccess);
    }
}
