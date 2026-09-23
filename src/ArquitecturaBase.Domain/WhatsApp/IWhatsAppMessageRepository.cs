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

    void Add(WhatsAppMessage message);
}
