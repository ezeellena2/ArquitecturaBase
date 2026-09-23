using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>La primera vez dice que el mensaje no está guardado, aunque ya lo esté: como si otro lo guardara justo después.</summary>
internal sealed class StaleReads(string waMessageId)
{
    private int _used;

    public string WaMessageId { get; } = waMessageId;

    public bool Used => Volatile.Read(ref _used) == 1;

    public bool TryUse() => Interlocked.Exchange(ref _used, 1) == 0;
}

/// <summary>
/// El repositorio real de los mensajes, con una lectura vieja (<see cref="StaleReads"/>): así un test llega al choque
/// con el índice único sin depender de cómo se crucen dos pedidos.
/// </summary>
internal sealed class StaleReadsMessageRepository(IWhatsAppMessageRepository inner, StaleReads staleReads) : IWhatsAppMessageRepository
{
    /// <summary>Cambia el repositorio de la Api por este, con el real adentro.</summary>
    public static void Replace(IServiceCollection services, StaleReads staleReads)
    {
        services.RemoveAll<IWhatsAppMessageRepository>();
        services.AddScoped<IWhatsAppMessageRepository>(serviceProvider => new StaleReadsMessageRepository(
            new WhatsAppMessageRepository(serviceProvider.GetRequiredService<ApplicationDbContext>()), staleReads));
    }

    public Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken) =>
        inner.LockAsync(waMessageIds, cancellationToken);

    public async Task<IReadOnlySet<string>> ListExistingIdsAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken)
    {
        var existing = await inner.ListExistingIdsAsync(waMessageIds, cancellationToken);

        return existing.Contains(staleReads.WaMessageId) && staleReads.TryUse()
            ? existing.Where(id => id != staleReads.WaMessageId).ToHashSet(StringComparer.Ordinal)
            : existing;
    }

    public Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken) =>
        inner.ListOutboundAsync(waMessageIds, cancellationToken);

    public void Add(WhatsAppMessage message) => inner.Add(message);
}
