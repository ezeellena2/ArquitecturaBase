using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>Manages account/contact associations only during explicit profile and administration phone changes.
/// The caller owns the transaction and takes contact locks before account locks.</summary>
internal sealed class WhatsAppContactLinker(IWhatsAppContactRepository contacts)
{
    /// <summary>
    /// Vincula <paramref name="contact"/> a la cuenta. Si la cuenta tenía otro (el mismo número, que WhatsApp mandó con
    /// otro BSUID, o el de un número anterior), lo suelta: el que escribe desde el número de la cuenta es su contacto. Si
    /// <paramref name="contact"/> era de otra cuenta (un vínculo viejo, de cuando el número era de esa), pasa a esta.
    /// </summary>
    /// <remarks>The caller already took the affected rows through LockAsync; the previous association is read under that lock.</remarks>
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
    /// el último que escribió desde ese número. Si todavía no hay contacto, suelta el anterior sin crear uno.
    /// Un mensaje posterior no vincula automáticamente la cuenta.
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
    /// <paramref name="newPhone"/>, los de ese número. Espera a quien las tenga. Lo llama el participante de WhatsApp
    /// (<see cref="Channels.WhatsAppPhoneLinkParticipant"/>), antes que cualquier otro lock o escritura de la cuenta, siguiendo
    /// el orden canónico (ver <see cref="ArquitecturaBase.Application.Services.Users.PhoneNumberLinker.LockAsync"/>).
    /// </summary>
    public Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken) =>
        contacts.LockForNumberChangeAsync(userId, newPhone is null ? null : WaIdOf(newPhone), cancellationToken);

    // WhatsApp manda el número sin el "+" y, los celulares argentinos, con el 9: igual que como se guarda.
    private static string WaIdOf(PhoneNumber phone) => phone.Value[1..];
}
