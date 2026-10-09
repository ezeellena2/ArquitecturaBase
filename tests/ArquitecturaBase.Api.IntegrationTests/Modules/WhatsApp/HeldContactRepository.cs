using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Frena el procesamiento entrante al tomar la fila de un contacto, hasta que el test lo suelta.
/// Permite cruzar perfil y procesamiento en un orden comprobable, sin introducir locks de identidad.
/// </summary>
internal sealed class InboundProcessingHold(Guid id)
{
    private readonly TaskCompletionSource _taken = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>El contacto en el que se frena.</summary>
    public Guid Id { get; } = id;

    /// <summary>Se completa cuando el procesador llegó al punto y quedó frenado.</summary>
    public Task Taken => _taken.Task;

    public void Release() => _released.TrySetResult();

    public async Task HoldAsync(CancellationToken cancellationToken)
    {
        _taken.TrySetResult();

        await _released.Task.WaitAsync(cancellationToken);
    }
}

/// <summary>El repositorio real de los contactos, que se detiene en <see cref="InboundProcessingHold"/> al tomar la fila de ese contacto.</summary>
internal sealed class HeldContactRepository(IWhatsAppContactRepository inner, InboundProcessingHold hold) : IWhatsAppContactRepository
{
    /// <summary>Cambia el repositorio de la Api por este, con el real adentro.</summary>
    public static void Replace(IServiceCollection services, InboundProcessingHold hold)
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
