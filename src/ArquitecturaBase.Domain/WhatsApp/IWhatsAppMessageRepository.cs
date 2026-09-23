namespace ArquitecturaBase.Domain.WhatsApp;

public interface IWhatsAppMessageRepository
{
    /// <summary>
    /// Pone en fila, hasta que termine la unidad de trabajo, los avisos de estado de esos mensajes: sin esto, dos
    /// avisos simultáneos del mismo mensaje leen el mismo estado y el que guarda último gana, aunque sea el más viejo.
    /// Toma los locks siempre en el mismo orden, y después de los de <see cref="IWhatsAppContactRepository.LockAsync"/>.
    /// </summary>
    Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken);

    /// <summary>Cuáles de esos ids ya están guardados, entrantes o salientes.</summary>
    Task<IReadOnlySet<string>> ListExistingIdsAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken);

    /// <summary>Los mensajes salientes guardados con esos ids.</summary>
    Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken);

    /// <summary>
    /// Los contactos con mensajes entrantes pendientes, primero el que espera hace más, hasta <paramref name="limit"/>.
    /// Deja afuera los de <paramref name="excluded"/>: los que el procesador ya intentó en esta vuelta, así uno que
    /// falla o que tiene otra instancia no se vuelve a pedir una y otra vez.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListContactsWithPendingInboundAsync(
        IReadOnlyCollection<Guid> excluded,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Los mensajes entrantes pendientes del contacto, del más viejo al más nuevo.</summary>
    Task<IReadOnlyList<WhatsAppMessage>> ListPendingInboundAsync(Guid contactId, CancellationToken cancellationToken);

    void Add(WhatsAppMessage message);
}
