using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class UserRepositoryTransactionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_invitation_enqueue_rolls_back_autosaved_user_and_initial_role()
    {
        var email = TestEmails.Unique("repository-invitation");
        var before = await factory.ExecuteDbContextAsync(db => db.UserInvitations.CountAsync(Ct));
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
            services.RemoveAll<IEmailQueue>();
            services.AddScoped<IEmailQueue, ThrowingEmailQueue>();
        }));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserService>().CreateUserAsync(
                new CreateUserRequest(email, "Invitada", null,
                    Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct)));

        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (UserExists: await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Email == email, Ct),
             InvitationCount: await db.UserInvitations.CountAsync(Ct)));
        Assert.False(persisted.UserExists);
        Assert.Equal(before, persisted.InvitationCount);
    }

    [Fact]
    public async Task Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone()
    {
        var originalEmail = TestEmails.Unique("repository-update-original");
        var changedEmail = TestEmails.Unique("repository-update-changed");
        var phone = TestPhones.Unique();
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .CreateUserAsync(new CreateUserRequest(originalEmail, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppContactRepository>();
            services.AddScoped<IWhatsAppContactRepository>(provider =>
            {
                var db = provider.GetRequiredService<ApplicationDbContext>();
                return new ThrowingContactRepository(
                    new WhatsAppContactRepository(db), db, created.Value, changedEmail, phone.Value);
            });
        }));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserService>().UpdateUserAsync(
                new UpdateUserRequest(created.Value, "Después", [SystemRoles.User], changedEmail,
                    new PhoneNumberInput("AR", TestPhones.AsTypedLocally(phone))), Ct)));

        var persisted = await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => new { user.Email, user.PhoneNumber, user.DisplayName })
            .SingleAsync(Ct));
        Assert.Equal(originalEmail, persisted.Email);
        Assert.Null(persisted.PhoneNumber);
        Assert.Equal("Antes", persisted.DisplayName);
    }

    private static async Task<T> InScopeAsync<T>(IServiceProvider provider, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;

        public bool IsAuthenticated => true;
    }

    private sealed class ExpectedWriteFailure : Exception;

    private sealed class ThrowingEmailQueue(ApplicationDbContext db) : IEmailQueue
    {
        public async ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Contains(db.ChangeTracker.Entries<UserInvitation>(), entry => entry.State == EntityState.Added);
            var created = await db.Users.AsNoTracking().SingleAsync(user => user.Email == message.To, cancellationToken);
            Assert.True(await db.UserRoles.AnyAsync(role => role.UserId == created.Id, cancellationToken));
            throw new ExpectedWriteFailure();
        }
    }

    private sealed class ThrowingContactRepository(
        IWhatsAppContactRepository inner,
        ApplicationDbContext db,
        Guid userId,
        string email,
        string phone) : IWhatsAppContactRepository
    {
        public Task LockAsync(IReadOnlyCollection<string> userIdentifiers, IReadOnlyCollection<string> waIds,
            CancellationToken cancellationToken) => inner.LockAsync(userIdentifiers, waIds, cancellationToken);

        public Task<WhatsAppContact?> GetByUserIdentifierAsync(string userIdentifier, CancellationToken cancellationToken) =>
            inner.GetByUserIdentifierAsync(userIdentifier, cancellationToken);

        public Task<WhatsAppContact?> GetLatestByWaIdAsync(string waId, CancellationToken cancellationToken) =>
            inner.GetLatestByWaIdAsync(waId, cancellationToken);

        public Task<WhatsAppContact?> GetForProcessingAsync(Guid contactId, CancellationToken cancellationToken) =>
            inner.GetForProcessingAsync(contactId, cancellationToken);

        public Task LockForNumberChangeAsync(Guid id, string? waId, CancellationToken cancellationToken) =>
            inner.LockForNumberChangeAsync(id, waId, cancellationToken);

        public async Task<WhatsAppContact?> GetByUserIdAsync(Guid id, CancellationToken cancellationToken)
        {
            Assert.Equal(userId, id);
            Assert.NotNull(db.Database.CurrentTransaction);
            var changed = await db.Users.AsNoTracking()
                .Where(user => user.Id == id)
                .Select(user => new { user.Email, user.PhoneNumber, user.DisplayName })
                .SingleAsync(cancellationToken);
            Assert.Equal(email, changed.Email);
            Assert.Equal(phone, changed.PhoneNumber);
            Assert.Equal("Después", changed.DisplayName);
            throw new ExpectedWriteFailure();
        }

        public Task<WhatsAppContact?> GetByUserIdForUnlinkAsync(Guid id, CancellationToken cancellationToken) =>
            inner.GetByUserIdForUnlinkAsync(id, cancellationToken);

        public void Add(WhatsAppContact contact) => inner.Add(contact);
    }
}
