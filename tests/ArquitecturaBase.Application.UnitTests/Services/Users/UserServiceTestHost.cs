using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// Los tres servicios de usuarios armados como en la Api, sin ningún módulo: invitan solo por correo (el canal real del
/// núcleo), el canal telefónico no pone regla de país y el número tiene un participante que anota en
/// <see cref="PhoneEvents"/>, en la misma lista que el lock de los enlaces de la cuenta. Un módulo suma sus canales y
/// participantes reales con el constructor protegido (con WhatsApp, WhatsAppUserServiceTestHost).
/// </summary>
internal class UserServiceTestHost
{
    public InMemoryUserAccounts Accounts { get; } = new();
    public FakeSignInService SignIn { get; } = new();
    public InMemoryUserInvitationRepository Invitations { get; } = new();
    public FakeUserInvitationReader InvitationReader { get; } = new();
    public FakeLogger<UserQueryService> QueryLogger { get; } = new();
    public FakeLogger<UserAdministrationService> AdministrationLogger { get; } = new();
    public FakeLogger<UserAccessService> AccessLogger { get; } = new();
    public FakeLogger<ProfilePhoneService> ProfilePhoneLogger { get; } = new();
    public FakeLogger<EmailInvitationChannel> InvitationLogger { get; } = new();
    public InMemoryLoginCodeRepository Destinations { get; } = new();

    /// <summary>Los locks y los avisos del número, en orden: los del participante y el de los enlaces de la cuenta.</summary>
    public List<string> PhoneEvents { get; } = [];

    public InMemoryLoginLinkRepository Links { get; }

    /// <summary>El participante del número del núcleo: anota sus locks y sus avisos en <see cref="PhoneEvents"/>.</summary>
    public RecordingPhoneLinkParticipant Participant { get; }

    public FakeCurrentUser CurrentUser { get; } = new() { UserId = Guid.CreateVersion7() };
    public FakeUnitOfWork UnitOfWork { get; }

    /// <summary>Cuántos correos había en la cola cuando se confirmó la unidad de trabajo.</summary>
    public int? QueuedAtCommit { get; private set; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
    public FakeEmailQueue EmailQueue { get; } = new();

    /// <summary>La fuente del estado de entrega de un canal que lo sigue (WhatsApp, como dato): el correo no tiene.</summary>
    public FakeInvitationDeliveryStatusSource DeliveryStatuses { get; } = new(UserInvitationChannel.WhatsApp);

    public FakeRoleReader RoleReader { get; }
    public UserQueryService Queries { get; }
    public UserAdministrationService Administration { get; }
    public UserAccessService Access { get; }
    public ProfilePhoneService ProfilePhone { get; }

    /// <summary>Sin módulos: el canal telefónico acepta un número de cualquier país.</summary>
    public UserServiceTestHost()
        : this(new FakePhoneChannel(isEnabled: false))
    {
    }

    /// <summary>Sin módulos, con el canal telefónico que diga el test (por ejemplo, uno que rechaza).</summary>
    public UserServiceTestHost(IPhoneChannel phoneChannel)
        : this(phoneChannel, [], [])
    {
    }

    /// <summary>
    /// Con lo que suma un módulo: su canal telefónico, sus canales de invitación (al lado del correo) y sus
    /// participantes del número (después del que anota).
    /// </summary>
    protected UserServiceTestHost(
        IPhoneChannel phoneChannel,
        IEnumerable<IInvitationChannel> moduleInvitationChannels,
        IEnumerable<IPhoneLinkParticipant> moduleParticipants)
    {
        UnitOfWork = new FakeUnitOfWork { OnCommit = () => QueuedAtCommit = EmailQueue.Messages.Count };
        Links = new InMemoryLoginLinkRepository { Events = PhoneEvents };
        Participant = new RecordingPhoneLinkParticipant(PhoneEvents);
        Accounts.InTransaction = () => UnitOfWork.InTransaction;
        SignIn.InTransaction = () => UnitOfWork.InTransaction;
        Destinations.InTransaction = () => UnitOfWork.InTransaction;
        Invitations.InTransaction = () => UnitOfWork.InTransaction;
        Links.InTransaction = () => UnitOfWork.InTransaction;
        RoleReader = new FakeRoleReader();
        var phoneNumbers = new FakePhoneNumberParser();
        var phoneLinker = new PhoneNumberLinker(
            Accounts,
            Accounts,
            new DestinationCodeVerifier(Destinations, new FakeLoginCodeHasher(), Clock),
            [Participant, .. moduleParticipants],
            Links,
            Clock);
        var guard = new UserGuard(CurrentUser, Accounts, Accounts, RoleReader);
        IInvitationChannel[] invitationChannels =
        [
            new EmailInvitationChannel(
                EmailQueue,
                new FakeEmailTemplateRenderer(),
                new FakePublicOrigin(new Uri("https://example.test/")),
                InvitationLogger),
            .. moduleInvitationChannels,
        ];

        var invitationIssuer = new UserInvitationIssuer(Invitations, invitationChannels, CurrentUser, Clock);
        var contacts = new UserContactLinker(Accounts, Accounts, Destinations, phoneLinker, phoneNumbers, phoneChannel, new AccountCreationPolicy(new FakeSystemSettingsReader(), new FakeInitialAdmin()));
        var revoker = new AccountAccessRevoker(Links, SignIn, Clock);

        Queries = new UserQueryService(
            Accounts,
            InvitationReader,
            [DeliveryStatuses],
            phoneNumbers,
            RequestValidators.For(new ListUsersRequestValidator()),
            QueryLogger);
        Administration = new UserAdministrationService(
            Accounts,
            Accounts,
            contacts,
            invitationIssuer,
            guard,
            RequestValidators.For(
                new CreateUserRequestValidator(),
                new UpdateUserRequestValidator(),
                new SendUserInvitationRequestValidator()),
            UnitOfWork,
            AdministrationLogger);
        Access = new UserAccessService(
            Accounts, Accounts, guard, Links, phoneLinker, revoker, UnitOfWork, AccessLogger);
        ProfilePhone = new ProfilePhoneService(
            CurrentUser, Accounts, guard, phoneLinker, UnitOfWork, ProfilePhoneLogger);
    }

    /// <summary>
    /// Los roles que existen, para que el alta y la edición validen los nombres (UserGuard.EnsureRolesExistAsync): las
    /// demás lecturas de roles no se usan acá.
    /// </summary>
    internal sealed class FakeRoleReader : IRoleReader
    {
        public List<string> RoleNames { get; } = ["Admin", "User"];

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(RoleNames);

        public Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");

        public Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");

        public Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");

        public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");
    }
}
