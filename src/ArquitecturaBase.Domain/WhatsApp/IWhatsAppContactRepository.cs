namespace ArquitecturaBase.Domain.WhatsApp;

public interface IWhatsAppContactRepository
{
    /// <summary>
    /// Pone en fila, hasta que termine la unidad de trabajo, todo lo que se hace con los contactos de esos BSUID y esos
    /// números. Sin esto, dos webhooks simultáneos de la misma persona (Meta reintenta, y a veces a la vez) buscan el
    /// contacto, no lo encuentran y lo crean dos veces, o guardan dos veces el mismo mensaje. Toma los locks siempre
    /// en el mismo orden, así dos webhooks con las mismas personas no se esperan uno al otro para siempre. Quien
    /// también necesite los de <see cref="IWhatsAppMessageRepository.LockAsync"/>, toma estos primero.
    /// </summary>
    Task LockAsync(IReadOnlyCollection<string> userIdentifiers, IReadOnlyCollection<string> waIds, CancellationToken cancellationToken);

    Task<WhatsAppContact?> GetByUserIdentifierAsync(string userIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// El contacto con ese número que escribió por última vez. Puede haber más de uno: si la persona cambió de número,
    /// o el número pasó a otra persona, WhatsApp manda otro BSUID y aparece otro contacto con el mismo número.
    /// </summary>
    Task<WhatsAppContact?> GetLatestByWaIdAsync(string waId, CancellationToken cancellationToken);

    void Add(WhatsAppContact contact);
}
