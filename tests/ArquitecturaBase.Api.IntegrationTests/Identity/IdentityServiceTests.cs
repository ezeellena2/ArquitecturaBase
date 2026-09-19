using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
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

    private static Email UniqueEmail(string prefix) =>
        Email.Create(prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com").Value;

    private Task<T> WithIdentityAsync<T>(Func<IIdentityService, Task<T>> action) =>
        factory.ExecuteScopeAsync(services => action(services.GetRequiredService<IIdentityService>()));
}
