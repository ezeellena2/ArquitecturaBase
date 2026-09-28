using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>
/// Lo técnico del ingreso sobre Identity y OpenIddict (ISignInService), contra Postgres. La cookie de la aplicación y la
/// cookie externa necesitan un HttpContext: las cubren por HTTP AuthFlow, LoginLinkTests y ExternalLoginTests. Que cada
/// miembro declare su regla de transacción lo fija UnitOfWorkTransactionTests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SignInServiceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenth_failed_attempt_locks_the_account()
    {
        var user = await CreateAccountAsync("lock");

        var lockedAfterNine = await InTransactionWithSignInAsync(async signIn =>
        {
            for (var i = 0; i < 9; i++)
            {
                await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            }

            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        var lockedAfterTen = await InTransactionWithSignInAsync(async signIn =>
        {
            await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(lockedAfterNine);
        Assert.True(lockedAfterTen);
    }

    [Fact]
    public async Task Resetting_failed_attempts_brings_the_count_back_to_zero()
    {
        var user = await CreateAccountAsync("reset");
        await factory.InTransactionAsync(async services =>
        {
            var signIn = services.GetRequiredService<ISignInService>();

            for (var i = 0; i < 9; i++)
            {
                await signIn.RegisterFailedAttemptAsync(user.Id, Ct);
            }
        });
        Assert.Equal(9, await AccessFailedCountAsync(user.Id));

        var locked = await InTransactionWithSignInAsync(async signIn =>
        {
            await signIn.ResetFailedAttemptsAsync(user.Id, Ct);
            await signIn.RegisterFailedAttemptAsync(user.Id, Ct);

            return await signIn.IsLockedOutAsync(user.Id, Ct);
        });

        Assert.False(locked);
        Assert.Equal(1, await AccessFailedCountAsync(user.Id));
    }

    /// <summary>
    /// Las operaciones cargan la cuenta con UserManagerExtensions.RequireUserAsync, la única carga por Id de una cuenta
    /// no borrada para modificarla: una borrada o una que no existe lanzan, también cuando la borrada sigue en el
    /// contexto del mismo scope.
    /// </summary>
    [Fact]
    public async Task Operations_reject_a_deleted_or_missing_account()
    {
        var user = await CreateAccountAsync("deleted");

        await factory.InTransactionAsync(async services =>
        {
            var signIn = services.GetRequiredService<ISignInService>();
            Assert.False(await signIn.IsLockedOutAsync(user.Id, Ct));

            await services.GetRequiredService<IUserRepository>().DeleteAsync(user.Id, Ct);

            await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.IsLockedOutAsync(user.Id, Ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => signIn.IsLockedOutAsync(Guid.CreateVersion7(), Ct));
        });
    }

    /// <summary>
    /// El stamp y las dos revocaciones de OpenIddict van juntos: las revocaciones son UPDATE inmediatos, y sin la
    /// transacción del caso de uso se confirmarían sueltas. Por eso lanza antes de tocar nada, también el stamp.
    /// </summary>
    [Fact]
    public async Task Revoking_sessions_outside_a_transaction_throws_before_touching_the_stamp()
    {
        var user = await CreateAccountAsync("revoke-outside");
        var stampBefore = await SecurityStampOfAsync(user.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<ISignInService>().RevokeSessionsAsync(user.Id, Ct)));

        Assert.Contains(nameof(IUnitOfWork.ExecuteInTransactionAsync), error.Message, StringComparison.Ordinal);
        Assert.Equal(stampBefore, await SecurityStampOfAsync(user.Id));
    }

    /// <summary>
    /// La cookie de la aplicación sale después del commit: adentro de un límite, SignInAsync lanza antes de tocar la
    /// respuesta, así que no necesita un HttpContext para fallar. Protege también la regla de oro de WhatsApp: el bot
    /// corre siempre adentro de un límite.
    /// </summary>
    [Fact]
    public async Task Signing_in_inside_a_boundary_throws_before_touching_the_response()
    {
        var user = await CreateAccountAsync("inside");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.InTransactionAsync(services =>
            services.GetRequiredService<ISignInService>().SignInAsync(user.Id, Ct)));

        Assert.Contains("outside IUnitOfWork.ExecuteInTransactionAsync", error.Message, StringComparison.Ordinal);
    }

    private Task<UserAccount> CreateAccountAsync(string prefix) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            Email.Create(TestEmails.Unique(prefix)).Value, phone: null, phoneConfirmed: false, displayName: null, "es", Ct));

    private Task<int> AccessFailedCountAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.AccessFailedCount)
            .SingleAsync(Ct));

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(Ct));

    private Task<T> InTransactionWithSignInAsync<T>(Func<ISignInService, Task<T>> action) =>
        factory.InTransactionAsync(services => action(services.GetRequiredService<ISignInService>()));
}
