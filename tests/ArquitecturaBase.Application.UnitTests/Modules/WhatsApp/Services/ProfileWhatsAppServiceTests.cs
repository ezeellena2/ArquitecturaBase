using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// Pedir el código, confirmar y desvincular el número propio desde el perfil. Nacieron como tests de caracterización,
/// antes de partir el perfil (tarea 6 del diseño de la Etapa 3): fijan el orden de los locks, la política de cada límite y
/// lo que queda escrito cuando la confirmación falla.
/// </summary>
public sealed class ProfileWhatsAppServiceTests
{
    private const string Phone = "+5493511234567";
    private const string WaId = "5493511234567";
    private const string Code = FakeLoginCodeGenerator.Code;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Disabled_whatsapp_is_a_programming_error_after_request_validation_without_opening_a_transaction()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DisabledService(user.Id)
            .RequestPhoneLinkCodeAsync(new RequestPhoneLinkCodeRequest("AR", "+5493515550101"), Ct));

        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    /// <summary>
    /// El validador corre antes del guard: con WhatsApp apagado, un pedido inválido igual responde su ValidationError.
    /// </summary>
    [Fact]
    public async Task Disabled_whatsapp_still_answers_an_invalid_request_with_its_validation_error()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");

        var result = await fixture.DisabledService(user.Id)
            .RequestPhoneLinkCodeAsync(new RequestPhoneLinkCodeRequest("", ""), Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task The_code_to_link_a_number_is_the_account_s_own_and_is_queued_before_the_commit()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", culture: "en");

        var result = await fixture.Service(user.Id)
            .RequestPhoneLinkCodeAsync(new RequestPhoneLinkCodeRequest("AR", Phone), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(Phone, result.Value.Phone);
        var code = Assert.Single(fixture.Codes.Codes);
        Assert.Equal((LoginCodePurpose.VerifyDestination, user.Id), (code.Purpose, code.RequestedByUserId));
        Assert.NotNull(code.SentAtUtc);
        var message = Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(fixture.SendQueue.Messages));
        Assert.Equal((Phone, "en", Code), (message.To.Value, message.LanguageCode, message.Code));
        Assert.Equal(["login-code:" + Phone], fixture.Locks.Keys);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_daily_limit_of_whatsapp_codes_is_checked_before_locking_the_number()
    {
        // El tope es de todos los números y no se protege con el lock de uno: se mira antes, sin esperar a nadie.
        var fixture = new Fixture { DailyAuthCodeLimit = 1 };
        var user = fixture.Accounts.AddUser("ana@example.com");
        var sent = LoginCode.Issue(
            LoginCodeDestination.ForPhone(PhoneNumber.Create("+5493515550199").Value), LoginCodePurpose.SignIn,
            requestedByUserId: null, "hash", fixture.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10), maxAttempts: 5);
        sent.MarkSent(fixture.Clock.GetUtcNow().UtcDateTime);
        fixture.Codes.Add(sent);

        var result = await fixture.Service(user.Id)
            .RequestPhoneLinkCodeAsync(new RequestPhoneLinkCodeRequest("AR", Phone), Ct);

        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, result.Error.Code);
        Assert.Empty(fixture.Locks.Keys);
        Assert.Single(fixture.Codes.Codes);
        Assert.Empty(fixture.SendQueue.Messages);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Confirm_locks_the_code_then_the_contacts_then_the_account_links_and_links_the_number()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        var contact = fixture.AddContact(WaId);
        var code = fixture.Issue(user.Id);
        fixture.Links.WhileWaitingForTheLock = userId =>
        {
            fixture.Locks.Lock(["login-link:" + userId]);
            return Task.CompletedTask;
        };

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(new ConfirmPhoneLinkRequest(Phone, Code), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                "login-code:" + Phone, "number-change:" + user.Id, "number-change:wa:" + WaId, "login-link:" + user.Id,
                "unlink:" + user.Id,
            ],
            fixture.Locks.Keys);
        var updated = await fixture.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Equal(Phone, updated!.PhoneNumber);
        Assert.True(updated.PhoneNumberConfirmed);
        Assert.Equal(user.Id, contact.UserId);
        Assert.NotNull(code.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(
            ["Handling ConfirmPhoneLink", "Handled ConfirmPhoneLink"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Confirm_reads_the_account_after_the_locks()
    {
        // Mientras espera el lock de los enlaces, otro pedido borra la cuenta: la lectura de después ya no la ve. Si la
        // cuenta se leyera antes de los locks, la confirmación seguiría con lo leído.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        var code = fixture.Issue(user.Id);
        fixture.Links.WhileWaitingForTheLock = userId =>
            fixture.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(userId, Ct));

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(new ConfirmPhoneLinkRequest(Phone, Code), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.NotNull(code.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occupied_number_is_revealed_only_after_a_correct_code_and_the_code_is_saved_as_spent(bool deleted)
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        if (deleted)
        {
            var owner = fixture.Accounts.AddUser("beto@example.com", phoneNumber: Phone);
            await fixture.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(owner.Id, Ct));
        }
        else
        {
            fixture.Accounts.AddUser("beto@example.com", phoneNumber: Phone);
        }

        var contact = fixture.AddContact(WaId);
        var code = fixture.Issue(user.Id);

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(new ConfirmPhoneLinkRequest(Phone, Code), Ct);

        Assert.Equal(UserErrors.PhoneAlreadyExists, result.Error);
        Assert.NotNull(code.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Null(contact.UserId);
    }

    [Fact]
    public async Task Unique_index_race_returns_conflict_and_still_saves_the_consumed_code()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        var contact = fixture.AddContact(WaId);
        var code = fixture.Issue(user.Id);
        fixture.Repository = new RejectingPhoneRepository(fixture.Accounts);

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(new ConfirmPhoneLinkRequest(Phone, Code), Ct);

        Assert.Equal(UserErrors.PhoneAlreadyExists, result.Error);
        Assert.NotNull(code.ConsumedAtUtc);
        Assert.True(fixture.CodeConsumedAtCommit);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Null(contact.UserId);
    }

    [Fact]
    public async Task Wrong_code_saves_the_failed_attempt_before_taking_any_other_lock()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        var code = fixture.Issue(user.Id);

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(
            new ConfirmPhoneLinkRequest(Phone, "000000"), Ct);

        Assert.Equal(LoginCodeErrors.InvalidCode, result.Error.Code);
        Assert.Equal(1, code.FailedAttempts);
        Assert.Equal(1, fixture.FailedAttemptsAtCommit);
        Assert.Null(code.ConsumedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnAnyResult, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(["login-code:" + Phone], fixture.Locks.Keys);
        Assert.Empty(fixture.Links.LockedAccounts);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Equal(
            "ConfirmPhoneLink failed with " + LoginCodeErrors.InvalidCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public void Confirming_or_unlinking_the_own_number_cannot_reach_the_session()
    {
        // Confirmar un destino no es un ingreso: no suma a los fallos de la cuenta (RegisterFailedAttemptAsync). Y
        // desvincular el número propio no cierra las sesiones: eso lo hace solo un administrador, con
        // AccountAccessRevoker. Ninguna de las piezas que arman el caso de uso recibe ISignInService.
        var dependencies = Dependencies(Fixture.EntryPoint);

        Assert.Contains(typeof(DestinationCodeVerifier), dependencies);
        Assert.DoesNotContain(typeof(ISignInService), dependencies);
        Assert.DoesNotContain(typeof(AccountAccessRevoker), dependencies);
    }

    [Theory]
    [InlineData("+5493517654321", true)]
    [InlineData(Phone, false)]
    [InlineData(null, false)]
    public async Task Confirm_voids_the_pending_links_only_when_the_number_changes(string? previous, bool voided)
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: previous);
        var link = fixture.AddLink(user.Id);
        fixture.Issue(user.Id);

        var result = await fixture.Service(user.Id).ConfirmPhoneLinkAsync(new ConfirmPhoneLinkRequest(Phone, Code), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(voided, link.InvalidatedAtUtc is not null);
        Assert.Equal(Phone, (await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task The_only_login_method_cannot_be_unlinked()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        var contact = fixture.AddContact(WaId, user.Id);

        var result = await fixture.Service(user.Id).UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.Equal(Phone, (await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
        Assert.Equal(user.Id, contact.UserId);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(
            "UnlinkOwnPhone failed with " + UserErrors.LastLoginMethodCode,
            fixture.Logger.Collector.GetSnapshot()[^1].Message);
    }

    [Fact]
    public async Task Google_counts_as_another_login_method()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null, phoneNumber: Phone);
        fixture.Accounts.LinkExternalLogin(user.Id, ExternalLoginProviders.Google, "google-123");

        var result = await fixture.Service(user.Id).UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null((await fixture.Accounts.FindByIdAsync(user.Id, Ct))!.PhoneNumber);
    }

    [Fact]
    public async Task Unlink_locks_the_contacts_then_the_account_links_and_voids_only_the_active_links()
    {
        // Sin AccountAccessRevoker: un enlace vencido queda como estaba (el administrador los invalida todos, porque
        // además corta las sesiones).
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        var contact = fixture.AddContact(WaId, user.Id);
        var active = fixture.AddLink(user.Id);
        var expired = LoginLink.Issue(user.Id, "hash-expired", fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-1));
        fixture.Links.Links.Add(expired);
        fixture.Links.WhileWaitingForTheLock = userId =>
        {
            fixture.Locks.Lock(["login-link:" + userId]);
            return Task.CompletedTask;
        };

        var result = await fixture.Service(user.Id).UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["number-change:" + user.Id, "login-link:" + user.Id], fixture.Locks.Keys);
        var updated = await fixture.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Null(updated!.PhoneNumber);
        Assert.False(updated.PhoneNumberConfirmed);
        Assert.Null(contact.UserId);
        Assert.NotNull(active.InvalidatedAtUtc);
        Assert.Null(expired.InvalidatedAtUtc);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(
            ["Handling UnlinkOwnPhone", "Handled UnlinkOwnPhone"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Unlink_without_a_number_still_releases_the_contact_and_voids_the_links()
    {
        // Sin número no hay regla que mirar, pero un contacto viejo o un enlace que mandó el bot igual se sueltan.
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser(email: null);
        var contact = fixture.AddContact(WaId, user.Id);
        var link = fixture.AddLink(user.Id);

        var result = await fixture.Service(user.Id).UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(contact.UserId);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal([user.Id], fixture.Links.LockedAccounts);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Unlink_reads_the_account_after_the_locks()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        fixture.Links.WhileWaitingForTheLock = userId =>
            fixture.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(userId, Ct));

        var result = await fixture.Service(user.Id).UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    /// <summary>Los tipos que recibe <paramref name="root"/> en su constructor y, de las clases, los que reciben ellas.</summary>
    private static HashSet<Type> Dependencies(Type root)
    {
        var found = new HashSet<Type>();
        var pending = new Stack<Type>([root]);

        while (pending.TryPop(out var type))
        {
            foreach (var parameter in type.GetConstructors(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters()))
            {
                if (found.Add(parameter.ParameterType)
                    && parameter.ParameterType.IsClass
                    && parameter.ParameterType.Assembly == root.Assembly)
                {
                    pending.Push(parameter.ParameterType);
                }
            }
        }

        return found;
    }

    private sealed class Fixture
    {
        /// <summary>La clase que abre el límite de los dos casos de uso.</summary>
        public static readonly Type EntryPoint = typeof(ProfileWhatsAppService);

        private readonly IOptions<LoginCodeOptions> _options = Options.Create(new LoginCodeOptions());

        public InMemoryUserAccounts Accounts { get; } = new();
        public InMemoryLoginCodeRepository Codes { get; } = new();
        public InMemoryLoginLinkRepository Links { get; } = new();
        public LockLog Locks { get; } = new();
        public InMemoryWhatsAppContactRepository Contacts { get; }
        public FakeWhatsAppSendQueue SendQueue { get; } = new();
        public int DailyAuthCodeLimit { get; init; } = 100;
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
        public FakeLogger<ProfileWhatsAppService> Logger { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public IUserRepository? Repository { get; set; }
        public int FailedAttemptsAtCommit { get; private set; }
        public bool CodeConsumedAtCommit { get; private set; }

        public Fixture()
        {
            UnitOfWork = new FakeUnitOfWork
            {
                OnCommit = () =>
                {
                    var code = Codes.Codes.LastOrDefault();
                    FailedAttemptsAtCommit = code?.FailedAttempts ?? 0;
                    CodeConsumedAtCommit = code?.ConsumedAtUtc is not null;
                },
            };
            Accounts.InTransaction = () => UnitOfWork.InTransaction;
            Codes.InTransaction = () => UnitOfWork.InTransaction;
            Links.InTransaction = () => UnitOfWork.InTransaction;
            Locks.InTransaction = () => UnitOfWork.InTransaction;
            Contacts = new InMemoryWhatsAppContactRepository(Locks);
        }

        public ProfileWhatsAppService Service(Guid? userId)
        {
            var currentUser = new FakeCurrentUser { UserId = userId };
            var codes = new SequencedLoginCodes(Codes, Locks);
            var hasher = new FakeLoginCodeHasher();
            var whatsAppOptions = Options.Create(new WhatsAppLoginOptions { DailyAuthCodeLimit = DailyAuthCodeLimit });
            var linker = new WhatsAppContactLinker(Contacts);
            var issuer = new WhatsAppCodeIssuer(
                new LoginCodeIssuer(codes, new FakeLoginCodeGenerator(), hasher, _options, Clock),
                new WhatsAppCodeQuotaGuard(codes, whatsAppOptions, Clock, NullLogger<WhatsAppCodeQuotaGuard>.Instance),
                Accounts,
                new FakePhoneNumberParser(),
                SendQueue,
                new FakeWhatsAppAvailability(IsEnabled: true),
                new AccountCreationPolicy(new FakeSystemSettingsReader(), new FakeInitialAdmin()),
                whatsAppOptions);
            var phoneLinker = new PhoneNumberLinker(
                Accounts, Repository ?? Accounts, new DestinationCodeVerifier(codes, hasher, Clock), linker, Links, Clock);

            return new ProfileWhatsAppService(
                currentUser, Accounts, new UserGuard(currentUser, Accounts, Accounts, new UserServiceTestHost.FakeRoleReader()), issuer, phoneLinker, Validator(), UnitOfWork,
                Logger);
        }

        /// <summary>
        /// Con WhatsApp apagado. Antes del guard solo corre el validador del pedido: el resto de las dependencias no se
        /// toca.
        /// </summary>
        public ProfileWhatsAppService DisabledService(Guid userId) =>
            new(
                new FakeCurrentUser { UserId = userId }, Accounts, null!,
                new WhatsAppCodeIssuer(
                    null!, null!, null!, new FakePhoneNumberParser(), null!, new FakeWhatsAppAvailability(IsEnabled: false),
                    null!, Options.Create(new WhatsAppLoginOptions())),
                null!, Validator(), UnitOfWork, Logger);

        private IRequestValidator Validator() =>
            RequestValidators.For(
                new RequestPhoneLinkCodeRequestValidator(), new ConfirmPhoneLinkRequestValidator(_options));

        /// <summary>El código que pidió <paramref name="owner"/> para vincular <see cref="Phone"/>.</summary>
        public LoginCode Issue(Guid owner)
        {
            var destination = LoginCodeDestination.ForPhone(PhoneNumber.Create(Phone).Value);
            var code = LoginCode.Issue(destination, LoginCodePurpose.VerifyDestination, owner,
                FakeLoginCodeHasher.HashOf(Phone, LoginCodePurpose.VerifyDestination, Code),
                Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10), maxAttempts: 5);
            Codes.Add(code);
            return code;
        }

        public WhatsAppContact AddContact(string waId, Guid? userId = null)
        {
            var contact = WhatsAppContact.Create(waId, userIdentifier: null, "Ana", Clock.GetUtcNow().UtcDateTime);
            if (userId is { } id)
            {
                contact.LinkUser(id);
            }

            Contacts.Contacts.Add(contact);
            return contact;
        }

        public LoginLink AddLink(Guid userId)
        {
            var link = LoginLink.Issue(userId, "hash-" + Links.Links.Count, Clock.GetUtcNow().UtcDateTime);
            Links.Links.Add(link);
            return link;
        }
    }

    /// <summary>Anota el lock de cada destino en el mismo registro que los contactos y los enlaces, para ver el orden.</summary>
    private sealed class SequencedLoginCodes(InMemoryLoginCodeRepository inner, LockLog locks) : ILoginCodeRepository
    {
        public Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken)
        {
            locks.Lock(["login-code:" + destination.Value]);
            return inner.LockDestinationAsync(destination, cancellationToken);
        }

        public Task<LoginCode?> GetLatestAsync(LoginCodeDestination destination, LoginCodePurpose purpose,
            Guid? requestedByUserId, CancellationToken cancellationToken) =>
            inner.GetLatestAsync(destination, purpose, requestedByUserId, cancellationToken);

        public Task<IReadOnlyList<LoginCode>> ListActiveAsync(LoginCodeDestination destination, LoginCodePurpose purpose,
            Guid? requestedByUserId, DateTime nowUtc, CancellationToken cancellationToken) =>
            inner.ListActiveAsync(destination, purpose, requestedByUserId, nowUtc, cancellationToken);

        public Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(LoginCodeDestination destination,
            DateTime sinceUtc, CancellationToken cancellationToken) =>
            inner.ListRequestTimesSinceAsync(destination, sinceUtc, cancellationToken);

        public Task<IReadOnlyList<DateTime>> ListLatestSentTimesAsync(LoginCodeChannel channel, DateTime sinceUtc,
            int count, CancellationToken cancellationToken) =>
            inner.ListLatestSentTimesAsync(channel, sinceUtc, count, cancellationToken);

        public void Add(LoginCode loginCode) => inner.Add(loginCode);
    }

    /// <summary>El 23505 de la carrera: otra cuenta guardó el número entre la consulta y la escritura.</summary>
    private sealed class RejectingPhoneRepository(InMemoryUserAccounts inner) : IUserRepository
    {
        public Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
            throw new UniqueConstraintViolationException("Phone number is already in use.");

        public Task LockExternalSignInAsync(
            Email email, string provider, string providerKey, CancellationToken cancellationToken) =>
            inner.LockExternalSignInAsync(email, provider, providerKey, cancellationToken);

        public Task LockAdminsAsync(CancellationToken cancellationToken) => inner.LockAdminsAsync(cancellationToken);

        public Task<UserAccount> CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed,
            string? displayName, string culture, CancellationToken cancellationToken) =>
            inner.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken);

        public Task<UserAccount> CreateUnverifiedAsync(Email? email, PhoneNumber? phone,
            string? displayName, string culture, CancellationToken cancellationToken) =>
            inner.CreateUnverifiedAsync(email, phone, displayName, culture, cancellationToken);

        public Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken) =>
            inner.AddExternalLoginAsync(userId, login, cancellationToken);

        public Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
            inner.SetActiveAsync(userId, isActive, cancellationToken);

        public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.RemovePhoneAsync(userId, cancellationToken);

        public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(userId, cancellationToken);

        public Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
            inner.RestoreAsync(userId, displayName, cancellationToken);

        public Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken) =>
            inner.SetEmailAsync(userId, email, confirmed, cancellationToken);

        public Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) =>
            inner.SetRolesAsync(userId, roles, cancellationToken);

        public Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken) =>
            inner.SetDisplayNameAsync(userId, displayName, cancellationToken);

        public Task UpdateProfileAsync(Guid userId, string? displayName, string culture,
            string timeZoneId, CancellationToken cancellationToken) =>
            inner.UpdateProfileAsync(userId, displayName, culture, timeZoneId, cancellationToken);
    }
}
