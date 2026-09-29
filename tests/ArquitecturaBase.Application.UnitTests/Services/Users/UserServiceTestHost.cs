using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.Validation.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

internal class UserServiceTestHost
{
    public InMemoryUserAccounts Accounts { get; } = new();
    public FakeSignInService SignIn { get; } = new();
    public InMemoryUserInvitationRepository Invitations { get; } = new();
    public FakeUserInvitationReader InvitationReader { get; } = new();
    public LockLog MessagesLog { get; } = new();
    public FakeLogger<UserQueryService> QueryLogger { get; } = new();
    public FakeLogger<UserAdministrationService> AdministrationLogger { get; } = new();
    public FakeLogger<UserAccessService> AccessLogger { get; } = new();
    public FakeLogger<EmailInvitationChannel> InvitationLogger { get; } = new();
    public InMemoryLoginCodeRepository Destinations { get; } = new();
    public InMemoryLoginLinkRepository Links { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new() { UserId = Guid.CreateVersion7() };
    public FakeUnitOfWork UnitOfWork { get; }

    /// <summary>Cuántos correos había en la cola cuando se confirmó la unidad de trabajo.</summary>
    public int? QueuedAtCommit { get; private set; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
    public FakeWhatsAppSendQueue SendQueue { get; } = new();
    public FakeEmailQueue EmailQueue { get; } = new();

    /// <summary>La fuente del estado de entrega de WhatsApp, la única que tiene el detalle.</summary>
    public FakeInvitationDeliveryStatusSource DeliveryStatuses { get; } = new(UserInvitationChannel.WhatsApp);

    public FakeRoleReader RoleReader { get; }
    public InMemoryWhatsAppContactRepository Contacts { get; }
    public InMemoryWhatsAppMessageRepository Messages { get; }
    public UserQueryService Queries { get; }
    public UserAdministrationService Administration { get; }
    public UserAccessService Access { get; }

    /// <summary>
    /// Invita con los dos canales reales, el correo y WhatsApp; sin <paramref name="whatsAppInvitations"/>, solo por
    /// correo, como sin el módulo de WhatsApp.
    /// </summary>
    public UserServiceTestHost(bool whatsAppInvitations = true)
    {
        UnitOfWork = new FakeUnitOfWork { OnCommit = () => QueuedAtCommit = EmailQueue.Messages.Count };
        Accounts.InTransaction = () => UnitOfWork.InTransaction;
        SignIn.InTransaction = () => UnitOfWork.InTransaction;
        Destinations.InTransaction = () => UnitOfWork.InTransaction;
        Invitations.InTransaction = () => UnitOfWork.InTransaction;
        Links.InTransaction = () => UnitOfWork.InTransaction;
        MessagesLog.InTransaction = () => UnitOfWork.InTransaction;
        Contacts = new InMemoryWhatsAppContactRepository(MessagesLog);
        Messages = new InMemoryWhatsAppMessageRepository(MessagesLog);
        RoleReader = new FakeRoleReader();
        var phoneNumbers = new FakePhoneNumberParser();
        var linker = new WhatsAppContactLinker(Contacts);
        var phoneLinker = new PhoneNumberLinker(
            Accounts, Accounts, new DestinationCodeVerifier(Destinations, new FakeLoginCodeHasher(), Clock), linker, Links, Clock);
        var guard = new UserGuard(CurrentUser, Accounts, Accounts, RoleReader);
        List<IInvitationChannel> invitationChannels =
        [
            new EmailInvitationChannel(
                EmailQueue,
                new FakeEmailTemplateRenderer(),
                new FakePublicOrigin(new Uri("https://example.test/")),
                InvitationLogger),
        ];
        if (whatsAppInvitations)
        {
            invitationChannels.Add(new WhatsAppInvitationChannel(
                SendQueue,
                new FakeWhatsAppAvailability(IsEnabled: true),
                new FakeAppName("Test"),
                NullLogger<WhatsAppInvitationChannel>.Instance));
        }

        var invitationIssuer = new UserInvitationIssuer(Invitations, invitationChannels, CurrentUser, Clock);
        var contacts = new UserContactLinker(
            Accounts,
            Accounts,
            Destinations,
            phoneLinker,
            phoneNumbers,
            new WhatsAppPhoneChannel(
                new FakeWhatsAppAvailability(IsEnabled: true), Options.Create(new WhatsAppLoginOptions()), phoneNumbers));
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
