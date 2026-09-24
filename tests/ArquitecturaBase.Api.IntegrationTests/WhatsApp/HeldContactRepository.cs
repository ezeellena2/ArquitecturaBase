using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Frena al bot en un punto de su vuelta, hasta que el test lo suelta: al tomar la fila de un contacto
/// (<see cref="HeldContactRepository"/>) o al llegar al lock de una cuenta (<see cref="HeldLoginLinkRepository"/>). Así
/// un test cruza al bot con otro pedido en el orden que quiere, sin depender de la suerte. Frena una sola vez: después
/// de soltarlo, el bot pasa de largo.
/// </summary>
internal sealed class BotHold(Guid id)
{
    private readonly TaskCompletionSource _taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>El contacto o la cuenta en la que se frena.</summary>
    public Guid Id { get; } = id;

    /// <summary>Se completa cuando el bot llegó al punto y quedó frenado.</summary>
    public Task Taken => _taken.Task;

    public void Release() => _released.TrySetResult();

    public async Task HoldAsync(CancellationToken cancellationToken)
    {
        _taken.TrySetResult();

        await _released.Task.WaitAsync(cancellationToken);
    }
}

/// <summary>El repositorio real de los contactos, que se detiene en <see cref="BotHold"/> al tomar la fila de ese contacto.</summary>
internal sealed class HeldContactRepository(IWhatsAppContactRepository inner, BotHold hold) : IWhatsAppContactRepository
{
    /// <summary>Cambia el repositorio de la Api por este, con el real adentro.</summary>
    public static void Replace(IServiceCollection services, BotHold hold)
    {
        services.RemoveAll<IWhatsAppContactRepository>();
        services.AddScoped<IWhatsAppContactRepository>(serviceProvider => new HeldContactRepository(
            new WhatsAppContactRepository(serviceProvider.GetRequiredService<ApplicationDbContext>()), hold));
    }

    public Task LockAsync(IReadOnlyCollection<string> userIdentifiers, IReadOnlyCollection<string> waIds, CancellationToken cancellationToken) =>
        inner.LockAsync(userIdentifiers, waIds, cancellationToken);

    public Task<WhatsAppContact?> GetByUserIdentifierAsync(string userIdentifier, CancellationToken cancellationToken) =>
        inner.GetByUserIdentifierAsync(userIdentifier, cancellationToken);

    public Task<WhatsAppContact?> GetLatestByWaIdAsync(string waId, CancellationToken cancellationToken) =>
        inner.GetLatestByWaIdAsync(waId, cancellationToken);

    public async Task<WhatsAppContact?> GetForProcessingAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await inner.GetForProcessingAsync(contactId, cancellationToken);

        if (contact is not null && contactId == hold.Id)
        {
            await hold.HoldAsync(cancellationToken);
        }

        return contact;
    }

    public Task LockForNumberChangeAsync(Guid userId, string? waId, CancellationToken cancellationToken) =>
        inner.LockForNumberChangeAsync(userId, waId, cancellationToken);

    public Task<WhatsAppContact?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        inner.GetByUserIdAsync(userId, cancellationToken);

    public Task<WhatsAppContact?> GetByUserIdForUnlinkAsync(Guid userId, CancellationToken cancellationToken) =>
        inner.GetByUserIdForUnlinkAsync(userId, cancellationToken);

    public void Add(WhatsAppContact contact) => inner.Add(contact);
}
