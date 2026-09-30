using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
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
            services.GetRequiredService<IUserAdministrationService>().CreateUserAsync(
                new CreateUserRequest(email, "Invitada", null,
                    Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct)));

        var persisted = await factory.ExecuteDbContextAsync(async db =>
            (UserExists: await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Email == email, Ct),
             InvitationCount: await db.UserInvitations.CountAsync(Ct)));
        Assert.False(persisted.UserExists);
        Assert.Equal(before, persisted.InvitationCount);
    }

    /// <summary>
    /// Un participante del número (con WhatsApp, el contacto del chat) que falla al soltar el número anterior deshace
    /// todo lo que Identity ya autoguardó: el nombre, el correo y el número nuevos.
    /// </summary>
    [Fact]
    public async Task Failed_phone_release_rolls_back_autosaved_name_email_and_phone()
    {
        var originalEmail = TestEmails.Unique("repository-update-original");
        var changedEmail = TestEmails.Unique("repository-update-changed");
        var phone = TestPhones.Unique();
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
            .CreateUserAsync(new CreateUserRequest(originalEmail, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        // Se suma a los participantes que haya: con WhatsApp, el suyo corre antes y no encuentra un chat que soltar.
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPhoneLinkParticipant>(provider => new ThrowingPhoneLinkParticipant(
                provider.GetRequiredService<ApplicationDbContext>(), created.Value, changedEmail, phone.Value))));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserAdministrationService>().UpdateUserAsync(
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

    /// <summary>
    /// Sin correo ni número la edición no toma ningún lock, pero igual corre adentro del límite del servicio: el
    /// nombre, que Identity autoguarda antes que los roles, no queda guardado si los roles fallan. O queda todo o no
    /// queda nada.
    /// </summary>
    [Fact]
    public async Task Failed_role_change_without_contact_rolls_back_the_autosaved_name()
    {
        var email = TestEmails.Unique("repository-roles");
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
            .CreateUserAsync(new CreateUserRequest(email, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
            services.RemoveAll<IUserRepository>();
            services.AddScoped<IUserRepository>(provider => new ThrowingRolesUserRepository(
                ActivatorUtilities.CreateInstance<UserRepository>(provider),
                provider.GetRequiredService<ApplicationDbContext>(),
                created.Value));
        }));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserAdministrationService>().UpdateUserAsync(
                new UpdateUserRequest(created.Value, "Después", [SystemRoles.User]), Ct)));

        Assert.Equal("Antes", await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => user.DisplayName)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task Failed_session_revocation_rolls_back_autosaved_deactivation()
    {
        var email = TestEmails.Unique("repository-deactivate");
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
            .CreateUserAsync(new CreateUserRequest(email, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
            services.RemoveAll<ILoginLinkRepository>();
            services.AddScoped<ILoginLinkRepository>(provider =>
            {
                var db = provider.GetRequiredService<ApplicationDbContext>();
                return new ThrowingLoginLinkRepository(new LoginLinkRepository(db), db, created.Value);
            });
        }));

        await Assert.ThrowsAsync<ExpectedWriteFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserAccessService>().SetUserActiveAsync(created.Value, isActive: false, Ct)));

        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => user.IsActive)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task Failed_delete_commit_rolls_back_soft_delete()
    {
        var email = TestEmails.Unique("repository-delete");
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
            .CreateUserAsync(new CreateUserRequest(email, "Antes", null), Ct));
        Assert.True(created.IsSuccess);

        var probe = new CommitFailureProbe();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new FixedCurrentUser(Guid.CreateVersion7()));
            FailingCommitUnitOfWork.Replace(services, probe);
        }));

        await Assert.ThrowsAsync<ExpectedCommitFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserAccessService>().DeleteUserAsync(created.Value, Ct)));

        Assert.True(probe.RolledBackBeforeLeaving);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => user.IsDeleted)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task Failed_unlink_commit_rolls_back_autosaved_phone_removal()
    {
        var phone = TestPhones.Unique();
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
            .CreateUserAsync(new CreateUserRequest(TestEmails.Unique("repository-unlink"), "Antes", null,
                Phone: new PhoneNumberInput("AR", TestPhones.AsTypedLocally(phone))), Ct));
        Assert.True(created.IsSuccess);

        var probe = new CommitFailureProbe();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            FailingCommitUnitOfWork.Replace(services, probe)));

        await Assert.ThrowsAsync<ExpectedCommitFailure>(() => InScopeAsync(api.Services, services =>
            services.GetRequiredService<IUserAccessService>().UnlinkUserPhoneAsync(created.Value, Ct)));

        Assert.True(probe.RolledBackBeforeLeaving);
        Assert.Equal(phone.Value, await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == created.Value)
            .Select(user => user.PhoneNumber)
            .SingleAsync(Ct)));
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
        // TryEnqueue es sincrónico: consulta la base con Single y Any, sobre el mismo contexto y su transacción.
        public bool TryEnqueue(EmailMessage message)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Contains(db.ChangeTracker.Entries<UserInvitation>(), entry => entry.State == EntityState.Added);
            var created = db.Users.AsNoTracking().Single(user => user.Email == message.To);
            Assert.True(db.UserRoles.Any(role => role.UserId == created.Id));
            throw new ExpectedWriteFailure();
        }
    }

    private sealed class ThrowingPhoneLinkParticipant(
        ApplicationDbContext db,
        Guid accountId,
        string changedEmail,
        string changedPhone) : IPhoneLinkParticipant
    {
        public Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PhoneConfirmedAsync(Guid userId, PhoneNumber phone, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async Task PhoneReleasedAsync(Guid userId, CancellationToken cancellationToken)
        {
            Assert.Equal(accountId, userId);
            Assert.NotNull(db.Database.CurrentTransaction);
            var changed = await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.Email, user.PhoneNumber, user.DisplayName })
                .SingleAsync(cancellationToken);
            Assert.Equal(changedEmail, changed.Email);
            Assert.Equal(changedPhone, changed.PhoneNumber);
            Assert.Equal("Después", changed.DisplayName);
            throw new ExpectedWriteFailure();
        }
    }

    private sealed class ThrowingLoginLinkRepository(
        ILoginLinkRepository inner,
        ApplicationDbContext db,
        Guid expectedUserId) : ILoginLinkRepository
    {
        public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.LockAccountAsync(userId, cancellationToken);

        public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
            inner.FindUserIdAsync(tokenHash, cancellationToken);

        public Task<LoginLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            inner.GetByTokenHashAsync(tokenHash, cancellationToken);

        public Task<IReadOnlyList<LoginLink>> ListActiveAsync(
            Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
            inner.ListActiveAsync(userId, nowUtc, cancellationToken);

        public async Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedUserId, userId);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.False(await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => user.IsActive)
                .SingleAsync(cancellationToken));
            throw new ExpectedWriteFailure();
        }

        public Task<IReadOnlyList<DateTime>> ListIssueTimesSinceAsync(
            Guid userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
            inner.ListIssueTimesSinceAsync(userId, sinceUtc, cancellationToken);

        public void Add(LoginLink loginLink) => inner.Add(loginLink);
    }

    /// <summary>
    /// El repositorio real, salvo SetRolesAsync: comprueba que el nombre ya se autoguardó adentro de una transacción y
    /// falla.
    /// </summary>
    private sealed class ThrowingRolesUserRepository(IUserRepository inner, ApplicationDbContext db, Guid userId) : IUserRepository
    {
        public Task LockExternalSignInAsync(Email email, string provider, string providerKey, CancellationToken cancellationToken) =>
            inner.LockExternalSignInAsync(email, provider, providerKey, cancellationToken);

        public Task LockAdminsAsync(CancellationToken cancellationToken) => inner.LockAdminsAsync(cancellationToken);

        public Task<UserAccount> CreateAsync(Email? email, PhoneNumber? phone, bool phoneConfirmed, string? displayName,
            string culture, CancellationToken cancellationToken, string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId) =>
            inner.CreateAsync(email, phone, phoneConfirmed, displayName, culture, cancellationToken, timeZoneId);

        public Task<UserAccount> CreateUnverifiedAsync(Email? email, PhoneNumber? phone, string? displayName, string culture,
            CancellationToken cancellationToken, string timeZoneId = ArquitecturaBase.Domain.Settings.SystemSettings.InitialTimeZoneId) =>
            inner.CreateUnverifiedAsync(email, phone, displayName, culture, cancellationToken, timeZoneId);

        public Task AddExternalLoginAsync(Guid id, ExternalLogin login, CancellationToken cancellationToken) =>
            inner.AddExternalLoginAsync(id, login, cancellationToken);

        public Task RestoreAsync(Guid id, string? displayName, CancellationToken cancellationToken) =>
            inner.RestoreAsync(id, displayName, cancellationToken);

        public Task SetEmailAsync(Guid id, Email email, bool confirmed, CancellationToken cancellationToken) =>
            inner.SetEmailAsync(id, email, confirmed, cancellationToken);

        public Task SetPhoneAsync(Guid id, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken) =>
            inner.SetPhoneAsync(id, phone, confirmed, cancellationToken);

        public Task RemovePhoneAsync(Guid id, CancellationToken cancellationToken) =>
            inner.RemovePhoneAsync(id, cancellationToken);

        public async Task SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
        {
            Assert.Equal(userId, id);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Equal("Después", await db.Users.AsNoTracking()
                .Where(user => user.Id == id)
                .Select(user => user.DisplayName)
                .SingleAsync(cancellationToken));

            throw new ExpectedWriteFailure();
        }

        public Task SetDisplayNameAsync(Guid id, string? displayName, CancellationToken cancellationToken) =>
            inner.SetDisplayNameAsync(id, displayName, cancellationToken);

        public Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) =>
            inner.SetActiveAsync(id, isActive, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task UpdateProfileAsync(Guid id, string? displayName, string culture, string timeZoneId,
            CancellationToken cancellationToken) =>
            inner.UpdateProfileAsync(id, displayName, culture, timeZoneId, cancellationToken);
    }
}
