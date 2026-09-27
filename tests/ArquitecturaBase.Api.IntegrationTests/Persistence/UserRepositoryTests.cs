using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Las escrituras de cuentas (IUserRepository) contra Postgres: el alta y sus roles iniciales, el número único y lo que
/// escriben sin tocar la sesión. Que exijan la transacción lo fija UnitOfWorkTransactionTests, y qué pasa con un 23505
/// adentro de un límite, UserRepositoryTransactionTests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserRepositoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_are_confirmed_and_get_the_user_role()
    {
        var email = UniqueEmail("new");

        var (user, roles) = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var created = await users.CreateAsync(email, "Ana", "en", Ct);
            return (created, await reader.ListRoleNamesForUserAsync(created.Id, Ct));
        });

        Assert.Equal(email.Value, user.Email);
        Assert.Equal("Ana", user.DisplayName);
        Assert.Equal("en", user.Culture);
        Assert.True(user.IsActive);
        Assert.Equal(["User"], roles);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.EmailConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Admin_email_gets_the_admin_role()
    {
        var adminEmail = Email.Create(ApiFactory.AdminEmail).Value;

        var roles = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var admin = await reader.FindByEmailAsync(adminEmail, Ct) ?? await users.CreateAsync(adminEmail, null, "es", Ct);
            return await reader.ListRoleNamesForUserAsync(admin.Id, Ct);
        });

        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Phone_only_accounts_can_be_created()
    {
        var phone = TestPhones.Unique();

        var (user, roles) = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            var created = await users.CreateAsync(email: null, phone, phoneConfirmed: true, "Laura", "es", Ct);
            return (created, await reader.ListRoleNamesForUserAsync(created.Id, Ct));
        });
        var stored = await factory.ExecuteDbContextAsync(db => db.Users.SingleAsync(u => u.Id == user.Id, Ct));

        Assert.Null(user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(phone.Value, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal(["User"], roles);
        Assert.Null(stored.Email);
        Assert.Null(stored.NormalizedEmail);
        Assert.Equal(phone.Value, stored.PhoneNumber);
    }

    [Fact]
    public async Task The_user_name_is_the_account_id_and_not_the_email_or_the_phone()
    {
        var withEmail = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("username"), null, "es", Ct));
        var withPhone = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        var userNames = await factory.ExecuteDbContextAsync(db => db.Users
            .Where(u => u.Id == withEmail.Id || u.Id == withPhone.Id)
            .ToDictionaryAsync(u => u.Id, u => u.UserName, Ct));

        Assert.Equal(withEmail.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withEmail.Id]);
        Assert.Equal(withPhone.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withPhone.Id]);
    }

    [Fact]
    public async Task A_phone_loaded_without_verifying_it_stays_unconfirmed()
    {
        var user = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(UniqueEmail("unverified"), TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.PhoneNumber);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task An_account_needs_an_email_or_a_phone()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, phone: null, phoneConfirmed: false, "Nadie", "es", Ct)));
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_phone_number()
    {
        var phone = TestPhones.Unique();
        await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        // La regla la da el índice único: Application se fija antes, así que chocar con él es un error de programación.
        // El 23505 que escapa del límite sale traducido, con el original adentro.
        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => InTransactionWithAccountsAsync(
            (users, _) => users.CreateAsync(UniqueEmail("samephone"), phone, phoneConfirmed: false, null, "es", Ct)));
        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
    }

    [Fact]
    public async Task A_deleted_account_keeps_its_phone_number_reserved()
    {
        var phone = TestPhones.Unique();
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));
        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        var found = await WithReaderAsync(reader => reader.FindByPhoneAsync(phone, Ct));
        var deleted = await WithReaderAsync(reader => reader.ExistsDeletedByPhoneAsync(phone, Ct));

        Assert.Null(found);
        Assert.True(deleted);
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct)));
    }

    [Fact]
    public async Task Linking_a_phone_or_an_email_writes_them_without_closing_the_session()
    {
        // Solo escriben los datos: renovar el security stamp le cortaría la cookie a quien vincula su propio número
        // desde el perfil. Cortar las sesiones lo decide quien llama.
        var phone = TestPhones.Unique();
        var email = UniqueEmail("linked");
        var user = await InTransactionWithAccountsAsync((users, _) =>
            users.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: true, null, "es", Ct));
        var stampBefore = await SecurityStampOfAsync(user.Id);

        var afterLinking = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.SetPhoneAsync(user.Id, phone, confirmed: false, Ct);
            await users.SetEmailAsync(user.Id, email, confirmed: true, Ct);
            return await reader.FindByIdAsync(user.Id, Ct);
        });
        var byEmail = await WithReaderAsync(reader => reader.FindByEmailAsync(email, Ct));

        var afterRemoving = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.RemovePhoneAsync(user.Id, Ct);
            return await reader.FindByIdAsync(user.Id, Ct);
        });

        Assert.Equal(phone.Value, afterLinking!.PhoneNumber);
        Assert.False(afterLinking.PhoneNumberConfirmed);
        Assert.Equal(email.Value, afterLinking.Email);
        Assert.True(afterLinking.EmailConfirmed);
        Assert.Equal(user.Id, byEmail?.Id);
        Assert.Null(afterRemoving!.PhoneNumber);
        Assert.False(afterRemoving.PhoneNumberConfirmed);
        Assert.Equal(stampBefore, await SecurityStampOfAsync(user.Id));
    }

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync(Ct));

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private Task<T> WithReaderAsync<T>(Func<IUserReader, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IUserReader>()));

    private Task WriteAsync(Func<IUserRepository, Task> write) =>
        factory.InTransactionAsync(services => write(services.GetRequiredService<IUserRepository>()));

    /// <summary>
    /// Adentro de un límite, con el repositorio y el lector del mismo scope: las escrituras de cuentas lo exigen, y el
    /// lector ve lo que escribió el repositorio antes del commit.
    /// </summary>
    private Task<T> InTransactionWithAccountsAsync<T>(Func<IUserRepository, IUserReader, Task<T>> action) =>
        factory.InTransactionAsync(services => action(
            services.GetRequiredService<IUserRepository>(), services.GetRequiredService<IUserReader>()));
}
