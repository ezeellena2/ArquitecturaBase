using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Equal(code.SentAtUtc, fixture.UnitOfWork.SentAtSave);
        Assert.Equal(UserEmail, Assert.Single(fixture.Queue.Messages).To);
        Assert.Equal(["enqueue", "save"], fixture.Events);
        Assert.Equal(
            ["Handling RequestLoginCodeCommand", "Handled RequestLoginCodeCommand"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.DoesNotContain(UserEmail, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
        Assert.DoesNotContain(FakeLoginCodeGenerator.Code, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
    }

    [Fact]
    public async Task Invite_only_saves_an_unsent_code_for_an_unknown_email_with_the_same_response()
    {
        var fixture = new Fixture();
        fixture.Settings.Mode = RegistrationMode.InviteOnly;
        fixture.Identity.AddUser("known@example.com");

        var unknown = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);
        var known = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest("known@example.com"), Ct);

        Assert.True(unknown.IsSuccess);
        Assert.True(known.IsSuccess);
        Assert.Equal(known.Value, unknown.Value);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.Null(fixture.Codes.Codes[0].SentAtUtc);
        Assert.NotNull(fixture.Codes.Codes[1].SentAtUtc);
        Assert.Equal("known@example.com", Assert.Single(fixture.Queue.Messages).To);
        Assert.Equal(["save", "enqueue", "save"], fixture.Events);
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
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Existing_account_uses_its_culture_for_the_email()
    {
        var fixture = new Fixture();
        fixture.Identity.AddUser(UserEmail, culture: "en");
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
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(LogLevel.Warning, fixture.Logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task Domain_email_rejection_returns_an_error_without_saving()
    {
        var fixture = new Fixture(validate: false);

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest("not-an-email"), Ct);

        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Resend_limit_returns_retry_after_without_saving_again()
    {
        var fixture = new Fixture();
        await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));

        var result = await fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, result.Error.Code);
        Assert.Equal(40, result.Error.Metadata![LoginCodeErrors.RetryAfterKey]);
        Assert.Single(fixture.Codes.Codes);
        Assert.Single(fixture.Queue.Messages);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal("RequestLoginCodeCommand failed with " + LoginCodeErrors.ResendTooSoonCode,
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
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
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
        Assert.Equal(5, fixture.UnitOfWork.SaveCalls);
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
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
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
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
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
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Queue_failure_does_not_mark_sent_or_save()
    {
        var fixture = new Fixture();
        fixture.Queue.Failure = new InvalidOperationException("Queue failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct));

        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(["enqueue"], fixture.Events);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Save_failure_propagates_after_enqueuing_without_a_success_log()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Failure = new InvalidOperationException("Save failed.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestLoginCodeAsync(new RequestLoginCodeRequest(UserEmail), Ct));

        Assert.Equal(["enqueue", "save"], fixture.Events);
        Assert.Single(fixture.Queue.Messages);
        Assert.NotNull(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Equal(["Handling RequestLoginCodeCommand"],
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
            var whatsAppOptions = Options.Create(new WhatsAppLoginOptions());
            var accountCreation = new AccountCreationPolicy(Settings, new FakeInitialAdmin());
            Queue = new RecordingEmailQueue(Events);
            UnitOfWork = new RecordingUnitOfWork(Events, Codes);
            Service = new AccountService(
                new FakeGoogleAvailability(false),
                new FakeWhatsAppAvailability(false),
                whatsAppOptions,
                new LoginCodeIssuer(
                    Codes,
                    new FakeLoginCodeGenerator(),
                    new FakeLoginCodeHasher(),
                    loginCodeOptions,
                    whatsAppOptions,
                    Clock,
                    NullLogger<LoginCodeIssuer>.Instance),
                new LoginCodeVerifier(
                    Codes,
                    new InMemoryLoginAuditRepository(),
                    Identity,
                    new FakeLoginCodeHasher(),
                    accountCreation,
                    new FakeRequestInfo(),
                    Clock),
                Identity,
                new FakePhoneNumberParser(),
                new FakeWhatsAppOutbox(),
                Renderer,
                Queue,
                accountCreation,
                loginCodeOptions,
                new ServiceRequestValidator<RequestLoginCodeRequest>(
                    validate ? [new RequestLoginCodeRequestValidator()] : []),
                new ServiceRequestValidator<RequestWhatsAppLoginCodeRequest>([new RequestWhatsAppLoginCodeRequestValidator()]),
                new ServiceRequestValidator<VerifyLoginCodeRequest>([new VerifyLoginCodeRequestValidator(loginCodeOptions)]),
                UnitOfWork,
                Logger);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));

        public InMemoryLoginCodeRepository Codes { get; } = new();

        public FakeIdentityService Identity { get; } = new();

        public FakeEmailTemplateRenderer Renderer { get; } = new();

        public FakeSystemSettingsReader Settings { get; } = new();

        public RecordingEmailQueue Queue { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public FakeLogger<AccountService> Logger { get; } = new();

        public List<string> Events { get; } = [];

        public AccountService Service { get; }
    }

    private sealed class RecordingEmailQueue(List<string> events) : IEmailQueue
    {
        public List<EmailMessage> Messages { get; } = [];

        public Exception? Failure { get; set; }

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            events.Add("enqueue");

            if (Failure is { } error)
            {
                return ValueTask.FromException(error);
            }

            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork(List<string> events, InMemoryLoginCodeRepository codes) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public DateTime? SentAtSave { get; private set; }

        public Exception? Failure { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            events.Add("save");
            SaveCalls++;
            SentAtSave = codes.Codes.LastOrDefault()?.SentAtUtc;
            return Failure is { } error ? Task.FromException<int>(error) : Task.FromResult(1);
        }
    }
}
