using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class ProfileEmailServiceTests
{
    private const string Email = "ana@example.com";
    private const string Code = FakeLoginCodeGenerator.Code;
    private const string Phone = "+5493511234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_normalizes_email_and_enqueues_in_account_language_before_saving()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, culture: "en");
        using var culture = new CultureScope("es");

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(
            new RequestEmailCodeRequest(" Ana@Example.com "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Equal([Email], fixture.Codes.LockedDestinations);
        var stored = Assert.Single(fixture.Codes.Codes);
        Assert.Equal(Email, stored.Destination);
        Assert.Equal(LoginCodePurpose.VerifyDestination, stored.Purpose);
        Assert.Equal(user.Id, stored.RequestedByUserId);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime, stored.SentAtUtc);
        Assert.Equal("en", fixture.Renderer.LastCulture?.Name);
        Assert.Equal(Email, Assert.Single(fixture.Queue.Messages).To);
        Assert.Equal(["enqueue", "commit"], fixture.Events);
        Assert.Equal(stored.SentAtUtc, fixture.SentAtCommit);
        Assert.Equal(
            ["Handling RequestEmailCode", "Handled RequestEmailCode"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.DoesNotContain(Email, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
        Assert.DoesNotContain(Code, string.Join(' ', fixture.Logger.Collector.GetSnapshot().Select(record => record.Message)));
    }

    [Fact]
    public async Task Request_shares_the_destination_cooldown_with_sign_in_codes()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        fixture.Issue(Email, LoginCodePurpose.SignIn, owner: null);

        var blocked = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, blocked.Error.Code);
        Assert.Single(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);

        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        var allowed = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.True(allowed.IsSuccess);
        Assert.Equal(2, fixture.Codes.Codes.Count);
        Assert.Null(fixture.Codes.Codes[0].InvalidatedAtUtc);
        Assert.Equal(LoginCodePurpose.VerifyDestination, fixture.Codes.Codes[1].Purpose);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_full_queue_keeps_the_code_unsent_but_saves_the_successful_request()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        fixture.Queue.Accepts = false;

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.ResendAfterSeconds);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Null(Assert.Single(fixture.Codes.Codes).SentAtUtc);
        Assert.Null(fixture.SentAtCommit);
        Assert.Equal(["enqueue", "commit"], fixture.Events);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_issuing_or_saving()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest("ana@"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("email", error.Errors.Keys);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Wrong_confirmation_code_saves_the_failed_attempt_without_affecting_session()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, "000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(1, issued.FailedAttempts);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(1, fixture.FailedAttemptsAtCommit);
        Assert.Equal([Email], fixture.Codes.LockedDestinations);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
        Assert.Equal(
            "ConfirmEmail failed with " + LoginCodeErrors.InvalidCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_email_of_another_account_is_refused_on_its_field_before_issuing_any_code(bool deleted)
    {
        // O28 (decisión del 2026-11-25): el aviso sale al pedir el código, aunque así se pueda averiguar si un correo
        // tiene cuenta.
        var fixture = new Fixture();
        var requester = fixture.Accounts.AddUser(email: null);
        if (deleted)
        {
            fixture.Accounts.DeletedEmails.Add(Email);
        }
        else
        {
            fixture.Accounts.AddUser(Email);
        }

        var result = await fixture.Service(requester.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(UserErrors.AlreadyExistsCode, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Contains("email", error.Errors.Keys);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_own_unverified_email_still_gets_its_code()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        await fixture.Accounts.ArrangeAsync(accounts =>
            accounts.SetEmailAsync(user.Id, ArquitecturaBase.Domain.ValueObjects.Email.Create(Email).Value, confirmed: false, Ct));

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(Email, Assert.Single(fixture.Codes.Codes).Destination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_email_taken_after_its_code_was_sent_is_refused_only_with_the_correct_code_and_the_code_is_spent(bool deleted)
    {
        var fixture = new Fixture();
        var requester = fixture.Accounts.AddUser(email: null);
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, requester.Id);
        if (deleted)
        {
            fixture.Accounts.DeletedEmails.Add(Email);
        }
        else
        {
            fixture.Accounts.AddUser(Email);
        }

        var result = await fixture.Service(requester.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.AlreadyExistsCode, result.Error.Code);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Null((await fixture.Accounts.FindByIdAsync(requester.Id, Ct))!.Email);
    }

    [Fact]
    public async Task Correct_code_sets_verified_email_without_revoking_the_current_session()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        var updated = await fixture.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Equal(Email, updated!.Email);
        Assert.True(updated.EmailConfirmed);
    }

    [Fact]
    public async Task The_initial_admin_does_not_change_its_email_from_the_profile()
    {
        // El arranque busca la cuenta de Seed:AdminEmail por ese correo: cambiarlo hace que la pierda (N25).
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(FakeInitialAdmin.DefaultEmail);
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.InitialAdmin, result.Error);
        Assert.Null(issued.ConsumedAtUtc);
        Assert.Equal(FakeInitialAdmin.DefaultEmail, (await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task Unique_index_race_returns_conflict_and_still_saves_the_consumed_code()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id, new RejectingEmailRepository()).ConfirmEmailAsync(
            new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.AlreadyExistsCode, result.Error.Code);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task Invalid_confirmation_does_not_lock_or_save_but_missing_user_after_validation_does_save()
    {
        var fixture = new Fixture();
        var missing = Guid.CreateVersion7();

        var invalid = await fixture.Service(missing).ConfirmEmailAsync(new ConfirmEmailRequest(Email, "12ab"), Ct);
        var valid = await fixture.Service(missing).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.IsType<ValidationError>(invalid.Error);
        Assert.Equal(UserErrors.NotFoundCode, valid.Error.Code);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
    }

    [Fact]
    public async Task With_a_verified_number_adding_an_email_needs_the_code_sent_to_that_number()
    {
        // N11: con la sesión abierta de otro, agregar su correo y desvincular el número dejaría afuera al dueño.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, error.Code);
        Assert.Equal(["phoneCode"], error.Errors.Keys);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task A_wrong_number_code_counts_its_attempt_on_that_field_and_spends_no_code()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var phoneCode = fixture.IssuePhone(user.Id);
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code, "000000"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(LoginCodeErrors.InvalidCode, error.Code);
        Assert.Equal(["phoneCode"], error.Errors.Keys);
        Assert.Equal(4, error.Metadata![LoginCodeErrors.AttemptsLeftKey]);
        Assert.Equal(1, phoneCode.FailedAttempts);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task A_wrong_email_code_keeps_the_number_code_usable_and_with_both_the_email_is_added()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var phoneCode = fixture.IssuePhone(user.Id);
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var wrong = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, "000000", Code), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.Error.Code);
        Assert.IsNotType<ValidationError>(wrong.Error);
        Assert.Equal(1, emailCode.FailedAttempts);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Equal(0, phoneCode.FailedAttempts);

        var right = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code, Code), Ct);

        Assert.True(right.IsSuccess);
        Assert.NotNull(phoneCode.ConsumedAtUtc);
        Assert.NotNull(emailCode.ConsumedAtUtc);
        // El orden único de los locks: el correo antes que el número.
        Assert.Equal([Email, Email, Phone], fixture.Codes.LockedDestinations);
        var updated = await fixture.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Equal(Email, updated!.Email);
        Assert.True(updated.EmailConfirmed);
        Assert.Equal(Phone, updated.PhoneNumber);
    }

    [Fact]
    public async Task A_verified_email_is_not_replaced_from_the_profile_and_no_code_is_spent()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("vieja@example.com");
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.EmailAlreadySet, result.Error);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Equal("vieja@example.com", (await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task Without_a_phone_channel_an_account_with_a_verified_number_cannot_add_an_email()
    {
        var fixture = new Fixture { PhoneChannelEnabled = false };
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code, Code), Ct);

        Assert.Equal(UserErrors.PhoneCodeUnavailable, result.Error);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task A_number_code_with_the_wrong_shape_is_rejected_before_the_transaction()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code, "12ab"), Ct);

        Assert.Contains("phoneCode", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_code_to_link_the_number_does_not_confirm_adding_an_email()
    {
        // Cada código sirve para su propósito: el que se pide para vincular un número no confirma agregar un correo.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var phoneCode = fixture.IssuePhone(user.Id, LoginCodePurpose.VerifyDestination);
        var emailCode = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code, Code), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(LoginCodeErrors.InvalidCode, error.Code);
        Assert.Equal(["phoneCode"], error.Errors.Keys);
        Assert.Null(phoneCode.ConsumedAtUtc);
        Assert.Null(emailCode.ConsumedAtUtc);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.Email);
    }

    [Fact]
    public async Task With_a_verified_email_no_code_for_another_email_is_sent()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("vieja@example.com");

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.Equal(UserErrors.EmailAlreadySet, result.Error);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
    }

    [Theory]
    [InlineData(ProfileChange.UnlinkPhone, "unlink subject")]
    [InlineData(ProfileChange.LinkPhone, "link subject")]
    public async Task The_confirmation_code_goes_to_the_verified_email_with_the_text_of_its_change(
        ProfileChange change, string subject)
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(
            Email, culture: "en", phoneNumber: change is ProfileChange.UnlinkPhone ? Phone : null);

        var result = await fixture.Service(user.Id).RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(change), Ct);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(fixture.Codes.Codes);
        Assert.Equal(Email, stored.Destination);
        Assert.Equal(LoginCodePurpose.ConfirmChange, stored.Purpose);
        Assert.Equal(user.Id, stored.RequestedByUserId);
        Assert.NotNull(stored.SentAtUtc);
        var message = Assert.Single(fixture.Queue.Messages);
        Assert.Equal(Email, message.To);
        Assert.Equal(subject, message.Subject);
        Assert.Equal("en", fixture.Renderer.LastCulture?.Name);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
    }

    [Fact]
    public async Task Without_a_verified_email_no_confirmation_code_is_sent()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);

        var result = await fixture.Service(user.Id).RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(ProfileChange.UnlinkPhone), Ct);

        Assert.Equal(UserErrors.NoVerifiedEmail, result.Error);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
    }

    [Fact]
    public async Task A_change_the_account_cannot_make_gets_no_confirmation_code()
    {
        // Sin número no hay qué desvincular, y un número verificado no se reemplaza desde el perfil.
        var fixture = new Fixture();
        var withoutPhone = fixture.Accounts.AddUser(Email);
        var withPhone = fixture.Accounts.AddUser("beto@example.com", phoneNumber: Phone);

        var unlink = await fixture.Service(withoutPhone.Id).RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(ProfileChange.UnlinkPhone), Ct);
        var link = await fixture.Service(withPhone.Id).RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(ProfileChange.LinkPhone), Ct);
        var missing = await fixture.Service(withPhone.Id).RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(null), Ct);

        Assert.Equal(UserErrors.NoPhone, unlink.Error);
        Assert.Equal(UserErrors.PhoneAlreadySet, link.Error);
        Assert.IsType<ValidationError>(missing.Error);
        Assert.Empty(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
    }

    [Fact]
    public async Task A_code_that_expires_between_the_check_and_the_spend_is_spent_with_the_time_of_the_check()
    {
        // Gastar con otra hora tiraba un 500 si el código vencía en el medio.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        var code = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        var verifier = new DestinationCodeVerifier(fixture.Codes, new FakeLoginCodeHasher(), fixture.Clock);
        var destination = LoginCodeDestination.ForEmail(ArquitecturaBase.Domain.ValueObjects.Email.Create(Email).Value);

        var result = await fixture.UnitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var check = await verifier.CheckAsync(destination, LoginCodePurpose.VerifyDestination, user.Id, Code, ct);
                fixture.Clock.Advance(TimeSpan.FromSeconds(2));

                DestinationCodeVerifier.Consume(check.Value);
                return Result.Success();
            },
            CommitPolicy.OnSuccess,
            Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(code.ExpiresAtUtc - TimeSpan.FromSeconds(1), code.ConsumedAtUtc);
    }

    private sealed class Fixture
    {
        private readonly IOptions<LoginCodeOptions> _options = Options.Create(new LoginCodeOptions());

        public InMemoryUserAccounts Accounts { get; } = new();
        public InMemoryLoginCodeRepository Codes { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        public FakeEmailTemplateRenderer Renderer { get; } = new();
        public FakeLogger<ProfileService> Logger { get; } = new();
        public List<string> Events { get; } = [];
        public RecordingEmailQueue Queue { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public DateTime? SentAtCommit { get; private set; }
        public int FailedAttemptsAtCommit { get; private set; }
        public bool CodeConsumedAtCommit { get; private set; }

        /// <summary>En false, el canal telefónico está apagado: no hay cómo mandarle un código al número.</summary>
        public bool PhoneChannelEnabled { get; init; } = true;

        public Fixture()
        {
            Queue = new RecordingEmailQueue(Events);
            UnitOfWork = new FakeUnitOfWork(Events)
            {
                // La foto que antes sacaba RecordingUnitOfWork al guardar, ahora al confirmar.
                OnCommit = () =>
                {
                    var code = Codes.Codes.LastOrDefault();
                    SentAtCommit = code?.SentAtUtc;
                    FailedAttemptsAtCommit = code?.FailedAttempts ?? 0;
                    CodeConsumedAtCommit = code?.ConsumedAtUtc is not null;
                },
            };
            Codes.InTransaction = () => UnitOfWork.InTransaction;
            Accounts.InTransaction = () => UnitOfWork.InTransaction;
        }

        public ProfileService Service(Guid? userId, IUserRepository? repository = null)
        {
            var currentUser = new FakeCurrentUser { UserId = userId };
            var hasher = new FakeLoginCodeHasher();
            var issuer = new DestinationCodeIssuer(
                new LoginCodeIssuer(Codes, new FakeLoginCodeGenerator(), hasher, _options, new FakeRequestInfo(), Clock),
                EmailQuotaGuards.For(Codes, Clock),
                Renderer,
                Queue);

            var emailLinker = new ProfileEmailLinker(
                Accounts,
                repository ?? Accounts,
                new DestinationCodeVerifier(Codes, hasher, Clock),
                new FakeInitialAdmin(),
                new FakePhoneChannel(PhoneChannelEnabled));

            return new ProfileService(currentUser, Accounts, repository ?? Accounts, issuer, emailLinker,
                RequestValidators.For(
                    new UpdateProfileRequestValidator(),
                    new RequestEmailCodeRequestValidator(),
                    new ConfirmEmailRequestValidator(_options),
                    new RequestEmailConfirmationCodeRequestValidator()),
                UnitOfWork, Logger);
        }

        /// <summary>
        /// El código que se le mandó al número de la cuenta, como el que pide el canal telefónico para confirmar el cambio.
        /// </summary>
        public LoginCode IssuePhone(Guid owner, LoginCodePurpose purpose = LoginCodePurpose.ConfirmChange)
        {
            var destination = LoginCodeDestination.ForPhone(PhoneNumber.Create(Phone).Value);
            var code = LoginCode.Issue(destination, purpose, owner,
                FakeLoginCodeHasher.HashOf(Phone, purpose, Code), Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(10), maxAttempts: 5);
            Codes.Add(code);
            return code;
        }

        public LoginCode Issue(string email, LoginCodePurpose purpose, Guid? owner)
        {
            var destination = LoginCodeDestination.ForEmail(ArquitecturaBase.Domain.ValueObjects.Email.Create(email).Value);
            var code = LoginCode.Issue(destination, purpose, owner,
                FakeLoginCodeHasher.HashOf(email, purpose, Code), Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(10), maxAttempts: 5);
            Codes.Add(code);
            return code;
        }
    }

    private sealed class RecordingEmailQueue(List<string> events) : IEmailQueue
    {
        public List<EmailMessage> Messages { get; } = [];

        /// <summary>En false, hace de cola llena.</summary>
        public bool Accepts { get; set; } = true;

        public bool TryEnqueue(EmailMessage message)
        {
            events.Add("enqueue");
            if (Accepts)
            {
                Messages.Add(message);
            }

            return Accepts;
        }
    }

    private sealed class RejectingEmailRepository : IUserRepository
    {
        public Task LockExternalSignInAsync(
            Email email, string provider, string providerKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task LockAdminsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task LockAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<UserAccount> CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed,
            string? displayName, string culture, CancellationToken cancellationToken, string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId) => throw new NotSupportedException();

        public Task<UserAccount> CreateUnverifiedAsync(Email? email, PhoneNumber? phone,
            string? displayName, string culture, CancellationToken cancellationToken, string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId) => throw new NotSupportedException();

        public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreAsync(
            Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
            throw new UniqueConstraintViolationException("Email is already in use.");

        public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RoleAssignment> SetRolesAsync(
            Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateProfileAsync(Guid userId, string? displayName, string culture,
            string timeZoneId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
