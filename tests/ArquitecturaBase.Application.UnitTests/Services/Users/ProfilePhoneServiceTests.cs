using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>La desvinculación del perfil sin canales: guardas, locks, enlaces y el límite, sin revocar sesiones.</summary>
public sealed class ProfilePhoneServiceTests
{
    private const string Phone = "+5493511234567";
    private static readonly UnlinkOwnPhoneRequest NoCode = new(Code: null);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_only_login_method_cannot_be_unlinked()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.Equal(Phone, (await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Null(link.InvalidatedAtUtc);
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Equal(CommitPolicy.OnAnyResult, host.UnitOfWork.LastPolicy);
        Assert.Equal("UnlinkOwnPhone failed with " + UserErrors.LastLoginMethodCode,
            host.ProfilePhoneLogger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public async Task Google_counts_as_another_login_method()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        host.Accounts.LinkExternalLogin(user.Id, ExternalLoginProviders.Google, "google-123");

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null((await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task Unlink_locks_the_participants_then_the_account_links_and_voids_only_the_active_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var active = AddLink(host, user.Id);
        var expired = LoginLink.Issue(user.Id, "hash-expired", host.Clock.GetUtcNow().UtcDateTime.AddHours(-1));
        host.Links.Links.Add(expired);
        var code = host.IssueUnlinkCode(user);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest(code), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["participant-lock:" + user.Id, "login-link:" + user.Id, "participant-released:" + user.Id],
            host.PhoneEvents);
        var updated = await host.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Null(updated!.PhoneNumber);
        Assert.False(updated.PhoneNumberConfirmed);
        Assert.NotNull(active.InvalidatedAtUtc);
        Assert.Null(expired.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Transactions);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, host.UnitOfWork.LastPolicy);
        Assert.NotNull(Assert.Single(host.Destinations.Codes).ConsumedAtUtc);
        Assert.Equal(["Handling UnlinkOwnPhone", "Handled UnlinkOwnPhone"],
            host.ProfilePhoneLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Unlink_without_a_number_still_notifies_the_participants_and_voids_the_links()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null);
        host.CurrentUser.UserId = user.Id;
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Contains("participant-released:" + user.Id, host.PhoneEvents);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Unlink_reads_the_account_after_the_locks()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        host.Links.WhileWaitingForTheLock = userId =>
            host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(userId, Ct));

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        // Sólo el lock del correo, que va primero por el orden único; ningún código se mira.
        Assert.Equal(["ana@example.com"], host.Destinations.LockedDestinations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_absent_session_or_account_cannot_unlink_a_number(bool hasSession)
    {
        var host = new UserServiceTestHost();
        host.CurrentUser.UserId = hasSession ? Guid.CreateVersion7() : null;

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(1, host.UnitOfWork.Transactions);
        Assert.Equal(CommitPolicy.OnAnyResult, host.UnitOfWork.LastPolicy);
    }

    [Fact]
    public async Task With_a_verified_email_unlinking_needs_the_code_sent_to_it()
    {
        // N11: con la sesión abierta de otro, desvincular el número lo dejaría afuera; el código llega a su correo.
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(NoCode, Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, error.Code);
        Assert.Equal(["code"], error.Errors.Keys);
        Assert.Equal(Phone, (await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Null(link.InvalidatedAtUtc);
        Assert.DoesNotContain("participant-released:" + user.Id, host.PhoneEvents);
    }

    [Fact]
    public async Task A_wrong_email_code_counts_its_attempt_and_keeps_the_number()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        host.IssueUnlinkCode(user);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, Assert.Single(host.Destinations.Codes).FailedAttempts);
        // El del orden único y el de la revisión, que en la misma transacción no espera.
        Assert.Equal(["ana@example.com", "ana@example.com"], host.Destinations.LockedDestinations);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(Phone, (await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task Unlink_locks_the_email_before_the_participants_and_the_account()
    {
        // El orden único de los locks, el de la edición de la administración: correo, número, participantes y cuenta. Al
        // revés, dos pedidos sobre la misma cuenta se esperan uno al otro y Postgres corta a uno (40P01).
        var host = new UserServiceTestHost();
        host.Destinations.Events = host.PhoneEvents;
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var code = host.IssueUnlinkCode(user);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest(code), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["login-code:ana@example.com", "participant-lock:" + user.Id, "login-link:" + user.Id],
            host.PhoneEvents.Take(3));
    }

    [Fact]
    public async Task A_code_to_verify_the_email_does_not_confirm_unlinking()
    {
        // Cada código sirve para su propósito: el de «verificá tu correo» no confirma desvincular el número.
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var code = host.IssueUnlinkCode(user, purpose: LoginCodePurpose.VerifyDestination);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest(code), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        var stored = Assert.Single(host.Destinations.Codes);
        Assert.Null(stored.ConsumedAtUtc);
        Assert.Equal(0, stored.FailedAttempts);
        Assert.Equal(Phone, (await host.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task The_only_login_method_is_refused_before_looking_at_the_code()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest("123456"), Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.Empty(host.Destinations.LockedDestinations);
    }

    [Fact]
    public async Task A_code_with_the_wrong_shape_is_rejected_before_the_transaction()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest("12ab"), Ct);

        Assert.Contains("code", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    private static LoginLink AddLink(UserServiceTestHost host, Guid userId)
    {
        var link = LoginLink.Issue(userId, "hash", host.Clock.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        return link;
    }
}
