using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

/// <summary>
/// Lo que WhatsApp ata al número de una cuenta: su contacto del chat, que el bot busca primero para mandarle los enlaces.
/// Delega cada aviso de PhoneNumberLinker en <see cref="WhatsAppContactLinker"/>, que sigue siendo el único que vincula y
/// suelta contactos: los locks de las filas de los contactos, vincular el del número confirmado y soltar el de la cuenta.
/// </summary>
internal sealed class WhatsAppPhoneLinkParticipant(WhatsAppContactLinker contactLinker) : IPhoneLinkParticipant
{
    public Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken) =>
        contactLinker.LockAsync(userId, newPhone, cancellationToken);

    public Task PhoneConfirmedAsync(Guid userId, PhoneNumber phone, CancellationToken cancellationToken) =>
        contactLinker.LinkNumberAsync(phone, userId, cancellationToken);

    public Task PhoneReleasedAsync(Guid userId, CancellationToken cancellationToken) =>
        contactLinker.UnlinkUserAsync(userId, cancellationToken);
}
