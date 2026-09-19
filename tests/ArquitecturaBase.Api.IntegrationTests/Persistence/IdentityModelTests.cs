using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class IdentityModelTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_users_get_a_version_7_id_the_default_profile_and_audit_dates()
    {
        var user = NewUser();

        await factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(user);
            return db.SaveChangesAsync(Ct);
        });

        var saved = await factory.ExecuteDbContextAsync(db => db.Users.SingleAsync(u => u.Id == user.Id, Ct));
        Assert.Equal(7, saved.Id.Version);
        Assert.Equal("es", saved.Culture);
        Assert.Equal("America/Argentina/Buenos_Aires", saved.TimeZoneId);
        Assert.True(saved.IsActive);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task Normalized_email_is_unique_in_the_database()
    {
        var first = NewUser();
        var second = NewUser();
        second.NormalizedEmail = first.NormalizedEmail;
        await factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(first);
            return db.SaveChangesAsync(Ct);
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => factory.ExecuteDbContextAsync(db =>
        {
            db.Users.Add(second);
            return db.SaveChangesAsync(Ct);
        }));
    }

    private static ApplicationUser NewUser()
    {
        var email = "model-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";

        return new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
        };
    }
}
