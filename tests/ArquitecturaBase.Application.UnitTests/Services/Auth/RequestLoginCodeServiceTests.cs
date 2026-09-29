using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class RequestLoginCodeServiceTests
{
    private const string UserEmail = "ana@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Success_normalizes_email_enqueues_before_saving_and_marks_code_sent()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(" Ana@Example.com "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal([UserEmail], fixture.Codes.LockedDestinations);
        var code = Assert.Single(fixture.Codes.Codes);
        Assert.Equal(FakeLoginCodeHasher.HashOf(UserEmail, LoginCodePurpose.SignIn, FakeLoginCodeGenerator.Code), code.CodeHash);
        Assert.Equal(LoginCodeChannel.Email, code.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, code.Purpose);
        Assert.Null(code.RequestedByUserId);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(10), code.ExpiresAtUtc);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime, code.SentAtUtc);
        Assert.Equal(code.SentAtUtc, fixture.SentAtCommit);
        Assert.Equal(UserEmail, Assert.Single(fixture.Queue.Messages).To);
        Assert.Equal(["enqueue", "commit"], fixture.Events);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(
            ["Handling RequestLoginCode", "Handled RequestLoginCode"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.DoesNotContain(UserEmail, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
        Assert.DoesNotContain(FakeLoginCodeGenerator.Code, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
    }

    [Fact]
    public async Task Invite_only_saves_an_unsent_code_for_an_unknown_email_with_the_same_response()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        fixture.Accounts.AddUser("known@example.com");

        var unknown = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);
        var known = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest("known@example.com"), Ct);

        Assert.True(unknown.IsSuccess);
        Assert.True(known.IsSuccess);
        Assert.Equal(known.Value, unknown.Value);
        Assert.Equal(2, fixture.UnitOfWork.Commits);
        Assert.Null(fixture.Codes.Codes[0].SentAtUtc);
        Assert.NotNull(fixture.Codes.Codes[1].SentAtUtc);
        Assert.Equal("known@example.com", Assert.Single(fixture.Queue.Messages).To);
        Assert.Equal(["commit", "enqueue", "commit"], fixture.Events);
    }

    [Fact]
    public async Task Invite_only_sends_the_code_to_the_initial_admin_without_an_account()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;

        var result = await fixture.Service.RequestLoginCodeAsync(
            new RequestLoginCodeRequest(FakeInitialAdmin.DefaultEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(FakeInitialAdmin.DefaultEmail, Assert.Single(fixture.Queue.Messages).To);
        Assert.NotNull(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Existing_account_uses_its_culture_for_the_email()
    {
        var fixture = new Fixture();
        fixture.Accounts.AddUser(UserEmail, culture: "en");
        using var culture = new CultureScope("es");

        await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal("en", fixture.Renderer.LastCulture?.Name);
    }

    [Fact]
    public async Task New_account_uses_the_culture_of_the_request()
    {
        var fixture = new Fixture();
        using var culture = new CultureScope("en");

        await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal("en", fixture.Renderer.LastCulture?.Name);
    }

    [Fact]
    public async Task Invalid_email_fails_validation_without_issuing_or_saving()
    {
        using var culture = new CultureScope("es");
        var fixture = new Fixture();

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest("ana@"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Ingresá un correo válido.", Assert.Single(error.Errors["email"]));
        Assert.Empty(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
        Assert.Equal(LogLevel.Warning, fixture.Logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task Domain_email_rejection_returns_an_error_without_saving()
    {
        var fixture = new Fixture(validate: false);

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest("not-an-email"), Ct);

        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Resend_limit_returns_retry_after_and_rolls_back()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(fixture.Codes.Codes);
        Assert.Single(fixture.Queue.Messages);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal("RequestLoginCode failed with " + LoginCodeErrors.ResendTooSoonCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public async Task New_sign_in_code_invalidates_previous_sign_in_code()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Codes.Codes.Count);
        Assert.NotNull(fixture.Codes.Codes[0].InvalidatedAtUtc);
        Assert.Null(fixture.Codes.Codes[1].InvalidatedAtUtc);
        Assert.Equal(2, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Sixth_request_in_window_returns_time_until_oldest_request_expires()
    {
        var fixture = new Fixture();
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct)).IsSuccess);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(600, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(5, fixture.Codes.Codes.Count);
        Assert.Equal(5, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Recent_verification_code_holds_back_sign_in_resend_for_same_email()
    {
        var fixture = new Fixture();
        IssueVerificationCode(fixture);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Verification_codes_count_toward_sign_in_request_limit()
    {
        var fixture = new Fixture();
        for (var i = 0; i < 5; i++)
        {
            IssueVerificationCode(fixture);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Equal(600, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Equal(5, fixture.Codes.Codes.Count);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task New_sign_in_code_keeps_verification_code_active()
    {
        var fixture = new Fixture();
        var verification = IssueVerificationCode(fixture);
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(verification.InvalidatedAtUtc);
        Assert.Null(fixture.Codes.Codes[1].InvalidatedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Queue_failure_does_not_mark_sent_and_rolls_back()
    {
        var fixture = new Fixture();
        fixture.Queue.Failure = new InvalidOperationException("Queue failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct));

        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(["enqueue"], fixture.Events);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task A_full_queue_keeps_the_code_unsent_but_saves_the_successful_request()
    {
        var fixture = new Fixture();
        fixture.Queue.Accepts = false;

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Null(fixture.SentAtCommit);
        Assert.Equal(["enqueue", "commit"], fixture.Events);
    }

    [Fact]
    public async Task Commit_failure_propagates_after_enqueuing_without_a_success_log()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.CommitFailure = new InvalidOperationException("Commit failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct));

        Assert.Equal(["enqueue", "commit"], fixture.Events);
        Assert.Single(fixture.Queue.Messages);
        Assert.NotNull(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(["Handling RequestLoginCode"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    private static LoginCode IssueVerificationCode(Fixture fixture)
    {
        var code = LoginCode.Issue(
            LoginCodeDestination.ForEmail(Email.Create(UserEmail).Value),
            LoginCodePurpose.VerifyDestination,
            requestedByUserId: Guid.CreateVersion7(),
            "hash-verify",
            fixture.Clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts: 5);

        fixture.Codes.Add(code);
        return code;
    }

    private sealed class Fixture
    {
        public Fixture(bool validate = true)
        {
            var loginCodeOptions = Options.Create(new LoginCodeOptions());
            var accountCreation = new AccountCreationPolicy(Settings, new FakeInitialAdmin());
            Queue = new RecordingEmailQueue(Events);
            UnitOfWork = new FakeUnitOfWork(Events) { OnCommit = () => SentAtCommit = Codes.Codes.LastOrDefault()?.SentAtUtc };
            Codes.InTransaction = () => UnitOfWork.InTransaction;
            Service = new LoginCodeService(
                new SignInCodeIssuer(
                    new LoginCodeIssuer(
                        Codes,
                        new FakeLoginCodeGenerator(),
                        new FakeLoginCodeHasher(),
                        loginCodeOptions,
                        Clock),
                    Accounts,
                    Renderer,
                    Queue,
                    accountCreation),
                new LoginCodeVerifier(
                    Codes,
                    new LoginAuditRecorder(new InMemoryLoginAuditRepository(), new FakeRequestInfo(), Clock),
                    Accounts,
                    Accounts,
                    new FakeSignInService(),
                    new FakeLoginCodeHasher(),
                    accountCreation,
                    Clock),
                new FakeSignInService(),
                RequestValidators.For(
                    [
                        .. validate ? new IValidator[] { new RequestLoginCodeRequestValidator() } : [],
                        new VerifyLoginCodeRequestValidator(loginCodeOptions),
                    ]),
                UnitOfWork,
                Logger);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginCodeRepository Codes { get; } = new();

        public InMemoryUserAccounts Accounts { get; } = new();

        public FakeEmailTemplateRenderer Renderer { get; } = new();

        public FakeSystemSettingsReader Settings { get; } = new();

        public RecordingEmailQueue Queue { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public DateTime? SentAtCommit { get; private set; }

        public FakeLogger<LoginCodeService> Logger { get; } = new();

        public List<string> Events { get; } = [];

        public LoginCodeService Service { get; }
    }

    private sealed class RecordingEmailQueue(List<string> events) : IEmailQueue
    {
        public List<EmailMessage> Messages { get; } = [];

        public Exception? Failure { get; set; }

        /// <summary>En false, hace de cola llena.</summary>
        public bool Accepts { get; set; } = true;

        public bool TryEnqueue(EmailMessage message)
        {
            events.Add("enqueue");

            if (Failure is { } error)
            {
                throw error;
            }

            if (Accepts)
            {
                Messages.Add(message);
            }

            return Accepts;
        }
    }
}
