using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;
using ArquitecturaBase.Application.Validation.Users;
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
    public FakeLogger<UserService> Logger { get; } = new();
    public InMemoryLoginCodeRepository Destinations { get; } = new();
    public InMemoryLoginLinkRepository Links { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new() { UserId = Guid.CreateVersion7() };
    public FakeUnitOfWork UnitOfWork { get; }

    /// <summary>Cuántos correos había en la cola cuando se confirmó la unidad de trabajo.</summary>
    public int? QueuedAtCommit { get; private set; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));
    public FakeWhatsAppOutbox Outbox { get; } = new();
    public FakeEmailQueue EmailQueue { get; } = new();
    public FakeRoleReader RoleReader { get; }
    public InMemoryWhatsAppContactRepository Contacts { get; }
    public InMemoryWhatsAppMessageRepository Messages { get; }
    public UserService Service { get; }

    public UserServiceTestHost()
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
        var invitationSender = new UserInvitationSender(
            Invitations,
            Outbox,
            new FakeWhatsAppAvailability(IsEnabled: true),
            EmailQueue,
            new FakeEmailTemplateRenderer(),
            new FakePublicOrigin(new Uri("https://example.test/")),
            new FakeAppName("Test"),
            CurrentUser,
            Clock,
            NullLogger<UserInvitationSender>.Instance);
        var writes = new UserWriteOperations(
            Accounts,
            Accounts,
            RoleReader,
            Destinations,
            new UserContactParser(phoneNumbers, Options.Create(new WhatsAppLoginOptions())),
            invitationSender,
            new UserGuards(CurrentUser, Accounts),
            phoneLinker,
            linker,
            RequestValidators.For(new CreateUserRequestValidator(), new UpdateUserRequestValidator()));
        var revoker = new AccountAccessRevoker(Links, SignIn, Clock);
        var status = new UserStatusOperations(
            Accounts, Accounts, new UserGuards(CurrentUser, Accounts), Links, revoker);
        var userPhone = new UserPhoneOperations(
            Accounts, Accounts, new UserGuards(CurrentUser, Accounts), linker, phoneLinker, revoker);

        Service = new UserService(
            Accounts,
            Invitations,
            InvitationReader,
            phoneNumbers,
            RequestValidators.For(
                new ListUsersRequestValidator(),
                new SendUserInvitationRequestValidator()),
            writes,
            invitationSender,
            UnitOfWork,
            Clock,
            status,
            userPhone,
            Logger);
    }

    /// <summary>
    /// Los roles que existen, para que el alta y la edición validen los nombres. UserService solo valida nombres: las demás
    /// lecturas de roles no se usan acá.
    /// </summary>
    internal sealed class FakeRoleReader : IRoleReader
    {
        public List<string> RoleNames { get; } = ["Admin", "User"];

        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(RoleNames);

        public Task<IReadOnlyCollection<RoleRow>> ListRolesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");

        public Task<RoleRow?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");

        public Task<bool> ExistsByNameAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("UserService only validates role names.");
    }
}
