namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services.Users;

/// <summary>
/// Desvincular el número desde la administración con el participante real del módulo WhatsApp: toma primero la fila del
/// contacto de la cuenta y después el lock de sus enlaces. Lo mismo con el participante que anota lo prueba
/// UserAccessServicePhoneTests.
/// </summary>
public sealed class WhatsAppUserAccessServicePhoneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unlink_locks_contact_then_account_revokes_sessions_and_commits()
    {
        var host = new WhatsAppUserServiceTestHost();
        var user = host.Accounts.AddUser("phone@example.com", phoneNumber: "+5491112345678");

        var result = await host.Access.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null((await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Equal(["number-change:" + user.Id], host.MessagesLog.Keys);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal([user.Id], host.SignIn.RevokedUsers);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }
}
