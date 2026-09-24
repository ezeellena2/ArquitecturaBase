using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class IdentityServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_are_confirmed_and_get_the_user_role()
    {
        var email = UniqueEmail("new");

        var (user, roles) = await WithIdentityAsync(async identity =>
        {
            var created = await identity.CreateAsync(email, "Ana", "en", Ct);
            return (created, await identity.GetRolesAsync(created.Id, Ct));
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

        var roles = await WithIdentityAsync(async identity =>
        {
            var admin = await identity.FindByEmailAsync(adminEmail, Ct) ?? await identity.CreateAsync(adminEmail, null, "es", Ct);
            return await identity.GetRolesAsync(admin.Id, Ct);
        });

        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task Users_are_found_by_email_and_by_external_login()
    {
        var email = UniqueEmail("find");
        var created = await WithIdentityAsync(identity => identity.CreateAsync(email, null, "es", Ct));
        var login = new ExternalLogin("Google", "google-" + created.Id.ToString("N", CultureInfo.InvariantCulture), email.Value, true, null);

        await WithIdentityAsync(async identity =>
        {
            await identity.AddExternalLoginAsync(created.Id, login, Ct);
            return true;
        });

        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByEmailAsync(email, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByExternalLoginAsync("Google", login.ProviderKey, Ct)))!.Id);
        Assert.Equal(created.Id, (await WithIdentityAsync(identity => identity.FindByIdAsync(created.Id, Ct)))!.Id);
    }

    [Fact]
    public async Task Tenth_failed_attempt_locks_the_account()
    {
        var user = await WithIdentityAsync(identity => identity.CreateAsync(UniqueEmail("lock"), null, "es", Ct));

        var lockedAfterNine = await WithIdentityAsync(async identity =>
        {
            for (var i = 0; i < 9; i++)
            {
                await identity.RegisterFailedAttemptAsync(user.Id, Ct);
            }

            return await identity.IsLockedOutAsync(user.Id, Ct);
        });

        var lockedAfterTen = await WithIdentityAsync(async identity =>
        {
            await identity.RegisterFailedAttemptAsync(user.Id, Ct);
            return await identity.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(lockedAfterNine);
        Assert.True(lockedAfterTen);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_as_literals()
    {
        var prefix = "srch" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        var withUnderscore = Email.Create(prefix + "-a_b@example.com").Value;
        var withoutUnderscore = Email.Create(prefix + "-axb@example.com").Value;
        await WithIdentityAsync(async identity =>
        {
            await identity.CreateAsync(withUnderscore, null, "es", Ct);
            await identity.CreateAsync(withoutUnderscore, null, "es", Ct);
            return true;
        });

        var page = await WithIdentityAsync(identity => identity.ListUsersAsync(new GetUsersQuery { Search = prefix + "-a_b" }, Ct));

        Assert.Equal([withUnderscore.Value], page.Items.Select(item => item.Email));
    }

    [Fact]
    public async Task Users_are_sorted_by_the_requested_field()
    {
        var prefix = "sort" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        await WithIdentityAsync(async identity =>
        {
            foreach (var name in new[] { "b", "c", "a" })
            {
                await identity.CreateAsync(Email.Create($"{prefix}-{name}@example.com").Value, null, "es", Ct);
            }

            return true;
        });

        var page = await WithIdentityAsync(identity =>
            identity.ListUsersAsync(new GetUsersQuery { Search = prefix, Sort = "-email", PageSize = 2 }, Ct));

        Assert.Equal([$"{prefix}-c@example.com", $"{prefix}-b@example.com"], page.Items.Select(item => item.Email));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task Phone_only_accounts_can_be_created()
    {
        var phone = TestPhones.Unique();

        var (user, roles) = await WithIdentityAsync(async identity =>
        {
            var created = await identity.CreateAsync(email: null, phone, phoneConfirmed: true, "Laura", "es", Ct);
            return (created, await identity.GetRolesAsync(created.Id, Ct));
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
        var withEmail = await WithIdentityAsync(identity => identity.CreateAsync(UniqueEmail("username"), null, "es", Ct));
        var withPhone = await WithIdentityAsync(identity =>
            identity.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        var userNames = await factory.ExecuteDbContextAsync(db => db.Users
            .Where(u => u.Id == withEmail.Id || u.Id == withPhone.Id)
            .ToDictionaryAsync(u => u.Id, u => u.UserName, Ct));

        Assert.Equal(withEmail.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withEmail.Id]);
        Assert.Equal(withPhone.Id.ToString("D", CultureInfo.InvariantCulture), userNames[withPhone.Id]);
    }

    [Fact]
    public async Task A_phone_loaded_without_verifying_it_stays_unconfirmed()
    {
        var user = await WithIdentityAsync(identity =>
            identity.CreateAsync(UniqueEmail("unverified"), TestPhones.Unique(), phoneConfirmed: false, null, "es", Ct));

        Assert.True(user.EmailConfirmed);
        Assert.NotNull(user.PhoneNumber);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task An_account_needs_an_email_or_a_phone()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => WithIdentityAsync(identity =>
            identity.CreateAsync(email: null, phone: null, phoneConfirmed: false, "Nadie", "es", Ct)));
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_phone_number()
    {
        var phone = TestPhones.Unique();
        await WithIdentityAsync(identity => identity.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        // La regla la da el índice único: Application se fija antes, así que chocar con él es un error de programación.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => WithIdentityAsync(identity =>
            identity.CreateAsync(UniqueEmail("samephone"), phone, phoneConfirmed: false, null, "es", Ct)));
    }

    [Fact]
    public async Task A_deleted_account_keeps_its_phone_number_reserved()
    {
        var phone = TestPhones.Unique();
        var user = await WithIdentityAsync(identity => identity.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));
        await WithIdentityAsync(async identity =>
        {
            await identity.DeleteAsync(user.Id, Ct);
            return true;
        });

        var found = await WithIdentityAsync(identity => identity.FindByPhoneAsync(phone, Ct));
        var deleted = await WithIdentityAsync(identity => identity.IsDeletedPhoneAsync(phone, Ct));

        Assert.Null(found);
        Assert.True(deleted);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => WithIdentityAsync(identity =>
            identity.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct)));
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
        var user = await WithIdentityAsync(identity => identity.CreateAsync(email: null, phone, phoneConfirmed: true, null, "en", Ct));
        await WithIdentityAsync(identity => identity.CreateAsync(email: null, activePhone, phoneConfirmed: true, null, "en", Ct));
        await WithIdentityAsync(async identity =>
        {
            await identity.DeleteAsync(user.Id, Ct);
            return true;
        });

        var deleted = await WithIdentityAsync(identity => identity.FindDeletedByPhoneAsync(phone, Ct));
        var active = await WithIdentityAsync(identity => identity.FindDeletedByPhoneAsync(activePhone, Ct));
        var unknown = await WithIdentityAsync(identity => identity.FindDeletedByPhoneAsync(TestPhones.Unique(), Ct));

        Assert.Equal(user.Id, deleted?.Id);
        Assert.Equal("en", deleted?.Culture);
        Assert.Null(active);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Users_are_found_by_phone()
    {
        var phone = TestPhones.Unique();
        var created = await WithIdentityAsync(identity => identity.CreateAsync(email: null, phone, phoneConfirmed: true, null, "es", Ct));

        var found = await WithIdentityAsync(identity => identity.FindByPhoneAsync(phone, Ct));
        var other = await WithIdentityAsync(identity => identity.FindByPhoneAsync(TestPhones.Unique(), Ct));
        var deleted = await WithIdentityAsync(identity => identity.IsDeletedPhoneAsync(phone, Ct));

        Assert.Equal(created.Id, found?.Id);
        Assert.Null(other);
        Assert.False(deleted);
    }

    [Fact]
    public async Task Linking_a_phone_or_an_email_writes_them_without_closing_the_session()
    {
        // Solo escriben los datos: renovar el security stamp le cortaría la cookie a quien vincula su propio número
        // desde el perfil. Cortar las sesiones lo decide quien llama.
        var phone = TestPhones.Unique();
        var email = UniqueEmail("linked");
        var user = await WithIdentityAsync(identity => identity.CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: true, null, "es", Ct));
        var stampBefore = await SecurityStampOfAsync(user.Id);

        var afterLinking = await WithIdentityAsync(async identity =>
        {
            await identity.SetPhoneAsync(user.Id, phone, confirmed: false, Ct);
            await identity.SetEmailAsync(user.Id, email, confirmed: true, Ct);
            return await identity.FindByIdAsync(user.Id, Ct);
        });
        var byEmail = await WithIdentityAsync(identity => identity.FindByEmailAsync(email, Ct));

        var afterRemoving = await WithIdentityAsync(async identity =>
        {
            await identity.RemovePhoneAsync(user.Id, Ct);
            return await identity.FindByIdAsync(user.Id, Ct);
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

    [Fact]
    public async Task External_logins_are_reported_by_provider()
    {
        var user = await WithIdentityAsync(identity => identity.CreateAsync(UniqueEmail("provider"), null, "es", Ct));
        var login = new ExternalLogin(
            ExternalLoginProviders.Google, "google-" + user.Id.ToString("N", CultureInfo.InvariantCulture), user.Email, true, null);

        var before = await WithIdentityAsync(identity => identity.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct));
        var after = await WithIdentityAsync(async identity =>
        {
            await identity.AddExternalLoginAsync(user.Id, login, Ct);
            return await identity.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, Ct);
        });
        var otherProvider = await WithIdentityAsync(identity => identity.HasExternalLoginAsync(user.Id, "Microsoft", Ct));

        Assert.False(before);
        Assert.True(after);
        Assert.False(otherProvider);
    }

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync(Ct));

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    /// <summary>
    /// Un texto de búsqueda que solo matchea con las cuentas del test. Lleva el Guid entero y no un pedazo porque la
    /// búsqueda también compara los dígitos del texto contra el número: ocho hex suelen traer cuatro dígitos o más, y
    /// "sort4e9f3a5b" (4935) cae dentro del 549351 de las cuentas con número que dejan otros tests en la base. El Guid
    /// entero trae unos veinte, más de los quince que caben en un número, y aun cuando salen menos son demasiados para
    /// coincidir por azar.
    /// </summary>
    private Task<T> WithIdentityAsync<T>(Func<IIdentityService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IIdentityService>()));
}
