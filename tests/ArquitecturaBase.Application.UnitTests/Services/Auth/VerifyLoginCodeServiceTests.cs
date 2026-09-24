using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class VerifyLoginCodeServiceTests
{
    private const string UserEmail = "ana@example.com";
    private const string UserPhone = "+5493515550101";
    private const string RightCode = "123456";
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invalid_request_neither_takes_a_lock_nor_saves()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.VerifyLoginCodeAsync(
            new VerifyLoginCodeRequest(UserEmail, "bad", "/login"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(["code", "returnUrl"], error.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Audits.Audits);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(
            ["Handling VerifyLoginCodeCommand", "VerifyLoginCodeCommand failed with Validation.Failed"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Valid_email_code_creates_the_account_signs_in_and_saves_the_code_and_audit()
    {
        var fixture = new Fixture();
        fixture.IssueEmailCode();
        using var culture = new CultureScope("en");

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReturnUrl, result.Value.ReturnUrl);
        Assert.Equal([UserEmail], fixture.Codes.LockedDestinations);
        var user = Assert.Single(fixture.Identity.Users);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], fixture.Identity.SignedInUsers);
        Assert.NotNull(Assert.Single(fixture.Codes.Codes).ConsumedAtUtc);
        Assert.True(Assert.Single(fixture.Audits.Audits).Succeeded);
        Assert.Equal(LoginMethod.Code, fixture.Audits.Audits[0].Method);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.UnitOfWork.AuditsAtSave);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(1, fixture.UnitOfWork.SignedInAtSave);
        Assert.Equal(
            ["Handling VerifyLoginCodeCommand", "Handled VerifyLoginCodeCommand"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.DoesNotContain(RightCode, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
    }

    [Fact]
    public async Task Wrong_code_counts_the_attempt_and_saves_failure_audit()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(UserEmail);
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest("000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(4, result.Error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, fixture.Identity.FailedAttempts[user.Id]);
        Assert.False(Assert.Single(fixture.Audits.Audits).Succeeded);
        Assert.Equal(LoginCodeErrors.InvalidCode, fixture.Audits.Audits[0].FailureReason);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.UnitOfWork.AuditsAtSave);
        Assert.Equal(0, fixture.UnitOfWork.SignedInAtSave);
        Assert.Equal(
            "VerifyLoginCodeCommand failed with " + LoginCodeErrors.InvalidCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public async Task Locked_out_account_is_audited_without_touching_the_code()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(UserEmail);
        fixture.Identity.LockedOutUsers.Add(user.Id);
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.Equal(AccountErrors.LockedOutCode, result.Error.Code);
        Assert.Null(Assert.Single(fixture.Codes.Codes).ConsumedAtUtc);
        Assert.Equal(0, fixture.Codes.Codes[0].FailedAttempts);
        Assert.Equal(AccountErrors.LockedOutCode, Assert.Single(fixture.Audits.Audits).FailureReason);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.UnitOfWork.AuditsAtSave);
        Assert.False(fixture.UnitOfWork.CodeConsumedAtSave);
    }

    [Fact]
    public async Task Invite_only_consumes_a_valid_code_and_saves_the_rejection()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.Empty(fixture.Identity.Users);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(1, fixture.UnitOfWork.AuditsAtSave);
        Assert.Equal(AccountErrors.NotInvitedCode, Assert.Single(fixture.Audits.Audits).FailureReason);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Invite_only_reports_a_deleted_email_as_not_invited_first()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        fixture.Identity.DeletedEmails.Add(UserEmail);
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.Equal(AccountErrors.NotInvitedCode, result.Error.Code);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(AccountErrors.NotInvitedCode, Assert.Single(fixture.Audits.Audits).FailureReason);
    }

    [Fact]
    public async Task Whatsapp_code_creates_a_phone_only_account_and_saves_its_audit()
    {
        var fixture = new Fixture();
        fixture.IssuePhoneCode();
        using var culture = new CultureScope("en");

        var result = await fixture.Service.VerifyLoginCodeAsync(new VerifyLoginCodeRequest(null, RightCode, ReturnUrl, UserPhone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([UserPhone], fixture.Codes.LockedDestinations);
        var user = Assert.Single(fixture.Identity.Users);
        Assert.Null(user.Email);
        Assert.Equal(UserPhone, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal("en", user.Culture);
        Assert.Equal([user.Id], fixture.Identity.SignedInUsers);
        Assert.Equal(LoginMethod.WhatsAppCode, Assert.Single(fixture.Audits.Audits).Method);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Valid_code_confirms_an_existing_email_and_resets_failed_attempts()
    {
        var fixture = new Fixture();
        var user = await fixture.Identity.CreateUnverifiedAsync(Email.Create(UserEmail).Value, null, "Ana", "es", Ct);
        fixture.Identity.FailedAttempts[user.Id] = 3;
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(fixture.Identity.Users).EmailConfirmed);
        Assert.Equal(0, fixture.Identity.FailedAttempts[user.Id]);
        Assert.Equal([user.Id], fixture.Identity.SignedInUsers);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Valid_whatsapp_code_confirms_a_number_loaded_by_an_administrator()
    {
        var fixture = new Fixture();
        var user = await fixture.Identity.CreateAsync(
            Email.Create(UserEmail).Value, PhoneNumber.Create(UserPhone).Value, phoneConfirmed: false,
            displayName: "Ana", culture: "es", Ct);
        fixture.IssuePhoneCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(
            new VerifyLoginCodeRequest(null, RightCode, ReturnUrl, UserPhone), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(fixture.Identity.Users).PhoneNumberConfirmed);
        Assert.Equal([user.Id], fixture.Identity.SignedInUsers);
        Assert.Equal(LoginMethod.WhatsAppCode, Assert.Single(fixture.Audits.Audits).Method);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Deleted_account_is_rejected_after_consuming_the_code_in_open_registration()
    {
        var fixture = new Fixture();
        fixture.Identity.DeletedEmails.Add(UserEmail);
        fixture.IssueEmailCode();

        var result = await fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct);

        Assert.Equal(AccountErrors.DisabledCode, result.Error.Code);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(AccountErrors.DisabledCode, Assert.Single(fixture.Audits.Audits).FailureReason);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Invalid_phone_format_keeps_the_legacy_save_even_without_an_audit()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.VerifyLoginCodeAsync(
            new VerifyLoginCodeRequest(null, RightCode, ReturnUrl, "11 2345-6789"), Ct);

        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Audits.Audits);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Save_failure_does_not_log_success()
    {
        var fixture = new Fixture();
        fixture.IssueEmailCode();
        fixture.UnitOfWork.Failure = new InvalidOperationException("save failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.VerifyLoginCodeAsync(EmailRequest(), Ct));

        Assert.Equal("save failed", exception.Message);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(["Handling VerifyLoginCodeCommand"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    private static VerifyLoginCodeRequest EmailRequest(string code = RightCode) => new(UserEmail, code, ReturnUrl);

    private sealed class Fixture
    {
        public Fixture()
        {
            var codeOptions = Options.Create(new LoginCodeOptions());
            var whatsAppOptions = Options.Create(new WhatsAppLoginOptions());
            var accountCreation = new AccountCreationPolicy(Settings, new FakeInitialAdmin());
            UnitOfWork = new RecordingUnitOfWork(this);
            Service = new AccountService(
                new FakeGoogleAvailability(false),
                new FakeWhatsAppAvailability(false),
                whatsAppOptions,
                new LoginCodeIssuer(
                    Codes,
                    new FakeLoginCodeGenerator(),
                    new FakeLoginCodeHasher(),
                    codeOptions,
                    whatsAppOptions,
                    Clock,
                    NullLogger<LoginCodeIssuer>.Instance),
                new LoginCodeVerifier(
                    Codes, Audits, Identity, new FakeLoginCodeHasher(), accountCreation, new FakeRequestInfo(), Clock),
                Identity,
                new FakePhoneNumberParser(),
                new FakeWhatsAppOutbox(),
                new FakeEmailTemplateRenderer(),
                new FakeEmailQueue(),
                accountCreation,
                codeOptions,
                new ServiceRequestValidator<RequestLoginCodeRequest>([new RequestLoginCodeRequestValidator()]),
                new ServiceRequestValidator<RequestWhatsAppLoginCodeRequest>([new RequestWhatsAppLoginCodeRequestValidator()]),
                new ServiceRequestValidator<VerifyLoginCodeRequest>([new VerifyLoginCodeRequestValidator(codeOptions)]),
                UnitOfWork,
                Logger);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginCodeRepository Codes { get; } = new();

        public InMemoryLoginAuditRepository Audits { get; } = new();

        public FakeIdentityService Identity { get; } = new();

        public FakeSystemSettingsReader Settings { get; } = new();

        public FakeLogger<AccountService> Logger { get; } = new();

        public RecordingUnitOfWork UnitOfWork { get; }

        public AccountService Service { get; }

        public void IssueEmailCode() => IssueCode(LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value));

        public void IssuePhoneCode() => IssueCode(LoginCodeDestination.ForPhone(PhoneNumber.Create(UserPhone).Value));

        private void IssueCode(LoginCodeDestination destination) => Codes.Add(LoginCode.Issue(
            destination,
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            FakeLoginCodeHasher.HashOf(destination.Value, LoginCodePurpose.SignIn, RightCode),
            Clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5));
    }

    private sealed class RecordingUnitOfWork(Fixture fixture) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public int AuditsAtSave { get; private set; }

        public bool CodeConsumedAtSave { get; private set; }

        public int SignedInAtSave { get; private set; }

        public Exception? Failure { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            AuditsAtSave = fixture.Audits.Audits.Count;
            CodeConsumedAtSave = fixture.Codes.Codes.Any(code => code.ConsumedAtUtc is not null);
            SignedInAtSave = fixture.Identity.SignedInUsers.Count;
            return Failure is { } error ? Task.FromException<int>(error) : Task.FromResult(1);
        }
    }
}
