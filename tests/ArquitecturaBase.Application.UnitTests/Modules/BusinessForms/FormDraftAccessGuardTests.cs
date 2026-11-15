using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// A named account, such as the person of a channel proposal, needs the permissions and an active account, as the other guards.
public sealed class FormDraftAccessGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_named_account_needs_to_be_active_besides_its_permissions(bool active, bool allowed)
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("persona@example.test", isActive: active).Id;
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];

        var result = await new FormDraftAccessGuard(new FakeCurrentUser(), permissions, users).CheckActorAsync(actor, true, Ct);

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed) Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
    }
}
