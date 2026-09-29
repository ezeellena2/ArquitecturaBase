using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de las transacciones de las cuentas (UserRepositoryTransactionTests): si soltar el
/// chat del número anterior falla, la edición no deja nada autoguardado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppUserRepositoryTransactionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_contact_unlink_rolls_back_autosaved_name_email_and_phone()
    {
        var originalEmail = TestEmails.Unique("repository-update-original");
        var changedEmail = TestEmails.Unique("repository-update-changed");
        var phone = TestPhones.Unique();
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserAdministrationService>()
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

    private static async Task<T> InScopeAsync<T>(IServiceProvider provider, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private sealed class ExpectedWriteFailure : Exception;

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
