using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;
using ArquitecturaBase.Application.Validation.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

internal class UserServiceTestHost
{
    public FakeIdentityService Identity { get; } = new();
    public InMemoryUserInvitationRepository Invitations { get; } = new();
    public LockLog MessagesLog { get; } = new();
    public FakeLogger<UserService> Logger { get; } = new();
    public InMemoryLoginCodeRepository Destinations { get; } = new();
    public InMemoryLoginLinkRepository Links { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new() { UserId = Guid.CreateVersion7() };
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeWhatsAppOutbox Outbox { get; } = new();
    public FakeEmailQueue EmailQueue { get; } = new();
    public FakeRoleReader RoleReader { get; }
    public InMemoryWhatsAppContactRepository Contacts { get; }
    public InMemoryWhatsAppMessageRepository Messages { get; }
    public UserService Service { get; }

    public UserServiceTestHost()
    {
        Contacts = new InMemoryWhatsAppContactRepository(MessagesLog);
        Messages = new InMemoryWhatsAppMessageRepository(MessagesLog);
        RoleReader = new FakeRoleReader(Identity);
        var phoneNumbers = new FakePhoneNumberParser();
        var linker = new WhatsAppContactLinker(Contacts);
        var phoneChange = new PhoneNumberChange(linker, Links, TimeProvider.System);
        var invitationSender = new UserInvitationSender(
            Invitations,
            Outbox,
            new FakeWhatsAppAvailability(IsEnabled: true),
            EmailQueue,
            new FakeEmailTemplateRenderer(),
            new FakePublicOrigin(new Uri("https://example.test/")),
            new FakeAppName("Test"),
            CurrentUser,
            TimeProvider.System,
            NullLogger<UserInvitationSender>.Instance);
        var writes = new UserWriteOperations(
            Identity,
            Identity,
            RoleReader,
            Destinations,
            new UserContactParser(phoneNumbers, Options.Create(new WhatsAppLoginOptions())),
            invitationSender,
            new UserGuards(CurrentUser, Identity),
            phoneChange,
            linker,
            new ServiceRequestValidator<CreateUserRequest>([new CreateUserRequestValidator()]),
            new ServiceRequestValidator<UpdateUserRequest>([new UpdateUserRequestValidator()]),
            UnitOfWork);

        Service = new UserService(
            Identity,
            Invitations,
            Messages,
            phoneNumbers,
            new ServiceRequestValidator<ListUsersRequest>([new ListUsersRequestValidator()]),
            new ServiceRequestValidator<UserFilterCountsRequest>([new UserFilterCountsRequestValidator()]),
            writes,
            Logger);
    }

    internal sealed class FakeRoleReader(FakeIdentityService identity) : IRoleReader
    {
        public Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(identity.RoleNames);

        public Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken) =>
            identity.ListRolesAsync(cancellationToken);

        public Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
            identity.FindRoleAsync(roleId, cancellationToken);

        public Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken) =>
            identity.RoleNameExistsAsync(name, excludedRoleId, cancellationToken);
    }
}
