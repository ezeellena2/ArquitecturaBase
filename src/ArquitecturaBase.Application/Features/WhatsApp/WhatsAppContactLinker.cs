using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Features.WhatsApp;

/// <summary>
/// El vínculo entre una cuenta y su contacto de WhatsApp (sección 6.5 del spec del ingreso con WhatsApp), en un solo
/// lugar: lo usan el bot, cuando alguien le escribe desde el número de una cuenta, y el perfil, cuando la persona
/// vincula, cambia o desvincula su número. Una cuenta tiene un solo contacto, y el bot la busca primero por él: un
/// contacto que quedara apuntando a una cuenta que ya no tiene ese número le seguiría mandando a ese chat los enlaces de
/// la cuenta. Los cambios los guarda la unidad de trabajo de quien llama.
/// </summary>
internal sealed class WhatsAppContactLinker(IWhatsAppContactRepository contacts)
{
    /// <summary>
    /// Vincula <paramref name="contact"/> a la cuenta. Si la cuenta tenía otro (el mismo número, que WhatsApp mandó con
    /// otro BSUID, o el de un número anterior), lo suelta: el que escribe desde el número de la cuenta es su contacto. Si
    /// <paramref name="contact"/> era de otra cuenta (un vínculo viejo, de cuando el número era de esa), pasa a esta.
    /// </summary>
    /// <remarks>
    /// El contacto anterior se toma sin esperar (<see cref="IWhatsAppContactRepository.GetByUserIdForUnlinkAsync"/>). El
    /// bot llega acá con su contacto y el lock de la cuenta, y un cambio de número del perfil toma el contacto anterior y
    /// después espera ese lock: si el bot esperara la fila, cada uno esperaría al otro, y Postgres cortaría a uno (40P01),
    /// que suele ser el perfil, con un 500. Así falla el bot, que deja sus mensajes para la próxima vuelta. El perfil tomó
    /// antes las filas de la cuenta (<see cref="LockAsync"/>), y las vuelve a tomar sin esperar.
    /// </remarks>
    public async Task LinkAsync(WhatsAppContact contact, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);

        if (contact.UserId == userId)
        {
            return;
        }

        if (await contacts.GetByUserIdForUnlinkAsync(userId, cancellationToken) is { } previous && previous.Id != contact.Id)
        {
            previous.UnlinkUser();
        }

        contact.LinkUser(userId);
    }

    /// <summary>
    /// Vincula a la cuenta el contacto de <paramref name="phone"/>, el número que la persona acaba de probar que es suyo:
    /// el último que escribió desde ese número. Si desde ahí no le escribió nadie al bot, no hay contacto que vincular: la
    /// cuenta suelta el de su número anterior, y el bot la vincula cuando le escriba.
    /// </summary>
    public async Task LinkNumberAsync(PhoneNumber phone, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        if (await contacts.GetLatestByWaIdAsync(WaIdOf(phone), cancellationToken) is { } contact)
        {
            await LinkAsync(contact, userId, cancellationToken);
        }
        else
        {
            await UnlinkUserAsync(userId, cancellationToken);
        }
    }

    /// <summary>Suelta el contacto de la cuenta, si tiene uno, porque la cuenta ya no tiene ese número.</summary>
    public async Task UnlinkUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await contacts.GetByUserIdAsync(userId, cancellationToken))?.UnlinkUser();

    /// <summary>
    /// Toma las filas de los contactos que va a tocar un cambio del número de la cuenta: el suyo y, si pasa a
    /// <paramref name="newPhone"/>, los de ese número. Espera a quien las tenga. Va antes que cualquier otro lock o
    /// escritura de la cuenta, en el orden del bot (ver <see cref="Users.PhoneNumberChange.LockAsync"/>).
    /// </summary>
    public Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken) =>
        contacts.LockForNumberChangeAsync(userId, newPhone is null ? null : WaIdOf(newPhone), cancellationToken);

    // WhatsApp manda el número sin el "+" y, los celulares argentinos, con el 9: igual que como se guarda.
    private static string WaIdOf(PhoneNumber phone) => phone.Value[1..];
}
