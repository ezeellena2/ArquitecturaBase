using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>Dónde se frena el bot con el lock de una cuenta.</summary>
internal enum AccountLockHoldPoint
{
    /// <summary>Ya encontró la cuenta y todavía no pidió su lock: otro pedido puede tomarlo y terminar antes.</summary>
    BeforeTakingIt,

    /// <summary>Ya tiene el lock: el que lo pida después espera al bot.</summary>
    AfterTakingIt,
}

/// <summary>
/// El repositorio real de los enlaces, que frena al bot en <see cref="BotHold"/> la primera vez que llega al lock de esa
/// cuenta, antes o después de tomarlo según <see cref="AccountLockHoldPoint"/>.
/// </summary>
internal sealed class HeldLoginLinkRepository(ILoginLinkRepository inner, BotHold hold, AccountLockHoldPoint point) : ILoginLinkRepository
{
    /// <summary>Cambia el repositorio de la Api por este, con el real adentro.</summary>
    public static void Replace(IServiceCollection services, BotHold hold, AccountLockHoldPoint point)
    {
        services.RemoveAll<ILoginLinkRepository>();
        services.AddScoped<ILoginLinkRepository>(serviceProvider => new HeldLoginLinkRepository(
            new LoginLinkRepository(serviceProvider.GetRequiredService<ApplicationDbContext>()), hold, point));
    }

    public async Task LockAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var held = userId == hold.Id;

        if (held && point == AccountLockHoldPoint.BeforeTakingIt)
        {
            await hold.HoldAsync(cancellationToken);
        }

        await inner.LockAccountAsync(userId, cancellationToken);

        if (held && point == AccountLockHoldPoint.AfterTakingIt)
        {
            await hold.HoldAsync(cancellationToken);
        }
    }

    public Task<Guid?> FindUserIdAsync(string tokenHash, CancellationToken cancellationToken) =>
        inner.FindUserIdAsync(tokenHash, cancellationToken);

    public Task<LoginLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        inner.GetByTokenHashAsync(tokenHash, cancellationToken);

    public Task<IReadOnlyList<LoginLink>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        inner.ListActiveAsync(userId, nowUtc, cancellationToken);

    public Task<IReadOnlyList<LoginLink>> ListPendingAsync(Guid userId, CancellationToken cancellationToken) =>
        inner.ListPendingAsync(userId, cancellationToken);

    public Task<IReadOnlyList<DateTime>> ListIssueTimesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        inner.ListIssueTimesSinceAsync(userId, sinceUtc, cancellationToken);

    public void Add(LoginLink loginLink) => inner.Add(loginLink);
}
