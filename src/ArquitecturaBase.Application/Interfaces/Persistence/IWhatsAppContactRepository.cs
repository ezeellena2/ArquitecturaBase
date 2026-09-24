using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

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

    /// <summary>
    /// El contacto con su fila tomada hasta que termine la unidad de trabajo, para que el bot procese sus mensajes. Es
    /// el lock por contacto del procesador (sección 7 del spec del ingreso con WhatsApp): si otra instancia lo está
    /// procesando, o un webhook lo está guardando, no espera y devuelve null. Sus mensajes siguen pendientes y quedan
    /// para la próxima vuelta. También devuelve null si el contacto no existe.
    /// </summary>
    Task<WhatsAppContact?> GetForProcessingAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Toma, hasta que termine la unidad de trabajo, las filas de los contactos que toca un cambio del número de una
    /// cuenta: el vinculado a <paramref name="userId"/> y, si viene <paramref name="waId"/>, los de ese número. A
    /// diferencia de <see cref="GetForProcessingAsync"/>, espera a quien las tenga (el bot o un webhook): el cambio no
    /// se puede dejar para la próxima vuelta. Las toma ordenadas, así dos cambios que tocan los mismos contactos no se
    /// esperan uno al otro para siempre.
    /// </summary>
    Task LockForNumberChangeAsync(Guid userId, string? waId, CancellationToken cancellationToken);

    /// <summary>El contacto vinculado a esa cuenta, o null. Una cuenta tiene a lo sumo uno.</summary>
    Task<WhatsAppContact?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// El contacto vinculado a esa cuenta, o null, con su fila tomada hasta que termine la unidad de trabajo, para
    /// soltarlo porque la cuenta pasa a otro contacto. No espera: si la fila la tiene otro, falla con una excepción, y la
    /// unidad de trabajo no guarda nada. Es para el bot, que ya tiene su contacto y el lock de la cuenta: un cambio de
    /// número (<see cref="LockForNumberChangeAsync"/>) toma esta fila y después espera el lock de la cuenta, y si el bot
    /// esperara la fila, cada uno esperaría al otro. Así el que se corre es el bot, que deja sus mensajes para la próxima
    /// vuelta. Quien ya tiene la fila la vuelve a tomar sin esperar.
    /// </summary>
    Task<WhatsAppContact?> GetByUserIdForUnlinkAsync(Guid userId, CancellationToken cancellationToken);

    void Add(WhatsAppContact contact);
}
