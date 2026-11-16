using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// Los tres servicios de usuarios armados como en la Api, sin ningún módulo: invitan solo por correo (el canal real del
/// núcleo), el canal telefónico no pone regla de país y el número tiene un participante que anota en
/// <see cref="PhoneEvents"/>, en la misma lista que el lock de los enlaces de la cuenta. Un módulo suma sus canales y
/// participantes reales con el constructor protegido (con WhatsApp, WhatsAppUserServiceTestHost). Quien pide
/// (<see cref="CurrentUser"/>) tiene el rol Admin, con todos los permisos del núcleo, sin ser una cuenta de
/// <see cref="Accounts"/>: no cambia los listados ni el conteo de administradores.
/// </summary>
internal class UserServiceTestHost
{
    public InMemoryUserAccounts Accounts { get; } = new();
    public FakeSignInService SignIn { get; } = new();
    public InMemoryUserInvitationRepository Invitations { get; } = new();
    public InMemoryAdministrativeChangeRepository Changes { get; } = new();
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
    public FakeInitialAdmin InitialAdmin { get; } = new();

    /// <summary>La presentación del sistema: el idioma y la zona horaria con los que nace (o se restaura) una cuenta.</summary>
    public FakeSystemSettingsReader SystemSettings { get; } = new();

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
        Changes.InTransaction = () => UnitOfWork.InTransaction;
        Links.InTransaction = () => UnitOfWork.InTransaction;
        RoleReader = new FakeRoleReader(Accounts) { InTransaction = () => UnitOfWork.InTransaction };
        Accounts.SetRoles(CurrentUser.UserId!.Value, SystemRoles.Admin);
        var phoneNumbers = new FakePhoneNumberParser();
        var phoneLinker = new PhoneNumberLinker(
            Accounts,
            Accounts,
            new DestinationCodeVerifier(Destinations, new FakeLoginCodeHasher(), Clock),
            [Participant, .. moduleParticipants],
            Links,
            Clock);
        var guard = new UserGuard(
            CurrentUser,
            Accounts,
            Accounts,
            RoleReader,
            RoleReader,
            new PermissionDelegationGuard(CurrentUser, RoleReader, RoleReader.Catalog, InitialAdmin),
            InitialAdmin);
        IInvitationChannel[] invitationChannels =
        [
            new EmailInvitationChannel(
                EmailQueue,
                new FakeEmailTemplateRenderer(),
                new FakePublicOrigin(new Uri("https://example.test/")),
                InvitationLogger),
            .. moduleInvitationChannels,
        ];

        var invitationIssuer = new UserInvitationIssuer(
            Invitations, Accounts, invitationChannels, CurrentUser, EmailQuotaGuards.For(Destinations, Clock, Invitations), Clock);
        var changes = new AdministrativeChangeRecorder(Changes, CurrentUser, Clock);
        var revoker = new AccountAccessRevoker(Links, SignIn, Clock);
        var contacts = new UserContactLinker(
            Accounts,
            Accounts,
            Destinations,
            phoneLinker,
            phoneNumbers,
            phoneChannel,
            new AccountCreationPolicy(SystemSettings, InitialAdmin),
            revoker);

        Queries = new UserQueryService(
            Accounts,
            InvitationReader,
            [DeliveryStatuses],
            phoneNumbers,
            RequestValidators.For(new ListUsersRequestValidator()),
            QueryLogger);
        Administration = new UserAdministrationService(
            Accounts,
            contacts,
            invitationIssuer,
            guard,
            changes,
            RequestValidators.For(
                new CreateUserRequestValidator(),
                new UpdateUserRequestValidator(),
                new SendUserInvitationRequestValidator()),
            UnitOfWork,
            AdministrationLogger);
        Access = new UserAccessService(
            Accounts, Accounts, guard, phoneLinker, revoker, changes, UnitOfWork, AccessLogger);
        var codeOptions = Options.Create(new LoginCodeOptions());
        var codeHasher = new FakeLoginCodeHasher();
        ProfilePhone = new ProfilePhoneService(
            CurrentUser,
            Accounts,
            guard,
            phoneLinker,
            new DestinationCodeVerifier(Destinations, codeHasher, Clock),
            RequestValidators.For(new UnlinkOwnPhoneRequestValidator(codeOptions)),
            UnitOfWork,
            ProfilePhoneLogger);
    }

    /// <summary>
    /// Un código al correo de <paramref name="user"/>, como el que pide
    /// <see cref="ProfileService.RequestEmailConfirmationCodeAsync"/> (de <see cref="LoginCodePurpose.ConfirmChange"/>),
    /// y lo devuelve. Con <paramref name="purpose"/>, uno de otro propósito.
    /// </summary>
    public string IssueUnlinkCode(
        UserAccount user, int maxAttempts = 5, LoginCodePurpose purpose = LoginCodePurpose.ConfirmChange)
    {
        var email = Domain.ValueObjects.Email.Create(user.Email).Value;
        Destinations.Add(LoginCode.Issue(
            LoginCodeDestination.ForEmail(email),
            purpose,
            user.Id,
            FakeLoginCodeHasher.HashOf(email.Value, purpose, FakeLoginCodeGenerator.Code),
            Clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(10),
            maxAttempts));
        return FakeLoginCodeGenerator.Code;
    }

    /// <summary>
    /// Los roles que existen, para que el alta y la edición validen los nombres (UserGuard.EnsureRolesExistAsync), y sus
    /// permisos, para la regla de no dar lo que no se tiene (PermissionDelegationGuard): Admin tiene todo el catálogo del
    /// núcleo; los demás, lo que diga <see cref="Permissions"/>. Cada nombre tiene un id fijo, y los roles de una cuenta
    /// salen de los nombres que guarda <paramref name="accounts"/>. Las demás lecturas de roles no se usan acá.
    /// </summary>
    /// <summary>El catálogo de roles y, como repositorio, sólo el lock de la asignación: escribir roles no es de estos tests.</summary>
    internal sealed class FakeRoleReader(InMemoryUserAccounts accounts) : IRoleReader, IRoleRepository
    {
        private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);

        /// <summary>Los nombres de cada lock de asignación (<see cref="IRoleRepository.LockForAssignmentAsync"/>), en orden.</summary>
        public List<IReadOnlyCollection<string>> LockedForAssignment { get; } = [];

        /// <summary>Si no es null, bloquear fuera de la transacción lanza, como el repositorio.</summary>
        public Func<bool>? InTransaction { get; set; }

        public Task LockForAssignmentAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
        {
            TransactionGuard.Require(InTransaction);
            LockedForAssignment.Add([.. names]);
            return Task.CompletedTask;
        }

        public Task LockAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid> CreateAsync(
            string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RoleRow?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(RoleNames.Contains(name, StringComparer.Ordinal) ? Row(name) : null);

        public Application.Channels.PermissionCatalog Catalog { get; } = new([]);

        public List<string> RoleNames { get; } = ["Admin", "User"];

        /// <summary>Los permisos de cada rol que no es Admin; sin entrada, ninguno.</summary>
        public Dictionary<string, string[]> Permissions { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(RoleNames);

        public Task<IReadOnlyCollection<RoleRow>> ListAllRolesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<RoleRow>>([.. RoleNames.Select(Row)]);

        public async Task<IReadOnlyCollection<RoleRow>> ListRolesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            [
                .. (await accounts.ListRoleNamesForUserAsync(userId, cancellationToken))
                    .Where(name => RoleNames.Contains(name, StringComparer.Ordinal))
                    .Select(Row),
            ];

        /// <summary>
        /// Renombra <paramref name="from"/> a <paramref name="to"/>, como en la base: el nombre nuevo queda con el id y los
        /// permisos del rol. Las cuentas del doble guardan nombres, así que una a la que se le asigne <paramref name="to"/>
        /// recibe este rol, como con SetRolesAsync, que busca cada rol por nombre.
        /// </summary>
        public void Rename(string from, string to)
        {
            _ids[to] = IdOf(from);
            Permissions[to] = Permissions.GetValueOrDefault(from) ?? [];
            _ids.Remove(from);
            Permissions.Remove(from);
            RoleNames[RoleNames.IndexOf(from)] = to;
        }

        private RoleRow Row(string name) =>
            new(
                IdOf(name),
                name,
                null,
                SystemRoles.All.Contains(name, StringComparer.Ordinal),
                0,
                name == SystemRoles.Admin ? [.. Catalog.All] : Permissions.GetValueOrDefault(name) ?? [],
                Revision: 1);

        private Guid IdOf(string name)
        {
            if (!_ids.TryGetValue(name, out var id))
            {
                id = Guid.CreateVersion7();
                _ids[name] = id;
            }

            return id;
        }

        public Task<PagedResult<RoleRow>> ListRolesAsync(ListRolesRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");

        public Task<RoleRow?> FindByIdAsync(Guid roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");

        public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The user services only validate role names.");
    }
}
