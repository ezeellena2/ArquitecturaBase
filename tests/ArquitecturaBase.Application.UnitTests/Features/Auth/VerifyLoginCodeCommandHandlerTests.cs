using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class VerifyLoginCodeCommandHandlerTests
{
    private const string UserEmail = "ana@example.com";
    private const string UserPhone = "+5493515550101";
    private const string RightCode = "123456";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryLoginCodeRepository _loginCodes = new();
    private readonly InMemoryLoginAuditRepository _audits = new();
    private readonly FakeIdentityService _identity = new();
    private readonly FakeSystemSettingsReader _settings = new();
    private readonly VerifyLoginCodeCommandHandler _handler;

    public VerifyLoginCodeCommandHandlerTests()
    {
        _handler = new VerifyLoginCodeCommandHandler(
            _loginCodes, _audits, _identity, new FakeLoginCodeHasher(), _settings, new FakeRequestInfo(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Right_code_for_a_new_email_creates_the_account_and_signs_in()
    {
        IssueCode();
        using var culture = new CultureScope("en");

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([UserEmail], _loginCodes.LockedDestinations);
        var user = Assert.Single(_identity.Users);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(user.Id, audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Equal("203.0.113.10", audit.IpAddress);
    }

    [Fact]
    public async Task Right_code_for_an_existing_user_resets_the_failed_attempts()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.FailedAttempts[user.Id] = 3;
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_identity.Users);
        Assert.Equal(0, _identity.FailedAttempts[user.Id]);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Wrong_code_counts_a_failed_attempt_and_is_audited()
    {
        var user = _identity.AddUser(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, _identity.FailedAttempts[user.Id]);
        Assert.Empty(_identity.SignedInUsers);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(LoginCodeErrors.InvalidCode, audit.FailureReason);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task Without_a_code_the_error_is_the_same_as_for_a_wrong_code()
    {
        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Empty(_identity.Users);
        Assert.Single(_audits.Audits);
    }

    [Fact]
    public async Task Locked_out_user_is_rejected_before_checking_the_code()
    {
        var user = _identity.AddUser(UserEmail);
        _identity.LockedOutUsers.Add(user.Id);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Null(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Equal(0, _loginCodes.Codes[0].FailedAttempts);
        Assert.Equal(AccountErrors.LockedOutCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Disabled_account_is_reported_after_the_code_is_verified()
    {
        _identity.AddUser(UserEmail, isActive: false);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(AccountErrors.DisabledCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        IssueCode();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.ExpiredCode, result.Error.Code);
        Assert.Empty(_identity.Users);
    }

    [Fact]
    public async Task A_code_to_verify_the_email_from_the_profile_does_not_sign_in()
    {
        var user = _identity.AddUser(UserEmail);
        _loginCodes.Add(LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value),
            LoginCodePurpose.VerifyDestination,
            user.Id,
            FakeLoginCodeHasher.HashOf(UserEmail, LoginCodePurpose.VerifyDestination, RightCode),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));

        var result = await _handler.Handle(Command(RightCode), Ct);

        // Como si no hubiera código: sin intentos restantes, y el código del perfil queda intacto.
        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Null(result.Error.Metadata);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(0, _loginCodes.Codes[0].FailedAttempts);
        Assert.Null(_loginCodes.Codes[0].ConsumedAtUtc);
    }

    [Fact]
    public async Task Invite_only_rejects_the_right_code_of_an_email_without_an_account()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Empty(_identity.FailedAttempts);

        // El código se gasta igual: ya probó que el correo es de quien lo ingresó y no sirve para otro intento.
        Assert.NotNull(_loginCodes.Codes[0].ConsumedAtUtc);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
        Assert.Equal(UserEmail, audit.Identifier);
        Assert.Null(audit.UserId);
        Assert.Equal(LoginMethod.Code, audit.Method);
    }

    [Fact]
    public async Task Invite_only_reports_a_deleted_account_as_not_invited()
    {
        // El mismo orden que el ingreso con Google: primero el modo de registro, después la cuenta borrada.
        _settings.Mode = RegistrationMode.InviteOnly;
        _identity.DeletedEmails.Add(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Equal(AccountErrors.NotInvitedCode, Assert.Single(_audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Invite_only_lets_in_an_account_that_already_exists()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var user = _identity.AddUser(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(_identity.Users);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Open_registration_creates_the_account_of_an_email_without_one()
    {
        _settings.Mode = RegistrationMode.Open;
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_identity.Users);
        Assert.Equal(UserEmail, user.Email);
        Assert.Equal([user.Id], _identity.SignedInUsers);
        Assert.True(Assert.Single(_audits.Audits).Succeeded);
    }

    [Fact]
    public async Task Open_registration_reports_a_deleted_account_as_disabled()
    {
        _settings.Mode = RegistrationMode.Open;
        _identity.DeletedEmails.Add(UserEmail);
        IssueCode();

        var result = await _handler.Handle(Command(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.Users);
    }

    [Fact]
    public async Task Right_code_for_a_new_number_creates_an_account_without_email_and_with_the_number_verified()
    {
        _settings.Mode = RegistrationMode.Open;
        IssuePhoneCode();
        using var culture = new CultureScope("en");

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([UserPhone], _loginCodes.LockedDestinations);
        var user = Assert.Single(_identity.Users);
        Assert.Null(user.Email);
        Assert.Equal(UserPhone, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], _identity.SignedInUsers);

        var audit = Assert.Single(_audits.Audits);
        Assert.True(audit.Succeeded);
        Assert.Equal(UserPhone, audit.Identifier);
        Assert.Equal(LoginMethod.WhatsAppCode, audit.Method);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task Right_code_verifies_a_number_that_an_administrator_loaded()
    {
        var user = await _identity.CreateAsync(
            Email.Create(UserEmail).Value, PhoneNumber.Create(UserPhone).Value, phoneConfirmed: false, "Laura", "es", Ct);
        IssuePhoneCode();

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(_identity.Users);
        Assert.True(updated.PhoneNumberConfirmed);
        Assert.Equal(UserEmail, updated.Email);
        Assert.Equal([user.Id], _identity.SignedInUsers);
    }

    [Fact]
    public async Task Wrong_code_for_a_number_counts_a_failed_attempt_on_its_account()
    {
        var user = _identity.AddUser(email: null, phoneNumber: UserPhone);
        IssuePhoneCode();

        var result = await _handler.Handle(PhoneCommand("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(1, _identity.FailedAttempts[user.Id]);

        var audit = Assert.Single(_audits.Audits);
        Assert.Equal(UserPhone, audit.Identifier);
        Assert.Equal(LoginMethod.WhatsAppCode, audit.Method);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task A_code_sent_to_the_email_does_not_open_the_account_by_its_number()
    {
        _identity.AddUser(UserEmail, phoneNumber: UserPhone);
        IssueCode();

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
    }

    [Fact]
    public async Task Invite_only_rejects_the_right_code_of_a_number_without_an_account()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        IssuePhoneCode();

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(_identity.Users);
        Assert.Empty(_identity.SignedInUsers);

        var audit = Assert.Single(_audits.Audits);
        Assert.False(audit.Succeeded);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
        Assert.Equal(UserPhone, audit.Identifier);
        Assert.Equal(LoginMethod.WhatsAppCode, audit.Method);
        Assert.Null(audit.UserId);
    }

    [Fact]
    public async Task Open_registration_reports_the_number_of_a_deleted_account_as_disabled()
    {
        _settings.Mode = RegistrationMode.Open;
        var deleted = _identity.AddUser(email: null, phoneNumber: UserPhone);
        await _identity.DeleteAsync(deleted.Id, Ct);
        IssuePhoneCode();

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.Users);
    }

    [Fact]
    public async Task A_disabled_account_is_reported_after_its_number_is_verified()
    {
        _identity.AddUser(email: null, isActive: false, phoneNumber: UserPhone);
        IssuePhoneCode();

        var result = await _handler.Handle(PhoneCommand(RightCode), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.Empty(_identity.SignedInUsers);
        Assert.Equal(LoginMethod.WhatsAppCode, Assert.Single(_audits.Audits).Method);
    }

    [Fact]
    public async Task A_phone_that_is_not_in_international_format_is_rejected_before_anything_else()
    {
        var result = await _handler.Handle(new VerifyLoginCodeCommand(null, RightCode, ReturnUrl, Phone: "11 2345-6789"), Ct);

        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
        Assert.Empty(_loginCodes.LockedDestinations);
        Assert.Empty(_audits.Audits);
    }

    private static VerifyLoginCodeCommand Command(string code) => new(UserEmail, code, ReturnUrl);

    private static VerifyLoginCodeCommand PhoneCommand(string code) => new(Email: null, code, ReturnUrl, Phone: UserPhone);

    private void IssuePhoneCode() =>
        _loginCodes.Add(LoginCode.Issue(
            LoginCodeDestination.ForPhone(PhoneNumber.Create(UserPhone).Value),
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            FakeLoginCodeHasher.HashOf(UserPhone, LoginCodePurpose.SignIn, RightCode),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));

    private void IssueCode() =>
        _loginCodes.Add(LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value),
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            FakeLoginCodeHasher.HashOf(UserEmail, LoginCodePurpose.SignIn, RightCode),
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));
}
