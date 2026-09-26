using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class ProfileEmailServiceTests
{
    private const string Email = "ana@example.com";
    private const string Code = FakeLoginCodeGenerator.Code;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Request_normalizes_email_and_enqueues_in_account_language_before_saving()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567", culture: "en");
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
        Assert.Equal(["enqueue", "save"], fixture.Events);
        Assert.Equal(stored.SentAtUtc, fixture.UnitOfWork.SentAtSave);
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
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");
        fixture.Issue(Email, LoginCodePurpose.SignIn, owner: null);

        var blocked = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, blocked.Error.Code);
        Assert.Single(fixture.Codes.Codes);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);

        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        var allowed = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);

        Assert.True(allowed.IsSuccess);
        Assert.Equal(2, fixture.Codes.Codes.Count);
        Assert.Null(fixture.Codes.Codes[0].InvalidatedAtUtc);
        Assert.Equal(LoginCodePurpose.VerifyDestination, fixture.Codes.Codes[1].Purpose);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_issuing_or_saving()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");

        var result = await fixture.Service(user.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest("ana@"), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("email", error.Errors.Keys);
        Assert.Empty(fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Queue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Wrong_confirmation_code_saves_the_failed_attempt_without_affecting_session()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, "000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(1, issued.FailedAttempts);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Equal(1, fixture.UnitOfWork.FailedAttemptsAtSave);
        Assert.Equal([Email], fixture.Codes.LockedDestinations);
        Assert.Empty(fixture.Identity.RevokedUsers);
        Assert.Empty(fixture.Identity.SignedInUsers);
        Assert.Null((await fixture.Identity.FindByIdAsync(user.Id, Ct))!.Email);
        Assert.Equal(
            "ConfirmEmail failed with " + LoginCodeErrors.InvalidCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occupied_email_is_revealed_only_after_a_correct_code_and_the_code_is_saved_as_spent(bool deleted)
    {
        var fixture = new Fixture();
        var requester = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");
        if (deleted)
        {
            fixture.Identity.DeletedEmails.Add(Email);
        }
        else
        {
            fixture.Identity.AddUser(Email);
        }

        var request = await fixture.Service(requester.Id).RequestEmailCodeAsync(new RequestEmailCodeRequest(Email), Ct);
        Assert.True(request.IsSuccess);
        var issued = Assert.Single(fixture.Codes.Codes);

        var result = await fixture.Service(requester.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.AlreadyExistsCode, result.Error.Code);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.Equal(2, fixture.UnitOfWork.SaveCalls);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Null((await fixture.Identity.FindByIdAsync(requester.Id, Ct))!.Email);
        Assert.Empty(fixture.Identity.RevokedUsers);
    }

    [Fact]
    public async Task Correct_code_sets_verified_email_without_revoking_the_current_session()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id).ConfirmEmailAsync(new ConfirmEmailRequest(Email, Code), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        var updated = await fixture.Identity.FindByIdAsync(user.Id, Ct);
        Assert.Equal(Email, updated!.Email);
        Assert.True(updated.EmailConfirmed);
        Assert.Equal("+5493511234567", updated.PhoneNumber);
        Assert.Empty(fixture.Identity.RevokedUsers);
        Assert.Empty(fixture.Identity.SignedInUsers);
    }

    [Fact]
    public async Task Unique_index_race_returns_conflict_and_still_saves_the_consumed_code()
    {
        var fixture = new Fixture();
        var user = fixture.Identity.AddUser(email: null, phoneNumber: "+5493511234567");
        var issued = fixture.Issue(Email, LoginCodePurpose.VerifyDestination, user.Id);

        var result = await fixture.Service(user.Id, new RejectingEmailRepository()).ConfirmEmailAsync(
            new ConfirmEmailRequest(Email, Code), Ct);

        Assert.Equal(UserErrors.AlreadyExistsCode, result.Error.Code);
        Assert.NotNull(issued.ConsumedAtUtc);
        Assert.True(fixture.UnitOfWork.CodeConsumedAtSave);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
        Assert.Null((await fixture.Identity.FindByIdAsync(user.Id, Ct))!.Email);
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
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public void Requests_do_not_write_email_or_code_into_action_argument_logs()
    {
        Assert.Equal(nameof(RequestEmailCodeRequest), new RequestEmailCodeRequest(Email).ToString());
        Assert.Equal(nameof(ConfirmEmailRequest), new ConfirmEmailRequest(Email, Code).ToString());
        Assert.Equal(nameof(RequestPhoneLinkCodeRequest),
            new RequestPhoneLinkCodeRequest("AR", "3515551234").ToString());
        Assert.Equal(nameof(ConfirmPhoneLinkRequest),
            new ConfirmPhoneLinkRequest("+5493515551234", Code).ToString());
    }

    private sealed class Fixture
    {
        private readonly IOptions<LoginCodeOptions> _options = Options.Create(new LoginCodeOptions());

        public FakeIdentityService Identity { get; } = new();
        public InMemoryLoginCodeRepository Codes { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        public FakeEmailTemplateRenderer Renderer { get; } = new();
        public FakeLogger<ProfileService> Logger { get; } = new();
        public List<string> Events { get; } = [];
        public RecordingEmailQueue Queue { get; }
        public RecordingUnitOfWork UnitOfWork { get; }

        public Fixture()
        {
            Queue = new RecordingEmailQueue(Events);
            UnitOfWork = new RecordingUnitOfWork(Events, Codes);
        }

        public ProfileService Service(Guid? userId, IUserRepository? repository = null)
        {
            var currentUser = new FakeCurrentUser { UserId = userId };
            var hasher = new FakeLoginCodeHasher();
            var operations = new ProfileEmailOperations(
                currentUser, Identity, repository ?? Identity,
                new LoginCodeIssuer(Codes, new FakeLoginCodeGenerator(), hasher, _options,
                    Options.Create(new WhatsAppLoginOptions()), Clock, NullLogger<LoginCodeIssuer>.Instance),
                new DestinationCodeVerifier(Codes, hasher, Clock), Renderer, Queue, _options,
                new ServiceRequestValidator<RequestEmailCodeRequest>([new RequestEmailCodeRequestValidator()]),
                new ServiceRequestValidator<ConfirmEmailRequest>([new ConfirmEmailRequestValidator(_options)]),
                UnitOfWork);

            return new ProfileService(currentUser, Identity, Identity, new FakePermissionService(),
                new InMemoryLoginAuditRepository(), new FakePhoneNumberParser(),
                new ServiceRequestValidator<UpdateProfileRequest>([new UpdateProfileRequestValidator()]),
                operations, null!, UnitOfWork, Logger);
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

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            events.Add("enqueue");
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork(List<string> events, InMemoryLoginCodeRepository codes) : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public DateTime? SentAtSave { get; private set; }
        public int FailedAttemptsAtSave { get; private set; }
        public bool CodeConsumedAtSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            events.Add("save");
            SaveCalls++;
            var code = codes.Codes.LastOrDefault();
            SentAtSave = code?.SentAtUtc;
            FailedAttemptsAtSave = code?.FailedAttempts ?? 0;
            CodeConsumedAtSave = code?.ConsumedAtUtc is not null;
            return Task.FromResult(1);
        }
    }

    private sealed class RejectingEmailRepository : IUserRepository
    {
        public Task LockExternalSignInAsync(
            Email email, string provider, string providerKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UserAccount> CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed,
            string? displayName, string culture, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserAccount> CreateUnverifiedAsync(Email? email, PhoneNumber? phone,
            string? displayName, string culture, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
            throw new UniqueConstraintViolationException("Email is already in use.");

        public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateProfileAsync(Guid userId, string? displayName, string culture,
            string timeZoneId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
