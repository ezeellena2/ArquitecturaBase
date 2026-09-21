using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UserSoftDeleteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_marks_the_row_and_hides_the_user_from_the_listing()
    {
        var email = await CreateAccountAsync("deleted");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var deletedAtUtc = factory.Clock.GetUtcNow().UtcDateTime;
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        await DeleteAsync(email);

        using var response = await client.GetWithTokenAsync($"/api/users?search={email}", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());

        var stored = await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, Ct));
        Assert.True(stored.IsDeleted);
        Assert.Equal(deletedAtUtc, stored.DeletedAtUtc);
    }

    [Fact]
    public async Task A_deleted_user_cannot_sign_in_again_with_a_code()
    {
        // El arnés está en Open: el pedido de código sigue su camino y la cuenta se crearía sola. Es el caso
        // peligroso, el que chocaría con el índice único del email.
        var email = await CreateAccountAsync("deletedlogin");
        await DeleteAsync(email);
        using var client = factory.CreateClient();
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Auth.Account.Disabled", problem.GetProperty("code").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .CountAsync(user => user.Email == email, Ct)));
    }

    [Fact]
    public async Task A_deleted_user_cannot_sign_in_again_with_google()
    {
        var email = await CreateAccountAsync("deletedgoogle");
        await DeleteAsync(email);
        using var client = factory.CreateClient();
        using var external = await client.PostJsonAsync(
            "/test/external-login",
            new { providerKey = "google-" + email, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal("/login?error=Auth.Account.Disabled", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_email_of_a_deleted_user_stays_taken_in_the_database()
    {
        // El índice único no se filtra a propósito: por eso el alta de la Tarea 7 restaura en lugar de insertar.
        var email = await CreateAccountAsync("deletedemail");
        await DeleteAsync(email);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IIdentityService>().CreateAsync(Email.Create(email).Value, null, "es", Ct)));
    }

    [Fact]
    public async Task Restoring_a_user_clears_the_lockout_it_had_when_it_was_deleted()
    {
        // Una cuenta que se bloqueó por códigos fallidos y después se eliminó tiene que volver desbloqueada.
        // Si no, la persona recibe Auth.Account.LockedOut al intentar entrar y no hay forma de destrabarla
        // desde el panel: dar de alta el mismo correo la restaura, pero con el bloqueo puesto.
        var email = await CreateAccountAsync("lockedrestore");

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            user.AccessFailedCount = 3;
            user.LockoutEnd = factory.Clock.GetUtcNow().AddHours(1);

            return await dbContext.SaveChangesAsync(Ct);
        });

        await DeleteAsync(email);

        var userId = await factory.ExecuteDbContextAsync(dbContext => dbContext.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(Ct));

        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().RestoreAsync(userId, "De vuelta", Ct);

            return true;
        });

        var restored = await factory.ExecuteDbContextAsync(dbContext => dbContext.Users
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, Ct));

        Assert.False(restored.IsDeleted);
        Assert.Equal(0, restored.AccessFailedCount);
        Assert.Null(restored.LockoutEnd);
    }

    private Task<string> CreateAccountAsync(string prefix)
    {
        var email = TestEmails.Unique(prefix);

        return factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>()
                .CreateAsync(Email.Create(email).Value, displayName: null, "es", Ct);

            return email;
        });
    }

    private Task<int> DeleteAsync(string email) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            dbContext.Users.Remove(user);

            return await dbContext.SaveChangesAsync(Ct);
        });
}
