using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Las lecturas de cuentas (IUserReader) contra Postgres: las búsquedas, las cuentas borradas, el detalle, el listado, el
/// conteo de administradores y los vínculos externos. Los datos se arman con IUserRepository adentro de un límite, como
/// los arma la aplicación.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_are_found_by_email_and_by_external_login()
    {
        var email = UniqueEmail("find");
        var created = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email, null, "es", Ct));
        var login = new ExternalLogin("Google", "google-" + created.Id.ToString("N", CultureInfo.InvariantCulture), email.Value, true, null);

        await WriteAsync(users => users.AddExternalLoginAsync(created.Id, login, Ct));

        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByEmailAsync(email, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByExternalLoginAsync("Google", login.ProviderKey, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithReaderAsync(reader => reader.FindByIdAsync(created.Id, Ct)))!.Id);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literals()
    {
        var prefix = SearchPrefix("srch");
        var withUnderscore = Email.Create(prefix + "-a_b@example.com").Value;
        var withoutUnderscore = Email.Create(prefix + "-axb@example.com").Value;
        await WriteAsync(async users =>
        {
            await users.CreateAsync(withUnderscore, null, "es", Ct);
            await users.CreateAsync(withoutUnderscore, null, "es", Ct);
        });

        var page = await WithReaderAsync(reader => reader.ListUsersAsync(new ListUsersRequest { Search = prefix + "-a_b" }, Ct));

        Assert.Equal([withUnderscore.Value], page.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Users_are_sorted_by_the_requested_field()
    {
        var prefix = SearchPrefix("sort");
        await WriteAsync(async users =>
        {
            foreach (var name in new[] { "b", "c", "a" })
            {
                await users.CreateAsync(Email.Create($"{prefix}-{name}@example.com").Value, null, "es", Ct);
            }
        });

        var page = await WithReaderAsync(reader =>
            reader.ListUsersAsync(new ListUsersRequest { Search = prefix, Sort = "-email", PageSize = 2 }, Ct));

        Assert.Equal([$"{prefix}-c@example.com", $"{prefix}-b@example.com"], page.Items.Select(item => item.Email));
        Assert.Equal(3, page.TotalCount);
    }

    /// <summary>
    /// El bot le contesta a una cuenta borrada como a una deshabilitada, en su idioma: necesita la cuenta, no solo saber
    /// que existe. Una cuenta que no está borrada no aparece.
    /// </summary>
    [Fact]
    public async Task A_deleted_account_is_found_by_its_phone_with_its_culture()
    {
        var phone = TestPhones.Unique();
        var activePhone = TestPhones.Unique();
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "en", Ct));
        await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, activePhone, phoneConfirmed: true, null, "en", Ct));
        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        var deleted = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(phone, Ct));
        var active = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(activePhone, Ct));
        var unknown = await WithReaderAsync(reader => reader.FindDeletedByPhoneAsync(TestPhones.Unique(), Ct));

        Assert.Equal(user.Id, deleted?.Id);
        Assert.Equal("en", deleted?.Culture);
        Assert.Null(active);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Users_are_found_by_phone()
    {
        var phone = TestPhones.Unique();
        var created = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        var found = await WithReaderAsync(reader => reader.FindByPhoneAsync(phone, Ct));
        var other = await WithReaderAsync(reader => reader.FindByPhoneAsync(TestPhones.Unique(), Ct));
        var deleted = await WithReaderAsync(reader => reader.ExistsDeletedByPhoneAsync(phone, Ct));

        Assert.Equal(created.Id, found?.Id);
        Assert.Null(other);
        Assert.False(deleted);
    }

    [Fact]
    public async Task Deleted_email_lookup_uses_identity_normalization_and_preserves_the_account()
    {
        var email = UniqueEmail("deleted-email");
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(email, "Lucía", "en", Ct));
        var uppercaseEmail = Email.Create(email.Value.ToUpperInvariant()).Value;

        Assert.False(await WithReaderAsync(reader => reader.ExistsDeletedByEmailAsync(uppercaseEmail, Ct)));
        Assert.Null(await WithReaderAsync(reader => reader.FindDeletedByEmailAsync(uppercaseEmail, Ct)));

        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        Assert.True(await WithReaderAsync(reader => reader.ExistsDeletedByEmailAsync(uppercaseEmail, Ct)));
        Assert.Null(await WithReaderAsync(reader => reader.FindByIdAsync(user.Id, Ct)));
        var deleted = await WithReaderAsync(reader => reader.FindDeletedByEmailAsync(uppercaseEmail, Ct));
        Assert.Equal(user.Id, deleted?.Id);
        Assert.Equal("en", deleted?.Culture);
    }

    [Fact]
    public async Task User_detail_sorts_roles_and_excludes_deleted_accounts()
    {
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("detail"), "Ana", "es", Ct));
        await WriteAsync(users => users.SetRolesAsync(user.Id, [SystemRoles.User, SystemRoles.Admin], Ct));

        var detail = await WithReaderAsync(reader => reader.FindDetailAsync(user.Id, Ct));
        Assert.Equal(user.Id, detail?.Id);
        Assert.Equal("Ana", detail?.DisplayName);
        Assert.Equal([SystemRoles.Admin, SystemRoles.User], detail?.Roles);

        await WriteAsync(users => users.DeleteAsync(user.Id, Ct));

        Assert.Null(await WithReaderAsync(reader => reader.FindDetailAsync(user.Id, Ct)));
    }

    [Fact]
    public async Task Admin_count_includes_only_active_and_not_deleted_accounts()
    {
        var before = await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct));
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("admin-count"), null, "es", Ct));
        await WriteAsync(users => users.SetRolesAsync(user.Id, [SystemRoles.Admin], Ct));

        Assert.Equal(before + 1, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));
        await WriteAsync(users => users.SetActiveAsync(user.Id, false, Ct));
        Assert.Equal(before, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));

        await WriteAsync(async users =>
        {
            await users.SetActiveAsync(user.Id, true, Ct);
            await users.DeleteAsync(user.Id, Ct);
        });
        Assert.Equal(before, await WithReaderAsync(reader => reader.CountActiveAdminsAsync(Ct)));
    }

    [Fact]
    public async Task External_logins_are_reported_by_provider()
    {
        var user = await InTransactionWithAccountsAsync((users, _) => users.CreateAsync(UniqueEmail("provider"), null, "es", Ct));
        var login = new ExternalLogin(
            ExternalLoginProviders.Google, "google-" + user.Id.ToString("N", CultureInfo.InvariantCulture), user.Email, true, null);

        var before = await WithReaderAsync(reader => reader.ExistsExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct));
        var after = await InTransactionWithAccountsAsync(async (users, reader) =>
        {
            await users.AddExternalLoginAsync(user.Id, login, Ct);
            return await reader.ExistsExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct);
        });
        var otherProvider = await WithReaderAsync(reader => reader.ExistsExternalLoginAsync(user.Id, "Microsoft", Ct));

        Assert.False(before);
        Assert.True(after);
        Assert.False(otherProvider);
    }

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    /// <summary>
    /// Un texto de búsqueda que solo matchea con las cuentas del test. Lleva el Guid entero y no un pedazo: todos los
    /// tests comparten la base, y ocho caracteres pueden repetirse entre corridas. Empieza con letras, así que la búsqueda
    /// no lo compara contra los números de teléfono: eso pasa solo con un texto que parece un número (dígitos, espacios,
    /// "+", guiones, puntos y paréntesis).
    /// </summary>
    private static string SearchPrefix(string name) => name + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    private Task<T> WithReaderAsync<T>(Func<IUserReader, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IUserReader>()));

    private Task WriteAsync(Func<IUserRepository, Task> write) =>
        factory.InTransactionAsync(services => write(services.GetRequiredService<IUserRepository>()));

    /// <summary>Adentro de un límite, con el repositorio y el lector del mismo scope.</summary>
    private Task<T> InTransactionWithAccountsAsync<T>(Func<IUserRepository, IUserReader, Task<T>> action) =>
        factory.InTransactionAsync(services => action(
            services.GetRequiredService<IUserRepository>(), services.GetRequiredService<IUserReader>()));
}
